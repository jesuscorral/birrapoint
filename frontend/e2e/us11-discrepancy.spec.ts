import path from 'node:path';
import { test, expect, Page, Locator } from '@playwright/test';
import { createJudgeUser, deleteUser, ProvisionedJudge } from './support/keycloak-admin';

// quickstart.md scenario 11 / spec.md US11 (FR-031/FR-032): two judges score the same sample 15
// points apart -> the second submission comes back `PendingConsensus` with a `discrepancy` payload
// and both judges' sessions surface the alert; closing the table is blocked (`409
// discrepancy-open`) until an adjustment brings the totals back within 7 points, after which the
// table close that was blocked succeeds.
//
// Single-sample table, both judges assigned to the SAME beer entry -- the point of this scenario
// is two judges diverging on one sample, not sequencing multiple (already covered by
// us6-order.spec.ts / us8-close.spec.ts).

const KEYCLOAK_ORIGIN = 'http://localhost:8081';
const ORGANIZER_USERNAME = 'organizer';
const ORGANIZER_PASSWORD = 'organizer';
const API_BASE_URL = 'http://localhost:5121';

const MAILPIT_ORIGIN = 'http://localhost:8025';
const MAILPIT_POLL_TIMEOUT_MS = 10_000;
const MAILPIT_POLL_INTERVAL_MS = 500;

const FIXTURE_PATH = path.resolve(__dirname, 'fixtures/tables-assignment.xlsx');

// Same fixture as us6/us8's "neutral" entry: a style whose owning participant doesn't match either
// freshly-provisioned judge's @birrapoint.local email, so there's no COI/BOS interaction to
// account for here.
const NEUTRAL_STYLE_CODE = '1A';

interface ScoreSet {
  aroma: number;
  appearance: number;
  flavor: number;
  mouthfeel: number;
  overall: number;
}

// Section caps (Aroma 12 / Appearance 3 / Flavor 20 / Mouthfeel 5 / Overall 10, FR-023) — all
// three sets below respect them.
const JUDGE_A_SCORES: ScoreSet = { aroma: 2, appearance: 1, flavor: 10, mouthfeel: 2, overall: 5 };
const JUDGE_A_TOTAL = Object.values(JUDGE_A_SCORES).reduce((sum, score) => sum + score, 0); // 20

// Exactly 15 points apart from JUDGE_A_TOTAL, matching quickstart scenario 11's wording verbatim
// and comfortably over the >7 discrepancy threshold (DiscrepancyRules.Threshold).
const JUDGE_B_SCORES: ScoreSet = { aroma: 8, appearance: 2, flavor: 16, mouthfeel: 4, overall: 5 };
const JUDGE_B_TOTAL = Object.values(JUDGE_B_SCORES).reduce((sum, score) => sum + score, 0); // 35
expect(JUDGE_B_TOTAL - JUDGE_A_TOTAL).toBe(15);

// Adjustment: brings judge B's total within 7 points of judge A's 20 (diff = 5), resolving the
// alert.
const JUDGE_B_ADJUSTED_SCORES: ScoreSet = {
  aroma: 4,
  appearance: 2,
  flavor: 12,
  mouthfeel: 3,
  overall: 4,
};
const JUDGE_B_ADJUSTED_TOTAL = Object.values(JUDGE_B_ADJUSTED_SCORES).reduce(
  (sum, score) => sum + score,
  0,
); // 25
expect(Math.abs(JUDGE_B_ADJUSTED_TOTAL - JUDGE_A_TOTAL)).toBeLessThanOrEqual(7);

interface SectionInput {
  legend: string;
  score: number;
  comment: string;
}

