import path from 'node:path';
import { test, expect, Page, Locator } from '@playwright/test';
import { createJudgeUser, deleteUser, ProvisionedJudge } from './support/keycloak-admin';

// quickstart.md scenario 12 / spec.md US12 (FR-039): the organizer removes a judge from a live
// table via the dashboard -> that judge's own session ejects immediately (live `JudgeRemoved` hub
// event), a subsequent request from that session against the table 404s (the membership guard
// that starts rejecting the removed judge the instant RemovedAt is set), and their
// already-submitted evaluation from before the removal stays in the organizer's audit
// drill-down. A two-sample table lets one pass exercise both halves of the scenario: judge A
// submits the first sample for real, then leaves an in-progress (unsubmitted) draft on the second
// -- that's the state they're in when removed.
//
// Judge A is mid-sheet on the *second* sample at removal time, so the ejection path exercised here
// is evaluation-sheet.component.ts's handleJudgeRemovedEvent/handleEjected -- not
// judge-table-order.component.ts's (already threading tableName through correctly). If the banner
// below is missing the table name, that's a real regression in the evaluation-sheet ejection path,
// not a flake to work around.

const KEYCLOAK_ORIGIN = 'http://localhost:8081';
const ORGANIZER_USERNAME = 'organizer';
const ORGANIZER_PASSWORD = 'organizer';

const MAILPIT_ORIGIN = 'http://localhost:8025';
const MAILPIT_POLL_TIMEOUT_MS = 10_000;
const MAILPIT_POLL_INTERVAL_MS = 500;

const FIXTURE_PATH = path.resolve(__dirname, 'fixtures/tables-assignment.xlsx');

// Same two entries us6/us7/us8/us11 reuse from this fixture, neither of which collides with a
// fresh @birrapoint.local judge email -- no COI/BOS interaction to account for here, just two
// distinct beers for one table.
const FIRST_STYLE_CODE = '1A';
const SECOND_STYLE_CODE = '20C';

interface ScoreSet {
  aroma: number;
  appearance: number;
  flavor: number;
  mouthfeel: number;
  overall: number;
}

// Section caps (Aroma 12 / Appearance 3 / Flavor 20 / Mouthfeel 5 / Overall 10, FR-023).
const JUDGE_A_FIRST_SCORES: ScoreSet = {
  aroma: 8,
  appearance: 2,
  flavor: 15,
  mouthfeel: 4,
  overall: 7,
};
const JUDGE_A_FIRST_TOTAL = Object.values(JUDGE_A_FIRST_SCORES).reduce(
  (sum, score) => sum + score,
  0,
); // 36

// Never submitted -- this is the in-progress draft left on the second sample at the moment of
// removal. Only needs to be a valid, fillable form; its total is never asserted.
const JUDGE_A_SECOND_SCORES: ScoreSet = {
  aroma: 5,
  appearance: 1,
  flavor: 10,
  mouthfeel: 2,
  overall: 5,
};

interface SectionInput {
  legend: string;
  score: number;
  comment: string;
}

function buildSections(scores: ScoreSet, tag: string): SectionInput[] {
  return [
    {
      legend: 'Aroma',
      score: scores.aroma,
      comment: `${tag} aroma note, long enough to satisfy the minimum comment length rule.`,
    },
    {
      legend: 'Appearance',
      score: scores.appearance,
      comment: `${tag} appearance note, long enough to satisfy the minimum length rule.`,
    },
    {
      legend: 'Flavor',
      score: scores.flavor,
      comment: `${tag} flavor note, long enough to satisfy the minimum comment length rule.`,
    },
    {
      legend: 'Mouthfeel',
      score: scores.mouthfeel,
      comment: `${tag} mouthfeel note, long enough to satisfy the minimum length rule.`,
    },
    {
      legend: 'Overall Impression',
      score: scores.overall,
      comment: `${tag} overall note, long enough to satisfy the minimum length rule.`,
    },
  ];
}

