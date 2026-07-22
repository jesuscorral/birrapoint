import path from 'node:path';
import { test, expect, Page, Locator } from '@playwright/test';
import { createJudgeUser, deleteUser, ProvisionedJudge } from './support/keycloak-admin';

// quickstart.md scenario 7 / spec.md US7 (FR-022/FR-023/FR-025/FR-026/FR-027, SC-003): a judge
// fills the five-section evaluation sheet, loses connectivity mid-sheet, restarts, and regains
// connectivity — the draft must survive the restart and exactly one evaluation must reach the
// server once the outbox replays.
//
// Harness note on "restart page" (read before touching the offline dance below): a literal
// Playwright `page.reload()` while `context.setOffline(true)` is in effect empirically does NOT
// work against this suite's dev-mode server. Verified directly: `npm start` (ng serve,
// what playwright.config.ts's webServer runs) answers every request — including `GET /` — with
// `Cache-Control: no-cache`, and the app's own service worker is deliberately disabled outside
// production builds (`provideServiceWorker(..., { enabled: !isDevMode() })`,
// frontend/src/app/app.config.ts) — neither gives Chromium anything to serve the document from
// once the network is actually cut, so `page.reload()` there throws
// `net::ERR_INTERNET_DISCONNECTED` unconditionally, regardless of anything the app itself does.
// That's a property of running E2E against the dev server (shared by every other spec in this
// suite), not something this task's scope can or should fix by changing the global Playwright
// webServer to a production build.
//
// The achievable, still-meaningful equivalent inside this harness is `page.goBack()` then
// `page.goForward()`: pure client-side (History API/`popstate`) navigation that Angular's Router
// handles entirely in-document (no network request at all, so it isn't blocked by
// `context.setOffline`), and — critically — genuinely destroys and reconstructs
// EvaluationSheetComponent (the table-order view, a different route/component, sits in
// between), exercising exactly the code path a real restart would: the constructor re-running
// `SyncService.loadDraft()` against Dexie. That's the part of "restart while offline, draft
// intact" this spec can actually prove in-process; a true cold full-reload-while-offline proof
// would need a production build with the service worker enabled, which is out of this task's
// scope (see final report).
//
// Defect found and fixed while wiring this up (small, targeted, in evaluation-sheet.component.ts
// only): EvaluationSheetComponent.loadSample() had no offline fallback at all. Rendering the form
// (and the style panel, and the "already evaluated" notice) is gated on a live GET
// /me/tables/{tableId}/samples succeeding on every mount — including the `goBack()`/`goForward()`
// "restart" below, still offline. Without a fix, that fetch fails (status 0), `loadError()` gets
// set, and the template's `@if (!loadError() && sample(); ...)` never renders the form at all —
// only a network-error alert — even though the Dexie-backed draft itself loaded correctly
// underneath. See the fix's own comment in evaluation-sheet.component.ts for the exact change
// (cache the last successfully-fetched sample per beerEntryId, fall back to it only on a genuine
// connectivity failure).
const KEYCLOAK_ORIGIN = 'http://localhost:8081';
const ORGANIZER_USERNAME = 'organizer';
const ORGANIZER_PASSWORD = 'organizer';

const MAILPIT_ORIGIN = 'http://localhost:8025';
const MAILPIT_POLL_TIMEOUT_MS = 10_000;
const MAILPIT_POLL_INTERVAL_MS = 500;

const FIXTURE_PATH = path.resolve(__dirname, 'fixtures/tables-assignment.xlsx');

// Same fixture/styles as us6-order.spec.ts's "neutral" pair: two distinct-style entries whose
// owning participants don't match this spec's freshly-provisioned judge email, so there's no
// COI/BOS interaction to account for — this spec's only concern is the offline dance on one of
// the two samples.
const STYLE_CODE_A = '1A';
const STYLE_CODE_B = '20C';

