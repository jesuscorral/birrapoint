import path from 'node:path';
import { test, expect, Page, Locator } from '@playwright/test';
import { createJudgeUser, deleteUser, ProvisionedJudge } from './support/keycloak-admin';

// quickstart.md scenario 9 / spec.md US9 (FR-037/FR-038): with the organizer dashboard's live
// monitoring view already open, a judge's evaluation submission updates that table's progress
// count within 1s with no page reload/navigation, and the organizer can open any evaluated
// sample from the dashboard to read every submitted evaluation in read-only mode (plain text,
// no form controls). Also incidentally proves the TableOrderFixed live note (FR-021's dashboard
// counterpart) since fixing order happens with the monitor page already open, for the same
// no-reload reason.

const KEYCLOAK_ORIGIN = 'http://localhost:8081';
const ORGANIZER_USERNAME = 'organizer';
const ORGANIZER_PASSWORD = 'organizer';

const MAILPIT_ORIGIN = 'http://localhost:8025';
const MAILPIT_POLL_TIMEOUT_MS = 10_000;
const MAILPIT_POLL_INTERVAL_MS = 500;

const FIXTURE_PATH = path.resolve(__dirname, 'fixtures/tables-assignment.xlsx');

// Same fixture as us6/us7/us8's "neutral"/"bos" entries: neither owning participant matches this
// spec's freshly-provisioned judge email, so there's no COI/BOS interaction to account for.
const NEUTRAL_STYLE_CODE = '1A';
const BOS_STYLE_CODE = '20C';

// createJudgeUser hardcodes firstName "Test"/lastName "Judge" (see support/keycloak-admin.ts) and
// JudgeResolver backfills Judge.DisplayName from the Keycloak token's `name` claim on first login
// -- deterministic across runs, so this is safe to assert exactly rather than just a pattern.
const JUDGE_DISPLAY_NAME = 'Test Judge';

const SECTIONS = [
  { legend: 'Aroma', score: 9, comment: 'Light citrus hop aroma with a faint malt sweetness.' },
  { legend: 'Appearance', score: 3, comment: 'Pale straw color, tight white head, good clarity.' },
  { legend: 'Flavor', score: 16, comment: 'Crisp bitterness balanced by a clean malt finish.' },
  {
    legend: 'Mouthfeel',
    score: 4,
    comment: 'Light-medium body with brisk carbonation throughout.',
  },
  {
    legend: 'Overall Impression',
    score: 7,
    comment: 'Solid, sessionable example of the style overall.',
  },
] as const;
const TOTAL = SECTIONS.reduce((sum, section) => sum + section.score, 0);

async function submitKeycloakLogin(page: Page, username: string, password: string): Promise<void> {
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.locator('#kc-login').click();
}

function uniqueCompetitionName(): string {
  return `E2E Dashboard Live Comp ${Date.now()}-${crypto.randomUUID()}`;
}

// Mirrors us6/us8's createCompetition: creates via the wizard's Basics step and returns the
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

// Mirrors us6/us8's blindCodeForStyle: reads the Consolidation summary's "Created entries" table.
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

// Mirrors us5/us6/us8's pointerDrag: Angular CDK drag-drop is pointer-event-based, not native
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

// Mirrors us8's advanceCompetitionState: drives the real dashboard advance-state control
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

// Mirrors us4/us6/us8's waitForMailpitMessageTo: invitation delivery is async via a DispatchJob,
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

// Mirrors us6/us8's readTemporaryPasswordFromInvitation: SendInvitationHandler resets the invited
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

// Mirrors us1/us6/us8's forced-temporary-password-change flow, ending on /judge/tables.
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
  for (const section of SECTIONS) {
    const fieldset = sectionFieldset(page, section.legend);
    await fieldset.getByLabel('Score').fill(String(section.score));
    await fieldset.getByLabel('Comment').fill(section.comment);
  }
}