interface SubmitEvaluationResponseBody {
  evaluationId: string;
  status: 'Confirmed' | 'PendingConsensus';
  total: number;
  discrepancy: unknown;
}

async function submitKeycloakLogin(page: Page, username: string, password: string): Promise<void> {
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.locator('#kc-login').click();
}

function uniqueCompetitionName(): string {
  return `E2E Removal Comp ${Date.now()}-${crypto.randomUUID()}`;
}

// Mirrors us11's createCompetition: creates via the wizard's Basics step and returns the persisted
// competitionId, leaving the competition Draft.
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

// Mirrors us6/us11's blindCodeForStyle: reads the Consolidation summary's "Created entries" table.
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

// Mirrors us5/us6/us8/us11's pointerDrag: Angular CDK drag-drop is pointer-event-based, not native
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

// Mirrors us8/us11's advanceCompetitionState: drives the real dashboard advance-state control
// (T102/FR-051) rather than a token-capture workaround.
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

// Mirrors us6/us8/us11's waitForMailpitMessageTo: invitation delivery is async via a DispatchJob,
// so it does not land synchronously after POST /judges.
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

// Mirrors us6/us8/us11's readTemporaryPasswordFromInvitation: SendInvitationHandler resets the
// invited judge's Keycloak password unconditionally on every call, including when the account was
// pre-provisioned (createJudgeUser below, purely to get the JUDGE realm role assigned) -- so the
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

// Mirrors us1/us6/us8/us11's forced-temporary-password-change flow, ending on /judge/tables.
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

// Mirrors us8/us11's sectionFieldset.
function sectionFieldset(page: Page, legend: string): Locator {
  return page
    .locator('fieldset.evaluation-section')
    .filter({ has: page.locator('legend', { hasText: legend }) });
}

async function fillEvaluationForm(page: Page, sections: SectionInput[]): Promise<void> {
  for (const section of sections) {
    const fieldset = sectionFieldset(page, section.legend);
    await fieldset.getByLabel('Score').fill(String(section.score));
    await fieldset.getByLabel('Comment').fill(section.comment);
  }
}

interface TableJudgeBody {
  id: string;
  email: string;
  displayName: string;
}

interface TableSummaryBody {
  id: string;
  name: string;
  judges: TableJudgeBody[];
}

// Navigates the organizer to the live monitoring dashboard (T070/US9) and resolves judge A's real
// Judge.Id off the dashboard's own GET /tables response -- the DOM alone never exposes it, only
// email/displayName (and displayName is unreliable to key off here: JudgeResolver backfills it
// from the Keycloak profile name on first login, so once judge A has actually authenticated their
// row shows "Test Judge", not the email-derived name used to drag them onto the table earlier).
async function loadMonitorAndFindJudgeId(
  page: Page,
  competitionId: string,
  tableId: string,
  judgeEmail: string,
): Promise<string> {
  const [response] = await Promise.all([
    page.waitForResponse(
      (r) =>
        r.request().method() === 'GET' &&
        new RegExp(`/api/v1/competitions/${competitionId}/tables$`).test(new URL(r.url()).pathname),
    ),
    page.goto(`/organizer/competitions/${competitionId}/monitor`),
  ]);
  const tables = (await response.json()) as TableSummaryBody[];
  const table = tables.find((t) => t.id === tableId);
  const judge = table?.judges.find((j) => j.email === judgeEmail);
  if (!judge) {
    throw new Error(
      `Judge ${judgeEmail} not found on table ${tableId} in the dashboard's tables response.`,
    );
  }
  return judge.id;
}

function tableProgressRow(page: Page, tableId: string): Locator {
  return page.locator(`li.table-progress-row[data-table-id="${tableId}"]`);
}

function dashboardJudgeRow(page: Page, tableId: string, judgeId: string): Locator {
  return tableProgressRow(page, tableId).locator(`li.judge-row[data-judge-id="${judgeId}"]`);
}

