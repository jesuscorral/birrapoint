import path from 'node:path';
import { test, expect, Page } from '@playwright/test';

// quickstart.md scenario 3 / spec.md US3 (FR-009–FR-013): upload entries-with-errors.xlsx
// (mirrors contracts/import-file.md §Example, incl. the 99Z mismatch row, plus one deliberately
// Invalid row) — row-level results render, the Mapping & Correction screen resolves both a
// StyleMismatch row (assign-style) and an Invalid row (exclude), consolidation stays blocked
// until every row is resolved, and consolidating generates blind codes.

const KEYCLOAK_ORIGIN = 'http://localhost:8081';
const ORGANIZER_USERNAME = 'organizer';
const ORGANIZER_PASSWORD = 'organizer';

const FIXTURE_PATH = path.resolve(__dirname, 'fixtures/entries-with-errors.xlsx');

async function submitKeycloakLogin(page: Page, username: string, password: string): Promise<void> {
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.locator('#kc-login').click();
}

function uniqueCompetitionName(): string {
  return `E2E Import Comp ${Date.now()}-${crypto.randomUUID()}`;
}

// Creates a competition via the wizard's Basics step (the import route needs a real, persisted
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

test.describe('US3 — beer entry import with in-flow correction', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.waitForURL(new RegExp(`^${KEYCLOAK_ORIGIN}/`));
    await submitKeycloakLogin(page, ORGANIZER_USERNAME, ORGANIZER_PASSWORD);
    await page.waitForURL('**/organizer/dashboard');
  });

  test('upload renders per-row results, resolves StyleMismatch and Invalid rows, then consolidates with blind codes', async ({
    page,
  }) => {
    const competitionId = await createCompetition(page);

    await page.goto(`/organizer/competitions/${competitionId}/import`);
    await expect(page.getByRole('heading', { name: 'Import beer entries' })).toBeVisible();

    await page.getByLabel('Entries file (.xlsx)').setInputFiles(FIXTURE_PATH);
    await page.getByRole('button', { name: 'Upload' }).click();

    const resultsSection = page.getByRole('region', { name: 'Import results' });
    await expect(resultsSection).toBeVisible();

    const row1 = page.locator('tr[data-row-number="1"]'); // Ana Gómez / 21A → Valid (code match)
    const row2 = page.locator('tr[data-row-number="2"]'); // Luis Pérez / Imperial Stout → Valid (name match)
    const row3 = page.locator('tr[data-row-number="3"]'); // Sam Roe / 99Z → StyleMismatch
    const row4 = page.locator('tr[data-row-number="4"]'); // Casey Void / missing email → Invalid

    await expect(row1).toContainText('Valid');
    await expect(row2).toContainText('Valid');
    await expect(row3).toContainText('StyleMismatch');
    await expect(row4).toContainText('Invalid');

    const consolidateButton = page.getByRole('button', { name: 'Consolidate' });
    await expect(consolidateButton).toBeDisabled();
    await expect(
      page.getByText('2 row(s) need correction before you can consolidate.'),
    ).toBeVisible();

    // Resolve the StyleMismatch row (99Z) via the searchable catalog picker.
    await row3.getByLabel('Filter styles').fill('American IPA');
    await row3.locator('select').selectOption('21A');
    await row3.getByRole('button', { name: 'Assign style' }).click();

    await expect(row3).toContainText('Valid');
    await expect(row3).not.toContainText('StyleMismatch');

    // Resolve the Invalid row (missing ParticipantEmail) via exclude.
    await row4.getByRole('button', { name: 'Exclude' }).click();
    await expect(row4).toContainText('Excluded');

    await expect(
      page.getByText('0 row(s) need correction before you can consolidate.'),
    ).toBeVisible();
    await expect(consolidateButton).toBeEnabled();

    await consolidateButton.click();

    const summary = page.getByRole('region', { name: 'Consolidation summary' });
    await expect(summary.getByText('Imported: 3', { exact: true })).toBeVisible();
    await expect(summary.getByText('Excluded: 1', { exact: true })).toBeVisible();

    // 3 entries consolidated: Ana (21A), Luis (20C), and Sam's corrected row (assigned 21A) —
    // the excluded Casey row contributes nothing.
    const entryRows = summary.locator('tbody tr');
    await expect(entryRows).toHaveCount(3);
    await expect(entryRows.locator('td:nth-child(2)').filter({ hasText: '21A' })).toHaveCount(2);
    await expect(entryRows.locator('td:nth-child(2)').filter({ hasText: '20C' })).toHaveCount(1);

    for (let i = 0; i < 3; i++) {
      await expect(entryRows.nth(i).locator('td').first()).not.toHaveText('');
    }
  });
});
