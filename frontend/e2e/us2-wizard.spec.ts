import { test, expect, Page } from '@playwright/test';

// quickstart.md scenario 2 / spec.md US2 (FR-007, FR-008): create a competition, leave step 2
// via "Save Draft", reopen — Draft state persisted, wizard resumes with data intact, and "Next"
// stays disabled until name/venue/dates are valid.

const KEYCLOAK_ORIGIN = 'http://localhost:8081';
const ORGANIZER_USERNAME = 'organizer';
const ORGANIZER_PASSWORD = 'organizer';

async function submitKeycloakLogin(page: Page, username: string, password: string): Promise<void> {
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.locator('#kc-login').click();
}

function uniqueCompetitionName(): string {
  return `E2E Wizard Comp ${Date.now()}-${crypto.randomUUID()}`;
}

test.describe('US2 — competition creation wizard with drafts', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.waitForURL(new RegExp(`^${KEYCLOAK_ORIGIN}/`));
    await submitKeycloakLogin(page, ORGANIZER_USERNAME, ORGANIZER_PASSWORD);
    await page.waitForURL('**/organizer/dashboard');
  });

  test('Next stays disabled until name/venue/dates are valid, then create → save draft → reopen resumes with data intact', async ({
    page,
  }) => {
    const name = uniqueCompetitionName();
    const venue = 'Salón de Actos, Madrid';
    const startDate = '2026-09-01';
    const endDate = '2026-09-03';
    const description = 'E2E-authored competition for US2 wizard draft verification.';
    const entryLimit = '50';
    const registrationStart = '2026-08-01';
    const registrationEnd = '2026-08-20';

    await page.goto('/organizer/competitions/new');

    const nextButton = page.getByRole('button', { name: 'Next' });

    // FR-007: required fields empty → Next disabled.
    await expect(nextButton).toBeDisabled();

    await page.getByLabel('Name').fill(name);
    await expect(nextButton).toBeDisabled();

    await page.getByLabel('Venue').fill(venue);
    await expect(nextButton).toBeDisabled();

    await page.getByLabel('Start date').fill(startDate);
    await expect(nextButton).toBeDisabled();

    // endDate before startDate → still invalid (endBeforeStart cross-field validator).
    await page.getByLabel('End date').fill('2026-08-01');
    await expect(nextButton).toBeDisabled();

    await page.getByLabel('End date').fill(endDate);
    await expect(nextButton).toBeEnabled();

    await nextButton.click();

    // Basics step creates the competition and the wizard swaps the URL via Location.replaceState.
    await page.waitForURL(/\/organizer\/competitions\/[0-9a-fA-F-]{36}$/);
    const competitionUrl = page.url();

    // Step 2 (Details) now renders.
    const saveDraftButton = page.getByRole('button', { name: 'Save Draft' });
    await expect(saveDraftButton).toBeVisible();

    await page.getByLabel('Description').fill(description);
    await page.getByLabel('Entry limit').fill(entryLimit);
    await page.getByLabel('Registration start').fill(registrationStart);
    await page.getByLabel('Registration end').fill(registrationEnd);

    await saveDraftButton.click();

    // FR-008: saved-as-Draft confirmation.
    await expect(page.getByText('Saved', { exact: true })).toBeVisible();

    // "Reopen": full reload at the same competition URL, as quickstart scenario 2 describes.
    await page.goto(competitionUrl);
    await page.waitForURL(competitionUrl);

    // A full reload always starts the wizard back on step 1 (Basics), repopulated from
    // GetCompetition via the route's :id param.
    await expect(page.getByLabel('Name')).toHaveValue(name);
    await expect(page.getByLabel('Venue')).toHaveValue(venue);
    await expect(page.getByLabel('Start date')).toHaveValue(startDate);
    await expect(page.getByLabel('End date')).toHaveValue(endDate);

    // Basics data was intact and valid, so Next is enabled again without re-entering anything.
    await expect(nextButton).toBeEnabled();
    await nextButton.click();

    // Step 2's previously saved data is also intact after reopening (no navigation: clicking
    // Next again on an already-persisted competition just advances currentStep client-side).
    await expect(page.getByLabel('Description')).toHaveValue(description);
    await expect(page.getByLabel('Entry limit')).toHaveValue(entryLimit);
    await expect(page.getByLabel('Registration start')).toHaveValue(registrationStart);
    await expect(page.getByLabel('Registration end')).toHaveValue(registrationEnd);
  });
});
