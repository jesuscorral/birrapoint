import { test, expect, Page, Locator } from '@playwright/test';

// quickstart.md scenario 13 / spec.md US13 (FR-050/FR-051): an organizer with two competitions in
// different lifecycle states logs in, confirms both are listed with the correct
// name/venue/dates/state, opens each and lands in the screen appropriate to its state (the
// setup wizard for Draft, the tables screen for Active+), then separately starts and completes
// creating a brand-new competition from the same screen. Acceptance Scenario 5 (and its paired
// edge case) additionally cover the dashboard's confirm-then-advance lifecycle control: a
// single-step forward transition per FR-006, and the FR-036 block when advancing to Finalized
// while a table remains open.
//
// Scoping note (per task instructions): this spec does not exercise the zero-competitions empty
// state. The shared `organizer`/`organizer` account used across this whole E2E suite accumulates
// competitions from every other spec's run and is never actually empty by the time this test
// runs, and forcing that state would require deleting the shared account's competitions —
// destructive to every other spec that depends on them. The empty-state branch (acceptance
// scenario 4) is already covered by OrganizerDashboardComponent's own Jest spec
// (frontend/src/app/features/dashboard/organizer-dashboard.component.spec.ts, T100:
// "shows an empty state with a create action when the organizer has no competitions").

const KEYCLOAK_ORIGIN = 'http://localhost:8081';
const ORGANIZER_USERNAME = 'organizer';
const ORGANIZER_PASSWORD = 'organizer';

async function submitKeycloakLogin(page: Page, username: string, password: string): Promise<void> {
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.locator('#kc-login').click();
}

function uniqueCompetitionName(label: string): string {
  return `E2E Dashboard Comp ${label} ${Date.now()}-${crypto.randomUUID()}`;
}

// Mirrors us5-tables.spec.ts / us6-order.spec.ts's createCompetition: creates via the wizard's
// Basics step and returns the persisted competitionId, leaving `page` on the wizard's step 2
// (Details) for that competition — never touches the rest of the wizard, so the competition stays
// Draft unless the caller advances it further (see advanceCompetitionState below).
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

function dashboardItem(page: Page, name: string): Locator {
  return page.locator('a.competition-list-item').filter({ hasText: name });
}

// The advance-state button lives as a sibling of the navigation `<a>` inside the shared
// `<li class="competition-list-row">` (T102, FR-051) — a row-scoped locator is what lets us find
// it without also matching another competition's identically-labelled action.
function competitionRow(page: Page, name: string): Locator {
  return page.locator('li.competition-list-row').filter({ hasText: name });
}

// Drives the real advance-state UI (T102): click the row's action, confirm the resulting
// `alertdialog`, and wait for it to close. Caller must already be on /organizer/dashboard. Works
// for both the success path (list refetches, badge updates in place) and the two documented 409
// paths (component closes the dialog and renders a `role="alert"` message either way) — assertions
// on the outcome are left to the caller.
async function advanceCompetitionState(
  page: Page,
  competitionName: string,
  actionLabel: string,
): Promise<void> {
  const row = competitionRow(page, competitionName);
  await row.getByRole('button', { name: actionLabel, exact: true }).click();

  const dialog = page.getByRole('alertdialog', { name: 'Confirm advance competition state' });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole('heading', { name: actionLabel, exact: true })).toBeVisible();
  await expect(dialog).toContainText(`This moves "${competitionName}" to`);

  await dialog.getByRole('button', { name: 'Confirm', exact: true }).click();
  await expect(dialog).not.toBeVisible();
}

// Mirrors us6-order.spec.ts's mesaCard (via us5-tables.spec.ts): locates a table card by its name
// heading, scoped so it doesn't match on substrings of other tables' names.
function tableCard(page: Page, name: string): Locator {
  return page
    .locator('article.mesa-card')
    .filter({ has: page.getByRole('heading', { level: 3, name, exact: true }) });
}