const EVALUATION_SECTIONS = [
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

async function submitKeycloakLogin(page: Page, username: string, password: string): Promise<void> {
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.locator('#kc-login').click();
}

function uniqueCompetitionName(): string {
  return `E2E Offline Comp ${Date.now()}-${crypto.randomUUID()}`;
}

// Mirrors us5/us6/us13's createCompetition: creates via the wizard's Basics step and returns the
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

// Mirrors us5/us6's blindCodeForStyle: reads the Consolidation summary's "Created entries" table.
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

// Mirrors us5/us6's pointerDrag: Angular CDK drag-drop is pointer-event-based, not native HTML5
// DnD, so a manual mouse-move sequence is required to trigger it reliably.
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

// Mirrors us13-dashboard.spec.ts's advanceCompetitionState: drives the real dashboard
// advance-state control (T102/FR-051) rather than a token-capture workaround.
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

// Mirrors us4/us6's waitForMailpitMessageTo: invitation delivery is async via a DispatchJob, so it
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

// Mirrors us6-order.spec.ts's readTemporaryPasswordFromInvitation: SendInvitationHandler resets
// the invited judge's Keycloak password unconditionally on every call, including when the account
// was pre-provisioned (createJudgeUser below, purely to get the JUDGE realm role assigned) — so
// the only reliable current password is the one actually delivered via email.
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

// Mirrors us1/us6's forced-temporary-password-change flow, ending on /judge/tables.
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
  for (const section of EVALUATION_SECTIONS) {
    const fieldset = sectionFieldset(page, section.legend);
    await fieldset.getByLabel('Score').fill(String(section.score));
    await fieldset.getByLabel('Comment').fill(section.comment);
  }
}

async function expectEvaluationFormIntact(page: Page): Promise<void> {
  for (const section of EVALUATION_SECTIONS) {
    const fieldset = sectionFieldset(page, section.legend);
    await expect(fieldset.getByLabel('Score')).toHaveValue(String(section.score));
    await expect(fieldset.getByLabel('Comment')).toHaveValue(section.comment);
  }
}

