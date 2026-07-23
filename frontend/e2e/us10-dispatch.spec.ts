import path from 'node:path';
import fs from 'node:fs/promises';
import os from 'node:os';
// Test-only dependency for E2E ZIP-structure verification (adm-zip) -- not part of the shipped
// app bundle, nothing under frontend/src/ imports it.
import AdmZip from 'adm-zip';
import { test, expect, Page, Locator } from '@playwright/test';
import { createJudgeUser, deleteUser, ProvisionedJudge } from './support/keycloak-admin';

// quickstart.md scenario 10 / spec.md US10 (FR-036/FR-040/FR-041): finalizing a competition with
// every table closed starts the PDF/ZIP/email dispatch pipeline in the background without
// blocking the UI; the downloaded archive follows the
// `/CompetitionName/ParticipantID/Style_BlindCode.pdf` hierarchy; every participant automatically
// receives their own PDFs by email; and the organizer's dispatch screen surfaces per-recipient
// delivery status with a retry affordance.

const KEYCLOAK_ORIGIN = 'http://localhost:8081';
const ORGANIZER_USERNAME = 'organizer';
const ORGANIZER_PASSWORD = 'organizer';

const MAILPIT_ORIGIN = 'http://localhost:8025';
const MAILPIT_POLL_TIMEOUT_MS = 20_000;
const MAILPIT_POLL_INTERVAL_MS = 500;

// The full GeneratePdfs -> BundleZip -> SendResultEmail pipeline runs against a real DispatchWorker
// (wake-up-signalled, not just the 30s safety-net poll) but still does real PDF rendering + a real
// SMTP send per participant, so it's given a generous ceiling.
const DISPATCH_POLL_TIMEOUT_MS = 45_000;
const DISPATCH_POLL_INTERVAL_MS = 1_000;

const FIXTURE_PATH = path.resolve(__dirname, 'fixtures/tables-assignment.xlsx');

// The fixture always imports all 3 of its rows in one go (Coi/Bos/Neutral Participant, one entry
// each). Only two of the three ("Neutral"/"Bos", as in us8/us9) get assigned to the table this
// spec drives through evaluation and close -- the third ("Coi") is deliberately left unassigned,
// same as us8/us9 do, so there's no COI/BOS interaction to account for from the judge's side.
// GeneratePdfsHandler/BundleZipHandler/GetDispatchStatus all operate over every participant/entry
// in the *competition*, not just those seated at a table, so that third, never-tasted entry still
// gets its own (zero-evaluation) score sheet, ZIP folder, and result email -- this test asserts on
// all three participants precisely to prove that competition-wide scope, not just the two that
// were actually evaluated.
const FIRST_STYLE_CODE = '1A';
const SECOND_STYLE_CODE = '20C';
const THIRD_STYLE_CODE = '21A';
const FIRST_PARTICIPANT_EMAIL = 'neutral.participant@brew.example';
const SECOND_PARTICIPANT_EMAIL = 'bos.participant@brew.example';
const THIRD_PARTICIPANT_EMAIL = 'coi.participant@brew.example';

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

async function submitKeycloakLogin(page: Page, username: string, password: string): Promise<void> {
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.locator('#kc-login').click();
}

function uniqueCompetitionName(): string {
  return `E2E Dispatch Comp ${Date.now()}-${crypto.randomUUID()}`;
}

// Mirrors us8/us9's createCompetition: creates via the wizard's Basics step and returns the
// persisted competitionId, leaving the competition Draft.
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

// Mirrors us8/us9's blindCodeForStyle: reads the Consolidation summary's "Created entries" table.
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

// Mirrors us5/us6/us8/us9's pointerDrag: Angular CDK drag-drop is pointer-event-based, not native
// HTML5 DnD, so a manual mouse-move sequence is required to trigger it reliably.
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

// Mirrors us8/us9's advanceCompetitionState: drives the real dashboard advance-state control
// (T102/FR-051) rather than a token-capture workaround.
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

interface MailpitMessageSummary {
  ID: string;
  To: { Address: string; Name: string }[];
  Subject: string;
}

interface MailpitMessagesResponse {
  messages: MailpitMessageSummary[];
  total: number;
}

interface MailpitAttachment {
  FileName: string;
  ContentType: string;
}

interface MailpitMessageDetail {
  Text: string;
  Attachments: MailpitAttachment[];
}