test.describe('US9 — live organizer monitoring dashboard', () => {
  let judge: ProvisionedJudge;

  test.beforeAll(async () => {
    // Provisioned purely to get the JUDGE realm role assigned before the organizer's own
    // "Register judges" flow runs against this same email — see us6/us8's identical note
    // (Features/Judges/SendInvitationHandler never assigns a realm role to a freshly-created
    // Keycloak user).
    judge = await createJudgeUser(
      `us9-judge-${Date.now()}-${crypto.randomUUID()}@birrapoint.local`,
    );
  });

  test.afterAll(async () => {
    if (judge?.id) {
      await deleteUser(judge.id);
    }
  });

  test('dashboard reflects a submitted evaluation within 1s with no reload, and the drill-down shows it read-only', async ({
    page,
    browser,
  }) => {
    // Full organizer setup + a real Keycloak-authenticated judge session + two live-update
    // assertions comfortably exceeds Playwright's 30s default.
    test.setTimeout(120_000);

    // --- Organizer: create competition, import entries, consolidate, register the judge, create
    // one table, assign the judge + two entries onto it ---
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

    const neutralBlindCode = await blindCodeForStyle(summary, NEUTRAL_STYLE_CODE);
    const bosBlindCode = await blindCodeForStyle(summary, BOS_STYLE_CODE);

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

    await pointerDrag(page, beerToken(page, neutralBlindCode), mesa1Beers);
    await expect(beerToken(mesa1Beers, neutralBlindCode)).toBeVisible();
    await pointerDrag(page, beerToken(page, bosBlindCode), mesa1Beers);
    await expect(beerToken(mesa1Beers, bosBlindCode)).toBeVisible();

    const judgeDisplayNameSeat = judge.email.slice(0, judge.email.indexOf('@'));
    await pointerDrag(page, judgeSeat(page, judgeDisplayNameSeat), mesa1Judges);
    await expect(judgeSeat(mesa1Judges, judgeDisplayNameSeat)).toBeVisible();

    // --- Draft -> Active -> InEvaluation: POST .../evaluations requires InEvaluation specifically
    // (not just Active), per contracts/rest-api.md's Judge workspace table ---
    await page.goto('/organizer/dashboard');
    await advanceCompetitionState(page, competitionName, 'Activate');
    await expect(competitionRow(page, competitionName).locator('.badge')).toHaveText('Active');
    await advanceCompetitionState(page, competitionName, 'Start evaluation');
    await expect(competitionRow(page, competitionName).locator('.badge')).toHaveText(
      'InEvaluation',
    );

    // --- Judge: independent browser context/session, real forced-password-change login. Order is
    // deliberately NOT fixed yet -- the organizer's monitor page below must already be open and
    // hub-connected to observe the TableOrderFixed live note, since GetProgressQuery's initial
    // payload (backend/src/BirraPoint.Api/Features/Monitoring/GetProgress.cs) carries no
    // order-fixed information at all; that note is populated purely from the live hub event. ---
    const judgeContext = await browser.newContext();
    const judgePage = await judgeContext.newPage();

    try {
      await loginAsJudge(judgePage, judge.email, judgeTempPassword);

      const tableLink = judgePage.getByRole('link', { name: new RegExp('Mesa 1') });
      await expect(tableLink).toContainText('Order not fixed');
      await tableLink.click();
      await judgePage.waitForURL(`**/judge/tables/${mesa1Id}`);
      await expect(judgePage.locator('li.sample-row')).toHaveCount(2);

      // --- Organizer: navigate via the dashboard list (Acceptance Scenario for US13's routing,
      // reused here) rather than a direct goto, proving destination() really does route an
      // InEvaluation competition straight to the monitor screen ---
      await page.goto('/organizer/dashboard');
      await competitionRow(page, competitionName).locator('a.competition-list-item').click();
      await page.waitForURL(new RegExp(`/organizer/competitions/${competitionId}/monitor$`));

      const tableRow = page.locator(`li.table-progress-row[data-table-id="${mesa1Id}"]`);
      await expect(tableRow).toBeVisible();
      await expect(tableRow.locator('.table-progress-count')).toHaveText('0 / 2 (0%)');
      await expect(tableRow.locator('.badge')).toHaveClass(/badge--open/);
      await expect(tableRow.locator('.order-fixed-note')).toHaveCount(0);

      // --- Judge fixes the order with the organizer's monitor page already open ---
      await judgePage.getByRole('button', { name: 'Fix order' }).click();
      const fixDialog = judgePage.getByRole('alertdialog', { name: 'Confirm fix order' });
      await expect(fixDialog).toBeVisible();
      await fixDialog.getByRole('button', { name: 'Confirm fix order' }).click();
      await expect(judgePage.locator('p.order-status--fixed')).toBeVisible();

      // Live TableOrderFixed propagation on the already-open, non-reloaded organizer page —
      // Playwright's own auto-retry within the 1000ms timeout is the ≤1s proof (same technique
      // us6-order.spec.ts uses for the judge-to-judge propagation case).
      const orderFixedNote = tableRow.locator('.order-fixed-note');
      await expect(orderFixedNote).toBeVisible({ timeout: 1000 });
      await expect(orderFixedNote).toHaveText(`Order fixed by ${JUDGE_DISPLAY_NAME}.`);

      // --- Judge evaluates the first sample in fixed order and submits ---
      const evaluateLink = judgePage.locator('a.evaluate-action');
      await expect(evaluateLink).toHaveCount(1);
      await evaluateLink.click();

      await judgePage.waitForURL(/\/judge\/tables\/[0-9a-fA-F-]+\/samples\/[0-9a-fA-F-]+$/);
      const submittedBlindCode = (await judgePage.locator('h1').innerText()).trim();
      expect([neutralBlindCode, bosBlindCode]).toContain(submittedBlindCode);

      await fillEvaluationForm(judgePage);

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
      expect(submitBody.total).toBe(TOTAL);

      // --- FR-037: the organizer's already-open, non-reloaded monitor page reflects the completed
      // evaluation within 1s -- no page.reload()/goto() anywhere in this assertion path, that
      // would defeat the point of the test ---
      await expect(tableRow.locator('.table-progress-count')).toHaveText('1 / 2 (50%)', {
        timeout: 1000,
      });

      // --- FR-038 Acceptance Scenario 2: opening the evaluated sample from the dashboard shows its
      // submitted evaluation in read-only mode ---
      await tableRow.locator(`button[data-entry-id]`, { hasText: submittedBlindCode }).click();

      const drillDown = page.locator(`section[aria-label="Evaluations for ${submittedBlindCode}"]`);
      await expect(drillDown).toBeVisible();
      await expect(drillDown.locator('.consolidated-mean')).toHaveText(
        'Consolidated mean: not yet closed',
      );

      const auditArticle = drillDown.locator('article.evaluation-audit');
      await expect(auditArticle).toHaveCount(1);
      await expect(auditArticle.locator('h3')).toHaveText(JUDGE_DISPLAY_NAME);

      const dtToScoreComment: Record<string, { score: number; comment: string }> = {
        Aroma: { score: SECTIONS[0].score, comment: SECTIONS[0].comment },
        Appearance: { score: SECTIONS[1].score, comment: SECTIONS[1].comment },
        Flavor: { score: SECTIONS[2].score, comment: SECTIONS[2].comment },
        Mouthfeel: { score: SECTIONS[3].score, comment: SECTIONS[3].comment },
        Overall: { score: SECTIONS[4].score, comment: SECTIONS[4].comment },
      };
      for (const [dtLabel, { score, comment }] of Object.entries(dtToScoreComment)) {
        const dd = auditArticle
          .locator('dl')
          .locator('dt', { hasText: dtLabel })
          .locator('xpath=following-sibling::dd[1]');
        await expect(dd).toHaveText(`${score} — ${comment}`);
      }
      await expect(auditArticle.locator('.evaluation-total')).toHaveText(`Total: ${TOTAL}`);

      // Read-only proof: no form controls anywhere inside the drill-down section (FR-038).
      await expect(drillDown.locator('input, textarea')).toHaveCount(0);

      const closeDrillDownButton = drillDown.getByRole('button', { name: 'Close', exact: true });
      await closeDrillDownButton.click();
      await expect(drillDown).not.toBeVisible();
    } finally {
      await judgeContext.close();
    }
  });
});