// Shared legend labels between evaluation-sheet.component.ts's SECTIONS and
// discrepancy-alert.component.ts's SECTIONS (identical labels, both forms use the same
// fieldset/legend/label markup) -- one builder covers both the initial submit and the adjustment.
function buildSections(scores: ScoreSet, tag: string): SectionInput[] {
  return [
    { legend: 'Aroma', score: scores.aroma, comment: `${tag} aroma note, long enough to satisfy the minimum comment length rule.` },
    { legend: 'Appearance', score: scores.appearance, comment: `${tag} appearance note, long enough to satisfy the minimum length rule.` },
    { legend: 'Flavor', score: scores.flavor, comment: `${tag} flavor note, long enough to satisfy the minimum comment length rule.` },
    { legend: 'Mouthfeel', score: scores.mouthfeel, comment: `${tag} mouthfeel note, long enough to satisfy the minimum length rule.` },
    { legend: 'Overall Impression', score: scores.overall, comment: `${tag} overall note, long enough to satisfy the minimum length rule.` },
  ];
}

interface DiscrepancyTotalBody {
  judgeDisplayName: string;
  total: number;
  isMine: boolean;
  evaluationId: string;
}

interface DiscrepancyViewBody {
  alertId: string;
  blindCode: string;
  totals: DiscrepancyTotalBody[];
}

interface SubmitEvaluationResponseBody {
  evaluationId: string;
  status: 'Confirmed' | 'PendingConsensus';
  total: number;
  discrepancy: DiscrepancyViewBody | null;
}

interface ProblemDetailsBody {
  type: string;
}

async function submitKeycloakLogin(page: Page, username: string, password: string): Promise<void> {
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.locator('#kc-login').click();
}

