import path from 'node:path';
import { test, expect, Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// quickstart.md scenario 14 / spec.md US14 (FR-055–FR-058): in wizard step 5, upload
// judges-with-errors.xlsx (a roster mixing valid rows, one row missing an email, and one
// duplicate email) — row-level results render, the correction screen resolves the Invalid row,
// consolidation stays blocked until every row is resolved, and consolidating creates one account
// per unique email (duplicate emails within the file resolve to a single last-import-wins upsert,
// contracts/judge-import-file.md §Semantics) without sending any invitation email yet — that is
// the separate "Notify judges" action covered by us4-judges.spec.ts.

const KEYCLOAK_ORIGIN = 'http://localhost:8081';
const ORGANIZER_USERNAME = 'organizer';
const ORGANIZER_PASSWORD = 'organizer';

const MAILPIT_ORIGIN = 'http://localhost:8025';

const FIXTURE_PATH = path.resolve(__dirname, 'fixtures/judges-with-errors.xlsx');

// Fixed addresses baked into the fixture (frontend/e2e/fixtures/judges-with-errors.xlsx) — see
// scratch generator notes in the PR: row 1 (Ana) is a plain valid row, rows 2 and 4 (Luis) share
// this same email to exercise dedup-on-consolidate, row 3 (Casey) is missing an email (Invalid).
const ANA_EMAIL = 'e2e-judge-import-ana@brew.example';
const LUIS_EMAIL = 'e2e-judge-import-luis@brew.example';

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
  return `E2E Judge Import Comp ${Date.now()}-${crypto.randomUUID()}`;
}

// Creates a competition via the wizard's Basics step (Spanish labels/button, per the wizard
// redesign already on this branch — commit 47a77f4) and returns its id, without going through the
// rest of the wizard. Navigates via the dashboard's own "New competition" link (an in-app
// RouterLink navigation) rather than page.goto() — a full page.goto() to a deep authenticated
// route forces a full reload that re-triggers the Keycloak OAuth redirect dance, which this app
// does not currently settle from within a reasonable time.
async function createCompetition(page: Page): Promise<string> {
  await page.getByRole('link', { name: 'New competition' }).click();

  // bp-input (shared/components/bp-input) reflects its static `id` attribute onto both its host
  // element and the inner native <input>, producing a duplicate DOM id — <label for> resolves to
  // the wrong (host) element as a result, so getByLabel() cannot find these fields. Targeting the
  // native <input> tag directly sidesteps that for functional interaction; the underlying
  // duplicate-id/broken-label-association defect itself is still expected to be caught by the
  // axe-core scan below, not masked by this locator.
  await page.locator('input#basics-name').fill(uniqueCompetitionName());
  await page.locator('input#basics-venue').fill('Salón de Actos, Madrid');
  await page.locator('input#basics-start').fill('2026-09-01');
  await page.locator('input#basics-end').fill('2026-09-03');

  await page.getByRole('button', { name: 'Continuar' }).click();
  await page.waitForURL(/\/organizer\/competitions\/[0-9a-fA-F-]{36}$/);

  return page.url().split('/').pop()!;
}

// Judge-roster import is wizard step 5 (JudgeImportStepComponent), reached via the stepper — not
// a standalone route. canJumpTo(5) only requires a persisted competitionId (set once Basics is
// saved), so this jump works straight from the auto-advanced Details step (2) without touching
// steps 2–4.
async function goToJudgeImportStep(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Importar jueces' }).click();
  await expect(page.getByLabel('Listado de jueces (.xlsx)')).toBeVisible();
}

interface MailpitMessagesResponse {
  // Mailpit's `total` is the whole mailbox's message count, not this query's match count — the
  // filtered results are `messages` (this endpoint's own `messages_count` is a further, paginated
  // subset of that array's length, so counting the array itself is the simplest correct signal).
  messages: unknown[];
}

async function countMailpitMessagesTo(request: Page['request'], email: string): Promise<number> {
  const response = await request.get(
    `${MAILPIT_ORIGIN}/api/v1/search?query=${encodeURIComponent(`to:${email}`)}`,
  );
  if (!response.ok()) {
    throw new Error(`Mailpit search failed for ${email}: ${response.status()}`);
  }
  const body = (await response.json()) as MailpitMessagesResponse;
  return body.messages.length;
}

// Consolidation only ever enqueues ProvisionJudgeAccount, never SendInvitation (R-20/FR-057), so
// the absence is structural rather than a timing race — the short buffer only guards against a
// regression that fires the invitation asynchronously via the DispatchJob queue.
async function assertNoNewMailpitMessage(
  request: Page['request'],
  email: string,
  baselineCount: number,
): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 2000));
  const count = await countMailpitMessagesTo(request, email);
  expect(count, `expected no new Mailpit message to ${email} yet`).toBe(baselineCount);
}

