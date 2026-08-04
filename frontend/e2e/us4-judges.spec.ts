import { test, expect, Page } from '@playwright/test';

// quickstart.md scenario 4 / spec.md US4 (FR-014/FR-015/FR-059): bulk-add judge emails incl. one
// duplicate -> profiles created (no invitation yet), duplicate reported; triggering the separate,
// explicit "Notify judges" action (FR-059, shared with User Story 14's spreadsheet import) then
// delivers the invitation, visible in Mailpit (:8025). Registration no longer sends an invitation
// automatically (Session 2026-08-02 — supersedes the originally-automatic dispatch).

const KEYCLOAK_ORIGIN = 'http://localhost:8081';
const ORGANIZER_USERNAME = 'organizer';
const ORGANIZER_PASSWORD = 'organizer';

const MAILPIT_ORIGIN = 'http://localhost:8025';
const MAILPIT_POLL_TIMEOUT_MS = 10_000;
const MAILPIT_POLL_INTERVAL_MS = 500;

// The birrapoint custom Keycloak theme (infra/keycloak/themes/birrapoint/login/login.ftl) renders
// the submit control as a bare `<input type="submit">` with no id — `#kc-login` (the default
// Keycloak theme's id, which every other spec in this suite still targets) does not exist here.
async function submitKeycloakLogin(page: Page, username: string, password: string): Promise<void> {
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.getByRole('button', { name: 'Log In' }).click();
}

// FR-001 (Session 2026-08-02): '/' now renders a public welcome page instead of redirecting
// straight to Keycloak — reach the hosted login via the "Iniciar sesión" action and skip the
// handoff screen's 1.5s auto-redirect by clicking through it directly.
async function signIn(page: Page, username: string, password: string): Promise<void> {
  await page.goto('/');
  await page.getByRole('button', { name: 'Iniciar sesión' }).click();
  await page.waitForURL('**/auth/handoff');
  await page.getByRole('button', { name: /Continuar/ }).click();
  await page.waitForURL(new RegExp(`^${KEYCLOAK_ORIGIN}/`));
  await submitKeycloakLogin(page, username, password);
}

function uniqueCompetitionName(): string {
  return `E2E Judges Comp ${Date.now()}-${crypto.randomUUID()}`;
}

function uniqueJudgeEmail(label: string): string {
  return `e2e-judge-${label}-${Date.now()}-${crypto.randomUUID()}@brew.example`;
}

// Creates a competition via the wizard's Basics step (the judges route needs a real, persisted
// competitionId) and returns its id, without going through the rest of the wizard. Spanish
// labels/button per the wizard redesign already on this branch (commit 47a77f4). Navigates via the
// dashboard's own "New competition" link (in-app RouterLink) rather than page.goto() — a full
// page.goto() to a deep authenticated route forces a reload that re-triggers the Keycloak OAuth
// redirect dance, which this app does not currently settle from within a reasonable time.
async function createCompetition(page: Page): Promise<string> {
  await page.getByRole('link', { name: 'New competition' }).click();

  // bp-input (shared/components/bp-input) reflects its static `id` attribute onto both its host
  // element and the inner native <input>, producing a duplicate DOM id — <label for> resolves to
  // the wrong (host) element as a result, so getByLabel() cannot find these fields. Targeting the
  // native <input> tag directly sidesteps that for functional interaction.
  await page.locator('input#basics-name').fill(uniqueCompetitionName());
  await page.locator('input#basics-venue').fill('Salón de Actos, Madrid');
  await page.locator('input#basics-start').fill('2026-09-01');
  await page.locator('input#basics-end').fill('2026-09-03');

  await page.getByRole('button', { name: 'Continuar' }).click();
  await page.waitForURL(/\/organizer\/competitions\/[0-9a-fA-F-]{36}$/);

  return page.url().split('/').pop()!;
}

interface MailpitMessageSummary {
  To: { Address: string; Name: string }[];
  Subject: string;
}

interface MailpitMessagesResponse {
  messages: MailpitMessageSummary[];
  total: number;
}

// Polls Mailpit's REST API (v1) for a message addressed to `email` — invitation delivery is
// async via a DispatchJob (SendInvitationHandler), so it does not land synchronously after
// POST /judges.
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