function uniqueCompetitionName(): string {
  return `E2E Discrepancy Comp ${Date.now()}-${crypto.randomUUID()}`;
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

// Mirrors us8-close.spec.ts's advanceCompetitionState: drives the real dashboard advance-state
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

// Mirrors us6/us8's waitForMailpitMessageTo: invitation delivery is async via a DispatchJob, so it
// does not land synchronously after POST /judges.
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

// Mirrors us8-close.spec.ts's sectionFieldset -- also used against
// discrepancy-alert.component.ts's adjustment form, which shares the exact same
// fieldset.evaluation-section/legend/label markup as evaluation-sheet.component.ts.
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

test.describe('US11 — discrepancy consensus', () => {
  let judgeA: ProvisionedJudge;
  let judgeB: ProvisionedJudge;

  test.beforeAll(async () => {
    // Provisioned purely to get the JUDGE realm role assigned before the organizer's own
    // "Register judges" flow runs against these emails -- see us6/us8's identical note
    // (Features/Judges/SendInvitationHandler never assigns a realm role to a freshly-created
    // Keycloak user).
    const suffix = `${Date.now()}-${crypto.randomUUID()}`;
    judgeA = await createJudgeUser(`us11-judge-a-${suffix}@birrapoint.local`);
    judgeB = await createJudgeUser(`us11-judge-b-${suffix}@birrapoint.local`);
  });

  test.afterAll(async () => {
    if (judgeA?.id) {
      await deleteUser(judgeA.id);
    }
    if (judgeB?.id) {
      await deleteUser(judgeB.id);
    }
  });

  test('two judges 15 points apart on one sample raise PendingConsensus, block close, and resolve within 7 points', async ({
    page,
    browser,
  }) => {
    // Full organizer setup + two real Keycloak-authenticated judge sessions + submit/adjust/close
    // cycles comfortably exceeds Playwright's 30s default.
    test.setTimeout(150_000);

    // --- Organizer: create competition, import entries, consolidate, register both judges, one
    // table with a single entry assigned to BOTH judges ---
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

    await page
      .getByLabel('Judge emails (one per line, or comma-separated)')
      .fill([judgeA.email, judgeB.email].join('\n'));
    await page.getByRole('button', { name: 'Register judges' }).click();

    const report = page.locator('div[aria-label="Registration report"]');
    await expect(report).toBeVisible();
    await expect(report.getByText(judgeA.email, { exact: true })).toBeVisible();
    await expect(report.getByText(judgeB.email, { exact: true })).toBeVisible();

    const judgeATempPassword = await readTemporaryPasswordFromInvitation(page.request, judgeA.email);
    const judgeBTempPassword = await readTemporaryPasswordFromInvitation(page.request, judgeB.email);

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

    // --- Two independent judge sessions (separate browser contexts -> separate Keycloak SSO
    // sessions), both assigned to the same single-sample table ---
    const contextA = await browser.newContext();
    const contextB = await browser.newContext();
    const pageA = await contextA.newPage();
    const pageB = await contextB.newPage();

    try {
      await loginAsJudge(pageA, judgeA.email, judgeATempPassword);

      const tableLinkA = pageA.getByRole('link', { name: new RegExp('Mesa 1') });
      await expect(tableLinkA).toContainText('Order not fixed');
      await tableLinkA.click();
      await pageA.waitForURL(`**/judge/tables/${mesa1Id}`);

      await expect(pageA.locator('li.sample-row')).toHaveCount(1);

      // Fixing the order itself isn't what this scenario is testing (covered by us6-order.spec.ts)
      // -- a single-sample table still requires the fix-order step (FR-022 precondition).
      await pageA.getByRole('button', { name: 'Fix order' }).click();
      const fixDialog = pageA.getByRole('alertdialog', { name: 'Confirm fix order' });
      await expect(fixDialog).toBeVisible();
      await fixDialog.getByRole('button', { name: 'Confirm fix order' }).click();
      await expect(pageA.locator('p.order-status--fixed')).toBeVisible();

      // --- Judge A submits first: nothing to compare against yet -> Confirmed, discrepancy null ---
      const evaluateLinkA = pageA.locator('a.evaluate-action');
      await expect(evaluateLinkA).toHaveCount(1);
      await evaluateLinkA.click();

      await pageA.waitForURL(/\/judge\/tables\/[0-9a-fA-F-]+\/samples\/[0-9a-fA-F-]+$/);
      await expect(pageA.locator('h1')).toHaveText(blindCode);

      await fillEvaluationForm(pageA, buildSections(JUDGE_A_SCORES, 'Judge A'));

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
      expect(submitBodyA.total).toBe(JUDGE_A_TOTAL);
      expect(submitBodyA.status).toBe('Confirmed');
      expect(submitBodyA.discrepancy).toBeNull();
      const evaluationIdA = submitBodyA.evaluationId;

      await pageA.waitForURL(`**/judge/tables/${mesa1Id}`);

      // --- Judge B: separate session, order already fixed by A ---
      await loginAsJudge(pageB, judgeB.email, judgeBTempPassword);

      const tableLinkB = pageB.getByRole('link', { name: new RegExp('Mesa 1') });
      await expect(tableLinkB).toContainText('Order fixed');
      await tableLinkB.click();
      await pageB.waitForURL(`**/judge/tables/${mesa1Id}`);

      await expect(pageB.locator('p.order-status--fixed')).toBeVisible();
      await expect(pageB.locator('.drag-handle')).toHaveCount(0);
      await expect(pageB.getByRole('button', { name: 'Fix order' })).toHaveCount(0);

      const evaluateLinkB = pageB.locator('a.evaluate-action');
      await expect(evaluateLinkB).toHaveCount(1);
      await evaluateLinkB.click();

      await pageB.waitForURL(/\/judge\/tables\/[0-9a-fA-F-]+\/samples\/[0-9a-fA-F-]+$/);
      await expect(pageB.locator('h1')).toHaveText(blindCode);

      await fillEvaluationForm(pageB, buildSections(JUDGE_B_SCORES, 'Judge B'));

      // --- Judge B submits 15 points apart -> PendingConsensus + discrepancy with both totals ---
      const [submitResponseB] = await Promise.all([
        pageB.waitForResponse(
          (response) =>
            response.request().method() === 'POST' &&
            /\/api\/v1\/me\/tables\/.+\/evaluations$/.test(new URL(response.url()).pathname),
        ),
        pageB.getByRole('button', { name: 'Submit evaluation' }).click(),
      ]);
      expect(submitResponseB.status()).toBe(201);
      const submitBodyB = (await submitResponseB.json()) as SubmitEvaluationResponseBody;
      expect(submitBodyB.total).toBe(JUDGE_B_TOTAL);
      expect(submitBodyB.status).toBe('PendingConsensus');
      expect(submitBodyB.discrepancy).not.toBeNull();
      const discrepancyB = submitBodyB.discrepancy!;
      expect(discrepancyB.blindCode).toBe(blindCode);
      expect(discrepancyB.totals).toHaveLength(2);
      const mineTotal = discrepancyB.totals.find((total) => total.isMine);
      const otherTotal = discrepancyB.totals.find((total) => !total.isMine);
      expect(mineTotal?.total).toBe(JUDGE_B_TOTAL);
      expect(otherTotal?.total).toBe(JUDGE_A_TOTAL);
      expect(otherTotal?.evaluationId).toBe(evaluationIdA);
      const alertId = discrepancyB.alertId;

      await pageB.waitForURL(`**/judge/tables/${mesa1Id}`);

      // --- "alert both sessions": judge B's freshly-reloaded table page shows the banner
      // immediately; judge A's still-open session must pick it up live within the realtime
      // propagation budget, with no manual reload/navigation ---
      const discrepancyBannerB = pageB.locator('p.discrepancy-banner');
      await expect(discrepancyBannerB).toBeVisible();
      await expect(discrepancyBannerB).toContainText('1 open discrepancy alert');

      const discrepancyBannerA = pageA.locator('p.discrepancy-banner');
      await expect(discrepancyBannerA).toBeVisible({ timeout: 1000 });
      await expect(discrepancyBannerA).toContainText('1 open discrepancy alert');

      // --- Judge A navigates into the discrepancy page and sees both totals, own row marked ---
      await discrepancyBannerA.getByRole('link', { name: 'Resolve now' }).click();
      await pageA.waitForURL(`**/judge/tables/${mesa1Id}/discrepancies`);

      const openAlertCardsA = pageA.locator('div.alert-card:not(.alert-card--resolved)');
      await expect(openAlertCardsA).toHaveCount(1);
      await expect(openAlertCardsA.locator('h2')).toHaveText(blindCode);

      const totalsRowsA = openAlertCardsA.locator('table.totals-table tbody tr');
      await expect(totalsRowsA).toHaveCount(2);
      await expect(totalsRowsA.filter({ hasText: String(JUDGE_A_TOTAL) })).toContainText('(you)');
      await expect(
        totalsRowsA.filter({ hasText: String(JUDGE_B_TOTAL) }),
      ).not.toContainText('(you)');

      // --- Attempt to close the table (from judge A): blocked with 409 discrepancy-open, UI
      // surfaces the blind code and a link to resolve ---
      await pageA.goto(`/judge/tables/${mesa1Id}`);
      const closeButtonA = pageA.getByRole('button', { name: 'Close table' });
      await expect(closeButtonA).toBeVisible();
      await closeButtonA.click();

      const closeDialogA = pageA.getByRole('alertdialog', { name: 'Confirm close table' });
      await expect(closeDialogA).toBeVisible();

      const [blockedCloseResponse] = await Promise.all([
        pageA.waitForResponse(
          (response) =>
            response.request().method() === 'POST' &&
            /\/api\/v1\/me\/tables\/.+\/close$/.test(new URL(response.url()).pathname),
        ),
        closeDialogA.getByRole('button', { name: 'Confirm close table' }).click(),
      ]);
      expect(blockedCloseResponse.status()).toBe(409);
      const blockedCloseProblem = (await blockedCloseResponse.json()) as ProblemDetailsBody;
      expect(blockedCloseProblem.type).toBe('urn:birrapoint:discrepancy-open');

      const closeErrorA = pageA.locator('[role="alert"]');
      await expect(closeErrorA).toContainText('Unresolved discrepancies');
      await expect(closeErrorA).toContainText(blindCode);
      await expect(
        closeErrorA.getByRole('link', { name: 'Resolve discrepancies' }),
      ).toBeVisible();

      // --- Resolve: judge B adjusts their evaluation to within 7 points of judge A's total ---
      await pageB.goto(`/judge/tables/${mesa1Id}/discrepancies`);

      const openAlertCardsB = pageB.locator('div.alert-card:not(.alert-card--resolved)');
      await expect(openAlertCardsB).toHaveCount(1);

      await openAlertCardsB.getByRole('button', { name: 'Adjust my evaluation' }).click();
      await fillEvaluationForm(pageB, buildSections(JUDGE_B_ADJUSTED_SCORES, 'Judge B adjusted'));

      const submitAdjustmentButtonB = openAlertCardsB.getByRole('button', {
        name: 'Submit adjustment',
      });
      await expect(submitAdjustmentButtonB).toBeEnabled();

      const [adjustResponse] = await Promise.all([
        pageB.waitForResponse(
          (response) =>
            response.request().method() === 'PUT' &&
            /\/api\/v1\/me\/tables\/.+\/evaluations\/[0-9a-fA-F-]+$/.test(
              new URL(response.url()).pathname,
            ),
        ),
        submitAdjustmentButtonB.click(),
      ]);
      expect(adjustResponse.status()).toBe(200);
      const adjustBody = (await adjustResponse.json()) as SubmitEvaluationResponseBody;
      expect(adjustBody.status).toBe('Confirmed');
      expect(adjustBody.total).toBe(JUDGE_B_ADJUSTED_TOTAL);
      expect(adjustBody.discrepancy).toBeNull();

      // Client-side resolved confirmation, driven purely by the PUT response (no reload).
      const resolvedCardB = pageB.locator('div.alert-card--resolved');
      await expect(resolvedCardB).toBeVisible();
      await expect(resolvedCardB.locator('h2')).toHaveText(blindCode);
      await expect(resolvedCardB).toContainText('Resolved — your evaluation is confirmed.');
      await expect(pageB.locator('div.alert-card:not(.alert-card--resolved)')).toHaveCount(0);

      // --- Judge A's still-open table-page session reflects the resolution live, no reload ---
      await expect(discrepancyBannerA).toHaveCount(0, { timeout: 1000 });

      // --- Close the table again: now succeeds ---
      const closeButtonA2 = pageA.getByRole('button', { name: 'Close table' });
      await expect(closeButtonA2).toBeVisible();
      await closeButtonA2.click();

      const closeDialogA2 = pageA.getByRole('alertdialog', { name: 'Confirm close table' });
      await expect(closeDialogA2).toBeVisible();

      const [finalCloseResponse] = await Promise.all([
        pageA.waitForResponse(
          (response) =>
            response.request().method() === 'POST' &&
            /\/api\/v1\/me\/tables\/.+\/close$/.test(new URL(response.url()).pathname),
        ),
        closeDialogA2.getByRole('button', { name: 'Confirm close table' }).click(),
      ]);
      expect(finalCloseResponse.status()).toBe(200);

      await expect(pageA.locator('[role="alert"]')).toHaveCount(0);
      const closedBannerA = pageA.locator('p.order-status--closed');
      await expect(closedBannerA).toBeVisible();
      await expect(closedBannerA).toContainText('Table closed');
      await expect(pageA.getByRole('button', { name: 'Close table' })).toHaveCount(0);

      // Discrepancy alert id observed throughout the scenario stays a single, consistent alert.
      expect(alertId).toBeTruthy();
    } finally {
      await contextA.close();
      await contextB.close();
    }
  });
});
