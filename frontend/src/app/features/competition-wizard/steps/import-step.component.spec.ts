import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';

import { CatalogApiService } from '../../../core/api/catalog-api.service';
import type { StyleSummary } from '../../../core/api/catalog-api.service';
import { CompetitionsApiService } from '../../../core/api/competitions-api.service';
import type { CompetitionCategoriesResponse } from '../../../core/api/competitions-api.service';
import { EntriesApiService } from '../../../core/api/entries-api.service';
import type { EntryListItem } from '../../../core/api/entries-api.service';
import { ImportApiService } from '../../../core/api/import-api.service';
import type { ImportBatch, ImportRow, ImportRowData } from '../../../core/api/import-api.service';
import { ImportStepComponent } from './import-step.component';

function rowDataFixture(overrides: Partial<ImportRowData> = {}): ImportRowData {
  return {
    participantName: 'José Deza Prieto',
    participantEmail: 'dezaprieto@gmail.com',
    acceMemberNumber: '1423',
    dateOfBirth: null,
    phone: '699989612',
    category: 'Estilos clásicos',
    competitionCategoryId: 'cat-1',
    style: '21C. Hazy IPA',
    resolvedStyleCode: '21C',
    submittedAt: '2025-09-01T09:21:16Z',
    abvPercent: 7.6,
    brewDate: '2025-08-12',
    bottlingDate: '2025-08-28',
    malts: 'Pale Ale, Trigo',
    hops: 'Citra, Mosaic',
    yeast: 'White Lab WL-001-P',
    otherIngredients: null,
    entryInstructions: null,
    beerName: null,
    ...overrides,
  };
}

function rowFixture(overrides: Partial<ImportRow> = {}): ImportRow {
  return {
    rowNumber: 1,
    status: 'Valid',
    data: rowDataFixture(),
    error: null,
    ...overrides,
  };
}

function batchFixture(rows: ImportRow[]): ImportBatch {
  return { importId: 'i1', rows };
}

function categoriesResponseFixture(
  overrides: Partial<CompetitionCategoriesResponse> = {},
): CompetitionCategoriesResponse {
  return {
    categories: [{ id: 'cat-1', name: 'Estilos clásicos', displayOrder: 0, styleCodes: ['21C'] }],
    ...overrides,
  };
}

function styleFixture(): StyleSummary[] {
  return [{ code: '21C', name: 'Hazy IPA', categoryNumber: '21', categoryName: 'IPA' }];
}

function entryFixture(overrides: Partial<EntryListItem> = {}): EntryListItem {
  return {
    id: 'e1',
    blindCode: 'AB12',
    styleCode: '21C',
    styleName: 'Hazy IPA',
    abvPercent: 7.5,
    abvLow: 6,
    abvHigh: 9,
    beerName: 'Bruma',
    notValidForBos: false,
    tastingTableId: null,
    tastingTableName: null,
    ...overrides,
  };
}