test.describe('US7 — offline-first validated evaluation sheet', () => {
  let judge: ProvisionedJudge;

  test.beforeAll(async () => {
    // Provisioned purely to get the JUDGE realm role assigned before the organizer's own
    // "Register judges" flow runs against this same email — see us6-order.spec.ts's identical
    // note (Features/Judges/SendInvitationHandler never assigns a realm role to a freshly-created
    // Keycloak user).
    judge = await createJudgeUser(
      `us7-judge-${Date.now()}-${crypto.randomUUID()}@birrapoint.local`,
    );
  });

  test.afterAll(async () => {
    if (judge?.id) {
      await deleteUser(judge.id);
    }
  });

  test('drafts survive an offline restart and exactly one evaluation reaches the server on replay', async ({
    page,
    browser,
  }) => {
    // Full organizer setup + a real Keycloak-authenticated judge session + the offline/online
    // dance comfortably exceeds Playwright's 30s default.
    test.setTimeout(120_000);

    // --- Organizer: create competition, import 2 distinct-style entries, consolidate, register
    // the judge, create one table, assign the judge + both entries onto it ---
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

    const blindCodeA = await blindCodeForStyle(summary, STYLE_CODE_A);
    const blindCodeB = await blindCodeForStyle(summary, STYLE_CODE_B);

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

    await pointerDrag(page, beerToken(page, blindCodeA), mesa1Beers);
    await expect(beerToken(mesa1Beers, blindCodeA)).toBeVisible();

    await pointerDrag(page, beerToken(page, blindCodeB), mesa1Beers);
    await expect(beerToken(mesa1Beers, blindCodeB)).toBeVisible();

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

      await expect(judgePage.locator('li.sample-row')).toHaveCount(2);

      // Fixing the order itself (drag vs. keyboard, which sample ends up first) isn't what this
      // scenario is testing — reordering is already covered by us6-order.spec.ts. Fix as-is.
      await judgePage.getByRole('button', { name: 'Fix order' }).click();
      const confirmDialog = judgePage.getByRole('alertdialog', { name: 'Confirm fix order' });
      await expect(confirmDialog).toBeVisible();
      await confirmDialog.getByRole('button', { name: 'Confirm fix order' }).click();
      await expect(judgePage.locator('p.order-status--fixed')).toBeVisible();

      // Exactly one sample is reachable once the order is fixed (FR-022 strict sequencing) — the
      // other shows a Locked badge instead.
      const evaluateLink = judgePage.locator('a.evaluate-action');
      await expect(evaluateLink).toHaveCount(1);
      await expect(judgePage.locator('.badge--locked')).toHaveCount(1);
      await evaluateLink.click();

      await judgePage.waitForURL(/\/judge\/tables\/[0-9a-fA-F-]+\/samples\/[0-9a-fA-F-]+$/);
      const sheetUrl = judgePage.url();
      const evaluatedBlindCode = (await judgePage.locator('h1').innerText()).trim();
      expect([blindCodeA, blindCodeB]).toContain(evaluatedBlindCode);

      // --- Go offline BEFORE filling anything: the sheet must be usable offline from the start,
      // not merely resilient to a mid-fill disconnect ---
      await judgeContext.setOffline(true);

      const offlineBadge = judgePage.locator('p.offline-badge');
      await expect(offlineBadge).toBeVisible();
      await expect(offlineBadge).toContainText('Offline mode');

      await fillEvaluationForm(judgePage);
      await expectEvaluationFormIntact(judgePage);

      // Past the 300ms debounce (SC-003/FR-026) — a short, deterministic wait, not an arbitrary
      // long one — so the final consolidated draft (all 5 sections) has actually reached Dexie
      // before the "restart" below.
      await judgePage.waitForTimeout(500);

      // --- "Restart the page" while still offline (see the harness note at the top of this file
      // for why this is `goBack()`/`goForward()`, a same-document/no-network SPA navigation,
      // rather than a literal `page.reload()`) ---
      await judgePage.goBack();
      await judgePage.goForward();

      await expect(judgePage).toHaveURL(sheetUrl);
      await expect(judgePage.locator('p.offline-badge')).toBeVisible();
      await expect(judgePage.locator('h1')).toHaveText(evaluatedBlindCode);
      await expectEvaluationFormIntact(judgePage);

      // --- Submit while still offline: must be instant/durable, never hang on a network attempt
      // that can't complete ---
      const evaluationPostResponses: number[] = [];
      judgePage.on('response', (response) => {
        if (
          response.request().method() === 'POST' &&
          /\/api\/v1\/me\/tables\/.+\/evaluations$/.test(new URL(response.url()).pathname)
        ) {
          evaluationPostResponses.push(response.status());
        }
      });

      await judgePage.getByRole('button', { name: 'Submit evaluation' }).click();
      await judgePage.waitForURL(`**/judge/tables/${mesa1Id}`, { timeout: 10_000 });
      expect(evaluationPostResponses).toEqual([]); // offline: no network attempt at all yet

      // --- Regain connectivity: the `online` event triggers SyncService.replayOutbox() with no
      // user action (US7 AC5) ---
      await judgeContext.setOffline(false);

      await judgePage.waitForResponse(
        (response) =>
          response.request().method() === 'POST' &&
          /\/api\/v1\/me\/tables\/.+\/evaluations$/.test(new URL(response.url()).pathname),
        { timeout: 10_000 },
      );
      // Settle window for any unwanted duplicate replay attempt before asserting the final count.
      await judgePage.waitForTimeout(1000);

      // Exactly one evaluation reached the server for this (judge, sample) pair — the idempotency
      // guarantee (FR-029/R-07) — not zero, not more than one.
      expect(evaluationPostResponses).toEqual([201]);

      // Now genuinely online: a real full reload is legitimate here, and is what surfaces the
      // reconciled server state (this component never live-updates on evaluation submission).
      await judgePage.reload();
      await expect(judgePage.locator('li.sample-row')).toHaveCount(2);

      const evaluatedRow = judgePage
        .locator('li.sample-row')
        .filter({ hasText: evaluatedBlindCode });
      await expect(evaluatedRow.locator('.badge--done')).toHaveText('Submitted');

      // The other sample is now the next reachable one (FR-022 sequential progression).
      await expect(judgePage.locator('a.evaluate-action')).toHaveCount(1);
      await expect(judgePage.locator('.badge--locked')).toHaveCount(0);
    } finally {
      await judgeContext.close();
    }
  });
});
