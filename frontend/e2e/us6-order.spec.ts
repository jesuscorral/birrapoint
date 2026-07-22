import path from 'node:path';
import { test, expect, Page, Locator } from '@playwright/test';
import { createJudgeUser, deleteUser, ProvisionedJudge } from './support/keycloak-admin';

// quickstart.md scenario 6 / spec.md US6 (FR-020/FR-021): two judge browser sessions on the same
// table; fixing the order in one session must reorder + lock the other session within 1 s, with
// no manual reload/navigation. The scenario's second half — "sheets openable only in fixed
// sequence, only after order fixed and state InEvaluation" — is evaluation-sheet territory (US7,
// frontend/src/app/features/evaluation-sheet/), which doesn't exist on this branch yet. That part
// is deferred to T061 (frontend/e2e/us7-offline.spec.ts) once the sheet lands, not silently
// dropped.

const KEYCLOAK_ORIGIN = 'http://localhost:8081';
const ORGANIZER_USERNAME = 'organizer';
const ORGANIZER_PASSWORD = 'organizer';

const MAILPIT_ORIGIN = 'http://localhost:8025';
const MAILPIT_POLL_TIMEOUT_MS = 10_000;
const MAILPIT_POLL_INTERVAL_MS = 500;

const FIXTURE_PATH = path.resolve(__dirname, 'fixtures/tables-assignment.xlsx');

// Reused from the fixture's rows (frontend/e2e/fixtures/tables-assignment.xlsx, see
// us5-tables.spec.ts) purely to get two distinct-style beer entries into a table — this spec has
// no COI/BOS concern of its own, since the two judges provisioned below use fresh
// @birrapoint.local emails that don't match any fixture participant.
const NEUTRAL_ENTRY_NAME = 'Neutral Beer';
const NEUTRAL_PARTICIPANT_NAME = 'Neutral Participant';
const NEUTRAL_PARTICIPANT_EMAIL = 'neutral.participant@brew.example';
const BOS_ENTRY_NAME = 'BOS Beer';
const BOS_PARTICIPANT_NAME = 'Bos Participant';
const BOS_PARTICIPANT_EMAIL = 'bos.participant@brew.example';

async function submitKeycloakLogin(page: Page, username: string, password: string): Promise<void> {
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.locator('#kc-login').click();
}

function uniqueCompetitionName(): string {
  return `E2E Order Comp ${Date.now()}-${crypto.randomUUID()}`;
}