// Mirrors us4/us6/us8/us9's waitForMailpitMessageTo: invitation/result delivery is async via a
// DispatchJob, so it does not land synchronously.
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

async function fetchMailpitMessageDetail(
  request: Page['request'],
  messageId: string,
): Promise<MailpitMessageDetail> {
  const detailResponse = await request.get(`${MAILPIT_ORIGIN}/api/v1/message/${messageId}`);
  if (!detailResponse.ok()) {
    throw new Error(`Failed to fetch Mailpit message ${messageId}: ${detailResponse.status()}`);
  }
  return (await detailResponse.json()) as MailpitMessageDetail;
}

// Mirrors us6/us8/us9's readTemporaryPasswordFromInvitation: SendInvitationHandler resets the
// invited judge's Keycloak password unconditionally on every call, including when the account was
// pre-provisioned (createJudgeUser below, purely to get the JUDGE realm role assigned) — so the
// only reliable current password is the one actually delivered via email.
async function readTemporaryPasswordFromInvitation(
  request: Page['request'],
  email: string,
): Promise<string> {
  const message = await waitForMailpitMessageTo(request, email);
  const detail = await fetchMailpitMessageDetail(request, message.ID);
  const match = /Your temporary password is: \*([^*]+)\*/.exec(detail.Text);
  if (!match) {
    throw new Error(`Could not find a temporary password in the invitation email to ${email}.`);
  }
  return match[1];
}

// Mirrors us1/us6/us8/us9's forced-temporary-password-change flow, ending on /judge/tables.
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

// Submits an evaluation for whichever sample the fixed order currently offers next (the caller
// doesn't need to know which blind code that is), returning it so callers who do care can use it.
async function submitEvaluationForNextSample(judgePage: Page): Promise<string> {
  const evaluateLink = judgePage.locator('a.evaluate-action');
  await expect(evaluateLink).toHaveCount(1);
  await evaluateLink.click();

  await judgePage.waitForURL(/\/judge\/tables\/[0-9a-fA-F-]+\/samples\/[0-9a-fA-F-]+$/);
  const blindCode = (await judgePage.locator('h1').innerText()).trim();

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

  return blindCode;
}

interface DispatchStatusRow {
  participantId: string;
  email: string;
  status: string;
  attempts: number;
  lastError: string | null;
}

