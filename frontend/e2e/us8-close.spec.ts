import path from 'node:path';
import { test, expect, Page, Locator } from '@playwright/test';
import { createJudgeUser, deleteUser, ProvisionedJudge } from './support/keycloak-admin';

// quickstart.md scenario 8 / spec.md US8 (FR-033/FR-034/FR-035/FR-042): complete every evaluation
// at a table, close it via the real judge UI, then prove immutability from both sides — a judge's
// evaluation-mutation attempt against the now-closed table is rejected (`409 table-closed`,
// FR-034), while an organizer correction is allowed and recomputes the total/consolidated mean
// (FR-035/FR-042).
//
// AuditLog persistence for the organizer correction (Acceptance Scenario 3: "recorded with
// who/when/what for audit") is already asserted directly against the database by the backend's
// own CorrectEvaluation coverage
// (backend/tests/BirraPoint.Api.IntegrationTests/Evaluations/CloseTableApiTests.cs,
// GetAuditLogAsync) — there is no organizer-facing UI or GET endpoint in this phase's scope that
// would let this E2E layer observe the AuditLog row, so a `200` + recomputed total here is the
// full extent of what this test can (and needs to) prove at this layer.
//
// FR-034 "late offline sync" simplification (read before touching the close/late-sync section
// below): CloseTableCommandHandler's completeness gate (CloseTableRules.ComputeMissingBlindCodes)
// is airtight — a table can only ever reach `Closed` once every active judge has a submitted
// (judge, sample) row for every one of its samples. That means there is no way, through the
// currently-implemented API, for a *genuinely* still-pending judge submission to exist against a
// table that has *validly* closed: replaying the exact same (judge, sample) pair that was already
// submitted hits SubmitEvaluationCommandHandler's idempotent-replay branch first (by design,
// FR-029/R-07 — a replay must succeed even if the table closed *after* the original submit) and
// returns `200`, never `409`, regardless of table state. Reproducing a true network race (this
// judge's POST in flight over the wire at the exact instant another table member's close commits)
// is not reliably constructible from Playwright. Per this task's own instructions, the accepted
// simplification is a direct API-level check: once the table is closed, fire a *new* (never
// previously submitted) evaluation POST for this table — using a fresh random beerEntryId to
// stand in for "one more sample this judge's outbox still had queued" — with the judge's own
// bearer token. SubmitEvaluationCommandHandler's `table.State != TableState.Open` check runs
// before it ever looks at whether the beerEntryId is one of the table's actual samples, so this
// exercises exactly the code path a real late/queued sync would hit and is a faithful proof of
// FR-034/AC2's "create ... is rejected, including delayed offline syncs" — it just doesn't route
// through Playwright's offline emulation to get there.
const KEYCLOAK_ORIGIN = 'http://localhost:8081';
const ORGANIZER_USERNAME = 'organizer';
const ORGANIZER_PASSWORD = 'organizer';
const API_BASE_URL = 'http://localhost:5121';

const MAILPIT_ORIGIN = 'http://localhost:8025';
const MAILPIT_POLL_TIMEOUT_MS = 10_000;
const MAILPIT_POLL_INTERVAL_MS = 500;

const FIXTURE_PATH = path.resolve(__dirname, 'fixtures/tables-assignment.xlsx');

// Same fixture as us6/us7's "neutral" entry: a style whose owning participant doesn't match this
// spec's freshly-provisioned judge email, so there's no COI/BOS interaction to account for — this
// spec only needs one sample, since the point of US8 is completing *all* of a table's evaluations
// (trivially true with one), not sequencing multiple (already covered by us6/us7).
const NEUTRAL_STYLE_CODE = '1A';

const ORIGINAL_SECTIONS = [
  { legend: 'Aroma', score: 10, comment: 'Citrus and pine hop aroma, moderate intensity.' },
  { legend: 'Appearance', score: 2, comment: 'Deep golden, persistent white head, brilliant.' },
  { legend: 'Flavor', score: 15, comment: 'Balanced malt backbone with resinous hop finish.' },
  { legend: 'Mouthfeel', score: 4, comment: 'Medium body, lively carbonation, dry finish.' },
  {
    legend: 'Overall Impression',
    score: 8,
    comment: 'A clean, well-executed example of the style.',
  },
] as const;
const ORIGINAL_TOTAL = ORIGINAL_SECTIONS.reduce((sum, section) => sum + section.score, 0);