test.describe('US14 — judge roster import via spreadsheet', () => {
  test.beforeEach(async ({ page }) => {
    await signIn(page, ORGANIZER_USERNAME, ORGANIZER_PASSWORD);
    await page.waitForURL('**/organizer/dashboard');
  });

  test('upload renders per-row results, resolves the Invalid row, then consolidates with dedup and no invitation sent', async ({
    page,
  }) => {
    const anaBaseline = await countMailpitMessagesTo(page.request, ANA_EMAIL);
    const luisBaseline = await countMailpitMessagesTo(page.request, LUIS_EMAIL);

    const competitionId = await createCompetition(page);

    await goToJudgeImportStep(page);
    const uploadViolations = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
      .analyze();
    expect(uploadViolations.violations).toEqual([]);

    await page.getByLabel('Listado de jueces (.xlsx)').setInputFiles(FIXTURE_PATH);
    await page.getByRole('button', { name: 'Subir archivo' }).click();

    const importRows = page.locator('.judge-import-row');
    await expect(importRows).toHaveCount(4);

    const rowAna = importRows.nth(0); // Ana Judge Uno -> Valid
    const rowLuis1 = importRows.nth(1); // Luis Judge Dos -> Valid
    const rowCasey = importRows.nth(2); // Casey Judge Tres / missing email -> Invalid
    const rowLuis2 = importRows.nth(3); // Luis Judge Dos (actualizado), duplicate email -> Valid

    await expect(rowAna).toContainText('Válida');
    await expect(rowLuis1).toContainText('Válida');
    await expect(rowCasey).toContainText('Incompleta');
    await expect(rowLuis2).toContainText('Válida');

    const correctionViolations = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
      .analyze();
    expect(correctionViolations.violations).toEqual([]);

    const consolidateButton = page.getByRole('button', { name: 'Consolidar' });
    await expect(consolidateButton).toBeDisabled();
    await expect(
      page.getByText('1 fila(s) necesitan corrección antes de poder consolidar.'),
    ).toBeVisible();

    // Resolve the Invalid row (missing Correo electrónico) via exclude — same resolution pattern
    // as the beer-entry correction screen (us3-import.spec.ts).
    await rowCasey.getByRole('button', { name: 'Excluir fila #3' }).click();
    await expect(rowCasey).toContainText('Excluida');
    await expect(rowCasey).toContainText('Fila excluida');

    await expect(
      page.getByText('0 fila(s) necesitan corrección antes de poder consolidar.'),
    ).toBeVisible();
    await expect(consolidateButton).toBeEnabled();

    await consolidateButton.click();

    // 3 Valid rows (Ana, Luis x2 sharing an email) minus 1 duplicate collapse to 2 unique created
    // judges; the excluded Casey row contributes nothing.
    const summary = page.getByRole('alert').filter({ hasText: 'Importación consolidada' });
    await expect(summary).toBeVisible();
    await expect(summary).toContainText('Creados: 2');
    await expect(summary).toContainText('Actualizados: 0');
    await expect(summary).toContainText('Excluidos: 1');

    // No invitation email sent yet for either address — that's the separate "Notify judges"
    // action (FR-059, covered by us4-judges.spec.ts).
    await assertNoNewMailpitMessage(page.request, ANA_EMAIL, anaBaseline);
    await assertNoNewMailpitMessage(page.request, LUIS_EMAIL, luisBaseline);

    // Verify via the judges list (quickstart scenario 14): one profile per unique email, both
    // still Pending, and the shared email reflects the later row's values (last-import-wins).
    await page.goto(`/organizer/competitions/${competitionId}/judges`);
    await expect(page.getByRole('heading', { name: 'Judge management' })).toBeVisible();

    await expect(page.locator('tr[data-judge-email]')).toHaveCount(2);

    const anaRow = page.locator(`tr[data-judge-email="${ANA_EMAIL}"]`);
    const luisRow = page.locator(`tr[data-judge-email="${LUIS_EMAIL}"]`);
    await expect(anaRow).toBeVisible();
    await expect(luisRow).toBeVisible();
    await expect(anaRow).toContainText('Pending');
    await expect(luisRow).toContainText('Pending');

    const luisRosterInfo = page.locator(`tr[data-judge-roster="${LUIS_EMAIL}"]`);
    await expect(luisRosterInfo).toContainText('10649-B');
    await expect(luisRosterInfo).toContainText('Actualizado en fila 4');
  });
});