// Mirrors us5-tables.spec.ts's createCompetition: creates via the wizard's Basics step and
// returns the persisted competitionId.
async function createCompetition(page: Page): Promise<string> {
  await page.goto('/organizer/competitions/new');

  await page.getByLabel('Name').fill(uniqueCompetitionName());
  await page.getByLabel('Venue').fill('Salón de Actos, Madrid');
  await page.getByLabel('Start date').fill('2026-09-01');
  await page.getByLabel('End date').fill('2026-09-03');

  await page.getByRole('button', { name: 'Next' }).click();
  await page.waitForURL(/\/organizer\/competitions\/[0-9a-fA-F-]{36}$/);

  return page.url().split('/').pop()!;
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

// Mirrors us4-judges.spec.ts's waitForMailpitMessageTo: invitation delivery is async via a
// DispatchJob (SendInvitationHandler), so it does not land synchronously after POST /judges.
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

// SendInvitationHandler (Features/Judges) resets the invited judge's Keycloak password to a fresh
// one on every call, unconditionally -- including when the Keycloak account already exists (e.g.
// pre-provisioned by createJudgeUser below, purely to get the JUDGE realm role assigned; see the
// defect note near its call site). That makes the ProvisionedJudge.tempPassword captured at
// provisioning time stale the instant "Register judges" runs: the only reliable current password
// is the one actually delivered, so this reads it back out of the invitation email exactly as a
// real judge would have to.
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

// Mirrors us5-tables.spec.ts's blindCodeForStyle.
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

// Mirrors us5-tables.spec.ts's pointerDrag: Angular CDK drag-drop is pointer-event-based, not
// native HTML5 DnD, so a manual mouse-move sequence is required to trigger it reliably.
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

// GET /me/tables hides Draft competitions (FR-020) and POST .../order requires Active/InEvaluation,
// so the organizer must advance the just-created competition out of Draft before the two judge
// sessions below can reach the table's order view. T102 shipped the real advance-state control on
// the organizer dashboard (FR-051) — this goes through it exactly as an organizer would: navigate
// to the dashboard, click the row's "Activate" action, confirm the resulting alertdialog. Mirrors
// us13-dashboard.spec.ts's own advanceCompetitionState helper; kept separate (rather than a shared
// support module) since this one only ever performs the single Draft -> Active transition and
// locates the row by the competition id (via its still-Draft href) rather than by name, since this
// spec's own createCompetition doesn't return the generated name.
async function activateCompetition(page: Page, competitionId: string): Promise<void> {
  await page.goto('/organizer/dashboard');

  // Match by href *prefix*, not exact: OrganizerDashboardComponent.destination() routes Draft
  // competitions to /organizer/competitions/{id} but everything Active+ to
  // /organizer/competitions/{id}/tables, so an exact match would stop matching this same row the
  // moment the click below succeeds and the list refetches -- breaking the post-click badge
  // assertion.
  const row = page
    .locator('li.competition-list-row')
    .filter({ has: page.locator(`a[href^="/organizer/competitions/${competitionId}"]`) });
  await row.getByRole('button', { name: 'Activate', exact: true }).click();

  const dialog = page.getByRole('alertdialog', { name: 'Confirm advance competition state' });
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: 'Confirm', exact: true }).click();
  await expect(dialog).not.toBeVisible();

  const badge = row.locator('.badge');
  await expect(badge).toHaveText('Active');
}

// Mirrors us1-auth.spec.ts's forced-temporary-password-change flow, ending on /judge/tables.
// Takes an explicit password rather than a ProvisionedJudge -- see
// readTemporaryPasswordFromInvitation for why the one captured at provisioning time can't be
// trusted once the organizer's real "Register judges" flow has run for this email.
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

