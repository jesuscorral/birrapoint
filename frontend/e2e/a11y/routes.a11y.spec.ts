import path from 'node:path';
import { test, expect, Page, Locator } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { createJudgeUser, deleteUser, ProvisionedJudge } from '../support/keycloak-admin';

// T089/SC-009: WCAG 2.1 AA sweep (Constitution Principle VIII) across every organizer and
// judge-facing route in src/app/app.routes.ts. One continuous, realistic journey — reusing the
// exact login/setup helpers other US* specs already use — rather than 12 independent tests each
// re-paying full Keycloak+import+table setup, since axe-core's own check is cheap relative to
// that setup cost. Every `test.step` below scans exactly once per distinct route:
//   /organizer/dashboard, /organizer/competitions/new, /organizer/competitions/:id,
//   /organizer/competitions/:id/import, /organizer/competitions/:id/judges,
//   /organizer/competitions/:id/tables, /organizer/competitions/:id/monitor,
//   /organizer/competitions/:id/dispatch, /judge/tables, /judge/tables/:tableId,
//   /judge/tables/:tableId/samples/:beerEntryId, /judge/tables/:tableId/discrepancies.

const KEYCLOAK_ORIGIN = 'http://localhost:8081';
const ORGANIZER_USERNAME = 'organizer';
const ORGANIZER_PASSWORD = 'organizer';

const MAILPIT_ORIGIN = 'http://localhost:8025';
const MAILPIT_POLL_TIMEOUT_MS = 10_000;
const MAILPIT_POLL_INTERVAL_MS = 500;

const FIXTURE_PATH = path.resolve(__dirname, '../fixtures/entries-with-errors.xlsx');

const SECTIONS = [
  { legend: 'Aroma', score: 10, comment: 'Citrus and pine hop aroma, moderate intensity.' },
  { legend: 'Appearance', score: 2, comment: 'Deep golden, persistent white head, brilliant.' },
  { legend: 'Flavor', score: 15, comment: 'Balanced malt backbone with resinous hop finish.' },
  { legend: 'Mouthfeel', score: 4, comment: 'Medium body, lively carbonation, dry finish.' },
  {
    legend: 'Overall Impression',
    score: 8,
    comment: 'A clean, well-executed example of the style.',
  },
] as const;

async function assertNoA11yViolations(page: Page, label: string): Promise<void> {
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze();
  expect(results.violations, `${label}:\n${JSON.stringify(results.violations, null, 2)}`).toEqual(
    [],
  );
}

async function submitKeycloakLogin(page: Page, username: string, password: string): Promise<void> {
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.locator('#kc-login').click();
}

function uniqueCompetitionName(): string {
  return `E2E A11y Comp ${Date.now()}-${crypto.randomUUID()}`;
}

function uniqueJudgeEmailSuffix(): string {
  return `${Date.now()}-${crypto.randomUUID()}`;
}

interface MailpitMessageSummary {
  ID: string;
  To: { Address: string; Name: string }[];
}

interface MailpitMessagesResponse {
  messages: MailpitMessageSummary[];
  total: number;
}

interface MailpitMessageDetail {
  Text: string;
}

// Mirrors us4/us6/us7's waitForMailpitMessageTo: invitation delivery is async via a DispatchJob.
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

// Mirrors us6/us7's readTemporaryPasswordFromInvitation.
async function readTemporaryPasswordFromInvitation(
  request: Page['request'],
  email: string,
): Promise<string> {
  const message = await waitForMailpitMessageTo(request, email);
  const detailResponse = await request.get(`${MAILPIT_ORIGIN}/api/v1/message/${message.ID}`);
  if (!detailResponse.ok()) {
    throw new Error(
      `Failed to fetch Mailpit message ${message.ID} for ${email}: ${detailResponse.status()}`,
    );
  }
  const detail = (await detailResponse.json()) as MailpitMessageDetail;
  const match = /Your temporary password is: \*([^*]+)\*/.exec(detail.Text);
  if (!match) {
    throw new Error(`Could not find a temporary password in the invitation email to ${email}.`);
  }
  return match[1];
}