test.describe('US13 — organizer competition selection', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.waitForURL(new RegExp(`^${KEYCLOAK_ORIGIN}/`));
    await submitKeycloakLogin(page, ORGANIZER_USERNAME, ORGANIZER_PASSWORD);
    await page.waitForURL('**/organizer/dashboard');
  });

  test('lists owned competitions with correct state badges, routes to the state-appropriate screen, and opens an empty wizard for a new one', async ({
    page,
  }) => {
    test.setTimeout(60_000);

    const draftName = uniqueCompetitionName('Draft');
    const activeName = uniqueCompetitionName('Active');

    // --- Competition A: created via the wizard, left untouched -> stays Draft ---
    const competitionIdA = await createCompetition(page, draftName);

    // --- Competition B: created via the wizard, then advanced to Active through the real
    // dashboard control (Acceptance Scenario 5) ---
    const competitionIdB = await createCompetition(page, activeName);

    await page.goto('/organizer/dashboard');
    await advanceCompetitionState(page, activeName, 'Activate');

    // --- Acceptance scenario 1: dashboard lists both, each with name/venue/dates/state ---
    const itemA = dashboardItem(page, draftName);
    await expect(itemA).toBeVisible();
    await expect(itemA.locator('.competition-venue')).toHaveText('Salón de Actos, Madrid');
    await expect(itemA.locator('.competition-dates')).toHaveText('2026-09-01 – 2026-09-03');
    const badgeA = itemA.locator('.badge');
    await expect(badgeA).toHaveClass(/badge--draft/);
    await expect(badgeA).toHaveText('Draft');

    const itemB = dashboardItem(page, activeName);
    await expect(itemB).toBeVisible();
    await expect(itemB.locator('.competition-venue')).toHaveText('Salón de Actos, Madrid');
    await expect(itemB.locator('.competition-dates')).toHaveText('2026-09-01 – 2026-09-03');
    const badgeB = itemB.locator('.badge');
    await expect(badgeB).toHaveClass(/badge--active/);
    await expect(badgeB).toHaveText('Active');

    // --- Acceptance scenario 2 (Draft -> setup wizard) ---
    await dashboardItem(page, draftName).click();
    await page.waitForURL(new RegExp(`/organizer/competitions/${competitionIdA}$`));
    await expect(page.getByLabel('Name')).toHaveValue(draftName);

    // --- Acceptance scenario 2 (Active -> tables screen, the relevant management view) ---
    await page.goto('/organizer/dashboard');
    await dashboardItem(page, activeName).click();
    await page.waitForURL(new RegExp(`/organizer/competitions/${competitionIdB}/tables$`));
    await expect(page.getByRole('heading', { name: 'Table management' })).toBeVisible();

    // --- Acceptance scenario 3: "new competition" opens the wizard empty ---
    await page.goto('/organizer/dashboard');
    await page.getByRole('link', { name: 'New competition' }).click();
    await page.waitForURL('**/organizer/competitions/new');
    await expect(page.getByLabel('Name')).toHaveValue('');
  });

  test('confirming the advance-state action moves a Draft competition to Active in place and relabels the next action (Acceptance Scenario 5)', async ({
    page,
  }) => {
    const name = uniqueCompetitionName('Advance');
    await createCompetition(page, name);

    await page.goto('/organizer/dashboard');
    const badge = competitionRow(page, name).locator('.badge');
    await expect(badge).toHaveText('Draft');

    await advanceCompetitionState(page, name, 'Activate');

    // No navigation/reload -- still on the dashboard, badge updated in place from the refetch.
    await expect(page).toHaveURL(/\/organizer\/dashboard$/);
    await expect(badge).toHaveText('Active');
    await expect(badge).toHaveClass(/badge--active/);

    // Next-state relabeling: the same row's action now reads the Active -> InEvaluation label.
    await expect(
      competitionRow(page, name).getByRole('button', { name: 'Start evaluation', exact: true }),
    ).toBeVisible();
  });

  test('blocks advancing to Finalized while a table remains open, naming the blocking table (Acceptance Scenario 5 edge case)', async ({
    page,
  }) => {
    test.setTimeout(60_000);

    const name = uniqueCompetitionName('Blocked');
    const competitionId = await createCompetition(page, name);

    await page.goto('/organizer/dashboard');
    await advanceCompetitionState(page, name, 'Activate'); // Draft -> Active
    await expect(competitionRow(page, name).locator('.badge')).toHaveText('Active');

    // A table must exist and stay open (never closed) to trigger FR-036's block. Table creation
    // requires Draft/Active (backend/src/BirraPoint.Api/Features/Tables/CreateTable.cs), so it
    // happens here while the competition is still Active; no judges/beers are needed — an
    // otherwise-empty table still defaults to TableState.Open until explicitly closed.
    await page.goto(`/organizer/competitions/${competitionId}/tables`);
    await expect(page.getByRole('heading', { name: 'Table management' })).toBeVisible();
    await page.getByLabel('New table name').fill('Blocking Table');
    await page.getByRole('button', { name: 'Add table' }).click();
    await expect(tableCard(page, 'Blocking Table')).toBeVisible();

    await page.goto('/organizer/dashboard');
    await advanceCompetitionState(page, name, 'Start evaluation'); // Active -> InEvaluation
    const badge = competitionRow(page, name).locator('.badge');
    await expect(badge).toHaveText('InEvaluation');

    // Attempt InEvaluation -> Finalized: blocked because the table above is still open.
    await advanceCompetitionState(page, name, 'Finalize');

    const alert = page.locator('[role="alert"]');
    await expect(alert).toContainText('1 table(s) still open');

    // The transition was rejected server-side: the refetched list still shows InEvaluation, not a
    // silently-applied Finalized.
    await expect(badge).toHaveText('InEvaluation');
    await expect(badge).toHaveClass(/badge--inevaluation/);
  });
});
