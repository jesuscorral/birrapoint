import { test, expect, Page } from '@playwright/test';

// quickstart.md scenario 13 / spec.md US13 (FR-050): an organizer with two competitions in
// different lifecycle states logs in, confirms both are listed with the correct
// name/venue/dates/state, opens each and lands in the screen appropriate to its state (the
// setup wizard for Draft, the tables screen for Active+), then separately starts and completes
// creating a brand-new competition from the same screen.
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
const API_BASE_URL = 'http://localhost:5121';
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
// Draft unless the caller advances it further (see activateCompetition below).
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

// The organizer wizard never leaves a competition anywhere but Draft, and no "Activate" UI exists
// yet on this branch — same gap already documented and worked around by us6-order.spec.ts's own
// activateCompetition. Reused here verbatim in approach: piggyback on the organizer's own
// already-authenticated browser session by reloading a page that fires an authenticated GET for
// this competition, capture the bearer token it sends, and reuse it for a direct REST call to the
// documented state-transition endpoint (contracts/rest-api.md POST /competitions/{id}/state).
// Draft -> Active has no prerequisites beyond ownership + the forward-only state gate
// (ChangeCompetitionStateCommandHandler only special-cases the Finalized target), so this works
// against a bare just-created Draft competition with no imports/judges/tables.
async function activateCompetition(page: Page, competitionId: string): Promise<void> {
  const [request] = await Promise.all([
    page.waitForRequest(
      (req) =>
        req.url().includes(`/api/v1/competitions/${competitionId}`) &&
        Boolean(req.headers()['authorization']),
    ),
    page.reload(),
  ]);

  const authorization = request.headers()['authorization'];
  const response = await page.request.post(
    `${API_BASE_URL}/api/v1/competitions/${competitionId}/state`,
    {
      headers: { Authorization: authorization, 'Content-Type': 'application/json' },
      data: { target: 'Active' },
    },
  );
  if (!response.ok()) {
    throw new Error(
      `Activating competition ${competitionId} failed: ${response.status()} ${await response.text()}`,
    );
  }
}

function dashboardItem(page: Page, name: string) {
  return page.locator('a.competition-list-item').filter({ hasText: name });
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

    // --- Competition B: created via the wizard, then advanced to Active via the direct-API
    // workaround (no UI for this transition yet) ---
    const competitionIdB = await createCompetition(page, activeName);
    await activateCompetition(page, competitionIdB);

    // --- Acceptance scenario 1: dashboard lists both, each with name/venue/dates/state ---
    await page.goto('/organizer/dashboard');

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
});