// Mirrors us1/us6/us7's forced-temporary-password-change flow, ending on /judge/tables.
async function loginAsJudge(page: Page, email: string, password: string): Promise<void> {
  await page.goto('/');
  await page.waitForURL(new RegExp(`^${KEYCLOAK_ORIGIN}/`));
  await submitKeycloakLogin(page, email, password);

  await expect(page.locator('#password-new')).toBeVisible();
  const newPassword = `Judge-${crypto.randomUUID()}`;
  await page.locator('#password-new').fill(newPassword);
  await page.locator('#password-confirm').fill(newPassword);
  await page.locator('#kc-passwd-update-form button[type="submit"]').click();

  await page.waitForURL('**/judge/tables');
}

function mesaCard(page: Page, name: string): Locator {
  return page
    .locator('article.mesa-card')
    .filter({ has: page.getByRole('heading', { level: 3, name, exact: true }) });
}

function beerToken(scope: Page | Locator, blindCode: string): Locator {
  return scope.getByRole('button', { name: `Beer ${blindCode} — view details`, exact: true });
}

function judgeSeat(scope: Page | Locator, displayName: string): Locator {
  return scope.getByRole('button', { name: `Judge ${displayName} — view details`, exact: true });
}

// Mirrors us5/us6's pointerDrag: Angular CDK drag-drop is pointer-event-based, not native HTML5
// DnD, so a manual mouse-move sequence is required to trigger it reliably.
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
  await page.mouse.move(endX, endY, { steps: 5 });
  await page.mouse.up();
}

function competitionRow(page: Page, name: string): Locator {
  return page.locator('li.competition-list-row').filter({ hasText: name });
}

async function advanceCompetitionState(
  page: Page,
  competitionName: string,
  actionLabel: string,
): Promise<void> {
  const row = competitionRow(page, competitionName);
  await row.getByRole('button', { name: actionLabel, exact: true }).click();

  const dialog = page.getByRole('alertdialog', { name: 'Confirm advance competition state' });
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: 'Confirm', exact: true }).click();
  await expect(dialog).not.toBeVisible();
}

function sectionFieldset(page: Page, legend: string): Locator {
  return page
    .locator('fieldset.evaluation-section')
    .filter({ has: page.locator('legend', { hasText: legend }) });
}

async function fillEvaluationForm(page: Page): Promise<void> {
  for (const section of SECTIONS) {
    const fieldset = sectionFieldset(page, section.legend);
    await fieldset.getByLabel('Score').fill(String(section.score));
    await fieldset.getByLabel('Comment').fill(section.comment);
  }
}

async function submitEvaluationForNextSample(judgePage: Page): Promise<void> {
  const evaluateLink = judgePage.locator('a.evaluate-action');
  await expect(evaluateLink).toHaveCount(1);
  await evaluateLink.click();

  await judgePage.waitForURL(/\/judge\/tables\/[0-9a-fA-F-]+\/samples\/[0-9a-fA-F-]+$/);
  await fillEvaluationForm(judgePage);

  const [submitResponse] = await Promise.all([
    judgePage.waitForResponse(
      (response) =>
        response.request().method() === 'POST' &&
        /\/api\/v1\/me\/tables\/.+\/evaluations$/.test(new URL(response.url()).pathname),
    ),
    judgePage.getByRole('button', { name: 'Submit evaluation' }).click(),
  ]);
  expect(submitResponse.status()).toBe(201);
}

