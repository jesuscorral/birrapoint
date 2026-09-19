import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { JudgeImportApiService } from '../../../core/api/judge-import-api.service';
import type {
  JudgeImportBatch,
  JudgeImportRow,
  JudgeImportRowData,
} from '../../../core/api/judge-import-api.service';
import { JudgeManagementApiService } from '../../../core/api/judge-management-api.service';
import type { JudgeProfile } from '../../../core/api/judge-management-api.service';
import { JudgeImportStepComponent } from './judge-import-step.component';

function judgeProfileFixture(overrides: Partial<JudgeProfile> = {}): JudgeProfile {
  return {
    id: 'j1',
    email: 'ana@example.com',
    displayName: 'Ana García Ruiz',
    bjcpRank: null,
    bjcpId: null,
    preferredCategory: null,
    preferences: null,
    invitationStatus: 'Sent',
    attempts: 1,
    lastError: null,
    sentAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

function rowDataFixture(overrides: Partial<JudgeImportRowData> = {}): JudgeImportRowData {
  return {
    name: 'Ana García Ruiz',
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
  let fakeJudgeManagementApi: {
    getJudges: jest.Mock;
    notifyJudges: jest.Mock;
    resendInvitation: jest.Mock;
  };

  beforeEach(() => {
    fakeJudgeImportApi = {
      upload: jest.fn(),
      getImport: jest.fn(),
      editRow: jest.fn(),
      excludeRow: jest.fn(),
      consolidate: jest.fn(),
    };
    fakeJudgeManagementApi = {
      getJudges: jest.fn().mockReturnValue(of([])),
      notifyJudges: jest.fn(),
      resendInvitation: jest.fn(),
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: JudgeImportApiService, useValue: fakeJudgeImportApi },
        { provide: JudgeManagementApiService, useValue: fakeJudgeManagementApi },
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

  // T125: the shared three-zone bar (bp-step-actions) — Atrás | the step's own action |
  // forward. Previously each step rendered its own two-slot bar and they had drifted apart.
  it('renders the shared bar: Atrás, its own centre action, and Siguiente', () => {
    const fixture = createComponent();

    const buttons = [...fixture.nativeElement.querySelectorAll('.step-actions button')].map(
      (button: HTMLButtonElement) => button.textContent?.trim(),
    );
    expect(buttons).toEqual(['Atrás', 'Subir archivo', 'Siguiente']);
  });

  it('uploads the selected file when "Siguiente" is clicked and no batch exists yet', () => {
    fakeJudgeImportApi.upload.mockReturnValue(of(batchFixture([rowFixture({ rowNumber: 1 })])));
    const fixture = createComponent();
    selectFile(fixture, new File(['data'], 'roster.xlsx'));
    fixture.detectChanges();

    buttonWithText(fixture.nativeElement, 'Siguiente').click();
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
    expect(text).toContain('Ana García Ruiz');
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

  it('does not consolidate while any row is Invalid — "Siguiente" just advances instead, leaving the batch pending', () => {
    const fixture = uploadedFixture([
      rowFixture({ rowNumber: 1, status: 'Valid' }),
      rowFixture({ rowNumber: 2, status: 'Invalid', error: 'x' }),
    ]);
    const emitted: void[] = [];
    fixture.componentInstance.saved.subscribe(() => emitted.push(undefined));

    buttonWithText(fixture.nativeElement, 'Siguiente').click();

    expect(fakeJudgeImportApi.consolidate).not.toHaveBeenCalled();
    expect(emitted.length).toBe(1);
  });

  it('consolidates once resolved, shows the created/updated/excluded summary, and only advances on a second "Siguiente" click', () => {
    const fixture = uploadedFixture([rowFixture({ rowNumber: 1, status: 'Valid' })]);
    const emitted: void[] = [];
    fixture.componentInstance.saved.subscribe(() => emitted.push(undefined));

    fakeJudgeImportApi.consolidate.mockReturnValue(
      of({
        created: [{ id: 'j1', email: 'rebeca@example.com' }],
        updated: [],
        excluded: 0,
        skipped: [],
      }),
    );
    buttonWithText(fixture.nativeElement, 'Siguiente').click();
    fixture.detectChanges();

    expect(fakeJudgeImportApi.consolidate).toHaveBeenCalledWith('c1', 'ji1');
    expect(fixture.nativeElement.textContent).toContain('Creados: 1');
    expect(fixture.nativeElement.textContent).toContain('Actualizados: 0');
    expect(fixture.nativeElement.textContent).toContain('Excluidos: 0');
    expect(emitted.length).toBe(0);

    buttonWithText(fixture.nativeElement, 'Siguiente').click();
    fixture.detectChanges();

    expect(emitted.length).toBe(1);
    expect(fakeJudgeImportApi.consolidate).toHaveBeenCalledTimes(1);
  });

  it('shows the skipped-duplicates count in the consolidate summary when the batch had any (FR-058)', () => {
    const fixture = uploadedFixture([rowFixture({ rowNumber: 1, status: 'Valid' })]);

    fakeJudgeImportApi.consolidate.mockReturnValue(
      of({
        created: [{ id: 'j1', email: 'ana@example.com' }],
        updated: [],
        excluded: 0,
        skipped: [{ email: 'ana@example.com', reason: 'duplicate-in-list' }],
      }),
    );
    buttonWithText(fixture.nativeElement, 'Siguiente').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Omitidos: 1');
  });

  it('does not mention omitted rows in the consolidate summary when nothing was skipped', () => {
    const fixture = uploadedFixture([rowFixture({ rowNumber: 1, status: 'Valid' })]);

    fakeJudgeImportApi.consolidate.mockReturnValue(
      of({
        created: [{ id: 'j1', email: 'ana@example.com' }],
        updated: [],
        excluded: 0,
        skipped: [],
      }),
    );
    buttonWithText(fixture.nativeElement, 'Siguiente').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('Omitidos');
  });

  it('shows a consolidate error banner when consolidation fails', () => {
    const fixture = uploadedFixture([rowFixture({ rowNumber: 1, status: 'Valid' })]);

    fakeJudgeImportApi.consolidate.mockReturnValue(throwError(() => new Error('boom')));
    buttonWithText(fixture.nativeElement, 'Siguiente').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('No hemos podido consolidar');
  });

  it('emits back when "Atrás" is clicked', () => {
    const fixture = createComponent();
    const emitted: void[] = [];
    fixture.componentInstance.back.subscribe(() => emitted.push(undefined));

    buttonWithText(fixture.nativeElement, 'Atrás').click();

    expect(emitted.length).toBe(1);
  });

  it('fetches the pending batch when a judgeImportId is already set (returning from another wizard step)', () => {
    fakeJudgeImportApi.getImport.mockReturnValue(
      of(batchFixture([rowFixture({ rowNumber: 1, status: 'Valid' })])),
    );
    const fixture = createComponent('ji1');

    expect(fakeJudgeImportApi.getImport).toHaveBeenCalledWith('c1', 'ji1');
    expect(fixture.nativeElement.querySelector('input[type="file"]')).toBeFalsy();
    expect(fixture.nativeElement.textContent).toContain('Ana García Ruiz');
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

  // FR-061 / Session 2026-09-19 clarification: read-only wizard once InEvaluation/Finalized. The
  // upload flow is skipped entirely in favour of listing the judges already registered for this
  // competition (GET /competitions/{id}/judges via JudgeManagementApiService).
  describe('readOnly', () => {
    function createReadOnlyComponent() {
      const fixture = TestBed.createComponent(JudgeImportStepComponent);
      fixture.componentRef.setInput('competitionId', 'c1');
      fixture.componentRef.setInput('readOnly', true);
      fixture.detectChanges();
      return fixture;
    }

    it('skips the upload UI and lists registered judges', () => {
      fakeJudgeManagementApi.getJudges.mockReturnValue(
        of([judgeProfileFixture({ displayName: 'Ana García Ruiz', email: 'ana@example.com' })]),
      );
      const fixture = createReadOnlyComponent();

      expect(fakeJudgeManagementApi.getJudges).toHaveBeenCalledWith('c1');
      expect(fixture.nativeElement.querySelector('input[type="file"]')).toBeNull();
      const section = fixture.nativeElement.querySelector(
        '[aria-label="Jueces registrados"]',
      ) as HTMLElement;
      expect(section).toBeTruthy();
      expect(section.textContent).toContain('Ana García Ruiz');
      expect(section.textContent).toContain('ana@example.com');
    });

    it('shows "No hay jueces registrados." when the competition has none', () => {
      const fixture = createReadOnlyComponent();

      expect(fixture.nativeElement.textContent).toContain('No hay jueces registrados.');
    });

    it('hides "Subir archivo" and "Consolidar"', () => {
      const fixture = createReadOnlyComponent();

      const texts = [...fixture.nativeElement.querySelectorAll('button')].map(
        (button: HTMLButtonElement) => button.textContent?.trim(),
      );
      expect(texts).not.toContain('Subir archivo');
      expect(texts).not.toContain('Consolidar');
    });

    it('advances via "Siguiente" without uploading, and never calls JudgeImportApiService', () => {
      const fixture = createReadOnlyComponent();
      const emitted: void[] = [];
      fixture.componentInstance.saved.subscribe(() => emitted.push(undefined));

      buttonWithText(fixture.nativeElement, 'Siguiente').click();

      expect(fakeJudgeImportApi.upload).not.toHaveBeenCalled();
      expect(fakeJudgeImportApi.consolidate).not.toHaveBeenCalled();
      expect(emitted.length).toBe(1);
    });
  });

  it('keeps the upload UI visible and never calls GET judges when readOnly is false (default)', () => {
    const fixture = createComponent();

    expect(fixture.nativeElement.querySelector('input[type="file"]')).not.toBeNull();
    expect(fakeJudgeManagementApi.getJudges).not.toHaveBeenCalled();
  });

  // T126-ish: the notify table/bulk action added right after a successful consolidation.
  describe('notify judges after consolidation', () => {
    function consolidatedFixture(judges: JudgeProfile[]) {
      const fixture = uploadedFixture([rowFixture({ rowNumber: 1, status: 'Valid' })]);
      fakeJudgeImportApi.consolidate.mockReturnValue(
        of({ created: [], updated: [], excluded: 0, skipped: [] }),
      );
      fakeJudgeManagementApi.getJudges.mockReturnValue(of(judges));
      buttonWithText(fixture.nativeElement, 'Siguiente').click();
      fixture.detectChanges();
      return fixture;
    }

    it('fetches and renders the whole competition roster after consolidation, not just this batch', () => {
      const fixture = consolidatedFixture([
        judgeProfileFixture({ id: 'j1', email: 'ana@example.com', invitationStatus: 'Pending' }),
        judgeProfileFixture({ id: 'j2', email: 'raul@example.com', invitationStatus: 'Sent' }),
      ]);

      expect(fakeJudgeManagementApi.getJudges).toHaveBeenCalledWith('c1');
      const rows = fixture.nativeElement.querySelectorAll('tr[data-judge-email]');
      expect(rows.length).toBe(2);
      expect(fixture.nativeElement.textContent).toContain('ana@example.com');
      expect(fixture.nativeElement.textContent).toContain('Pendiente');
      expect(fixture.nativeElement.textContent).toContain('Enviada');
    });

    it('does not render the notify table when the competition has no registered judges', () => {
      const fixture = consolidatedFixture([]);

      expect(fixture.nativeElement.querySelector('table')).toBeNull();
    });

    it('bulk-notifies only after confirming, naming the pending count', () => {
      const confirmSpy = jest.spyOn(window, 'confirm').mockReturnValue(true);
      fakeJudgeManagementApi.notifyJudges.mockReturnValue(
        of({ queued: [{ id: 'j1', email: 'ana@example.com' }] }),
      );
      const fixture = consolidatedFixture([
        judgeProfileFixture({ id: 'j1', email: 'ana@example.com', invitationStatus: 'Pending' }),
      ]);

      buttonWithText(fixture.nativeElement, 'Notificar a todos los pendientes (1)').click();
      fixture.detectChanges();

      expect(confirmSpy).toHaveBeenCalledWith(expect.stringContaining('1 juez(es)'));
      expect(fakeJudgeManagementApi.notifyJudges).toHaveBeenCalledWith('c1');
      expect(fixture.nativeElement.textContent).toContain('Se enviarán 1 invitaciones en breve.');

      confirmSpy.mockRestore();
    });

    it('does not call notifyJudges when the confirm dialog is dismissed', () => {
      const confirmSpy = jest.spyOn(window, 'confirm').mockReturnValue(false);
      const fixture = consolidatedFixture([
        judgeProfileFixture({ id: 'j1', email: 'ana@example.com', invitationStatus: 'Pending' }),
      ]);

      buttonWithText(fixture.nativeElement, 'Notificar a todos los pendientes (1)').click();
      fixture.detectChanges();

      expect(fakeJudgeManagementApi.notifyJudges).not.toHaveBeenCalled();

      confirmSpy.mockRestore();
    });

    it('resends a single judge invitation and refetches the roster afterwards', () => {
      fakeJudgeManagementApi.resendInvitation.mockReturnValue(of({ status: 'Sent' }));
      const fixture = consolidatedFixture([
        judgeProfileFixture({ id: 'j1', email: 'ana@example.com', invitationStatus: 'Failed' }),
      ]);
      fakeJudgeManagementApi.getJudges.mockReturnValue(
        of([judgeProfileFixture({ id: 'j1', email: 'ana@example.com', invitationStatus: 'Sent' })]),
      );

      buttonWithText(fixture.nativeElement, 'Notificar').click();
      fixture.detectChanges();

      expect(fakeJudgeManagementApi.resendInvitation).toHaveBeenCalledWith('c1', 'j1');
      expect(fakeJudgeManagementApi.getJudges).toHaveBeenCalledTimes(2);
      expect(fixture.nativeElement.textContent).toContain('Enviada');
    });
  });
});