// Deliberately maxes out every section cap so the recomputed total is unmistakably different from
// ORIGINAL_TOTAL above — proves the organizer's correction actually took effect server-side rather
// than the endpoint silently no-oping.
const CORRECTION_SCORES = { aroma: 12, appearance: 3, flavor: 20, mouthfeel: 5, overall: 10 };
const CORRECTION_TOTAL = Object.values(CORRECTION_SCORES).reduce((sum, score) => sum + score, 0);
const CORRECTION_COMMENTS = {
  aroma: 'Organizer correction: aroma note now reflects the judges panel review.',
  appearance: 'Organizer correction: appearance note updated after panel review.',
  flavor: 'Organizer correction: flavor note updated after the panel re-review.',
  mouthfeel: 'Organizer correction: mouthfeel note updated after panel review.',
  overall: 'Organizer correction: overall impression updated after panel review.',
};

async function submitKeycloakLogin(page: Page, username: string, password: string): Promise<void> {
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.locator('#kc-login').click();
}

function uniqueCompetitionName(): string {
  return `E2E Close Comp ${Date.now()}-${crypto.randomUUID()}`;
}

// Mirrors us6/us7's createCompetition: creates via the wizard's Basics step and returns the
// persisted competitionId, leaving the competition Draft.
async function createCompetition(page: Page, name: string): Promise<string> {
  await page.goto('/organizer/competitions/new');

  await page.getByLabel('Name').fill(name);
  await page.getByLabel('Venue').fill('Salón de Actos, Madrid');
  await page.getByLabel('Start date').fill('2026-09-01');
  await page.getByLabel('End date').fill('2026-09-03');

  await page.getByRole('button', { name: 'Next' }).click();
  await page.waitForURL(/\/organizer\/competitions\/[0-9a-fA-F-]{36}$/);

  return page.url().split('/').pop()!;
}

// Mirrors us6/us7's blindCodeForStyle: reads the Consolidation summary's "Created entries" table.
async function blindCodeForStyle(summary: Locator, styleCode: string): Promise<string> {
  const rows = summary.locator('tbody tr');
  const count = await rows.count();
  for (let i = 0; i < count; i++) {
    const row = rows.nth(i);
    const style = (await row.locator('td').nth(1).innerText()).trim();
    if (style === styleCode) {
      return (await row.locator('td').first().innerText()).trim();
    }
  }
  throw new Error(`No consolidated entry found with style ${styleCode}`);
}

function beerToken(scope: Page | Locator, blindCode: string): Locator {
  return scope.getByRole('button', { name: `Beer ${blindCode} — view details`, exact: true });
}

function judgeSeat(scope: Page | Locator, displayName: string): Locator {
  return scope.getByRole('button', { name: `Judge ${displayName} — view details`, exact: true });
}

function mesaCard(page: Page, name: string): Locator {
  return page
    .locator('article.mesa-card')
    .filter({ has: page.getByRole('heading', { level: 3, name, exact: true }) });
}

// Mirrors us5/us6/us7's pointerDrag: Angular CDK drag-drop is pointer-event-based, not native
// HTML5 DnD, so a manual mouse-move sequence is required to trigger it reliably.
async function pointerDrag(page: Page, source: Locator, target: Locator): Promise<void> {
  await source.scrollIntoViewIfNeeded();
  await target.scrollIntoViewIfNeeded();

  const sourceBox = await source.boundingBox();
  const targetBox = await target.boundingBox();
  if (!sourceBox || !targetBox) {
    throw new Error('Cannot compute a bounding box for the drag source/target.');
  }

  const startX = sourceBox.x + sourceBox.width / 2;
  const startY = sourceBox.y + sourceBox.height / 2;
  const endX = targetBox.x + targetBox.width / 2;
  const endY = targetBox.y + targetBox.height / 2;

  await page.mouse.move(startX, startY);
  await page.mouse.down();
  for (const fraction of [0.25, 0.5, 0.75, 1]) {
    await page.mouse.move(
      startX + (endX - startX) * fraction,
      startY + (endY - startY) * fraction,
      { steps: 8 },
    );
  }
  await page.mouse.move(endX, endY, { steps: 5 }); // settle at the destination before releasing
  await page.mouse.up();
}

