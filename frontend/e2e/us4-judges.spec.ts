import { test, expect, Page } from '@playwright/test';

// quickstart.md scenario 4 / spec.md US4 (FR-014/FR-015): bulk-add judge emails incl. one
// duplicate -> profiles created, duplicate reported; invitation visible in Mailpit (:8025).

const KEYCLOAK_ORIGIN = 'http://localhost:8081';
const ORGANIZER_USERNAME = 'organizer';
const ORGANIZER_PASSWORD = 'organizer';

const MAILPIT_ORIGIN = 'http://localhost:8025';
const MAILPIT_POLL_TIMEOUT_MS = 10_000;
const MAILPIT_POLL_INTERVAL_MS = 500;

async function submitKeycloakLogin(page: Page, username: string, password: string): Promise<void> {
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.locator('#kc-login').click();
}

function uniqueCompetitionName(): string {
  return `E2E Judges Comp ${Date.now()}-${crypto.randomUUID()}`;
}

function uniqueJudgeEmail(label: string): string {
  return `e2e-judge-${label}-${Date.now()}-${crypto.randomUUID()}@brew.example`;
}

// Creates a competition via the wizard's Basics step (the judges route needs a real, persisted
// competitionId) and returns its id, without going through the rest of the wizard.
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

test.describe('US4 — judge registration and automatic invitations', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.waitForURL(new RegExp(`^${KEYCLOAK_ORIGIN}/`));
    await submitKeycloakLogin(page, ORGANIZER_USERNAME, ORGANIZER_PASSWORD);
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

    // Delivery status table lists all three created judges.
    const deliveryStatus = page.getByRole('region', { name: 'Delivery status' });
    for (const email of [emailA, emailB, emailC]) {
      await expect(deliveryStatus.locator(`tr[data-judge-email="${email}"]`)).toBeVisible();
    }

    // Registering the same email again (a second call) now hits the already-registered path.
    await page.getByLabel('Judge emails (one per line, or comma-separated)').fill(emailA);
    await page.getByRole('button', { name: 'Register judges' }).click();

    await expect(report.getByText(`${emailA} — already registered`)).toBeVisible();

    // Invitation delivery is asynchronous (DispatchJob) -- verify it actually landed in Mailpit.
    const message = await waitForMailpitMessageTo(page.request, emailA);
    expect(message.Subject).toContain('invited to judge');
  });
});
