import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';

import { ApiError } from '../../core/api/api-error';
import { EntryImportApiService } from './entry-import-api.service';
import type { ImportBatch, ImportRow, StyleSummary } from './entry-import-api.service';
import { EntryImportComponent } from './entry-import.component';

const styles: StyleSummary[] = [
  {
    code: '4A',
    name: 'Munich Helles',
    categoryNumber: '4',
    categoryName: 'Pale Malty European Lager',
  },
  { code: '21A', name: 'American IPA', categoryNumber: '21', categoryName: 'IPA' },
];

function batchFixture(): ImportBatch {
  return {
    importId: 'i1',
    rows: [
      {
        rowNumber: 1,
        status: 'Valid',
        data: {
          participantName: 'Ada Lovelace',
          participantEmail: 'ada@example.com',
          beerName: 'Golden Helles',
          style: 'Munich Helles',
          collaborators: [],
          resolvedStyleCode: '4A',
        },
        error: null,
      },
      {
        rowNumber: 2,
        status: 'StyleMismatch',
        data: {
          participantName: 'Grace Hopper',
          participantEmail: 'grace@example.com',
          beerName: 'Hazy Dream',
          style: 'IPAA',
          collaborators: [],
          resolvedStyleCode: null,
        },
        error: "Style 'IPAA' not found in the BJCP 2021 catalog.",
      },
      {
        rowNumber: 3,
        status: 'Invalid',
        data: {
          participantName: null,
          participantEmail: null,
          beerName: 'Mystery Brew',
          style: null,
          collaborators: [],
          resolvedStyleCode: null,
        },
        error: 'Missing participant email.',
      },
    ],
  };
}

function selectFile(fixture: ReturnType<typeof TestBed.createComponent>, file: File): void {
  const input = fixture.nativeElement.querySelector('input[type="file"]') as HTMLInputElement;
  Object.defineProperty(input, 'files', { value: [file], writable: false, configurable: true });
  input.dispatchEvent(new Event('change'));
}

function buttonWithText(root: Element, text: string): HTMLButtonElement {
  const buttons = [...root.querySelectorAll('button')] as HTMLButtonElement[];
  const match = buttons.find((button) => button.textContent?.trim() === text);
  if (!match) {
    throw new Error(`No button with text "${text}" found`);
  }
  return match;
}