// Mirrors us7-offline.spec.ts's advanceCompetitionState: drives the real dashboard advance-state
// control (T102/FR-051) rather than a token-capture workaround.
function competitionRow(page: Page, name: string): Locator {
  return page.locator('li.competition-list-row').filter({ hasText: name });
}

async function advanceCompetitionState(
  page: Page,
  competitionName: string,
  actionLabel: string,
): Promise<void> {
  const row = competitionRow(page, competitionName);
  await row.getByRole('button', { name: actionLabel, exact: true }).click();

  const dialog = page.getByRole('alertdialog', { name: 'Confirm advance competition state' });
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: 'Confirm', exact: true }).click();
  await expect(dialog).not.toBeVisible();
}

interface MailpitMessageSummary {
  ID: string;
  To: { Address: string; Name: string }[];
  Subject: string;
}

interface MailpitMessagesResponse {
  messages: MailpitMessageSummary[];
  total: number;
}

interface MailpitMessageDetail {
  Text: string;
}

// Mirrors us4/us6/us7's waitForMailpitMessageTo: invitation delivery is async via a DispatchJob, so
// it does not land synchronously after POST /judges.
async function waitForMailpitMessageTo(
  request: Page['request'],
  email: string,
): Promise<MailpitMessageSummary> {
  const deadline = Date.now() + MAILPIT_POLL_TIMEOUT_MS;

  while (Date.now() < deadline) {
    const response = await request.get(
      `${MAILPIT_ORIGIN}/api/v1/search?query=${encodeURIComponent(`to:${email}`)}`,
    );
    if (response.ok()) {
      const body = (await response.json()) as MailpitMessagesResponse;
      const match = body.messages.find((message) =>
        message.To.some((to) => to.Address.toLowerCase() === email.toLowerCase()),
      );
      if (match) {
        return match;
      }
    }
    await new Promise((resolve) => setTimeout(resolve, MAILPIT_POLL_INTERVAL_MS));
  }

  throw new Error(`Timed out waiting for a Mailpit message addressed to ${email}`);
}

// Mirrors us6/us7's readTemporaryPasswordFromInvitation: SendInvitationHandler resets the invited
// judge's Keycloak password unconditionally on every call, including when the account was
// pre-provisioned (createJudgeUser below, purely to get the JUDGE realm role assigned) — so the
// only reliable current password is the one actually delivered via email.
async function readTemporaryPasswordFromInvitation(
  request: Page['request'],
  email: string,
): Promise<string> {
  const message = await waitForMailpitMessageTo(request, email);
  const detailResponse = await request.get(`${MAILPIT_ORIGIN}/api/v1/message/${message.ID}`);
  if (!detailResponse.ok()) {
    throw new Error(
      `Failed to fetch Mailpit message ${message.ID} for ${email}: ${detailResponse.status()}`,
    );
  }
  const detail = (await detailResponse.json()) as MailpitMessageDetail;
  const match = /Your temporary password is: \*([^*]+)\*/.exec(detail.Text);
  if (!match) {
    throw new Error(`Could not find a temporary password in the invitation email to ${email}.`);
  }
  return match[1];
}

// Mirrors us1/us6/us7's forced-temporary-password-change flow, ending on /judge/tables.
async function loginAsJudge(page: Page, email: string, password: string): Promise<void> {
  await page.goto('/');
  await page.waitForURL(new RegExp(`^${KEYCLOAK_ORIGIN}/`));

  await submitKeycloakLogin(page, email, password);

  await expect(page.locator('#password-new')).toBeVisible();
  const newPassword = `Judge-${crypto.randomUUID()}`;
  await page.locator('#password-new').fill(newPassword);
  await page.locator('#password-confirm').fill(newPassword);
  await page.locator('#kc-passwd-update-form button[type="submit"]').click();

  await page.waitForURL('**/judge/tables');
}

function sectionFieldset(page: Page, legend: string): Locator {
  return page
    .locator('fieldset.evaluation-section')
    .filter({ has: page.locator('legend', { hasText: legend }) });
}

async function fillEvaluationForm(page: Page): Promise<void> {
  for (const section of ORIGINAL_SECTIONS) {
    const fieldset = sectionFieldset(page, section.legend);
    await fieldset.getByLabel('Score').fill(String(section.score));
    await fieldset.getByLabel('Comment').fill(section.comment);
  }
}

