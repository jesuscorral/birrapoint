import path from 'node:path';
import { test, expect, Page } from '@playwright/test';

import { loginAsOrganizer } from './support/auth';
import {
  createCompetition,
  defineCategory,
  goToWizardStep,
  WIZARD_STEPS,
} from './support/organizer-wizard';

// quickstart.md scenario 3 / spec.md US3 (FR-009–FR-013): upload entries-with-errors.xlsx
// (mirrors contracts/import-file.md §Example, incl. the 99Z mismatch row, plus one deliberately
// Invalid row) — row-level results render, the Mapping & Correction screen resolves both a
// StyleMismatch row (assign-style) and an Invalid row (exclude), consolidation stays blocked
// until every row is resolved, and consolidating generates blind codes.

const ORGANIZER_USERNAME = 'organizer';
const ORGANIZER_PASSWORD = 'organizer';

const FIXTURE_PATH = path.resolve(__dirname, 'fixtures/entries-with-errors.xlsx');

function uniqueCompetitionName(): string {
  return `E2E Import Comp ${Date.now()}-${crypto.randomUUID()}`;
}

test.describe('US3 — beer entry import with in-flow correction', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsOrganizer(page, ORGANIZER_USERNAME, ORGANIZER_PASSWORD);
  });

  test('upload renders per-row results, resolves StyleMismatch and Invalid rows, then consolidates with blind codes', async ({
    page,
  }) => {
    const competitionId = await createCompetition(page, uniqueCompetitionName());

    // The fixture's rows all declare "Estilos clásicos" and use styles 21A/20C, so the category
    // has to exist and own those styles before any row can come back Valid (FR-052/FR-053).
    await defineCategory(page, competitionId, 'Estilos clásicos', ['21A', '20C']);

    // defineCategory's "Siguiente" already lands on step 4.
    await page.getByLabel('Archivo de inscripciones (.xlsx)').setInputFiles(FIXTURE_PATH);
    await page.getByRole('button', { name: 'Subir archivo' }).click();

    const rows = page.getByRole('region', { name: 'Filas importadas' });
    await expect(rows).toBeVisible();

    // Row order matches the fixture: Ana Gómez / 21A (code match), Luis Pérez / Imperial Stout
    // (name match), Sam Roe / 99Z (StyleMismatch), Casey Void / missing email (Invalid).
    const row = (n: number) => rows.locator('.import-row').nth(n - 1);

    await expect(row(1)).toContainText('Válida');
    await expect(row(2)).toContainText('Válida');
    await expect(row(3)).toContainText('Estilo no reconocido');
    await expect(row(4)).toContainText('Incompleta');

    const consolidateButton = page.getByRole('button', { name: 'Consolidar' });
    await expect(consolidateButton).toBeDisabled();
    await expect(
      page.getByText('2 fila(s) necesitan corrección antes de poder consolidar.'),
    ).toBeVisible();

    // Resolve the StyleMismatch row (99Z). The correction moved into the row editor, so the
    // catalog picker is reached through "Editar fila #3" and committed with "Guardar fila".
    await page.getByRole('button', { name: 'Editar fila #3' }).click();
    const editor = page.locator('.import-row__editor');
    await editor.getByLabel('Filter styles').fill('American IPA');
    await editor.locator('app-style-picker select').selectOption('21A');
    await editor.getByRole('button', { name: 'Assign style' }).click();
    await page.getByRole('button', { name: 'Guardar fila' }).click();

    await expect(row(3)).toContainText('Válida');
    await expect(row(3)).not.toContainText('Estilo no reconocido');

    // Resolve the Invalid row (missing ParticipantEmail) via exclude.
    await page.getByRole('button', { name: 'Excluir fila #4' }).click();
    await expect(row(4)).toContainText('Excluida');

    await expect(
      page.getByText('0 fila(s) necesitan corrección antes de poder consolidar.'),
    ).toBeVisible();
    await expect(consolidateButton).toBeEnabled();

    await consolidateButton.click();

    // 3 entries consolidated: Ana (21A), Luis (20C), and Sam's corrected row (assigned 21A) —
    // the excluded Casey row contributes nothing.
    await expect(page.getByText('Importación consolidada')).toBeVisible();
    await expect(page.getByText('Importadas: 3. Excluidas: 1.')).toBeVisible();

    // Blind codes are generated on consolidation. The wizard step lists them under "Cervezas ya
    // importadas" when it reloads with no pending batch (the old standalone screen had a
    // consolidation summary table; this replaced it).
    await page.goto(`/organizer/competitions/${competitionId}`);
    await goToWizardStep(page, competitionId, WIZARD_STEPS.importEntries);

    const imported = page.getByRole('region', { name: 'Cervezas ya importadas' });
    await expect(imported).toBeVisible();
    await expect(imported.locator('li')).toHaveCount(3);
    await expect(imported.getByText('21A —')).toHaveCount(2);
    await expect(imported.getByText('20C —')).toHaveCount(1);
    for (let i = 0; i < 3; i++) {
      await expect(imported.locator('.existing-entries__code').nth(i)).not.toHaveText('');
    }
  });
});