test.describe('WCAG 2.1 AA sweep — every organizer and judge route', () => {
  let judgeA: ProvisionedJudge;
  let judgeB: ProvisionedJudge;

  test.beforeAll(async () => {
    // Provisioned purely to get the JUDGE realm role assigned before the organizer's own
    // "Register judges" flow runs against these emails — see us6/us7's identical note
    // (Features/Judges/SendInvitationHandler never assigns a realm role to a freshly-created
    // Keycloak user).
    const suffix = uniqueJudgeEmailSuffix();
    judgeA = await createJudgeUser(`a11y-judge-a-${suffix}@birrapoint.local`);
    judgeB = await createJudgeUser(`a11y-judge-b-${suffix}@birrapoint.local`);
  });

  test.afterAll(async () => {
    if (judgeA?.id) {
      await deleteUser(judgeA.id);
    }
    if (judgeB?.id) {
      await deleteUser(judgeB.id);
    }
  });

  test('every route is free of WCAG 2.1 A/AA violations', async ({ page, browser }) => {
    test.setTimeout(300_000);

    // --- /organizer/dashboard ---
    await page.goto('/');
    await page.waitForURL(new RegExp(`^${KEYCLOAK_ORIGIN}/`));
    await submitKeycloakLogin(page, ORGANIZER_USERNAME, ORGANIZER_PASSWORD);
    await page.waitForURL('**/organizer/dashboard');
    await test.step('/organizer/dashboard', async () => {
      await assertNoA11yViolations(page, '/organizer/dashboard');
    });

    // --- /organizer/competitions/new (empty Basics step) ---
    await page.goto('/organizer/competitions/new');
    await expect(page.getByLabel('Name')).toBeVisible();
    await test.step('/organizer/competitions/new', async () => {
      await assertNoA11yViolations(page, '/organizer/competitions/new');
    });

    const competitionName = uniqueCompetitionName();
    await page.getByLabel('Name').fill(competitionName);
    await page.getByLabel('Venue').fill('Salón de Actos, Madrid');
    await page.getByLabel('Start date').fill('2026-09-01');
    await page.getByLabel('End date').fill('2026-09-03');
    await page.getByRole('button', { name: 'Next' }).click();
    await page.waitForURL(/\/organizer\/competitions\/[0-9a-fA-F-]{36}$/);
    const competitionId = page.url().split('/').pop()!;

    // --- /organizer/competitions/:id (Details step, reopened wizard) ---
    await expect(page.getByRole('button', { name: 'Save Draft' })).toBeVisible();
    await test.step('/organizer/competitions/:id', async () => {
      await assertNoA11yViolations(page, '/organizer/competitions/:id');
    });

    // --- /organizer/competitions/:id/import ---
    await page.goto(`/organizer/competitions/${competitionId}/import`);
    await expect(page.getByRole('heading', { name: 'Import beer entries' })).toBeVisible();
    await test.step('/organizer/competitions/:id/import (empty)', async () => {
      await assertNoA11yViolations(page, '/organizer/competitions/:id/import (empty)');
    });

    await page.getByLabel('Entries file (.xlsx)').setInputFiles(FIXTURE_PATH);
    await page.getByRole('button', { name: 'Upload' }).click();

    const resultsSection = page.getByRole('region', { name: 'Import results' });
    await expect(resultsSection).toBeVisible();
    const row3 = page.locator('tr[data-row-number="3"]');
    const row4 = page.locator('tr[data-row-number="4"]');
    await expect(row3).toContainText('StyleMismatch');
    await expect(row4).toContainText('Invalid');
    await test.step('/organizer/competitions/:id/import (results, correction UI)', async () => {
      await assertNoA11yViolations(
        page,
        '/organizer/competitions/:id/import (results, correction UI)',
      );
    });

    await row3.getByLabel('Filter styles').fill('American IPA');
    await row3.locator('select').selectOption('21A');
    await row3.getByRole('button', { name: 'Assign style' }).click();
    await expect(row3).toContainText('Valid');

    await row4.getByRole('button', { name: 'Exclude' }).click();
    await expect(row4).toContainText('Excluded');

    const consolidateButton = page.getByRole('button', { name: 'Consolidate' });
    await expect(consolidateButton).toBeEnabled();
    await consolidateButton.click();

    const importSummary = page.getByRole('region', { name: 'Consolidation summary' });
    await expect(importSummary.getByText('Imported: 3', { exact: true })).toBeVisible();
    await test.step('/organizer/competitions/:id/import (consolidation summary)', async () => {
      await assertNoA11yViolations(
        page,
        '/organizer/competitions/:id/import (consolidation summary)',
      );
    });

    const entryRows = importSummary.locator('tbody tr');
    const beerBlindCodeA = (await entryRows.nth(0).locator('td').first().innerText()).trim();
    const beerBlindCodeB = (await entryRows.nth(1).locator('td').first().innerText()).trim();

    // --- /organizer/competitions/:id/judges ---
    await page.goto(`/organizer/competitions/${competitionId}/judges`);
    await expect(page.getByRole('heading', { name: 'Judge management' })).toBeVisible();
    await test.step('/organizer/competitions/:id/judges (empty)', async () => {
      await assertNoA11yViolations(page, '/organizer/competitions/:id/judges (empty)');
    });

    await page
      .getByLabel('Judge emails (one per line, or comma-separated)')
      .fill([judgeA.email, judgeB.email].join('\n'));
    await page.getByRole('button', { name: 'Register judges' }).click();

    const report = page.locator('div[aria-label="Registration report"]');
    await expect(report).toBeVisible();
    await expect(report.getByText(judgeA.email, { exact: true })).toBeVisible();
    await test.step('/organizer/competitions/:id/judges (registration report)', async () => {
      await assertNoA11yViolations(
        page,
        '/organizer/competitions/:id/judges (registration report)',
      );
    });

    const judgeATempPassword = await readTemporaryPasswordFromInvitation(
      page.request,
      judgeA.email,
    );

    // --- /organizer/competitions/:id/tables ---
    await page.goto(`/organizer/competitions/${competitionId}/tables`);
    await expect(page.getByRole('heading', { name: 'Table management' })).toBeVisible();
    await test.step('/organizer/competitions/:id/tables (empty)', async () => {
      await assertNoA11yViolations(page, '/organizer/competitions/:id/tables (empty)');
    });

    await page.getByLabel('New table name').fill('Mesa 1');
    await page.getByRole('button', { name: 'Add table' }).click();
    const mesa1 = mesaCard(page, 'Mesa 1');
    await expect(mesa1).toBeVisible();
    const mesa1Id = await mesa1.getAttribute('data-table-id');
    if (!mesa1Id) {
      throw new Error('Table id missing from data-table-id attribute.');
    }
    const mesa1Beers = page.locator(`#beers-${mesa1Id}`);
    const mesa1Judges = page.locator(`#judges-${mesa1Id}`);

    await pointerDrag(page, beerToken(page, beerBlindCodeA), mesa1Beers);
    await expect(beerToken(mesa1Beers, beerBlindCodeA)).toBeVisible();
    await pointerDrag(page, beerToken(page, beerBlindCodeB), mesa1Beers);
    await expect(beerToken(mesa1Beers, beerBlindCodeB)).toBeVisible();

    const judgeADisplayName = judgeA.email.slice(0, judgeA.email.indexOf('@'));
    await pointerDrag(page, judgeSeat(page, judgeADisplayName), mesa1Judges);
    await expect(judgeSeat(mesa1Judges, judgeADisplayName)).toBeVisible();

    await test.step('/organizer/competitions/:id/tables (assigned)', async () => {
      await assertNoA11yViolations(page, '/organizer/competitions/:id/tables (assigned)');
    });

    // --- Draft -> Active -> InEvaluation, via the real dashboard advance-state control ---
    await page.goto('/organizer/dashboard');
    await advanceCompetitionState(page, competitionName, 'Activate');
    await expect(competitionRow(page, competitionName).locator('.badge')).toHaveText('Active');
    await advanceCompetitionState(page, competitionName, 'Start evaluation');
    await expect(competitionRow(page, competitionName).locator('.badge')).toHaveText(
      'InEvaluation',
    );

    // --- /organizer/competitions/:id/monitor ---
    await competitionRow(page, competitionName).locator('a.competition-list-item').click();
    await page.waitForURL(new RegExp(`/organizer/competitions/${competitionId}/monitor$`));
    const tableRow = page.locator(`li.table-progress-row[data-table-id="${mesa1Id}"]`);
    await expect(tableRow).toBeVisible();
    await test.step('/organizer/competitions/:id/monitor', async () => {
      await assertNoA11yViolations(page, '/organizer/competitions/:id/monitor');
    });

    // --- Judge session: independent browser context/session, real forced-password-change login ---
    const judgeContext = await browser.newContext();
    const judgePage = await judgeContext.newPage();

    try {
      await loginAsJudge(judgePage, judgeA.email, judgeATempPassword);

      // --- /judge/tables ---
      const tableLink = judgePage.getByRole('link', { name: new RegExp('Mesa 1') });
      await expect(tableLink).toContainText('Order not fixed');
      await test.step('/judge/tables', async () => {
        await assertNoA11yViolations(judgePage, '/judge/tables');
      });

      // --- /judge/tables/:tableId (order not yet fixed) ---
      await tableLink.click();
      await judgePage.waitForURL(`**/judge/tables/${mesa1Id}`);
      await expect(judgePage.locator('li.sample-row')).toHaveCount(2);
      await test.step('/judge/tables/:tableId (order not fixed)', async () => {
        await assertNoA11yViolations(judgePage, '/judge/tables/:tableId (order not fixed)');
      });

      await judgePage.getByRole('button', { name: 'Fix order' }).click();
      const fixDialog = judgePage.getByRole('alertdialog', { name: 'Confirm fix order' });
      await expect(fixDialog).toBeVisible();
      await fixDialog.getByRole('button', { name: 'Confirm fix order' }).click();
      await expect(judgePage.locator('p.order-status--fixed')).toBeVisible();

      // --- /judge/tables/:tableId/discrepancies (empty state — no discrepancy needs to exist for
      // this route to render; DiscrepancyAlertComponent shows a "No open discrepancies" message) ---
      await judgePage.goto(`/judge/tables/${mesa1Id}/discrepancies`);
      await expect(judgePage.getByRole('heading', { name: 'Discrepancy alerts' })).toBeVisible();
      await test.step('/judge/tables/:tableId/discrepancies (empty)', async () => {
        await assertNoA11yViolations(judgePage, '/judge/tables/:tableId/discrepancies (empty)');
      });

      await judgePage.goto(`/judge/tables/${mesa1Id}`);

      // --- /judge/tables/:tableId/samples/:beerEntryId ---
      const evaluateLink = judgePage.locator('a.evaluate-action');
      await expect(evaluateLink).toHaveCount(1);
      await evaluateLink.click();
      await judgePage.waitForURL(/\/judge\/tables\/[0-9a-fA-F-]+\/samples\/[0-9a-fA-F-]+$/);
      await expect(judgePage.locator('h1')).toHaveText(new RegExp('.+'));
      await test.step('/judge/tables/:tableId/samples/:beerEntryId', async () => {
        await assertNoA11yViolations(judgePage, '/judge/tables/:tableId/samples/:beerEntryId');
      });

      await fillEvaluationForm(judgePage);
      const [firstSubmit] = await Promise.all([
        judgePage.waitForResponse(
          (response) =>
            response.request().method() === 'POST' &&
            /\/api\/v1\/me\/tables\/.+\/evaluations$/.test(new URL(response.url()).pathname),
        ),
        judgePage.getByRole('button', { name: 'Submit evaluation' }).click(),
      ]);
      expect(firstSubmit.status()).toBe(201);
      await judgePage.waitForURL(`**/judge/tables/${mesa1Id}`);

      // Second sample: needed so this judge's evaluation coverage of Mesa 1 is complete, which is
      // the close precondition (CloseTableRules — every active judge must have a submitted row for
      // every sample) that lets the organizer's competition reach Finalized below and expose
      // /dispatch.
      await submitEvaluationForNextSample(judgePage);
      await judgePage.waitForURL(`**/judge/tables/${mesa1Id}`);

      const closeButton = judgePage.getByRole('button', { name: 'Close table' });
      await expect(closeButton).toBeVisible();
      await closeButton.click();
      const closeDialog = judgePage.getByRole('alertdialog', { name: 'Confirm close table' });
      await expect(closeDialog).toBeVisible();
      await closeDialog.getByRole('button', { name: 'Confirm close table' }).click();
      await expect(judgePage.locator('p.order-status--closed')).toBeVisible();
    } finally {
      await judgeContext.close();
    }

    // --- Finalize (InEvaluation -> Finalized) and reach /organizer/competitions/:id/dispatch ---
    await page.goto('/organizer/dashboard');
    await advanceCompetitionState(page, competitionName, 'Finalize');
    await expect(competitionRow(page, competitionName).locator('.badge')).toHaveText('Finalized');

    await competitionRow(page, competitionName).locator('a.competition-list-item').click();
    await page.waitForURL(new RegExp(`/organizer/competitions/${competitionId}/monitor$`));
    await page.getByRole('link', { name: 'Results & Dispatch' }).click();
    await page.waitForURL(new RegExp(`/organizer/competitions/${competitionId}/dispatch$`));
    await expect(page.getByRole('heading', { name: 'Results & Dispatch' })).toBeVisible();
    // Dispatch status rows populate asynchronously once the Finalize-triggered pipeline actually
    // starts — an active retry via the real "Refresh status" control (mirroring
    // us10-dispatch.spec.ts's toPass polling) is more robust under load than a single passive wait.
    await expect(async () => {
      await page.getByRole('button', { name: 'Refresh status' }).click();
      await expect(page.locator('tr[data-participant-id]').first()).toBeVisible();
    }).toPass({ timeout: 30_000 });
    await test.step('/organizer/competitions/:id/dispatch', async () => {
      await assertNoA11yViolations(page, '/organizer/competitions/:id/dispatch');
    });
  });
});
