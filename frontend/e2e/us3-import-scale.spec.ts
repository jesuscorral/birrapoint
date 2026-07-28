import { test, expect, Page } from '@playwright/test';
import AdmZip from 'adm-zip';

// SC-006 scale check (tasks.md T090): a 500-row import with ~20% (100) BJCP-style-catalog errors
// must all be resolvable and the whole batch consolidated within one continuous session — no
// multi-step/multi-visit friction — not just the small 3-4 row fixture us3-import.spec.ts exercises
// for the correction *mechanics* themselves (assign-style vs. exclude). This spec reuses "exclude"
// for every error row since the point here is scale/throughput through the real Mapping &
// Correction flow, not re-proving assign-style (already covered by us3-import.spec.ts).

const KEYCLOAK_ORIGIN = 'http://localhost:8081';
const ORGANIZER_USERNAME = 'organizer';
const ORGANIZER_PASSWORD = 'organizer';

const TOTAL_ROWS = 500;
const MISMATCH_EVERY = 5; // every 5th row -> exactly 100 of 500 rows
const VALID_STYLE_CODES = ['21A', '20C', '1A']; // confirmed-valid BJCP codes, reused from
// us3/us5/us6-order.spec.ts's fixtures
const MISMATCH_STYLE_CODE = '99Z'; // same deliberately-invalid code us3-import.spec.ts's fixture uses

async function submitKeycloakLogin(page: Page, username: string, password: string): Promise<void> {
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.locator('#kc-login').click();
}

function uniqueCompetitionName(): string {
  return `E2E Import Scale Comp ${Date.now()}-${crypto.randomUUID()}`;
}

// Mirrors us3-import.spec.ts's createCompetition: creates via the wizard's Basics step and
// returns the persisted competitionId, leaving the competition Draft (import is allowed in
// Draft/Active per contracts/rest-api.md).
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

function escapeXml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&apos;');
}

const COLUMN_LETTERS = ['A', 'B', 'C', 'D', 'E'];
const HEADER = ['ParticipantName', 'ParticipantEmail', 'BeerName', 'Style', 'Collaborators'];

function buildRowXml(rowNumber: number, values: string[]): string {
  const cells = values
    .map((value, colIndex) => {
      const ref = `${COLUMN_LETTERS[colIndex]}${rowNumber}`;
      return `<c r="${ref}" t="inlineStr"><is><t>${escapeXml(value)}</t></is></c>`;
    })
    .join('');
  return `<row r="${rowNumber}">${cells}</row>`;
}

// Hand-assembled, spec-minimal OOXML package (inline strings — no sharedStrings.xml needed; a
// minimal styles.xml so cells without an explicit `s` attribute still resolve against a real
// cellXfs entry). There's no existing fixture-generation script in this repo to reuse at this
// scale (frontend/e2e/fixtures/*.xlsx are static, pre-built files) and pulling in a new
// xlsx-writing dependency for a single scale test isn't justified (Constitution Principle V) —
// adm-zip is already a devDependency (used by us10-dispatch.spec.ts to *read* a zip), and it's
// equally capable of *writing* one.
function buildFixtureBuffer(rows: string[][]): Buffer {
  const sheetRows = [buildRowXml(1, HEADER), ...rows.map((row, i) => buildRowXml(i + 2, row))].join(
    '',
  );

  const contentTypes = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
</Types>`;

  const rootRels = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>`;

  const workbook = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <sheets>
    <sheet name="Entries" sheetId="1" r:id="rId1"/>
  </sheets>
</workbook>`;

  const workbookRels = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>`;

  const worksheet = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>${sheetRows}</sheetData></worksheet>`;

  const styles = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts>
  <fills count="1"><fill><patternFill patternType="none"/></fill></fills>
  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
</styleSheet>`;

  const zip = new AdmZip();
  zip.addFile('[Content_Types].xml', Buffer.from(contentTypes, 'utf-8'));
  zip.addFile('_rels/.rels', Buffer.from(rootRels, 'utf-8'));
  zip.addFile('xl/workbook.xml', Buffer.from(workbook, 'utf-8'));
  zip.addFile('xl/_rels/workbook.xml.rels', Buffer.from(workbookRels, 'utf-8'));
  zip.addFile('xl/worksheets/sheet1.xml', Buffer.from(worksheet, 'utf-8'));
  zip.addFile('xl/styles.xml', Buffer.from(styles, 'utf-8'));
  return zip.toBuffer();
}