// Captures the Authorization header of the next request `page` makes to the backend API,
// triggered by `triggerRequest` — the "capture the caller's own bearer token from an authenticated
// request" technique this suite uses for backend calls that have no organizer-facing UI yet
// (T065's PUT /competitions/{id}/evaluations/{evaluationId} correction, and this test's own
// FR-034 late-sync check). Captured fresh at the point of use (rather than passively accumulated
// over the whole test) so there's no risk of relying on a token that's gone stale by the time it's
// needed.
async function captureBearerToken(
  page: Page,
  triggerRequest: () => Promise<unknown>,
): Promise<string> {
  const [request] = await Promise.all([
    page.waitForRequest(
      (req) => req.url().startsWith(API_BASE_URL) && !!req.headers()['authorization'],
    ),
    triggerRequest(),
  ]);
  const header = request.headers()['authorization'];
  if (!header) {
    throw new Error('Could not capture a bearer token from an authenticated API request.');
  }
  return header;
}

test.describe('US8 — table closing and score immutability', () => {
  let judge: ProvisionedJudge;

  test.beforeAll(async () => {
    // Provisioned purely to get the JUDGE realm role assigned before the organizer's own
    // "Register judges" flow runs against this same email — see us6/us7's identical note
    // (Features/Judges/SendInvitationHandler never assigns a realm role to a freshly-created
    // Keycloak user).
    judge = await createJudgeUser(
      `us8-judge-${Date.now()}-${crypto.randomUUID()}@birrapoint.local`,
    );
  });

  test.afterAll(async () => {
    if (judge?.id) {
      await deleteUser(judge.id);
    }
  });

  test('closing a complete table locks it for judges while an organizer correction still succeeds and recomputes the total', async ({
    page,
    browser,
  }) => {
    // Full organizer setup + a real Keycloak-authenticated judge session + the close/correction/
    // late-sync assertions comfortably exceeds Playwright's 30s default.
    test.setTimeout(120_000);

    // --- Organizer: create competition, import entries, consolidate, register the judge, create
    // one table, assign the judge + a single entry onto it ---
    await page.goto('/');
    await page.waitForURL(new RegExp(`^${KEYCLOAK_ORIGIN}/`));
    await submitKeycloakLogin(page, ORGANIZER_USERNAME, ORGANIZER_PASSWORD);
    await page.waitForURL('**/organizer/dashboard');

    const competitionName = uniqueCompetitionName();
    const competitionId = await createCompetition(page, competitionName);

    await page.goto(`/organizer/competitions/${competitionId}/import`);
    await expect(page.getByRole('heading', { name: 'Import beer entries' })).toBeVisible();

    await page.getByLabel('Entries file (.xlsx)').setInputFiles(FIXTURE_PATH);
    await page.getByRole('button', { name: 'Upload' }).click();

    const resultsSection = page.getByRole('region', { name: 'Import results' });
    await expect(resultsSection).toBeVisible();
    for (let row = 1; row <= 3; row++) {
      await expect(page.locator(`tr[data-row-number="${row}"]`)).toContainText('Valid');
    }

    const consolidateButton = page.getByRole('button', { name: 'Consolidate' });
    await expect(consolidateButton).toBeEnabled();
    await consolidateButton.click();

    const summary = page.getByRole('region', { name: 'Consolidation summary' });
    await expect(summary.getByText('Imported: 3', { exact: true })).toBeVisible();

    const blindCode = await blindCodeForStyle(summary, NEUTRAL_STYLE_CODE);

    await page.goto(`/organizer/competitions/${competitionId}/judges`);
    await expect(page.getByRole('heading', { name: 'Judge management' })).toBeVisible();

    await page.getByLabel('Judge emails (one per line, or comma-separated)').fill(judge.email);
    await page.getByRole('button', { name: 'Register judges' }).click();

    const report = page.locator('div[aria-label="Registration report"]');
    await expect(report).toBeVisible();
    await expect(report.getByText(judge.email, { exact: true })).toBeVisible();

    const judgeTempPassword = await readTemporaryPasswordFromInvitation(page.request, judge.email);

    await page.goto(`/organizer/competitions/${competitionId}/tables`);
    await expect(page.getByRole('heading', { name: 'Table management' })).toBeVisible();

    await page.getByLabel('New table name').fill('Mesa 1');
    await page.getByRole('button', { name: 'Add table' }).click();
    const mesa1 = mesaCard(page, 'Mesa 1');
    await expect(mesa1).toBeVisible();

    const mesa1Id = await mesa1.getAttribute('data-table-id');
    if (!mesa1Id) {
      throw new Error('Table id missing from data-table-id attribute.');
    }
    const mesa1Beers = page.locator(`#beers-${mesa1Id}`);
    const mesa1Judges = page.locator(`#judges-${mesa1Id}`);

    await pointerDrag(page, beerToken(page, blindCode), mesa1Beers);
    await expect(beerToken(mesa1Beers, blindCode)).toBeVisible();

    const judgeDisplayName = judge.email.slice(0, judge.email.indexOf('@'));
    await pointerDrag(page, judgeSeat(page, judgeDisplayName), mesa1Judges);
    await expect(judgeSeat(mesa1Judges, judgeDisplayName)).toBeVisible();

    // --- Draft -> Active -> InEvaluation: POST .../evaluations requires InEvaluation specifically
    // (not just Active), per contracts/rest-api.md's Judge workspace table ---
    await page.goto('/organizer/dashboard');
    await advanceCompetitionState(page, competitionName, 'Activate');
    await expect(competitionRow(page, competitionName).locator('.badge')).toHaveText('Active');
    await advanceCompetitionState(page, competitionName, 'Start evaluation');
    await expect(competitionRow(page, competitionName).locator('.badge')).toHaveText(
      'InEvaluation',
    );

    // --- Judge: independent browser context/session, real forced-password-change login ---
    const judgeContext = await browser.newContext();
    const judgePage = await judgeContext.newPage();

    try {
      await loginAsJudge(judgePage, judge.email, judgeTempPassword);

      const tableLink = judgePage.getByRole('link', { name: new RegExp('Mesa 1') });
      await expect(tableLink).toContainText('Order not fixed');
      await tableLink.click();
      await judgePage.waitForURL(`**/judge/tables/${mesa1Id}`);

      await expect(judgePage.locator('li.sample-row')).toHaveCount(1);

      // Fixing the order itself (drag vs. keyboard) isn't what this scenario is testing — already
      // covered by us6-order.spec.ts. A single-sample table still requires the fix-order step
      // (FR-022 mandatory precondition) before any sheet can be opened.
      await judgePage.getByRole('button', { name: 'Fix order' }).click();
      const fixDialog = judgePage.getByRole('alertdialog', { name: 'Confirm fix order' });
      await expect(fixDialog).toBeVisible();
      await fixDialog.getByRole('button', { name: 'Confirm fix order' }).click();
      await expect(judgePage.locator('p.order-status--fixed')).toBeVisible();

      // Close isn't offered until every sample is evaluated (FR-033 precondition, mirrored
      // client-side by canCloseTable()).
      await expect(judgePage.getByRole('button', { name: 'Close table' })).toHaveCount(0);

      const evaluateLink = judgePage.locator('a.evaluate-action');
      await expect(evaluateLink).toHaveCount(1);
      await evaluateLink.click();

      await judgePage.waitForURL(/\/judge\/tables\/[0-9a-fA-F-]+\/samples\/[0-9a-fA-F-]+$/);
      await expect(judgePage.locator('h1')).toHaveText(blindCode);

      await fillEvaluationForm(judgePage);

      // Capture the submission's own response body (rather than just its status) to get the real
      // server-assigned evaluationId — needed below for the organizer's correction call, which has
      // no UI in this phase and must address the evaluation by id directly.
      const [submitResponse] = await Promise.all([
        judgePage.waitForResponse(
          (response) =>
            response.request().method() === 'POST' &&
            /\/api\/v1\/me\/tables\/.+\/evaluations$/.test(new URL(response.url()).pathname),
        ),
        judgePage.getByRole('button', { name: 'Submit evaluation' }).click(),
      ]);
      expect(submitResponse.status()).toBe(201);
      const submitBody = (await submitResponse.json()) as { evaluationId: string; total: number };
      expect(submitBody.total).toBe(ORIGINAL_TOTAL);
      const evaluationId = submitBody.evaluationId;

      await judgePage.waitForURL(`**/judge/tables/${mesa1Id}`);

      // --- Acceptance Scenario 1 (FR-033): every evaluation at the table is complete and no
      // discrepancy is open (only one judge submitted, so there's nothing to discrepancy-check
      // against) -> "Close table" is now offered ---
      const closeButton = judgePage.getByRole('button', { name: 'Close table' });
      await expect(closeButton).toBeVisible();
      await closeButton.click();

      const closeDialog = judgePage.getByRole('alertdialog', { name: 'Confirm close table' });
      await expect(closeDialog).toBeVisible();
      await closeDialog.getByRole('button', { name: 'Confirm close table' }).click();
      await expect(closeDialog).not.toBeVisible();

      // Closing succeeded with no error surfaced, and the table flips to the permanent closed
      // banner for this judge (Acceptance Scenario 1).
      await expect(judgePage.locator('[role="alert"]')).toHaveCount(0);
      const closedBanner = judgePage.locator('p.order-status--closed');
      await expect(closedBanner).toBeVisible();
      await expect(closedBanner).toContainText('Table closed');
      await expect(judgePage.getByRole('button', { name: 'Close table' })).toHaveCount(0);
      // No path left in the UI for this judge to open or resubmit the (now locked) sample.
      await expect(judgePage.locator('a.evaluate-action')).toHaveCount(0);

      // --- Acceptance Scenario 3 / FR-035 / FR-042: organizer correction is allowed regardless of
      // table state, re-validates the same caps/comment-length rules, and recomputes the total +
      // consolidated mean. No organizer UI exists for this in this phase's scope (T065 is
      // backend-only) — driven directly via the same "capture this session's own bearer token"
      // technique other specs in this suite use for backend calls with no UI yet. ---
      const organizerToken = await captureBearerToken(page, () =>
        page.goto('/organizer/dashboard'),
      );

      const correctionResponse = await page.request.put(
        `${API_BASE_URL}/api/v1/competitions/${competitionId}/evaluations/${evaluationId}`,
        {
          headers: { Authorization: organizerToken },
          data: { scores: CORRECTION_SCORES, comments: CORRECTION_COMMENTS },
        },
      );
      expect(correctionResponse.status()).toBe(200);
      const correctionBody = (await correctionResponse.json()) as {
        evaluationId: string;
        total: number;
        consolidatedMean: number;
      };
      expect(correctionBody.evaluationId).toBe(evaluationId);
      expect(correctionBody.total).toBe(CORRECTION_TOTAL);
      // Only this one judge evaluated the sample, so the consolidated mean (FR-042) equals the
      // corrected total exactly.
      expect(correctionBody.consolidatedMean).toBe(CORRECTION_TOTAL);

      // --- Acceptance Scenario 2 / FR-034: after close, any non-organizer mutation attempt
      // (including a delayed/late offline sync) is rejected. See the file-header comment for why
      // this is a direct API-level check with a fresh beerEntryId rather than a genuine
      // Playwright-driven offline race. ---
      const judgeToken = await captureBearerToken(judgePage, () => judgePage.reload());
      // The reload also re-confirms the closed state survives a fresh fetch, not just the
      // in-memory signal flipped by this judge's own close action above.
      await expect(judgePage.locator('p.order-status--closed')).toBeVisible();

      const lateSyncEntryId = crypto.randomUUID();
      const lateSyncResponse = await judgePage.request.post(
        `${API_BASE_URL}/api/v1/me/tables/${mesa1Id}/evaluations`,
        {
          headers: {
            Authorization: judgeToken,
            'X-Idempotency-Key': `${competitionId}:${mesa1Id}:late-sync-check:${lateSyncEntryId}`,
          },
          data: {
            beerEntryId: lateSyncEntryId,
            scores: { aroma: 10, appearance: 2, flavor: 15, mouthfeel: 4, overall: 8 },
            comments: {
              aroma: 'Late offline sync attempt: aroma note, long enough to pass validation.',
              appearance: 'Late offline sync attempt: appearance note, long enough here.',
              flavor: 'Late offline sync attempt: flavor note, long enough to pass validation.',
              mouthfeel: 'Late offline sync attempt: mouthfeel note, long enough here too.',
              overall: 'Late offline sync attempt: overall note, long enough to pass.',
            },
          },
        },
      );
      expect(lateSyncResponse.status()).toBe(409);
      const lateSyncProblem = (await lateSyncResponse.json()) as { type: string };
      expect(lateSyncProblem.type).toBe('urn:birrapoint:table-closed');
    } finally {
      await judgeContext.close();
    }
  });
});
