import { expect } from '@playwright/test';
import type { Locator, Page } from '@playwright/test';

// T125/T125b: the organizer console's standalone `/organizer/competitions/:id/import` route was
// folded into the competition wizard as step 4, and the whole wizard was translated. Eleven specs
// had their own copy of the old flow — `page.goto(.../import)` plus `Upload` / `Consolidate` — so
// every one of them died on the redirect long before reaching what it actually tested. The flow
// lives here now, once.
//
// Note for whoever changes the wizard next: these helpers arrange preconditions. They deliberately
// assert only enough to fail loudly at the right step; the specs that call them own the real
// assertions.

/** Wizard step numbers, in the order the stepper renders them. */
export const WIZARD_STEPS = {
  basics: 'Datos básicos',
  details: 'Detalles',
  styles: 'Estilos',
  importEntries: 'Importar cervezas',
  importJudges: 'Importar jueces',
  tables: 'Mesas',
} as const;

export type WizardStep = (typeof WIZARD_STEPS)[keyof typeof WIZARD_STEPS];

/**
 * Jumps straight to a wizard step. The stepper allows this for any step once the competition
 * exists (`canJumpTo`), so specs never have to walk through steps they do not care about.
 */
export async function goToWizardStep(
  page: Page,
  competitionId: string,
  step: WizardStep,
): Promise<void> {
  if (!page.url().includes(`/organizer/competitions/${competitionId}`)) {
    await page.goto(`/organizer/competitions/${competitionId}`);
  }
  // The stepper button's accessible name is its marker (a number, or a tick once complete)
  // followed by the label, so match on the label rather than the whole string.
  await page.getByRole('button', { name: new RegExp(step) }).click();
}

/**
 * Creates a competition through the wizard's Basics step and returns its persisted id, leaving it
 * in Draft. Step 1 is the only step reachable without an id, so this is every spec's entry point.
 */
export async function createCompetition(page: Page, name: string): Promise<string> {
  await page.goto('/organizer/competitions/new');

  await page.getByLabel('Nombre de la competición').fill(name);
  await page.getByLabel('Sede / ubicación').fill('Salón de Actos, Madrid');
  await page.getByLabel('Fecha de inicio').fill('2026-09-01');
  await page.getByLabel('Fecha de fin').fill('2026-09-03');

  await page.getByRole('button', { name: 'Siguiente' }).click();
  // Step 1 replaces the URL with the persisted id rather than navigating (see onBasicsSaved).
  await page.waitForURL(/\/organizer\/competitions\/[0-9a-fA-F-]{36}$/);

  return page.url().split('/').pop()!;
}

/**
 * Names the competition's first category and assigns the given BJCP styles to it (wizard step 3).
 *
 * This is a precondition for importing, not an optional nicety: `Categoria` is a required column
 * that must exact-match one of the competition's own categories, and FR-053 additionally requires
 * the row's style to be assigned to that category — otherwise every row lands
 * CategoryMismatch/CategoryStyleMismatch and "Subir archivo" stays disabled on a category-less
 * competition.
 */
export async function defineCategory(
  page: Page,
  competitionId: string,
  name: string,
  styleCodes: string[],
): Promise<void> {
  await goToWizardStep(page, competitionId, WIZARD_STEPS.styles);

  const panel = page.getByRole('region', { name: 'Categorías de la competición' });
  await expect(panel).toBeVisible();

  // The step always starts with one row (its Eliminar is disabled while it is the only one), so
  // this renames that row rather than adding a second.
  await panel
    .locator('.category-row')
    .first()
    .getByRole('button', { name: /^Editar/ })
    .click();
  await panel.getByLabel('Nombre de la categoría').fill(name);
  await panel.getByRole('button', { name: 'Listo' }).click();

  const catalog = page.getByRole('region', { name: 'Catálogo de estilos BJCP' });
  for (const code of styleCodes) {
    await catalog.getByLabel('Buscar estilo por código o nombre').fill(code);
    const row = catalog.locator('.style-row').filter({ hasText: new RegExp(`^${code} —`) });
    await expect(row).toHaveCount(1);
    // Option values are the category's index, and there is exactly one category here.
    await row.locator('select').selectOption('0');
  }

  // "Siguiente" saves and advances. Wait for the step to actually change: clicking it and moving
  // on immediately races the PUT, and a stepper jump while step 3 is still dirty raises the FR-007
  // modal, whose backdrop then swallows the next click.
  await page.getByRole('button', { name: 'Siguiente' }).click();
  await expect(panel).toBeHidden();
}