describe('ImportStepComponent', () => {
  let fakeImportApi: {
    upload: jest.Mock;
    getImport: jest.Mock;
    editRow: jest.Mock;
    excludeRow: jest.Mock;
    consolidate: jest.Mock;
    revalidate: jest.Mock;
  };
  let fakeCatalogApi: { getStyles: jest.Mock };
  let fakeCompetitionsApi: { getCategories: jest.Mock };
  let fakeEntriesApi: { getEntries: jest.Mock };

  beforeEach(() => {
    fakeImportApi = {
      upload: jest.fn(),
      getImport: jest.fn(),
      editRow: jest.fn(),
      excludeRow: jest.fn(),
      consolidate: jest.fn(),
      revalidate: jest.fn(),
    };
    fakeCatalogApi = { getStyles: jest.fn().mockReturnValue(of(styleFixture())) };
    fakeCompetitionsApi = {
      getCategories: jest.fn().mockReturnValue(of(categoriesResponseFixture())),
    };
    fakeEntriesApi = { getEntries: jest.fn().mockReturnValue(of([])) };

    TestBed.configureTestingModule({
      providers: [
        { provide: ImportApiService, useValue: fakeImportApi },
        { provide: CatalogApiService, useValue: fakeCatalogApi },
        { provide: CompetitionsApiService, useValue: fakeCompetitionsApi },
        { provide: EntriesApiService, useValue: fakeEntriesApi },
        provideRouter([]),
      ],
    });
  });

  function createComponent(importId: string | null = null) {
    const fixture = TestBed.createComponent(ImportStepComponent);
    fixture.componentRef.setInput('competitionId', 'c1');
    if (importId) {
      fixture.componentRef.setInput('importId', importId);
    }
    fixture.detectChanges();
    return fixture;
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

  function uploadedFixture(rows: ImportRow[]) {
    fakeImportApi.upload.mockReturnValue(of(batchFixture(rows)));
    const fixture = createComponent();
    selectFile(fixture, new File(['data'], 'entries.xlsx'));
    fixture.detectChanges();
    fixture.componentInstance['onUpload']();
    fixture.detectChanges();
    return fixture;
  }

  it('shows an empty-state alert and disables the upload control when the competition has zero categories', () => {
    fakeCompetitionsApi.getCategories.mockReturnValue(
      of(categoriesResponseFixture({ categories: [] })),
    );
    const fixture = createComponent();

    expect(fixture.nativeElement.textContent).toContain('crea al menos una categoría');
    const fileInput = fixture.nativeElement.querySelector('input[type="file"]') as HTMLInputElement;
    expect(fileInput.disabled).toBe(true);
  });

  it('binds the native form submit event to onUpload via ngSubmit (regression: FormsModule must be imported, or a submit click falls through to a real page navigation)', () => {
    fakeImportApi.upload.mockReturnValue(of(batchFixture([rowFixture({ rowNumber: 1 })])));
    const fixture = createComponent();
    selectFile(fixture, new File(['data'], 'entries.xlsx'));
    fixture.detectChanges();

    const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit', { cancelable: true, bubbles: true }));
    fixture.detectChanges();

    expect(fakeImportApi.upload).toHaveBeenCalledWith('c1', expect.any(File));
  });

  it('uploads a file and renders every row after success', () => {
    fakeImportApi.upload.mockReturnValue(
      of(
        batchFixture([
          rowFixture({ rowNumber: 1 }),
          rowFixture({
            rowNumber: 2,
            status: 'CategoryMismatch',
            data: rowDataFixture({
              participantName: 'Grace Hopper',
              category: 'Estilos experimentales',
              competitionCategoryId: null,
            }),
          }),
        ]),
      ),
    );
    const fixture = createComponent();

    selectFile(fixture, new File(['data'], 'entries.xlsx'));
    fixture.detectChanges();
    fixture.componentInstance['onUpload']();
    fixture.detectChanges();

    expect(fakeImportApi.upload).toHaveBeenCalledWith('c1', expect.any(File));
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('José Deza Prieto');
    expect(text).toContain('Grace Hopper');
  });

  it('expands a row, corrects its category and style, and saves it', () => {
    const fixture = uploadedFixture([
      rowFixture({
        rowNumber: 1,
        status: 'CategoryMismatch',
        data: rowDataFixture({
          competitionCategoryId: null,
          resolvedStyleCode: null,
          style: '99Z. X',
        }),
      }),
    ]);

    buttonWithText(fixture.nativeElement, 'Editar').click();
    fixture.detectChanges();

    const categorySelect = fixture.nativeElement.querySelector(
      '.import-row__editor select',
    ) as HTMLSelectElement;
    categorySelect.value = 'cat-1';
    categorySelect.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const stylePicker = fixture.nativeElement.querySelector('app-style-picker') as Element;
    const styleSelect = stylePicker.querySelector('select') as HTMLSelectElement;
    styleSelect.value = '21C';
    styleSelect.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    buttonWithText(stylePicker, 'Assign style').click();
    fixture.detectChanges();

    fakeImportApi.editRow.mockReturnValue(of(rowFixture({ rowNumber: 1, status: 'Valid' })));
    buttonWithText(fixture.nativeElement, 'Guardar fila').click();
    fixture.detectChanges();

    expect(fakeImportApi.editRow).toHaveBeenCalledWith(
      'c1',
      'i1',
      1,
      expect.objectContaining({ competitionCategoryId: 'cat-1', styleCode: '21C' }),
    );
    expect(fixture.nativeElement.querySelector('.import-row__editor')).toBeFalsy();
    expect(fixture.nativeElement.textContent).toContain('Válida');
  });

  it('excludes a row directly from the collapsed summary row, without opening the editor', () => {
    const fixture = uploadedFixture([rowFixture({ rowNumber: 1, status: 'Invalid', error: 'x' })]);

    fakeImportApi.excludeRow.mockReturnValue(of(rowFixture({ rowNumber: 1, status: 'Excluded' })));
    buttonWithText(fixture.nativeElement, 'Excluir').click();
    fixture.detectChanges();

    expect(fakeImportApi.excludeRow).toHaveBeenCalledWith('c1', 'i1', 1);
    expect(fixture.nativeElement.textContent).toContain('Excluida');
    expect(
      [...fixture.nativeElement.querySelectorAll('button')].some(
        (button: HTMLButtonElement) => button.textContent?.trim() === 'Editar',
      ),
    ).toBe(false);
    expect(
      [...fixture.nativeElement.querySelectorAll('button')].some(
        (button: HTMLButtonElement) => button.textContent?.trim() === 'Excluir',
      ),
    ).toBe(false);
  });

  it('shows an "Excluir" button in the summary row for every non-excluded, non-editing row, with an accessible name distinguishing each row', () => {
    const fixture = uploadedFixture([
      rowFixture({ rowNumber: 1, status: 'Valid' }),
      rowFixture({ rowNumber: 2, status: 'Invalid', error: 'x' }),
    ]);

    const rows = fixture.nativeElement.querySelectorAll('.import-row');
    const excludeButton1 = buttonWithText(rows[0] as Element, 'Excluir');
    const excludeButton2 = buttonWithText(rows[1] as Element, 'Excluir');

    expect(excludeButton1.getAttribute('aria-label')).toBeTruthy();
    expect(excludeButton2.getAttribute('aria-label')).toBeTruthy();
    expect(excludeButton1.getAttribute('aria-label')).not.toBe(
      excludeButton2.getAttribute('aria-label'),
    );
  });

  it('excludes a row from its collapsed summary row while another row is being edited', () => {
    const fixture = uploadedFixture([
      rowFixture({ rowNumber: 1, status: 'Valid' }),
      rowFixture({ rowNumber: 2, status: 'Invalid', error: 'x' }),
    ]);

    const rows = fixture.nativeElement.querySelectorAll('.import-row');
    buttonWithText(rows[0] as Element, 'Editar').click();
    fixture.detectChanges();

    fakeImportApi.excludeRow.mockReturnValue(of(rowFixture({ rowNumber: 2, status: 'Excluded' })));
    const rowsAfterEdit = fixture.nativeElement.querySelectorAll('.import-row');
    buttonWithText(rowsAfterEdit[1] as Element, 'Excluir').click();
    fixture.detectChanges();

    expect(fakeImportApi.excludeRow).toHaveBeenCalledWith('c1', 'i1', 2);
  });

  it('does not render an "Excluir" button inside the row editor anymore', () => {
    const fixture = uploadedFixture([
      rowFixture({
        rowNumber: 1,
        status: 'CategoryMismatch',
        data: rowDataFixture({ competitionCategoryId: null }),
      }),
    ]);

    buttonWithText(fixture.nativeElement, 'Editar').click();
    fixture.detectChanges();

    const editorActions = fixture.nativeElement.querySelector('.import-row__editor-actions');
    expect(
      [...editorActions!.querySelectorAll('button')].some(
        (button: HTMLButtonElement) => button.textContent?.trim() === 'Excluir',
      ),
    ).toBe(false);
    expect(buttonWithText(fixture.nativeElement, 'Cancelar')).toBeTruthy();
    expect(buttonWithText(fixture.nativeElement, 'Guardar fila')).toBeTruthy();
  });

  it('keeps Consolidar disabled while any row is unresolved, and enables it once every row is resolved', () => {
    const fixture = uploadedFixture([
      rowFixture({ rowNumber: 1, status: 'Valid' }),
      rowFixture({
        rowNumber: 2,
        status: 'Invalid',
        error: 'x',
        data: rowDataFixture({ participantName: null }),
      }),
    ]);

    const consolidateButton = buttonWithText(fixture.nativeElement, 'Consolidar');
    expect(consolidateButton.disabled).toBe(true);

    const row2 = fixture.nativeElement.querySelectorAll('.import-row')[1] as Element;
    fakeImportApi.excludeRow.mockReturnValue(of(rowFixture({ rowNumber: 2, status: 'Excluded' })));
    buttonWithText(row2, 'Excluir').click();
    fixture.detectChanges();

    const consolidateButtonAfter = buttonWithText(fixture.nativeElement, 'Consolidar');
    expect(consolidateButtonAfter.disabled).toBe(false);
  });

  it('consolidates successfully, shows the summary, and only navigates once the organizer confirms', () => {
    const fixture = uploadedFixture([rowFixture({ rowNumber: 1, status: 'Valid' })]);
    const router = TestBed.inject(Router);
    const navigateSpy = jest.spyOn(router, 'navigateByUrl');

    fakeImportApi.consolidate.mockReturnValue(
      of({
        imported: 1,
        excluded: 0,
        entries: [{ id: 'e1', blindCode: 'AB12', styleCode: '21C' }],
      }),
    );
    buttonWithText(fixture.nativeElement, 'Consolidar').click();
    fixture.detectChanges();

    expect(fakeImportApi.consolidate).toHaveBeenCalledWith('c1', 'i1');
    expect(fixture.nativeElement.textContent).toContain('Importadas: 1');
    expect(navigateSpy).not.toHaveBeenCalled();
    const buttons = [...fixture.nativeElement.querySelectorAll('button')] as HTMLButtonElement[];
    expect(buttons.some((button) => button.textContent?.trim() === 'Consolidar')).toBe(false);

    buttonWithText(fixture.nativeElement, 'Ir al panel de organizador').click();
    fixture.detectChanges();

    expect(navigateSpy).toHaveBeenCalledWith('/organizer/dashboard');
  });

  it('emits back when "← Volver" is clicked', () => {
    const fixture = createComponent();
    const emitted: void[] = [];
    fixture.componentInstance.back.subscribe(() => emitted.push(undefined));

    buttonWithText(fixture.nativeElement, '← Volver').click();

    expect(emitted.length).toBe(1);
  });

  it('revalidates and shows the rows list when an importId is already set (returning from another wizard step)', () => {
    fakeImportApi.revalidate.mockReturnValue(
      of(batchFixture([rowFixture({ rowNumber: 1, status: 'Valid' })])),
    );
    const fixture = createComponent('i1');

    expect(fakeImportApi.revalidate).toHaveBeenCalledWith('c1', 'i1');
    expect(fixture.nativeElement.querySelector('input[type="file"]')).toBeFalsy();
    expect(fixture.nativeElement.textContent).toContain('José Deza Prieto');
  });

  it('shows the upload form and does not call revalidate when no importId is set', () => {
    const fixture = createComponent();

    expect(fakeImportApi.revalidate).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('input[type="file"]')).toBeTruthy();
  });

  it('shows a load error banner when revalidate fails', () => {
    fakeImportApi.revalidate.mockReturnValue(throwError(() => new Error('boom')));
    const fixture = createComponent('i1');

    expect(fixture.nativeElement.textContent).toContain('No hemos podido cargar los datos');
  });

  it('shows the already-imported entries list when reopening step 4 with no pending batch but existing entries (e.g. after consolidating, leaving the wizard, and coming back)', () => {
    fakeEntriesApi.getEntries.mockReturnValue(
      of([entryFixture({ id: 'e1', blindCode: 'AB12', beerName: 'Bruma' })]),
    );
    const fixture = createComponent();

    expect(fakeEntriesApi.getEntries).toHaveBeenCalledWith('c1');
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('AB12');
    expect(text).toContain('Bruma');
    expect(fixture.nativeElement.querySelector('input[type="file"]')).toBeTruthy();
  });

  it('does not show the already-imported entries block when the competition has no existing entries', () => {
    fakeEntriesApi.getEntries.mockReturnValue(of([]));
    const fixture = createComponent();

    expect(fixture.nativeElement.querySelector('.existing-entries')).toBeFalsy();
  });

  it('does not show the already-imported entries block while a pending import batch is active, even if existing entries are also returned', () => {
    fakeEntriesApi.getEntries.mockReturnValue(of([entryFixture()]));
    fakeImportApi.revalidate.mockReturnValue(
      of(batchFixture([rowFixture({ rowNumber: 1, status: 'Valid' })])),
    );
    const fixture = createComponent('i1');

    expect(fixture.nativeElement.querySelector('.existing-entries')).toBeFalsy();
  });

  it('still loads categories/styles and shows the upload form when the already-imported check fails, with a warning instead of silently looking like "nothing imported"', () => {
    fakeEntriesApi.getEntries.mockReturnValue(throwError(() => new Error('boom')));
    const fixture = createComponent();

    expect(fixture.nativeElement.querySelector('input[type="file"]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.existing-entries')).toBeFalsy();
    expect(fixture.nativeElement.textContent).toContain(
      'No hemos podido comprobar cervezas ya importadas',
    );
  });

  it('emits importIdChange with the new batch id after a successful upload', () => {
    fakeImportApi.upload.mockReturnValue(of(batchFixture([rowFixture({ rowNumber: 1 })])));
    const fixture = createComponent();
    const emitted: string[] = [];
    fixture.componentInstance.importIdChange.subscribe((id) => emitted.push(id));

    selectFile(fixture, new File(['data'], 'entries.xlsx'));
    fixture.detectChanges();
    fixture.componentInstance['onUpload']();
    fixture.detectChanges();

    expect(emitted).toEqual(['i1']);
  });

  it('renders the row-specific error reason for an unresolved row', () => {
    const fixture = uploadedFixture([
      rowFixture({
        rowNumber: 1,
        status: 'CategoryStyleMismatch',
        error: 'El estilo no está asignado a esta categoría.',
        data: rowDataFixture({ competitionCategoryId: 'cat-1', resolvedStyleCode: '21C' }),
      }),
    ]);

    expect(fixture.nativeElement.textContent).toContain(
      'El estilo no está asignado a esta categoría.',
    );
  });

  it('does not render an error line for a Valid row', () => {
    const fixture = uploadedFixture([rowFixture({ rowNumber: 1, status: 'Valid', error: null })]);

    expect(fixture.nativeElement.querySelector('.import-row__error')).toBeFalsy();
  });

  it('treats CategoryStyleMismatch rows as unresolved, blocking consolidation', () => {
    const fixture = uploadedFixture([
      rowFixture({ rowNumber: 1, status: 'CategoryStyleMismatch', error: 'x' }),
    ]);

    const consolidateButton = buttonWithText(fixture.nativeElement, 'Consolidar');
    expect(consolidateButton.disabled).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('1 fila(s) necesitan corrección');
  });

  it('emits dirtyChange(false) on init with no pending edits', () => {
    const fixture = TestBed.createComponent(ImportStepComponent);
    const emitted: boolean[] = [];
    fixture.componentInstance.dirtyChange.subscribe((value) => emitted.push(value));
    fixture.componentRef.setInput('competitionId', 'c1');
    fixture.detectChanges();

    expect(emitted).toEqual([false]);
  });

  it('emits dirtyChange(true) while a row editor is open, and dirtyChange(false) again once it is closed', () => {
    const fixture = uploadedFixture([rowFixture({ rowNumber: 1, status: 'Valid' })]);
    const emitted: boolean[] = [];
    fixture.componentInstance.dirtyChange.subscribe((value) => emitted.push(value));

    buttonWithText(fixture.nativeElement, 'Editar').click();
    fixture.detectChanges();
    expect(emitted).toEqual([true]);

    fixture.componentInstance['stopEditing']();
    fixture.detectChanges();
    expect(emitted).toEqual([true, false]);
  });

  it('emits dirtyChange(true) once a file is selected but not yet uploaded', () => {
    const fixture = createComponent();
    const emitted: boolean[] = [];
    fixture.componentInstance.dirtyChange.subscribe((value) => emitted.push(value));

    selectFile(fixture, new File(['data'], 'entries.xlsx'));
    fixture.detectChanges();

    expect(emitted).toEqual([true]);
  });

  it('emits dirtyChange(false) again once the selected file finishes uploading', () => {
    fakeImportApi.upload.mockReturnValue(of(batchFixture([rowFixture({ rowNumber: 1 })])));
    const fixture = createComponent();
    const emitted: boolean[] = [];
    fixture.componentInstance.dirtyChange.subscribe((value) => emitted.push(value));

    selectFile(fixture, new File(['data'], 'entries.xlsx'));
    fixture.detectChanges();
    expect(emitted).toEqual([true]);

    fixture.componentInstance['onUpload']();
    fixture.detectChanges();

    expect(emitted).toEqual([true, false]);
  });
});