// Every email used in this spec is generated fresh per run (crypto.randomUUID()), so an absence
// check is safe against a plain `messages.length === 0` rather than needing a before/after count
// delta — Mailpit's own `total` field is the *whole mailbox's* message count, not this query's
// match count, so it is deliberately not used here. RegisterJudges only ever enqueues
// ProvisionJudgeAccount, never SendInvitation (R-20/FR-059), so this is a structural absence, not
// a race — the short buffer only guards against a regression that fires the invitation
// asynchronously via the DispatchJob queue.
async function assertNoMailpitMessageTo(request: Page['request'], email: string): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 2000));
  const response = await request.get(
    `${MAILPIT_ORIGIN}/api/v1/search?query=${encodeURIComponent(`to:${email}`)}`,
  );
  if (!response.ok()) {
    throw new Error(`Mailpit search failed for ${email}: ${response.status()}`);
  }
  const body = (await response.json()) as MailpitMessagesResponse;
  expect(
    body.messages.length,
    `expected no Mailpit message to ${email} before "Notificar jueces"`,
  ).toBe(0);
}

test.describe('US4 — judge registration with deferred, explicit invitation notification', () => {
  test.beforeEach(async ({ page }) => {
    await signIn(page, ORGANIZER_USERNAME, ORGANIZER_PASSWORD);
    await page.waitForURL('**/organizer/dashboard');
  });

  test('bulk-add emails incl. one duplicate reports created/skipped and delivers an invitation visible in Mailpit', async ({
    page,
  }) => {
    const competitionId = await createCompetition(page);

    await page.goto(`/organizer/competitions/${competitionId}/judges`);
    await expect(page.getByRole('heading', { name: 'Judge management' })).toBeVisible();

    const emailA = uniqueJudgeEmail('a');
    const emailB = uniqueJudgeEmail('b');
    const emailC = uniqueJudgeEmail('c');
    const duplicateEmail = emailB; // repeated in the pasted list -> duplicate-in-list

    const emailsInput = [emailA, emailB, emailC, duplicateEmail].join('\n');

    await page.getByLabel('Judge emails (one per line, or comma-separated)').fill(emailsInput);
    await page.getByRole('button', { name: 'Register judges' }).click();

    // Registration report is a plain <div aria-label="..."> (no implicit landmark role), so an
    // attribute selector is used rather than getByRole('region', ...) / getByLabel (both of which
    // target elements with a nameable ARIA role, which a bare div lacks).
    const report = page.locator('div[aria-label="Registration report"]');
    await expect(report).toBeVisible();

    // 3 unique emails created, the repeated occurrence of emailB skipped as duplicate-in-list.
    for (const email of [emailA, emailB, emailC]) {
      await expect(report.getByText(email, { exact: true })).toBeVisible();
    }
    await expect(
      report.getByText(`${duplicateEmail} — duplicate in the pasted list`),
    ).toBeVisible();

    // Delivery status table lists all three created judges, still Pending — RegisterJudges only
    // provisions the Keycloak account (ProvisionJudgeAccount), it no longer enqueues the
    // invitation send (FR-014/Session 2026-08-02).
    const deliveryStatus = page.getByRole('region', { name: 'Delivery status' });
    for (const email of [emailA, emailB, emailC]) {
      const row = deliveryStatus.locator(`tr[data-judge-email="${email}"]`);
      await expect(row).toBeVisible();
      await expect(row).toContainText('Pending');
    }

    // No invitation has been sent yet for any of them.
    await assertNoMailpitMessageTo(page.request, emailA);

    // Registering the same email again (a second call) now hits the already-registered path.
    await page.getByLabel('Judge emails (one per line, or comma-separated)').fill(emailA);
    await page.getByRole('button', { name: 'Register judges' }).click();

    await expect(report.getByText(`${emailA} — already registered`)).toBeVisible();

    // FR-059: the separate, explicit "Notify judges" action is what actually delivers the
    // invitation — covering judges from both provisioning paths (this plain email-list flow and
    // User Story 14's spreadsheet import).
    await deliveryStatus.getByRole('button', { name: 'Notificar jueces' }).click();
    await expect(deliveryStatus.getByText('Se han notificado 3 juez(es).')).toBeVisible();

    // Invitation delivery is asynchronous (DispatchJob) -- verify it actually landed in Mailpit.
    const message = await waitForMailpitMessageTo(page.request, emailA);
    expect(message.Subject).toContain('invited to judge');

    // The delivery status table reflects the now-Sent invitation once reloaded post-notify.
    await expect(async () => {
      await page.reload();
      await expect(
        page
          .getByRole('region', { name: 'Delivery status' })
          .locator(`tr[data-judge-email="${emailA}"]`),
      ).toContainText('Sent');
    }).toPass({ timeout: 10_000 });
  });
});