/**
 * Uploads an entries `.xlsx` on wizard step 4 and consolidates it. `expectedValidRows` asserts
 * every row landed Valid before consolidating, which is what the fixtures used for setup expect —
 * pass 0 for a fixture whose rows deliberately need correcting, and drive the corrections in the
 * spec itself.
 */
export async function importEntries(
  page: Page,
  competitionId: string,
  fixturePath: string,
  expectedValidRows: number,
): Promise<void> {
  await goToWizardStep(page, competitionId, WIZARD_STEPS.importEntries);

  await page.getByLabel('Archivo de inscripciones (.xlsx)').setInputFiles(fixturePath);
  await page.getByRole('button', { name: 'Subir archivo' }).click();

  const rows = page.getByRole('region', { name: 'Filas importadas' });
  await expect(rows).toBeVisible();
  for (let row = 1; row <= expectedValidRows; row++) {
    await expect(rows.getByText(`#${row}`, { exact: true })).toBeVisible();
  }

  const consolidate = page.getByRole('button', { name: 'Consolidar' });
  await expect(consolidate).toBeEnabled();
  await consolidate.click();

  await expect(page.getByText('Importación consolidada')).toBeVisible();
}

/**
 * Blind codes of the competition's entries, keyed by BJCP style code.
 *
 * The consolidation result itself no longer lists them: the old standalone import screen rendered
 * a "Consolidation summary" table of created entries, and the wizard step that replaced it shows
 * only counts. The codes are still on step 4 — in its "Cervezas ya importadas" section, which
 * lists what is already imported whenever the step loads without a pending batch — so this
 * re-enters the step to read them.
 */
export async function blindCodesByStyle(
  page: Page,
  competitionId: string,
): Promise<Map<string, string[]>> {
  await page.goto(`/organizer/competitions/${competitionId}`);
  await goToWizardStep(page, competitionId, WIZARD_STEPS.importEntries);

  const section = page.getByRole('region', { name: 'Cervezas ya importadas' });
  await expect(section).toBeVisible();

  const items = section.locator('li');
  const byStyle = new Map<string, string[]>();
  for (let i = 0; i < (await items.count()); i++) {
    const item = items.nth(i);
    const blindCode = (await item.locator('.existing-entries__code').innerText()).trim();
    // Rendered as "21A — American IPA"; the style code is everything before the dash.
    const styleText = (await item.locator('.existing-entries__style').innerText()).trim();
    const styleCode = styleText.split('—')[0].trim();
    byStyle.set(styleCode, [...(byStyle.get(styleCode) ?? []), blindCode]);
  }
  return byStyle;
}

/** Single blind code for a style, failing loudly rather than returning undefined. */
export async function blindCodeForStyle(
  page: Page,
  competitionId: string,
  styleCode: string,
): Promise<string> {
  const byStyle = await blindCodesByStyle(page, competitionId);
  const [blindCode] = byStyle.get(styleCode) ?? [];
  if (!blindCode) {
    throw new Error(
      `No imported entry with style ${styleCode}. Found: ${[...byStyle.keys()].join(', ') || '(none)'}`,
    );
  }
  return blindCode;
}

/** The board's beer token, addressed by the accessible name nine specs pin. */
export function beerToken(scope: Page | Locator, blindCode: string): Locator {
  return scope.getByRole('button', { name: `Beer ${blindCode} — view details`, exact: true });
}

/** The board's judge seat, addressed by the accessible name nine specs pin. */
export function judgeSeat(scope: Page | Locator, displayName: string): Locator {
  return scope.getByRole('button', { name: `Judge ${displayName} — view details`, exact: true });
}