async function removeJudgeViaDashboard(
  page: Page,
  tableId: string,
  judgeId: string,
): Promise<void> {
  await dashboardJudgeRow(page, tableId, judgeId).getByRole('button', { name: 'Remove' }).click();

  const dialog = page.getByRole('alertdialog', { name: 'Confirm remove judge' });
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: 'Confirm remove judge' }).click();
  await expect(dialog).not.toBeVisible();
}

test.describe('US12 — live judge removal', () => {
  let judgeA: ProvisionedJudge;
  let judgeB: ProvisionedJudge;

  test.beforeAll(async () => {
    // Provisioned purely to get the JUDGE realm role assigned before the organizer's own
    // "Register judges" flow runs against these emails -- see us6/us8/us11's identical note
    // (Features/Judges/SendInvitationHandler never assigns a realm role to a freshly-created
    // Keycloak user).
    const suffix = `${Date.now()}-${crypto.randomUUID()}`;
    judgeA = await createJudgeUser(`us12-judge-a-${suffix}@birrapoint.local`);
    judgeB = await createJudgeUser(`us12-judge-b-${suffix}@birrapoint.local`);
  });

  test.afterAll(async () => {
    if (judgeA?.id) {
      await deleteUser(judgeA.id);
    }
    if (judgeB?.id) {
      await deleteUser(judgeB.id);
    }
  });

  test('organizer removing a judge from a live table ejects that session instantly, 404s their next request, and keeps their submitted evaluation in the audit trail', async ({
    page,
    browser,
  }) => {
    // Full organizer setup + one real Keycloak-authenticated judge session + submit/draft/remove
    // cycles comfortably exceeds Playwright's 30s default.
    test.setTimeout(150_000);

    // --- Organizer: create competition, import entries, consolidate, register both judges, one
    // table with two samples assigned to both judges ---
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

    const firstBlindCode = await blindCodeForStyle(summary, FIRST_STYLE_CODE);
    const secondBlindCode = await blindCodeForStyle(summary, SECOND_STYLE_CODE);

    await page.goto(`/organizer/competitions/${competitionId}/judges`);
    await expect(page.getByRole('heading', { name: 'Judge management' })).toBeVisible();

    await page
      .getByLabel('Judge emails (one per line, or comma-separated)')
      .fill([judgeA.email, judgeB.email].join('\n'));
    await page.getByRole('button', { name: 'Register judges' }).click();

    const report = page.locator('div[aria-label="Registration report"]');
    await expect(report).toBeVisible();
    await expect(report.getByText(judgeA.email, { exact: true })).toBeVisible();
    await expect(report.getByText(judgeB.email, { exact: true })).toBeVisible();

    const judgeATempPassword = await readTemporaryPasswordFromInvitation(
      page.request,
      judgeA.email,
    );

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

    await pointerDrag(page, beerToken(page, firstBlindCode), mesa1Beers);
    await expect(beerToken(mesa1Beers, firstBlindCode)).toBeVisible();
    await pointerDrag(page, beerToken(page, secondBlindCode), mesa1Beers);
    await expect(beerToken(mesa1Beers, secondBlindCode)).toBeVisible();

    // Pre-login, each judge's displayName is the local part of their (unique) email --
    // JudgeResolver only backfills it to the Keycloak profile name ("Test Judge" for both, since
    // createJudgeUser hardcodes it) once a judge actually authenticates (us6/us11's note). Judge B
    // never logs in in this scenario, so its dashboard row keeps this name throughout -- used
    // below to prove the removal only affected judge A.
    const judgeADisplayName = judgeA.email.slice(0, judgeA.email.indexOf('@'));
    const judgeBDisplayName = judgeB.email.slice(0, judgeB.email.indexOf('@'));

    await pointerDrag(page, judgeSeat(page, judgeADisplayName), mesa1Judges);
    await expect(judgeSeat(mesa1Judges, judgeADisplayName)).toBeVisible();
    await pointerDrag(page, judgeSeat(page, judgeBDisplayName), mesa1Judges);
    await expect(judgeSeat(mesa1Judges, judgeBDisplayName)).toBeVisible();

    // --- Draft -> Active -> InEvaluation: POST .../evaluations requires InEvaluation specifically
    // (not just Active), per contracts/rest-api.md's Judge workspace table ---
    await page.goto('/organizer/dashboard');
    await advanceCompetitionState(page, competitionName, 'Activate');
    await expect(competitionRow(page, competitionName).locator('.badge')).toHaveText('Active');
    await advanceCompetitionState(page, competitionName, 'Start evaluation');
    await expect(competitionRow(page, competitionName).locator('.badge')).toHaveText(
      'InEvaluation',
    );

    // --- Judge A: real session. Submits the first sample for real, then leaves an in-progress
    // (unsubmitted) draft on the second -- that's the state they're in when the organizer removes
    // them below ---
    const contextA = await browser.newContext();
    const pageA = await contextA.newPage();

    try {
      await loginAsJudge(pageA, judgeA.email, judgeATempPassword);

      const tableLinkA = pageA.getByRole('link', { name: new RegExp('Mesa 1') });
      await expect(tableLinkA).toContainText('Order not fixed');
      await tableLinkA.click();
      await pageA.waitForURL(`**/judge/tables/${mesa1Id}`);

      await expect(pageA.locator('li.sample-row')).toHaveCount(2);

      // Fixing the order itself isn't what this scenario is testing (covered by
      // us6-order.spec.ts) -- fix as-is.
      await pageA.getByRole('button', { name: 'Fix order' }).click();
      const fixDialog = pageA.getByRole('alertdialog', { name: 'Confirm fix order' });
      await expect(fixDialog).toBeVisible();
      await fixDialog.getByRole('button', { name: 'Confirm fix order' }).click();
      await expect(pageA.locator('p.order-status--fixed')).toBeVisible();

      // --- Sample 1: fill and submit for real -- this is the evaluation that must survive the
      // removal in the organizer's audit drill-down ---
      const evaluateLinkFirst = pageA.locator('a.evaluate-action');
      await expect(evaluateLinkFirst).toHaveCount(1);
      await evaluateLinkFirst.click();

      await pageA.waitForURL(/\/judge\/tables\/[0-9a-fA-F-]+\/samples\/[0-9a-fA-F-]+$/);
      const evaluatedFirstBlindCode = (await pageA.locator('h1').innerText()).trim();
      expect([firstBlindCode, secondBlindCode]).toContain(evaluatedFirstBlindCode);

      await fillEvaluationForm(pageA, buildSections(JUDGE_A_FIRST_SCORES, 'Judge A first'));

      const [submitResponseA] = await Promise.all([
        pageA.waitForResponse(
          (response) =>
            response.request().method() === 'POST' &&
            /\/api\/v1\/me\/tables\/.+\/evaluations$/.test(new URL(response.url()).pathname),
        ),
        pageA.getByRole('button', { name: 'Submit evaluation' }).click(),
      ]);
      expect(submitResponseA.status()).toBe(201);
      const submitBodyA = (await submitResponseA.json()) as SubmitEvaluationResponseBody;
      expect(submitBodyA.total).toBe(JUDGE_A_FIRST_TOTAL);
      expect(submitBodyA.status).toBe('Confirmed');

      await pageA.waitForURL(`**/judge/tables/${mesa1Id}`);

      // --- Sample 2: fill but never submit -- the in-progress draft at removal time. This is
      // deliberately left on the *evaluation sheet* (not navigated back to the table view), so
      // the removal below is detected and handled by evaluation-sheet.component.ts's own
      // JudgeRemoved listener, not judge-table-order.component.ts's ---
      const evaluateLinkSecond = pageA.locator('a.evaluate-action');
      await expect(evaluateLinkSecond).toHaveCount(1);
      await evaluateLinkSecond.click();

      await pageA.waitForURL(/\/judge\/tables\/[0-9a-fA-F-]+\/samples\/[0-9a-fA-F-]+$/);
      const evaluatedSecondBlindCode = (await pageA.locator('h1').innerText()).trim();
      expect(evaluatedSecondBlindCode).not.toBe(evaluatedFirstBlindCode);
      expect([firstBlindCode, secondBlindCode]).toContain(evaluatedSecondBlindCode);

      await fillEvaluationForm(pageA, buildSections(JUDGE_A_SECOND_SCORES, 'Judge A second'));

      // Past the 300ms draft debounce (SC-003/FR-026, us7-offline.spec.ts's idiom) so the
      // in-progress draft has actually reached Dexie before the removal below.
      await pageA.waitForTimeout(500);

      // --- Organizer: open the live monitoring dashboard, resolve judge A's real Judge.Id off its
      // own GET /tables response ---
      const judgeAId = await loadMonitorAndFindJudgeId(page, competitionId, mesa1Id, judgeA.email);
      await expect(dashboardJudgeRow(page, mesa1Id, judgeAId)).toBeVisible();

      // --- The actual removal: a real UI action (Remove -> confirm) while judge A's session is
      // still open mid-draft on sample 2. Captures two effects of that one action: the DELETE
      // itself (200), and judge A's own live-JudgeRemoved-triggered re-check of table membership
      // (GET .../samples -- 404, FR-039's "subsequent request 404s") ---
      const [deleteResponse, samplesRecheckResponse] = await Promise.all([
        page.waitForResponse(
          (response) =>
            response.request().method() === 'DELETE' &&
            /\/api\/v1\/competitions\/.+\/tables\/.+\/judges\/.+$/.test(
              new URL(response.url()).pathname,
            ),
        ),
        pageA.waitForResponse(
          (response) =>
            response.request().method() === 'GET' &&
            new RegExp(`/api/v1/me/tables/${mesa1Id}/samples$`).test(
              new URL(response.url()).pathname,
            ),
          { timeout: 5_000 },
        ),
        removeJudgeViaDashboard(page, mesa1Id, judgeAId),
      ]);
      expect(deleteResponse.status()).toBe(200);
      expect(samplesRecheckResponse.status()).toBe(404);

      // --- Judge A's live session ejects within the realtime propagation budget: no manual
      // reload/navigation, lands on /judge/tables with a banner naming the actual table (not the
      // generic "a table" fallback). A slightly looser bound than the pure ≤1s hub-propagation
      // budget (FR-021/FR-037's convention elsewhere) because this path also does one extra HTTP
      // round trip (the membership re-check above) before navigating -- still tight enough to
      // catch a real regression, not generous enough to mask one. ---
      await pageA.waitForURL('**/judge/tables', { timeout: 3_000 });
      const ejectionBanner = pageA.locator('p.ejection-banner');
      await expect(ejectionBanner).toBeVisible();
      await expect(ejectionBanner).toContainText('You were removed from Mesa 1 by the organizer.');

      // --- Organizer's dashboard: judge A's row is gone, judge B's stays -- the removal only
      // affected the targeted judge, and required no refetch to reflect (T087's optimistic
      // client-side update on a successful DELETE) ---
      await expect(dashboardJudgeRow(page, mesa1Id, judgeAId)).toHaveCount(0);
      await expect(tableProgressRow(page, mesa1Id)).toContainText(judgeBDisplayName);

      // --- Judge A's already-submitted first evaluation stays in the organizer's audit
      // drill-down, untouched by the removal ---
      await page.getByRole('button', { name: evaluatedFirstBlindCode, exact: true }).click();

      const drillDown = page.locator('section.drill-down');
      await expect(drillDown).toBeVisible();
      await expect(
        drillDown.getByRole('heading', { level: 2, name: evaluatedFirstBlindCode }),
      ).toBeVisible();
      await expect(drillDown).toContainText(`Total: ${JUDGE_A_FIRST_TOTAL}`);
      await expect(drillDown).toContainText(
        'Judge A first flavor note, long enough to satisfy the minimum comment length rule.',
      );
    } finally {
      await contextA.close();
    }
  });
});
