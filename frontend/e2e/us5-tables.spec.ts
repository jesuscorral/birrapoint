import path from 'node:path';
import { test, expect, Page, Locator } from '@playwright/test';

// quickstart.md scenario 5 / spec.md US5 (FR-016/FR-017/FR-018), extended per tasks.md T049:
// assigning a judge onto a table alongside a beer they own -> 409 conflict-of-interest, nothing
// persisted; assigning a judge who owns a *different*, still-unassigned entry -> succeeds and
// flags that entry Not Valid for BOS; click-to-detail (beer + judge, including on already-assigned
// items and immediately after a completed drag) and real pointer-simulated drag-and-drop between
// "Unassigned" and MesaCards and between two MesaCards.

const KEYCLOAK_ORIGIN = 'http://localhost:8081';
const ORGANIZER_USERNAME = 'organizer';
const ORGANIZER_PASSWORD = 'organizer';

const FIXTURE_PATH = path.resolve(__dirname, 'fixtures/tables-assignment.xlsx');

// Matches the fixture's rows (frontend/e2e/fixtures/tables-assignment.xlsx) -- fixed, not
// per-run-random, is safe here because Participant/Judge dedup is scoped per competition (a fresh
// one every run) and Keycloak user provisioning (EnsureUserWithTemporaryPasswordAsync) is an
// idempotent upsert by email, so reusing these across runs never collides.
const COI_PARTICIPANT_EMAIL = 'coi.participant@brew.example';
const BOS_PARTICIPANT_EMAIL = 'bos.participant@brew.example';
const COI_JUDGE_DISPLAY_NAME = 'coi.participant'; // Judge.DisplayName = email local-part
const BOS_JUDGE_DISPLAY_NAME = 'bos.participant';

async function submitKeycloakLogin(page: Page, username: string, password: string): Promise<void> {
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.locator('#kc-login').click();
}

function uniqueCompetitionName(): string {
  return `E2E Tables Comp ${Date.now()}-${crypto.randomUUID()}`;
}

// Creates a competition via the wizard's Basics step (the tables route needs a real, persisted
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

// Reads the "Created entries" table in the Consolidation summary and returns the blind code for
// the row whose Style column matches `styleCode` exactly. Blind codes are opaque/random, but each
// fixture row uses a distinct BJCP code, so matching on Style unambiguously identifies the row.
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

// Angular CDK drag-drop (@angular/cdk/drag-drop) is pointer-event-based, not native HTML5 DnD --
// Playwright's dragTo()/native drag helpers don't reliably trigger it. A manual pointer sequence
// with several intermediate mousemoves before the final one is what CDK's own pointer tracking
// needs to recognise a drag and pick the right connected drop list.
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