describe('EntryImportComponent', () => {
  let fakeApi: {
    upload: jest.Mock;
    getImport: jest.Mock;
    resolveRow: jest.Mock;
    consolidate: jest.Mock;
    getStyles: jest.Mock;
  };

  beforeEach(() => {
    fakeApi = {
      upload: jest.fn(),
      getImport: jest.fn(),
      resolveRow: jest.fn(),
      consolidate: jest.fn(),
      getStyles: jest.fn().mockReturnValue(of(styles)),
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: EntryImportApiService, useValue: fakeApi },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ id: 'c1' }) } },
        },
      ],
    });
  });

  function createComponent() {
    const fixture = TestBed.createComponent(EntryImportComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('disables Upload until a file is chosen', () => {
    const fixture = createComponent();
    const uploadButton = buttonWithText(fixture.nativeElement, 'Upload');
    expect(uploadButton.disabled).toBe(true);

    selectFile(fixture, new File(['data'], 'entries.xlsx'));
    fixture.detectChanges();

    expect(uploadButton.disabled).toBe(false);
  });

  it('binds the native form submit event to onUpload via ngSubmit (regression: FormsModule must be imported, or a submit click falls through to a real page navigation)', () => {
    fakeApi.upload.mockReturnValue(of(batchFixture()));
    const fixture = createComponent();
    selectFile(fixture, new File(['data'], 'entries.xlsx'));
    fixture.detectChanges();

    const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit', { cancelable: true, bubbles: true }));
    fixture.detectChanges();

    expect(fakeApi.upload).toHaveBeenCalledWith('c1', expect.any(File));
  });

  it('uploads the file and renders every row with its status', () => {
    fakeApi.upload.mockReturnValue(of(batchFixture()));
    const fixture = createComponent();

    selectFile(fixture, new File(['data'], 'entries.xlsx'));
    fixture.detectChanges();
    fixture.componentInstance['onUpload']();
    fixture.detectChanges();

    expect(fakeApi.upload).toHaveBeenCalledWith('c1', expect.any(File));
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Valid');
    expect(text).toContain('StyleMismatch');
    expect(text).toContain('Invalid');
  });

  it('offers the style picker only for StyleMismatch rows — Invalid rows only get Exclude', () => {
    fakeApi.upload.mockReturnValue(of(batchFixture()));
    const fixture = createComponent();

    selectFile(fixture, new File(['data'], 'entries.xlsx'));
    fixture.detectChanges();
    fixture.componentInstance['onUpload']();
    fixture.detectChanges();

    const rows = fixture.nativeElement.querySelectorAll('tr[data-row-number]');
    // Row 2 is StyleMismatch: picker + Exclude. Row 3 is Invalid: Exclude only, no picker —
    // a style code can't repair a row broken for an unrelated reason (missing email/name).
    expect(rows[1].querySelector('app-style-picker')).not.toBeNull();
    expect(rows[2].querySelector('app-style-picker')).toBeNull();
    expect(rows[2].textContent).toContain('Exclude');
  });

  it('surfaces the server error message when upload fails instead of silently failing', () => {
    fakeApi.upload.mockReturnValue(
      throwError(
        () =>
          new ApiError({
            status: 400,
            title: 'Invalid import file',
            urn: 'urn:birrapoint:invalid-import-file',
            detail: 'The workbook is empty.',
          }),
      ),
    );
    const fixture = createComponent();

    selectFile(fixture, new File(['data'], 'entries.xlsx'));
    fixture.detectChanges();
    fixture.componentInstance['onUpload']();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('The workbook is empty.');
  });

  function uploadAndGetFixture() {
    fakeApi.upload.mockReturnValue(of(batchFixture()));
    const fixture = createComponent();
    selectFile(fixture, new File(['data'], 'entries.xlsx'));
    fixture.detectChanges();
    fixture.componentInstance['onUpload']();
    fixture.detectChanges();
    return fixture;
  }

  it('resolves a StyleMismatch row via assign-style and updates it locally without a refetch', () => {
    const fixture = uploadAndGetFixture();
    const updatedRow: ImportRow = {
      rowNumber: 2,
      status: 'Valid',
      data: { ...batchFixture().rows[1].data, resolvedStyleCode: '21A' },
      error: null,
    };
    fakeApi.resolveRow.mockReturnValue(of(updatedRow));

    const row2 = fixture.nativeElement.querySelector('tr[data-row-number="2"]') as Element;
    const select = row2.querySelector('select') as HTMLSelectElement;
    select.value = '21A';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    buttonWithText(row2, 'Assign style').click();
    fixture.detectChanges();

    expect(fakeApi.resolveRow).toHaveBeenCalledWith('c1', 'i1', 2, 'assign-style', '21A');
    expect(fakeApi.getImport).not.toHaveBeenCalled();
    const refreshedRow2 = fixture.nativeElement.querySelector('tr[data-row-number="2"]') as Element;
    expect(refreshedRow2.textContent).toContain('Valid');
    expect(refreshedRow2.querySelector('select')).toBeNull();
  });

  it('resolves an Invalid row via exclude and updates it locally', () => {
    const fixture = uploadAndGetFixture();
    const updatedRow: ImportRow = {
      rowNumber: 3,
      status: 'Excluded',
      data: batchFixture().rows[2].data,
      error: null,
    };
    fakeApi.resolveRow.mockReturnValue(of(updatedRow));

    const row3 = fixture.nativeElement.querySelector('tr[data-row-number="3"]') as Element;
    buttonWithText(row3, 'Exclude').click();
    fixture.detectChanges();

    expect(fakeApi.resolveRow).toHaveBeenCalledWith('c1', 'i1', 3, 'exclude', undefined);
    const refreshedRow3 = fixture.nativeElement.querySelector('tr[data-row-number="3"]') as Element;
    expect(refreshedRow3.textContent).toContain('Excluded');
  });

  it('keeps Consolidate disabled and shows the unresolved count while rows are unresolved', () => {
    const fixture = uploadAndGetFixture();

    const consolidateButton = buttonWithText(fixture.nativeElement, 'Consolidate');
    expect(consolidateButton.disabled).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('2');
    expect(fakeApi.consolidate).not.toHaveBeenCalled();
  });

  it('enables Consolidate once every row is resolved and shows the summary on success', () => {
    const fixture = uploadAndGetFixture();

    fakeApi.resolveRow.mockReturnValueOnce(
      of({
        rowNumber: 2,
        status: 'Valid',
        data: { ...batchFixture().rows[1].data, resolvedStyleCode: '21A' },
        error: null,
      } satisfies ImportRow),
    );
    const row2 = fixture.nativeElement.querySelector('tr[data-row-number="2"]') as Element;
    const select = row2.querySelector('select') as HTMLSelectElement;
    select.value = '21A';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    buttonWithText(row2, 'Assign style').click();
    fixture.detectChanges();

    fakeApi.resolveRow.mockReturnValueOnce(
      of({
        rowNumber: 3,
        status: 'Excluded',
        data: batchFixture().rows[2].data,
        error: null,
      } satisfies ImportRow),
    );
    const row3 = fixture.nativeElement.querySelector('tr[data-row-number="3"]') as Element;
    buttonWithText(row3, 'Exclude').click();
    fixture.detectChanges();

    const consolidateButton = buttonWithText(fixture.nativeElement, 'Consolidate');
    expect(consolidateButton.disabled).toBe(false);

    fakeApi.consolidate.mockReturnValue(
      of({
        imported: 1,
        excluded: 1,
        entries: [{ id: 'e1', blindCode: 'AB12', styleCode: '4A' }],
      }),
    );
    consolidateButton.click();
    fixture.detectChanges();

    expect(fakeApi.consolidate).toHaveBeenCalledWith('c1', 'i1');
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('AB12');
    expect(text).toContain('4A');
    expect(text).not.toContain('Golden Helles');
    expect(text).not.toContain('Ada Lovelace');
  });
});
