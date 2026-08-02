import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';

import { JudgeImportApiService } from '../../../core/api/judge-import-api.service';
import type {
  JudgeImportBatch,
  JudgeImportRow,
  JudgeImportRowData,
} from '../../../core/api/judge-import-api.service';
import { JudgeImportStepComponent } from './judge-import-step.component';

function rowDataFixture(overrides: Partial<JudgeImportRowData> = {}): JudgeImportRowData {
  return {
    name: 'Rebeca Ruifernández Calzada',
    email: 'rebeca@example.com',
    bjcpRank: 'Certificado',
    bjcpId: 'E4612',
    preferredCategory: 'Estilos Clásicos',
    preferences: null,
    ...overrides,
  };
}

function rowFixture(overrides: Partial<JudgeImportRow> = {}): JudgeImportRow {
  return {
    rowNumber: 1,
    status: 'Valid',
    data: rowDataFixture(),
    error: null,
    ...overrides,
  };
}

function batchFixture(rows: JudgeImportRow[]): JudgeImportBatch {
  return { importId: 'ji1', rows };
}

describe('JudgeImportStepComponent', () => {
  let fakeJudgeImportApi: {
    upload: jest.Mock;
    getImport: jest.Mock;
    editRow: jest.Mock;
    excludeRow: jest.Mock;
    consolidate: jest.Mock;
  };

  beforeEach(() => {
    fakeJudgeImportApi = {
      upload: jest.fn(),
      getImport: jest.fn(),
      editRow: jest.fn(),
      excludeRow: jest.fn(),
      consolidate: jest.fn(),
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: JudgeImportApiService, useValue: fakeJudgeImportApi },
        provideRouter([]),
      ],
    });
  });

  function createComponent(judgeImportId: string | null = null) {
    const fixture = TestBed.createComponent(JudgeImportStepComponent);
    fixture.componentRef.setInput('competitionId', 'c1');
    if (judgeImportId) {
      fixture.componentRef.setInput('judgeImportId', judgeImportId);
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

  function uploadedFixture(rows: JudgeImportRow[]) {
    fakeJudgeImportApi.upload.mockReturnValue(of(batchFixture(rows)));
    const fixture = createComponent();
    selectFile(fixture, new File(['data'], 'roster.xlsx'));
    fixture.detectChanges();
    fixture.componentInstance['onUpload']();
    fixture.detectChanges();
    return fixture;
  }

  it('binds the native form submit event to onUpload via ngSubmit', () => {
    fakeJudgeImportApi.upload.mockReturnValue(of(batchFixture([rowFixture({ rowNumber: 1 })])));
    const fixture = createComponent();
    selectFile(fixture, new File(['data'], 'roster.xlsx'));
    fixture.detectChanges();

    const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit', { cancelable: true, bubbles: true }));
    fixture.detectChanges();

    expect(fakeJudgeImportApi.upload).toHaveBeenCalledWith('c1', expect.any(File));
  });

  it('uploads a file and renders every row after success', () => {
    fakeJudgeImportApi.upload.mockReturnValue(
      of(
        batchFixture([
          rowFixture({ rowNumber: 1 }),
          rowFixture({
            rowNumber: 2,
            status: 'Invalid',
            error: 'Falta el correo electrónico.',
            data: rowDataFixture({ name: 'Grace Hopper', email: null }),
          }),
        ]),
      ),
    );
    const fixture = createComponent();

    selectFile(fixture, new File(['data'], 'roster.xlsx'));
    fixture.detectChanges();
    fixture.componentInstance['onUpload']();
    fixture.detectChanges();

    expect(fakeJudgeImportApi.upload).toHaveBeenCalledWith('c1', expect.any(File));
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Rebeca Ruifernández Calzada');
    expect(text).toContain('Grace Hopper');
    expect(text).toContain('Falta el correo electrónico.');
  });

  it('expands a row, corrects the missing email, and saves it', () => {
    const fixture = uploadedFixture([
      rowFixture({
        rowNumber: 1,
        status: 'Invalid',
        error: 'Falta el correo electrónico.',
        data: rowDataFixture({ email: null }),
      }),
    ]);

    buttonWithText(fixture.nativeElement, 'Editar').click();
    fixture.detectChanges();

    const emailInput = fixture.nativeElement.querySelector(
      '#judge-row-email-0',
    ) as HTMLInputElement;
    emailInput.value = 'rebeca@example.com';
    emailInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    fakeJudgeImportApi.editRow.mockReturnValue(of(rowFixture({ rowNumber: 1, status: 'Valid' })));
    buttonWithText(fixture.nativeElement, 'Guardar fila').click();
    fixture.detectChanges();

    expect(fakeJudgeImportApi.editRow).toHaveBeenCalledWith(
      'c1',
      'ji1',
      1,
      expect.objectContaining({ email: 'rebeca@example.com' }),
    );
    expect(fixture.nativeElement.querySelector('.judge-import-row__editor')).toBeFalsy();
    expect(fixture.nativeElement.textContent).toContain('Válida');
  });

  it('excludes a row directly from the collapsed summary row, without opening the editor', () => {
    const fixture = uploadedFixture([rowFixture({ rowNumber: 1, status: 'Invalid', error: 'x' })]);

    fakeJudgeImportApi.excludeRow.mockReturnValue(
      of(rowFixture({ rowNumber: 1, status: 'Excluded' })),
    );
    buttonWithText(fixture.nativeElement, 'Excluir').click();
    fixture.detectChanges();

    expect(fakeJudgeImportApi.excludeRow).toHaveBeenCalledWith('c1', 'ji1', 1);
    expect(fixture.nativeElement.textContent).toContain('Excluida');
    expect(
      [...fixture.nativeElement.querySelectorAll('button')].some(
        (button: HTMLButtonElement) => button.textContent?.trim() === 'Editar',
      ),
    ).toBe(false);
  });

  it('keeps Consolidar disabled while any row is Invalid, and enables it once resolved', () => {
    const fixture = uploadedFixture([
      rowFixture({ rowNumber: 1, status: 'Valid' }),
      rowFixture({ rowNumber: 2, status: 'Invalid', error: 'x' }),
    ]);

    const consolidateButton = buttonWithText(fixture.nativeElement, 'Consolidar');
    expect(consolidateButton.disabled).toBe(true);

    fakeJudgeImportApi.excludeRow.mockReturnValue(
      of(rowFixture({ rowNumber: 2, status: 'Excluded' })),
    );
    const row2 = fixture.nativeElement.querySelectorAll('.judge-import-row')[1] as Element;
    buttonWithText(row2, 'Excluir').click();
    fixture.detectChanges();

    const consolidateButtonAfter = buttonWithText(fixture.nativeElement, 'Consolidar');
    expect(consolidateButtonAfter.disabled).toBe(false);
  });

  it('consolidates successfully, shows the created/updated/excluded summary, and only navigates once confirmed', () => {
    const fixture = uploadedFixture([rowFixture({ rowNumber: 1, status: 'Valid' })]);
    const router = TestBed.inject(Router);
    const navigateSpy = jest.spyOn(router, 'navigateByUrl');

    fakeJudgeImportApi.consolidate.mockReturnValue(
      of({
        created: [{ id: 'j1', email: 'rebeca@example.com' }],
        updated: [],
        excluded: 0,
      }),
    );
    buttonWithText(fixture.nativeElement, 'Consolidar').click();
    fixture.detectChanges();

    expect(fakeJudgeImportApi.consolidate).toHaveBeenCalledWith('c1', 'ji1');
    expect(fixture.nativeElement.textContent).toContain('Creados: 1');
    expect(fixture.nativeElement.textContent).toContain('Actualizados: 0');
    expect(fixture.nativeElement.textContent).toContain('Excluidos: 0');
    expect(navigateSpy).not.toHaveBeenCalled();
    const buttons = [...fixture.nativeElement.querySelectorAll('button')] as HTMLButtonElement[];
    expect(buttons.some((button) => button.textContent?.trim() === 'Consolidar')).toBe(false);

    buttonWithText(fixture.nativeElement, 'Ir al panel de organizador').click();
    fixture.detectChanges();

    expect(navigateSpy).toHaveBeenCalledWith('/organizer/dashboard');
  });

  it('shows a consolidate error banner when consolidation fails', () => {
    const fixture = uploadedFixture([rowFixture({ rowNumber: 1, status: 'Valid' })]);

    fakeJudgeImportApi.consolidate.mockReturnValue(throwError(() => new Error('boom')));
    buttonWithText(fixture.nativeElement, 'Consolidar').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('No hemos podido consolidar');
  });

  it('emits back when "← Volver" is clicked', () => {
    const fixture = createComponent();
    const emitted: void[] = [];
    fixture.componentInstance.back.subscribe(() => emitted.push(undefined));

    buttonWithText(fixture.nativeElement, '← Volver').click();

    expect(emitted.length).toBe(1);
  });

  it('fetches the pending batch when a judgeImportId is already set (returning from another wizard step)', () => {
    fakeJudgeImportApi.getImport.mockReturnValue(
      of(batchFixture([rowFixture({ rowNumber: 1, status: 'Valid' })])),
    );
    const fixture = createComponent('ji1');

    expect(fakeJudgeImportApi.getImport).toHaveBeenCalledWith('c1', 'ji1');
    expect(fixture.nativeElement.querySelector('input[type="file"]')).toBeFalsy();
    expect(fixture.nativeElement.textContent).toContain('Rebeca Ruifernández Calzada');
  });

  it('shows the upload form and does not call getImport when no judgeImportId is set', () => {
    const fixture = createComponent();

    expect(fakeJudgeImportApi.getImport).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('input[type="file"]')).toBeTruthy();
  });

  it('shows a load error banner when fetching the pending batch fails', () => {
    fakeJudgeImportApi.getImport.mockReturnValue(throwError(() => new Error('boom')));
    const fixture = createComponent('ji1');

    expect(fixture.nativeElement.textContent).toContain('No hemos podido cargar los datos');
  });

  it('emits judgeImportIdChange with the new batch id after a successful upload', () => {
    fakeJudgeImportApi.upload.mockReturnValue(of(batchFixture([rowFixture({ rowNumber: 1 })])));
    const fixture = createComponent();
    const emitted: string[] = [];
    fixture.componentInstance.judgeImportIdChange.subscribe((id) => emitted.push(id));

    selectFile(fixture, new File(['data'], 'roster.xlsx'));
    fixture.detectChanges();
    fixture.componentInstance['onUpload']();
    fixture.detectChanges();

    expect(emitted).toEqual(['ji1']);
  });

  it('emits dirtyChange(false) on init with no pending edits', () => {
    const fixture = TestBed.createComponent(JudgeImportStepComponent);
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

    selectFile(fixture, new File(['data'], 'roster.xlsx'));
    fixture.detectChanges();

    expect(emitted).toEqual([true]);
  });

  it('renders the row-specific error reason for an unresolved row', () => {
    const fixture = uploadedFixture([
      rowFixture({
        rowNumber: 1,
        status: 'Invalid',
        error: 'Falta el nombre y el correo electrónico.',
        data: rowDataFixture({ name: null, email: null }),
      }),
    ]);

    expect(fixture.nativeElement.textContent).toContain('Falta el nombre y el correo electrónico.');
  });

  it('does not render an error line for a Valid row', () => {
    const fixture = uploadedFixture([rowFixture({ rowNumber: 1, status: 'Valid', error: null })]);

    expect(fixture.nativeElement.querySelector('.judge-import-row__error')).toBeFalsy();
  });
});