function buildFixture(): { buffer: Buffer; mismatchRowNumbers: number[] } {
  const rows: string[][] = [];
  const mismatchRowNumbers: number[] = [];
  const runSuffix = `${Date.now()}-${crypto.randomUUID()}`;

  for (let i = 0; i < TOTAL_ROWS; i++) {
    const rowNumber = i + 1; // 1-indexed data row (header excluded) -- matches the app's own
    // per-row `rowNumber` (see contracts/import-file.md's Example table / us3-import.spec.ts's
    // `tr[data-row-number="1"]` referring to the first *data* row, not the header).
    const isMismatch = rowNumber % MISMATCH_EVERY === 0;
    const style = isMismatch
      ? MISMATCH_STYLE_CODE
      : VALID_STYLE_CODES[i % VALID_STYLE_CODES.length];

    rows.push([
      `Scale Participant ${rowNumber}`,
      `scale-${runSuffix}-${rowNumber}@brew.example`,
      `Scale Beer ${rowNumber}`,
      style,
      '',
    ]);
    if (isMismatch) {
      mismatchRowNumbers.push(rowNumber);
    }
  }

  return { buffer: buildFixtureBuffer(rows), mismatchRowNumbers };
}

test.describe('US3 scale check — 500-row import with 20% style errors (SC-006)', () => {
  test('every StyleMismatch row is resolvable and the full batch consolidates in one session', async ({
    page,
  }) => {
    // 500-row upload + up to 100 sequential correction round-trips comfortably exceeds
    // Playwright's 30s default.
    test.setTimeout(180_000);

    const { buffer, mismatchRowNumbers } = buildFixture();
    expect(mismatchRowNumbers).toHaveLength(100);

    await page.goto('/');
    await page.waitForURL(new RegExp(`^${KEYCLOAK_ORIGIN}/`));
    await submitKeycloakLogin(page, ORGANIZER_USERNAME, ORGANIZER_PASSWORD);
    await page.waitForURL('**/organizer/dashboard');

    const competitionId = await createCompetition(page);

    await page.goto(`/organizer/competitions/${competitionId}/import`);
    await expect(page.getByRole('heading', { name: 'Import beer entries' })).toBeVisible();

    await page.getByLabel('Entries file (.xlsx)').setInputFiles({
      name: 'entries-scale-500.xlsx',
      mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      buffer,
    });
    await page.getByRole('button', { name: 'Upload' }).click();

    const resultsSection = page.getByRole('region', { name: 'Import results' });
    await expect(resultsSection).toBeVisible({ timeout: 30_000 });
    await expect(page.locator('tr[data-row-number]')).toHaveCount(TOTAL_ROWS);

    await expect(
      page.getByText(
        `${mismatchRowNumbers.length} row(s) need correction before you can consolidate.`,
      ),
    ).toBeVisible();

    const consolidateButton = page.getByRole('button', { name: 'Consolidate' });
    await expect(consolidateButton).toBeDisabled();

    // Resolve every StyleMismatch row via Exclude -- a real per-row round-trip through the actual
    // Mapping & Correction UI (contracts/import-file.md), not a shortcut through the API -- proving
    // all 100 errors are resolvable inside the same session as the rest of the 500-row batch
    // (SC-006). assign-style specifically is already covered at small scale by us3-import.spec.ts.
    for (const rowNumber of mismatchRowNumbers) {
      const row = page.locator(`tr[data-row-number="${rowNumber}"]`);
      await expect(row).toContainText('StyleMismatch');
      await row.getByRole('button', { name: 'Exclude' }).click();
      await expect(row).toContainText('Excluded');
    }

    await expect(
      page.getByText('0 row(s) need correction before you can consolidate.'),
    ).toBeVisible();
    await expect(consolidateButton).toBeEnabled();

    await consolidateButton.click();

    const summary = page.getByRole('region', { name: 'Consolidation summary' });
    await expect(summary).toBeVisible({ timeout: 30_000 });

    const expectedImported = TOTAL_ROWS - mismatchRowNumbers.length;
    await expect(summary.getByText(`Imported: ${expectedImported}`, { exact: true })).toBeVisible();
    await expect(
      summary.getByText(`Excluded: ${mismatchRowNumbers.length}`, { exact: true }),
    ).toBeVisible();

    const entryRows = summary.locator('tbody tr');
    await expect(entryRows).toHaveCount(expectedImported);
  });
});