test.describe('US10 — finalize and automated results dispatch', () => {
  let judge: ProvisionedJudge;

  test.beforeAll(async () => {
    // Provisioned purely to get the JUDGE realm role assigned before the organizer's own
    // "Register judges" flow runs against this same email — see us6/us8/us9's identical note
    // (Features/Judges/SendInvitationHandler never assigns a realm role to a freshly-created
    // Keycloak user).
    judge = await createJudgeUser(
      `us10-judge-${Date.now()}-${crypto.randomUUID()}@birrapoint.local`,
    );
  });

  test.afterAll(async () => {
    if (judge?.id) {
      await deleteUser(judge.id);
    }
  });

  test('finalizing closes the loop: responsive UI, correct ZIP hierarchy, per-participant emails, and a clean retry affordance', async ({
    page,
    browser,
  }) => {
    // Full organizer setup + a real Keycloak-authenticated judge session + the full
    // GeneratePdfs -> BundleZip -> SendResultEmail pipeline comfortably exceeds Playwright's
    // 30s default.
    test.setTimeout(180_000);

    // --- Organizer: create competition, import entries, consolidate, register the judge, create
    // one table, assign the judge + both entries onto it ---
    await page.goto('/');
    await page.waitForURL(new RegExp(`^${KEYCLOAK_ORIGIN}/`));
    await submitKeycloakLogin(page, ORGANIZER_USERNAME, ORGANIZER_PASSWORD);
    await page.waitForURL('**/organizer/dashboard');

    const competitionName = uniqueCompetitionName();
    const competitionId = await createCompetition(page, competitionName);

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

    const firstBlindCode = await blindCodeForStyle(summary, FIRST_STYLE_CODE);
    const secondBlindCode = await blindCodeForStyle(summary, SECOND_STYLE_CODE);
    const thirdBlindCode = await blindCodeForStyle(summary, THIRD_STYLE_CODE);

    await page.goto(`/organizer/competitions/${competitionId}/judges`);
    await expect(page.getByRole('heading', { name: 'Judge management' })).toBeVisible();

    await page.getByLabel('Judge emails (one per line, or comma-separated)').fill(judge.email);
    await page.getByRole('button', { name: 'Register judges' }).click();

    const report = page.locator('div[aria-label="Registration report"]');
    await expect(report).toBeVisible();
    await expect(report.getByText(judge.email, { exact: true })).toBeVisible();

    const judgeTempPassword = await readTemporaryPasswordFromInvitation(page.request, judge.email);

    await page.goto(`/organizer/competitions/${competitionId}/tables`);
    await expect(page.getByRole('heading', { name: 'Table management' })).toBeVisible();

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

    await pointerDrag(page, beerToken(page, firstBlindCode), mesa1Beers);
    await expect(beerToken(mesa1Beers, firstBlindCode)).toBeVisible();
    await pointerDrag(page, beerToken(page, secondBlindCode), mesa1Beers);
    await expect(beerToken(mesa1Beers, secondBlindCode)).toBeVisible();

    const judgeDisplayNameSeat = judge.email.slice(0, judge.email.indexOf('@'));
    await pointerDrag(page, judgeSeat(page, judgeDisplayNameSeat), mesa1Judges);
    await expect(judgeSeat(mesa1Judges, judgeDisplayNameSeat)).toBeVisible();

    // --- Draft -> Active -> InEvaluation ---
    await page.goto('/organizer/dashboard');
    await advanceCompetitionState(page, competitionName, 'Activate');
    await expect(competitionRow(page, competitionName).locator('.badge')).toHaveText('Active');
    await advanceCompetitionState(page, competitionName, 'Start evaluation');
    await expect(competitionRow(page, competitionName).locator('.badge')).toHaveText(
      'InEvaluation',
    );

    // --- Judge: fix order, evaluate both samples, close the table ---
    const judgeContext = await browser.newContext();
    const judgePage = await judgeContext.newPage();

    try {
      await loginAsJudge(judgePage, judge.email, judgeTempPassword);

      const tableLink = judgePage.getByRole('link', { name: new RegExp('Mesa 1') });
      await expect(tableLink).toContainText('Order not fixed');
      await tableLink.click();
      await judgePage.waitForURL(`**/judge/tables/${mesa1Id}`);
      await expect(judgePage.locator('li.sample-row')).toHaveCount(2);

      await judgePage.getByRole('button', { name: 'Fix order' }).click();
      const fixDialog = judgePage.getByRole('alertdialog', { name: 'Confirm fix order' });
      await expect(fixDialog).toBeVisible();
      await fixDialog.getByRole('button', { name: 'Confirm fix order' }).click();
      await expect(judgePage.locator('p.order-status--fixed')).toBeVisible();

      await submitEvaluationForNextSample(judgePage);
      await judgePage.waitForURL(`**/judge/tables/${mesa1Id}`);

      await submitEvaluationForNextSample(judgePage);
      await judgePage.waitForURL(`**/judge/tables/${mesa1Id}`);

      const closeButton = judgePage.getByRole('button', { name: 'Close table' });
      await expect(closeButton).toBeVisible();
      await closeButton.click();
      const closeDialog = judgePage.getByRole('alertdialog', { name: 'Confirm close table' });
      await expect(closeDialog).toBeVisible();
      await closeDialog.getByRole('button', { name: 'Confirm close table' }).click();
      await expect(closeDialog).not.toBeVisible();
      await expect(judgePage.locator('p.order-status--closed')).toBeVisible();
    } finally {
      await judgeContext.close();
    }

    // --- Acceptance Scenario 1: InEvaluation -> Finalized, and the dashboard stays responsive
    // (the confirm dialog resolves and the badge updates immediately, with no wait on the
    // background pipeline) ---
    await page.goto('/organizer/dashboard');
    await advanceCompetitionState(page, competitionName, 'Finalize');
    await expect(competitionRow(page, competitionName).locator('.badge')).toHaveText('Finalized');

    // --- Navigate to the results & dispatch screen via the monitor page's link ---
    await competitionRow(page, competitionName).locator('a.competition-list-item').click();
    await page.waitForURL(new RegExp(`/organizer/competitions/${competitionId}/monitor$`));

    await page.getByRole('link', { name: 'Results & Dispatch' }).click();
    await page.waitForURL(new RegExp(`/organizer/competitions/${competitionId}/dispatch$`));
    await expect(page.getByRole('heading', { name: 'Results & Dispatch' })).toBeVisible();

    // --- Acceptance Scenario 2/3: poll until the background pipeline (GeneratePdfs -> BundleZip
    // -> SendResultEmail) has reached a terminal state for all three participants and the download
    // button is enabled, proving the pipeline actually ran to completion ---
    const downloadButton = page.getByRole('button', { name: 'Download results ZIP' });
    await expect(async () => {
      await page.getByRole('button', { name: 'Refresh status' }).click();
      const rows = page.locator('tr[data-participant-id]');
      await expect(rows).toHaveCount(3);
      const badges = rows.locator('td span.badge');
      const statuses = await badges.allTextContents();
      expect(statuses).toEqual(['Completed', 'Completed', 'Completed']);
      await expect(downloadButton).toBeEnabled();
    }).toPass({ timeout: DISPATCH_POLL_TIMEOUT_MS, intervals: [DISPATCH_POLL_INTERVAL_MS] });

    // No "Retry" affordance anywhere once every row is Completed (proves the UI correctly hides
    // it when not applicable, Acceptance Scenario 4's negative case).
    await expect(page.getByRole('button', { name: 'Retry', exact: true })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Retry all failed' })).toHaveCount(0);

    // --- Acceptance Scenario 2: download the ZIP and verify its hierarchy ---
    const [download] = await Promise.all([page.waitForEvent('download'), downloadButton.click()]);
    const downloadPath = path.join(
      await fs.mkdtemp(path.join(os.tmpdir(), 'us10-zip-')),
      'results.zip',
    );
    await download.saveAs(downloadPath);

    // Read the participantId <-> email mapping straight from the same dispatch-status endpoint the
    // page itself calls, using the organizer's own session cookie/bearer via an authenticated page
    // request (same "capture this session's own request" technique other specs in this suite use
    // for data with no dedicated UI exposure).
    const [statusResponse] = await Promise.all([
      page.waitForResponse(
        (response) =>
          response.request().method() === 'GET' &&
          new URL(response.url()).pathname === `/api/v1/competitions/${competitionId}/dispatch`,
      ),
      page.getByRole('button', { name: 'Refresh status' }).click(),
    ]);
    const statusRows = (await statusResponse.json()) as DispatchStatusRow[];
    const firstParticipant = statusRows.find((r) => r.email === FIRST_PARTICIPANT_EMAIL);
    const secondParticipant = statusRows.find((r) => r.email === SECOND_PARTICIPANT_EMAIL);
    const thirdParticipant = statusRows.find((r) => r.email === THIRD_PARTICIPANT_EMAIL);
    if (!firstParticipant || !secondParticipant || !thirdParticipant) {
      throw new Error(
        `Expected dispatch rows for all three fixture participants, got: ${JSON.stringify(statusRows)}`,
      );
    }

    const zip = new AdmZip(downloadPath);
    const entryNames = zip.getEntries().map((entry) => entry.entryName);
    expect(entryNames.sort()).toEqual(
      [
        `${competitionName}/${firstParticipant.participantId}/${FIRST_STYLE_CODE}_${firstBlindCode}.pdf`,
        `${competitionName}/${secondParticipant.participantId}/${SECOND_STYLE_CODE}_${secondBlindCode}.pdf`,
        `${competitionName}/${thirdParticipant.participantId}/${THIRD_STYLE_CODE}_${thirdBlindCode}.pdf`,
      ].sort(),
    );

    // --- Acceptance Scenario 3: each participant automatically receives an email with their own
    // evaluation sheet(s) attached as a PDF -- including the third participant, whose entry was
    // never evaluated (still gets a zero-evaluation score sheet, per FR-040's "every evaluation
    // sheet" scope being the whole competition, not just tasted samples) ---
    for (const [email, styleCode, blindCode] of [
      [FIRST_PARTICIPANT_EMAIL, FIRST_STYLE_CODE, firstBlindCode],
      [SECOND_PARTICIPANT_EMAIL, SECOND_STYLE_CODE, secondBlindCode],
      [THIRD_PARTICIPANT_EMAIL, THIRD_STYLE_CODE, thirdBlindCode],
    ] as const) {
      const message = await waitForMailpitMessageTo(page.request, email);
      const detail = await fetchMailpitMessageDetail(page.request, message.ID);
      const expectedFileName = `${styleCode}_${blindCode}.pdf`;
      expect(detail.Attachments.map((a) => a.FileName)).toContain(expectedFileName);
      expect(detail.Attachments.find((a) => a.FileName === expectedFileName)?.ContentType).toBe(
        'application/pdf',
      );
    }
  });
});