test.describe('US6 — blind table dynamics: shared fixed order', () => {
  let judgeA: ProvisionedJudge;
  let judgeB: ProvisionedJudge;

  test.beforeAll(async () => {
    // Provisioned here purely to get the JUDGE realm role assigned (createJudgeUser does this via
    // the role-mappings endpoint, see support/keycloak-admin.ts). This exposes a real gap: the
    // app's own invitation path (Features/Judges/SendInvitationHandler ->
    // Common/Keycloak/KeycloakAdminClient.CreateUserAsync) never assigns any realm role to a newly
    // created Keycloak user, so a judge invited purely through the organizer's "Register judges"
    // UI -- with no Keycloak account pre-existing -- would get RequireRole("JUDGE") = 403 forever,
    // never able to complete login. us4-judges.spec.ts didn't catch this because it only asserts
    // the invitation email is delivered, it never logs the invited judge in. Reported upward
    // rather than patched here: it's Keycloak role-provisioning logic in an already-merged,
    // different story's slice (T038-T044), not something this task's "small targeted fix in
    // T053/T052 code" allowance covers.
    const suffix = `${Date.now()}-${crypto.randomUUID()}`;
    judgeA = await createJudgeUser(`us6-judge-a-${suffix}@birrapoint.local`);
    judgeB = await createJudgeUser(`us6-judge-b-${suffix}@birrapoint.local`);
  });

  test.afterAll(async () => {
    // Guard against beforeAll having thrown partway through (e.g. Keycloak down) — an unguarded
    // `.id` here would mask the real failure with a TypeError in afterAll.
    if (judgeA?.id) {
      await deleteUser(judgeA.id);
    }
    if (judgeB?.id) {
      await deleteUser(judgeB.id);
    }
  });

  test('fixing the order in one judge session locks and reorders the other session on the same table within 1s', async ({
    page,
    browser,
  }) => {
    // Full organizer setup + two independent Keycloak-authenticated judge sessions comfortably
    // exceeds Playwright's 30s default.
    test.setTimeout(120_000);

    // --- Organizer: create competition, import 2 entries, consolidate, register both judges ---
    await page.goto('/');
    await page.waitForURL(new RegExp(`^${KEYCLOAK_ORIGIN}/`));
    await submitKeycloakLogin(page, ORGANIZER_USERNAME, ORGANIZER_PASSWORD);
    await page.waitForURL('**/organizer/dashboard');

    const competitionId = await createCompetition(page);

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

    // Neither of these two entries' owning participant matches either provisioned judge's email,
    // so no COI/BOS interaction is expected while assigning them below.
    const neutralBlindCode = await blindCodeForStyle(summary, '1A');
    const bosBlindCode = await blindCodeForStyle(summary, '20C');

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

    // Read back each judge's *current* temporary password from their delivered invitation (see
    // readTemporaryPasswordFromInvitation) rather than trusting judgeA/judgeB.tempPassword, which
    // is stale the instant "Register judges" above runs.
    const judgeATempPassword = await readTemporaryPasswordFromInvitation(
      page.request,
      judgeA.email,
    );
    const judgeBTempPassword = await readTemporaryPasswordFromInvitation(
      page.request,
      judgeB.email,
    );

    // --- Table setup: one table, both judges, both entries ---
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

    const judgeADisplayName = judgeA.email.slice(0, judgeA.email.indexOf('@'));
    const judgeBDisplayName = judgeB.email.slice(0, judgeB.email.indexOf('@'));

    await pointerDrag(page, judgeSeat(page, judgeADisplayName), mesa1Judges);
    await expect(judgeSeat(mesa1Judges, judgeADisplayName)).toBeVisible();

    await pointerDrag(page, judgeSeat(page, judgeBDisplayName), mesa1Judges);
    await expect(judgeSeat(mesa1Judges, judgeBDisplayName)).toBeVisible();

    // --- Draft -> Active, so /me/tables surfaces this table to both judges and POST .../order is
    // accepted (409 invalid-state-transition otherwise) ---
    await activateCompetition(page, competitionId);

    // --- Two independent judge sessions (separate browser contexts -> separate Keycloak SSO
    // sessions), each landing on the shared table's order view ---
    const contextA = await browser.newContext();
    const contextB = await browser.newContext();
    const pageA = await contextA.newPage();
    const pageB = await contextB.newPage();

    try {
      await loginAsJudge(pageA, judgeA.email, judgeATempPassword);
      await loginAsJudge(pageB, judgeB.email, judgeBTempPassword);

      const tableLinkA = pageA.getByRole('link', { name: new RegExp(`Mesa 1`) });
      await expect(tableLinkA).toContainText('Order not fixed');
      await tableLinkA.click();
      await pageA.waitForURL(`**/judge/tables/${mesa1Id}`);

      const tableLinkB = pageB.getByRole('link', { name: new RegExp(`Mesa 1`) });
      await expect(tableLinkB).toContainText('Order not fixed');
      await tableLinkB.click();
      await pageB.waitForURL(`**/judge/tables/${mesa1Id}`);

      const rowsA = pageA.locator('li.sample-row');
      await expect(rowsA).toHaveCount(2);
      const rowsB = pageB.locator('li.sample-row');
      await expect(rowsB).toHaveCount(2);

      // BR-01/FR-019: the blind sample view must never surface entrant data — structurally
      // asserted at the wire level by the backend contract test
      // (OrderApiTests.GetTableSamples_payload_never_contains_entrant_fields); this is the UI-side
      // defensive check that none of it leaks into the rendered page either.
      for (const p of [pageA, pageB]) {
        const listText = await p.locator('ol.sample-order-list').innerText();
        for (const leaked of [
          NEUTRAL_ENTRY_NAME,
          NEUTRAL_PARTICIPANT_NAME,
          NEUTRAL_PARTICIPANT_EMAIL,
          BOS_ENTRY_NAME,
          BOS_PARTICIPANT_NAME,
          BOS_PARTICIPANT_EMAIL,
        ]) {
          expect(listText).not.toContain(leaked);
        }
      }

      const firstBlindCodeBefore = (
        await rowsA.nth(0).locator('.sample-blind-code').innerText()
      ).trim();
      const secondBlindCodeBefore = (
        await rowsA.nth(1).locator('.sample-blind-code').innerText()
      ).trim();

      // Exercise the keyboard-accessible reorder mechanism only (FR-020) — drag-and-drop reorder
      // mechanics are already covered in isolation by T053's Jest specs; this E2E's job is the
      // cross-session propagation, not re-proving the reorder gesture itself.
      await pageA.getByRole('button', { name: `Move ${firstBlindCodeBefore} down` }).click();
      await expect(rowsA.nth(0).locator('.sample-blind-code')).toHaveText(secondBlindCodeBefore);
      await expect(rowsA.nth(1).locator('.sample-blind-code')).toHaveText(firstBlindCodeBefore);

      await pageA.getByRole('button', { name: 'Fix order' }).click();
      const confirmDialog = pageA.getByRole('alertdialog', { name: 'Confirm fix order' });
      await expect(confirmDialog).toBeVisible();
      await confirmDialog.getByRole('button', { name: 'Confirm fix order' }).click();

      // Judge A's own session reflects the fix immediately (local state update from the fixOrder
      // response, not dependent on the hub round-trip). Note: judgeADisplayName (the email
      // local-part, used above for the organizer's pre-login table assignment) no longer applies
      // here -- JudgeResolver backfills Judge.DisplayName from the Keycloak token's `name` claim
      // on first login (Common/Auth/JudgeResolver.cs), so once a judge has actually authenticated
      // their "fixed by" label is their real Keycloak name ("Test Judge" for both judges
      // provisioned by createJudgeUser, since it hardcodes that firstName/lastName). Rather than
      // pin that incidental test-data value, assert the format and then assert judge B's session
      // shows the identical label judge A's does -- the actual propagation invariant this
      // scenario is testing.
      const fixedStatusA = pageA.locator('p.order-status--fixed');
      await expect(fixedStatusA).toBeVisible();
      const fixedByLabel = (await fixedStatusA.innerText()).trim();
      expect(fixedByLabel).toMatch(/^Order fixed by .+\.$/);
      await expect(pageA.locator('.drag-handle')).toHaveCount(0);
      await expect(pageA.getByRole('button', { name: /^Move / })).toHaveCount(0);
      await expect(pageA.getByRole('button', { name: 'Fix order' })).toHaveCount(0);

      // Judge B's session, with NO manual reload/navigation, reflects the fix via the live
      // TableOrderFixed hub event within the FR-021 ≤1s propagation budget — a bounded timeout
      // here (not a generous one) is what actually enforces that budget rather than just masking a
      // slower-than-spec regression.
      const fixedStatusB = pageB.locator('p.order-status--fixed');
      await expect(fixedStatusB).toBeVisible({ timeout: 1000 });
      await expect(fixedStatusB).toHaveText(fixedByLabel);
      await expect(pageB.locator('.drag-handle')).toHaveCount(0);
      await expect(pageB.getByRole('button', { name: /^Move / })).toHaveCount(0);
      await expect(pageB.getByRole('button', { name: 'Fix order' })).toHaveCount(0);

      // Judge B's sample order matches exactly what judge A fixed.
      await expect(rowsB.nth(0).locator('.sample-blind-code')).toHaveText(secondBlindCodeBefore);
      await expect(rowsB.nth(1).locator('.sample-blind-code')).toHaveText(firstBlindCodeBefore);
    } finally {
      await contextA.close();
      await contextB.close();
    }
  });
});