test.describe('US5 — table management: COI protection, BOS flagging, detail modals, drag-and-drop', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await page.waitForURL(new RegExp(`^${KEYCLOAK_ORIGIN}/`));
    await submitKeycloakLogin(page, ORGANIZER_USERNAME, ORGANIZER_PASSWORD);
    await page.waitForURL('**/organizer/dashboard');
  });

  test('rejects a COI assignment, flags BOS on a non-colliding assignment, and supports detail modals plus drag-and-drop', async ({
    page,
  }) => {
    const competitionId = await createCompetition(page);

    // --- Import 3 clean, distinct-style entries (no correction needed) and capture blind codes ---
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
    await expect(summary.getByText('Excluded: 0', { exact: true })).toBeVisible();

    const coiBeerBlindCode = await blindCodeForStyle(summary, '21A'); // owned by coi.participant
    const bosBeerBlindCode = await blindCodeForStyle(summary, '20C'); // owned by bos.participant
    const neutralBeerBlindCode = await blindCodeForStyle(summary, '1A'); // unrelated to either judge

    // --- Register the two judges whose emails match the COI/BOS participants ---
    await page.goto(`/organizer/competitions/${competitionId}/judges`);
    await expect(page.getByRole('heading', { name: 'Judge management' })).toBeVisible();

    await page
      .getByLabel('Judge emails (one per line, or comma-separated)')
      .fill([COI_PARTICIPANT_EMAIL, BOS_PARTICIPANT_EMAIL].join('\n'));
    await page.getByRole('button', { name: 'Register judges' }).click();

    const report = page.locator('div[aria-label="Registration report"]');
    await expect(report).toBeVisible();
    await expect(report.getByText(COI_PARTICIPANT_EMAIL, { exact: true })).toBeVisible();
    await expect(report.getByText(BOS_PARTICIPANT_EMAIL, { exact: true })).toBeVisible();

    // --- Table management ---
    await page.goto(`/organizer/competitions/${competitionId}/tables`);
    await expect(page.getByRole('heading', { name: 'Table management' })).toBeVisible();

    await page.getByLabel('New table name').fill('Mesa 1');
    await page.getByRole('button', { name: 'Add table' }).click();
    await expect(mesaCard(page, 'Mesa 1')).toBeVisible();

    await page.getByLabel('New table name').fill('Mesa 2');
    await page.getByRole('button', { name: 'Add table' }).click();
    await expect(mesaCard(page, 'Mesa 2')).toBeVisible();

    const mesa1 = mesaCard(page, 'Mesa 1');
    const mesa2 = mesaCard(page, 'Mesa 2');
    const mesa1Id = await mesa1.getAttribute('data-table-id');
    const mesa2Id = await mesa2.getAttribute('data-table-id');
    if (!mesa1Id || !mesa2Id) {
      throw new Error('Table id missing from data-table-id attribute.');
    }

    const unassignedBeers = page.locator('#beers-unassigned');
    const unassignedJudges = page.locator('#judges-unassigned');
    const mesa1Beers = page.locator(`#beers-${mesa1Id}`);
    const mesa1Judges = page.locator(`#judges-${mesa1Id}`);
    const mesa2Beers = page.locator(`#beers-${mesa2Id}`);
    const mesa2Judges = page.locator(`#judges-${mesa2Id}`);

    // 1) Drag the COI beer from Unassigned onto Mesa 1.
    await pointerDrag(page, beerToken(page, coiBeerBlindCode), mesa1Beers);
    await expect(beerToken(mesa1Beers, coiBeerBlindCode)).toBeVisible();
    await expect(beerToken(unassignedBeers, coiBeerBlindCode)).toHaveCount(0);

    // 2) Drag the COI judge (owns the beer just seated at Mesa 1) onto Mesa 1 -> 409, atomic
    //    rollback: nothing persisted.
    await pointerDrag(page, judgeSeat(page, COI_JUDGE_DISPLAY_NAME), mesa1Judges);

    const conflictDialog = page.getByRole('alertdialog', { name: 'Conflict of interest' });
    await expect(conflictDialog).toBeVisible();
    await expect(
      conflictDialog.getByText(`${COI_JUDGE_DISPLAY_NAME} conflicts with: ${coiBeerBlindCode}`),
    ).toBeVisible();
    await conflictDialog.getByRole('button', { name: 'Close' }).click();
    await expect(conflictDialog).toBeHidden();

    // Rejected -> still Unassigned, never seated at Mesa 1.
    await expect(judgeSeat(unassignedJudges, COI_JUDGE_DISPLAY_NAME)).toBeVisible();
    await expect(judgeSeat(mesa1Judges, COI_JUDGE_DISPLAY_NAME)).toHaveCount(0);

    // 3) Drag the BOS judge (owns a *different*, still-unassigned entry -- no direct collision
    //    with what's on Mesa 2) onto Mesa 2 -> succeeds and flags their own entry elsewhere.
    await pointerDrag(page, judgeSeat(page, BOS_JUDGE_DISPLAY_NAME), mesa2Judges);
    await expect(judgeSeat(mesa2Judges, BOS_JUDGE_DISPLAY_NAME)).toBeVisible();

    const bosBanner = page.getByRole('status');
    await expect(bosBanner).toContainText('1 entry flagged Not Valid for BOS.');

    // Their own entry (BOS Beer), which stayed unassigned, is now flagged -- live, no reload.
    await expect(beerToken(unassignedBeers, bosBeerBlindCode)).toHaveClass(
      /beer-token--bos-flagged/,
    );

    await bosBanner.getByRole('button', { name: 'Dismiss' }).click();
    await expect(bosBanner).toBeHidden();

    // 4) Click (no drag) the now-seated BOS judge -> opens the judge detail modal.
    await judgeSeat(page, BOS_JUDGE_DISPLAY_NAME).click();
    let modal = page.getByRole('dialog', { name: 'Judge details' });
    await expect(modal).toBeVisible();
    await expect(modal.getByText(BOS_JUDGE_DISPLAY_NAME, { exact: true })).toBeVisible();
    await expect(modal.getByText(BOS_PARTICIPANT_EMAIL, { exact: true })).toBeVisible();
    await expect(modal.getByText('Assigned table: Mesa 2')).toBeVisible();
    await modal.getByRole('button', { name: 'Close' }).click();
    await expect(modal).toBeHidden();

    // 5) Click (no drag) the now-seated COI beer -> opens the beer detail modal; close via Escape.
    await beerToken(page, coiBeerBlindCode).click();
    modal = page.getByRole('dialog', { name: 'Beer details' });
    await expect(modal).toBeVisible();
    await expect(modal.getByText(coiBeerBlindCode, { exact: true })).toBeVisible();
    await expect(modal.getByText('Assigned table: Mesa 1')).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(modal).toBeHidden();

    // 6) Drag the neutral beer from Unassigned onto Mesa 1.
    await pointerDrag(page, beerToken(page, neutralBeerBlindCode), mesa1Beers);
    await expect(beerToken(mesa1Beers, neutralBeerBlindCode)).toBeVisible();

    // 7) Immediately after that drop, a plain click on it still opens the detail modal instead of
    //    starting/re-arming a drag.
    await beerToken(page, neutralBeerBlindCode).click();
    modal = page.getByRole('dialog', { name: 'Beer details' });
    await expect(modal).toBeVisible();
    await modal.getByRole('button', { name: 'Close' }).click();
    await expect(modal).toBeHidden();

    // 8) Drag the neutral beer again, this time between two MesaCards (Mesa 1 -> Mesa 2).
    await pointerDrag(page, beerToken(page, neutralBeerBlindCode), mesa2Beers);
    await expect(beerToken(mesa2Beers, neutralBeerBlindCode)).toBeVisible();
    await expect(beerToken(mesa1Beers, neutralBeerBlindCode)).toHaveCount(0);
  });
});
