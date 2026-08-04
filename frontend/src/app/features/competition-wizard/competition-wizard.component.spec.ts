import { Location } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { CatalogApiService } from '../../core/api/catalog-api.service';
import { CompetitionsApiService } from '../../core/api/competitions-api.service';
import type { CompetitionDetail } from '../../core/api/competitions-api.service';
import { EntriesApiService } from '../../core/api/entries-api.service';
import { ImportApiService } from '../../core/api/import-api.service';
import { JudgeImportApiService } from '../../core/api/judge-import-api.service';
import { TableManagementApiService } from '../table-management/table-management-api.service';
import { CompetitionWizardComponent } from './competition-wizard.component';
import { ImportStepComponent } from './steps/import-step.component';
import { JudgeImportStepComponent } from './steps/judge-import-step.component';

function detailFixture(overrides: Partial<CompetitionDetail> = {}): CompetitionDetail {
  return {
    id: 'c1',
    name: 'Golden Ale Cup',
    venue: 'Town Hall',
    startDate: '2026-08-01',
    endDate: '2026-08-02',
    description: null,
    logoUrl: null,
    entryLimit: null,
    registrationStart: null,
    registrationEnd: null,
    state: 'Draft',
    ...overrides,
  };
}

describe('CompetitionWizardComponent', () => {
  let fakeApi: {
    create: jest.Mock;
    update: jest.Mock;
    getById: jest.Mock;
    getCategories: jest.Mock;
  };
  let fakeCatalogApi: { getStyles: jest.Mock };
  let fakeImportApi: {
    upload: jest.Mock;
    getImport: jest.Mock;
    editRow: jest.Mock;
    excludeRow: jest.Mock;
    consolidate: jest.Mock;
    revalidate: jest.Mock;
  };
  let fakeEntriesApi: { getEntries: jest.Mock };
  let fakeJudgeImportApi: {
    upload: jest.Mock;
    getImport: jest.Mock;
    editRow: jest.Mock;
    excludeRow: jest.Mock;
    consolidate: jest.Mock;
  };
  let fakeTableManagementApi: {
    getTables: jest.Mock;
    getJudges: jest.Mock;
    createTable: jest.Mock;
    updateTable: jest.Mock;
  };

  function configure(id: string | null) {
    fakeApi = {
      create: jest.fn(),
      update: jest.fn(),
      getById: jest.fn(),
      getCategories: jest.fn().mockReturnValue(of({ categories: [] })),
    };
    fakeCatalogApi = { getStyles: jest.fn().mockReturnValue(of([])) };
    fakeImportApi = {
      upload: jest.fn(),
      getImport: jest.fn(),
      editRow: jest.fn(),
      excludeRow: jest.fn(),
      consolidate: jest.fn(),
      revalidate: jest.fn().mockReturnValue(of({ importId: 'i1', rows: [] })),
    };
    fakeEntriesApi = { getEntries: jest.fn().mockReturnValue(of([])) };
    fakeJudgeImportApi = {
      upload: jest.fn(),
      getImport: jest.fn().mockReturnValue(of({ importId: 'ji1', rows: [] })),
      editRow: jest.fn(),
      excludeRow: jest.fn(),
      consolidate: jest.fn(),
    };
    fakeTableManagementApi = {
      getTables: jest.fn().mockReturnValue(of([])),
      getJudges: jest.fn().mockReturnValue(of([])),
      createTable: jest.fn(),
      updateTable: jest.fn(),
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: CompetitionsApiService, useValue: fakeApi },
        { provide: CatalogApiService, useValue: fakeCatalogApi },
        { provide: ImportApiService, useValue: fakeImportApi },
        { provide: EntriesApiService, useValue: fakeEntriesApi },
        { provide: JudgeImportApiService, useValue: fakeJudgeImportApi },
        { provide: TableManagementApiService, useValue: fakeTableManagementApi },
        provideRouter([]),
        // Must come after provideRouter([]) — it registers its own root ActivatedRoute, which
        // would otherwise win over this mock and silently drop the :id route param.
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap(id ? { id } : {}) } },
        },
      ],
    });
  }

  it('starts blank on step 1 when no :id route param is present', () => {
    configure(null);
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    expect(fakeApi.getById).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('app-basics-step')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('app-details-step')).toBeFalsy();
  });

  it('loads the competition and populates step 1 when :id is present (resume-with-data)', () => {
    configure('c1');
    fakeApi.getById.mockReturnValue(of(detailFixture({ name: 'Resumed Cup' })));

    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    expect(fakeApi.getById).toHaveBeenCalledWith('c1');
    const nameInput = fixture.nativeElement.querySelector('input#basics-name') as HTMLInputElement;
    expect(nameInput.value).toBe('Resumed Cup');
  });

  it('shows a loading state while fetching, and an error state if the fetch fails', () => {
    configure('missing');
    fakeApi.getById.mockReturnValue(throwError(() => new Error('not found')));

    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('No hemos podido cargar esta competición');
  });

  it('advances to step 2 and updates the URL after basics is saved for a brand-new competition', () => {
    configure(null);
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();
    const location = TestBed.inject(Location);
    const replaceStateSpy = jest.spyOn(location, 'replaceState');

    const detail = detailFixture({ id: 'new-id' });
    fixture.componentInstance['onBasicsSaved'](detail);
    fixture.detectChanges();

    expect(replaceStateSpy).toHaveBeenCalledWith('/organizer/competitions/new-id');
    expect(fixture.nativeElement.querySelector('app-details-step')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('app-basics-step')).toBeFalsy();
  });

  it('does not touch the URL when basics is saved for an already-existing competition', () => {
    configure('c1');
    fakeApi.getById.mockReturnValue(of(detailFixture()));
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();
    const location = TestBed.inject(Location);
    const replaceStateSpy = jest.spyOn(location, 'replaceState');

    fixture.componentInstance['onBasicsSaved'](detailFixture({ name: 'Updated name' }));
    fixture.detectChanges();

    expect(replaceStateSpy).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('app-details-step')).toBeTruthy();
  });

  it('advances to step 3 after details is saved', () => {
    configure('c1');
    fakeApi.getById.mockReturnValue(of(detailFixture()));
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    fixture.componentInstance['onDetailsSaved'](detailFixture({ description: 'Updated' }));
    fixture.detectChanges();

    expect(fixture.componentInstance['currentStep']()).toBe(3);
    expect(fixture.nativeElement.querySelector('app-categories-step')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('app-details-step')).toBeFalsy();
  });

  function stepButtons(fixture: { nativeElement: HTMLElement }): HTMLButtonElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('.stepper__step'));
  }

  it('disables the step 2, 3, 4 and 5 stepper buttons for a brand-new, unsaved competition', () => {
    configure(null);
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    const [step1Button, step2Button, step3Button, step4Button, step5Button] = stepButtons(fixture);
    expect(step1Button.disabled).toBe(false);
    expect(step2Button.disabled).toBe(true);
    expect(step3Button.disabled).toBe(true);
    expect(step4Button.disabled).toBe(true);
    expect(step5Button.disabled).toBe(true);

    fixture.componentInstance['goToStep'](2);
    fixture.componentInstance['goToStep'](3);
    fixture.detectChanges();

    expect(fixture.componentInstance['currentStep']()).toBe(1);
    expect(fixture.nativeElement.querySelector('app-basics-step')).toBeTruthy();
  });

  it('enables the step 2, 3, 4 and 5 stepper buttons once basics is saved, and jumps directly to step 3', () => {
    configure(null);
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    fixture.componentInstance['onBasicsSaved'](detailFixture({ id: 'new-id' }));
    fixture.detectChanges();

    const [, step2Button, step3Button, step4Button, step5Button] = stepButtons(fixture);
    expect(step2Button.disabled).toBe(false);
    expect(step3Button.disabled).toBe(false);
    expect(step4Button.disabled).toBe(false);
    expect(step5Button.disabled).toBe(false);

    step3Button.click();
    fixture.detectChanges();

    expect(fixture.componentInstance['currentStep']()).toBe(3);
    expect(fixture.nativeElement.querySelector('app-categories-step')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('app-details-step')).toBeFalsy();
  });

  it('jumps back from step 3 to step 1 via the stepper, keeping the last-saved competition snapshot', () => {
    configure('c1');
    fakeApi.getById.mockReturnValue(of(detailFixture({ name: 'Resumed Cup' })));
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    fixture.componentInstance['onDetailsSaved'](detailFixture({ name: 'Resumed Cup' }));
    fixture.detectChanges();
    expect(fixture.componentInstance['currentStep']()).toBe(3);

    const [step1Button] = stepButtons(fixture);
    step1Button.click();
    fixture.detectChanges();

    expect(fixture.componentInstance['currentStep']()).toBe(1);
    expect(fixture.nativeElement.querySelector('app-basics-step')).toBeTruthy();
    const nameInput = fixture.nativeElement.querySelector('input#basics-name') as HTMLInputElement;
    expect(nameInput.value).toBe('Resumed Cup');
  });

  it('enables all stepper buttons immediately when editing an existing competition, before any save', () => {
    configure('c1');
    fakeApi.getById.mockReturnValue(of(detailFixture()));
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    const [step1Button, step2Button, step3Button, step4Button, step5Button] = stepButtons(fixture);
    expect(step1Button.disabled).toBe(false);
    expect(step2Button.disabled).toBe(false);
    expect(step3Button.disabled).toBe(false);
    expect(step4Button.disabled).toBe(false);
    expect(step5Button.disabled).toBe(false);

    step3Button.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-categories-step')).toBeTruthy();
  });

  it('advances to step 4 and renders the import step once categories is saved, marking step 3 done', () => {
    configure('c1');
    fakeApi.getById.mockReturnValue(of(detailFixture()));
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    fixture.componentInstance['onDetailsSaved'](detailFixture());
    fixture.detectChanges();
    expect(fixture.componentInstance['currentStep']()).toBe(3);

    fixture.componentInstance['onCategoriesSaved']();
    fixture.detectChanges();

    expect(fixture.componentInstance['currentStep']()).toBe(4);
    expect(fixture.nativeElement.querySelector('app-import-step')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('app-categories-step')).toBeFalsy();

    const items = fixture.nativeElement.querySelectorAll('.stepper__item');
    expect(items[2].classList.contains('is-done')).toBe(true);
  });

  it('renders the judge-import step when navigating to step 5 via the stepper, marking step 4 done', () => {
    configure('c1');
    fakeApi.getById.mockReturnValue(of(detailFixture()));
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    fixture.componentInstance['onDetailsSaved'](detailFixture());
    fixture.detectChanges();
    fixture.componentInstance['onCategoriesSaved']();
    fixture.detectChanges();
    expect(fixture.componentInstance['currentStep']()).toBe(4);

    const [, , , step4Button, step5Button] = stepButtons(fixture);
    expect(step5Button.disabled).toBe(false);
    step5Button.click();
    fixture.detectChanges();

    expect(fixture.componentInstance['currentStep']()).toBe(5);
    expect(fixture.nativeElement.querySelector('app-judge-import-step')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('app-import-step')).toBeFalsy();

    const items = fixture.nativeElement.querySelectorAll('.stepper__item');
    expect(items[3].classList.contains('is-done')).toBe(true);
    expect(step4Button).toBeTruthy();
  });

  it('renders a 6th stepper item labeled "Mesas"', () => {
    configure('c1');
    fakeApi.getById.mockReturnValue(of(detailFixture()));
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    const markers = fixture.nativeElement.querySelectorAll('.stepper__marker');
    const labels = fixture.nativeElement.querySelectorAll('.stepper__label');
    expect(markers[5].textContent?.trim()).toBe('6');
    expect(labels[5].textContent?.trim()).toBe('Mesas');
  });

  it('advances to step 6 and renders app-tables-step when onJudgeImportSaved() runs, marking step 5 done', () => {
    configure('c1');
    fakeApi.getById.mockReturnValue(of(detailFixture()));
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    fixture.componentInstance['onDetailsSaved'](detailFixture());
    fixture.detectChanges();
    fixture.componentInstance['onCategoriesSaved']();
    fixture.detectChanges();
    fixture.componentInstance['goToStep'](5);
    fixture.detectChanges();
    expect(fixture.componentInstance['currentStep']()).toBe(5);

    fixture.componentInstance['onJudgeImportSaved']();
    fixture.detectChanges();

    expect(fixture.componentInstance['currentStep']()).toBe(6);
    expect(fixture.nativeElement.querySelector('app-tables-step')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('app-judge-import-step')).toBeFalsy();
    expect(fakeTableManagementApi.getTables).toHaveBeenCalledWith('c1');

    const items = fixture.nativeElement.querySelectorAll('.stepper__item');
    expect(items[4].classList.contains('is-done')).toBe(true);
  });

  it('advances to step 6 when the judge-import step emits saved (real wiring, not a direct call)', () => {
    configure('c1');
    fakeApi.getById.mockReturnValue(of(detailFixture()));
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    fixture.componentInstance['onDetailsSaved'](detailFixture());
    fixture.detectChanges();
    fixture.componentInstance['onCategoriesSaved']();
    fixture.detectChanges();
    fixture.componentInstance['goToStep'](5);
    fixture.detectChanges();

    const judgeImportStepDebugEl = fixture.debugElement.query(
      By.directive(JudgeImportStepComponent),
    );
    judgeImportStepDebugEl.componentInstance.saved.emit();
    fixture.detectChanges();

    expect(fixture.componentInstance['currentStep']()).toBe(6);
    expect(fixture.nativeElement.querySelector('app-tables-step')).toBeTruthy();
  });

  it('hoists the judge-roster import batch id emitted by the judge-import step and re-supplies it after navigating away and back', () => {
    configure('c1');
    fakeApi.getById.mockReturnValue(of(detailFixture()));
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    fixture.componentInstance['onDetailsSaved'](detailFixture());
    fixture.detectChanges();
    fixture.componentInstance['onCategoriesSaved']();
    fixture.detectChanges();
    fixture.componentInstance['goToStep'](5);
    fixture.detectChanges();
    expect(fixture.componentInstance['currentStep']()).toBe(5);

    let judgeImportStepDebugEl = fixture.debugElement.query(By.directive(JudgeImportStepComponent));
    expect(judgeImportStepDebugEl.componentInstance.judgeImportId()).toBeNull();

    judgeImportStepDebugEl.componentInstance.judgeImportIdChange.emit('ji-1');
    fixture.detectChanges();

    expect(fixture.componentInstance['judgeImportId']()).toBe('ji-1');

    // Navigate back to step 4 (destroys the app-judge-import-step instance) and forward again —
    // the wizard-held signal, not the child's local state, is what must survive this round trip.
    fixture.componentInstance['onBack']();
    fixture.detectChanges();
    fixture.componentInstance['goToStep'](5);
    fixture.detectChanges();

    judgeImportStepDebugEl = fixture.debugElement.query(By.directive(JudgeImportStepComponent));
    expect(judgeImportStepDebugEl.componentInstance.judgeImportId()).toBe('ji-1');
  });

  it('marks only the active step button with aria-current="step"', () => {
    configure('c1');
    fakeApi.getById.mockReturnValue(of(detailFixture()));
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    const [step1Button, step2Button, step3Button] = stepButtons(fixture);
    expect(step1Button.getAttribute('aria-current')).toBe('step');
    expect(step2Button.getAttribute('aria-current')).toBeNull();
    expect(step3Button.getAttribute('aria-current')).toBeNull();

    step2Button.click();
    fixture.detectChanges();

    const [step1After, step2After] = stepButtons(fixture);
    expect(step1After.getAttribute('aria-current')).toBeNull();
    expect(step2After.getAttribute('aria-current')).toBe('step');
  });

  it('hoists the import batch id emitted by the import step and re-supplies it after navigating away and back (bug: local import-step state was lost on step navigation)', () => {
    configure('c1');
    fakeApi.getById.mockReturnValue(of(detailFixture()));
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    fixture.componentInstance['onDetailsSaved'](detailFixture());
    fixture.detectChanges();
    fixture.componentInstance['onCategoriesSaved']();
    fixture.detectChanges();
    expect(fixture.componentInstance['currentStep']()).toBe(4);

    let importStepDebugEl = fixture.debugElement.query(By.directive(ImportStepComponent));
    expect(importStepDebugEl.componentInstance.importId()).toBeNull();

    importStepDebugEl.componentInstance.importIdChange.emit('imp-1');
    fixture.detectChanges();

    expect(fixture.componentInstance['importId']()).toBe('imp-1');

    // Navigate back to step 3 (destroys the app-import-step instance) and forward again — the
    // wizard-held signal, not the child's local state, is what must survive this round trip.
    fixture.componentInstance['onBack']();
    fixture.detectChanges();
    fixture.componentInstance['onCategoriesSaved']();
    fixture.detectChanges();

    importStepDebugEl = fixture.debugElement.query(By.directive(ImportStepComponent));
    expect(importStepDebugEl.componentInstance.importId()).toBe('imp-1');
  });

  describe('unsaved-edits confirmation (FR-007)', () => {
    it('navigates immediately via the stepper when the active step reports no unsaved edits', () => {
      configure('c1');
      fakeApi.getById.mockReturnValue(of(detailFixture()));
      const fixture = TestBed.createComponent(CompetitionWizardComponent);
      fixture.detectChanges();

      const [, , step3Button] = stepButtons(fixture);
      step3Button.click();
      fixture.detectChanges();

      expect(fixture.componentInstance['currentStep']()).toBe(3);
      expect(fixture.nativeElement.querySelector('[role="alertdialog"]')).toBeFalsy();
    });

    it('shows a confirm dialog instead of navigating when the active step reports unsaved edits, via a stepper jump', () => {
      configure('c1');
      fakeApi.getById.mockReturnValue(of(detailFixture()));
      const fixture = TestBed.createComponent(CompetitionWizardComponent);
      fixture.detectChanges();

      fixture.componentInstance['stepDirty'].set(true);
      fixture.detectChanges();

      const [, , step3Button] = stepButtons(fixture);
      step3Button.click();
      fixture.detectChanges();

      expect(fixture.componentInstance['currentStep']()).toBe(1);
      const dialog = fixture.nativeElement.querySelector('[role="alertdialog"]');
      expect(dialog).toBeTruthy();
      expect(dialog.textContent).toContain('Cambios sin guardar');
    });

    it('shows a confirm dialog instead of navigating on "Back" when the active step reports unsaved edits', () => {
      configure('c1');
      fakeApi.getById.mockReturnValue(of(detailFixture()));
      const fixture = TestBed.createComponent(CompetitionWizardComponent);
      fixture.detectChanges();

      fixture.componentInstance['onDetailsSaved'](detailFixture());
      fixture.detectChanges();
      expect(fixture.componentInstance['currentStep']()).toBe(3);

      fixture.componentInstance['stepDirty'].set(true);
      fixture.componentInstance['onBack']();
      fixture.detectChanges();

      expect(fixture.componentInstance['currentStep']()).toBe(3);
      expect(fixture.nativeElement.querySelector('[role="alertdialog"]')).toBeTruthy();
    });

    it('"Descartar y continuar" discards the pending edits and completes the navigation', () => {
      configure('c1');
      fakeApi.getById.mockReturnValue(of(detailFixture()));
      const fixture = TestBed.createComponent(CompetitionWizardComponent);
      fixture.detectChanges();

      fixture.componentInstance['stepDirty'].set(true);
      fixture.detectChanges();
      const [, , step3Button] = stepButtons(fixture);
      step3Button.click();
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('[role="alertdialog"]')).toBeTruthy();

      fixture.componentInstance['onDiscardAndNavigate']();
      fixture.detectChanges();

      expect(fixture.componentInstance['currentStep']()).toBe(3);
      expect(fixture.componentInstance['stepDirty']()).toBe(false);
      expect(fixture.nativeElement.querySelector('[role="alertdialog"]')).toBeFalsy();
      expect(fixture.nativeElement.querySelector('app-categories-step')).toBeTruthy();
    });

    it('"Seguir editando" closes the dialog and leaves currentStep unchanged', () => {
      configure('c1');
      fakeApi.getById.mockReturnValue(of(detailFixture()));
      const fixture = TestBed.createComponent(CompetitionWizardComponent);
      fixture.detectChanges();

      fixture.componentInstance['stepDirty'].set(true);
      fixture.detectChanges();
      const [, , step3Button] = stepButtons(fixture);
      step3Button.click();
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('[role="alertdialog"]')).toBeTruthy();

      fixture.componentInstance['onKeepEditing']();
      fixture.detectChanges();

      expect(fixture.componentInstance['currentStep']()).toBe(1);
      expect(fixture.nativeElement.querySelector('[role="alertdialog"]')).toBeFalsy();
      expect(fixture.nativeElement.querySelector('app-basics-step')).toBeTruthy();
    });

    it('closes the dialog without navigating when the backdrop is clicked', () => {
      configure('c1');
      fakeApi.getById.mockReturnValue(of(detailFixture()));
      const fixture = TestBed.createComponent(CompetitionWizardComponent);
      fixture.detectChanges();

      fixture.componentInstance['stepDirty'].set(true);
      fixture.detectChanges();
      const [, , step3Button] = stepButtons(fixture);
      step3Button.click();
      fixture.detectChanges();

      const backdrop = fixture.nativeElement.querySelector('.modal-backdrop') as HTMLElement;
      backdrop.click();
      fixture.detectChanges();

      expect(fixture.componentInstance['currentStep']()).toBe(1);
      expect(fixture.nativeElement.querySelector('[role="alertdialog"]')).toBeFalsy();
    });

    it('does not re-prompt when clicking the already-active step button', () => {
      configure('c1');
      fakeApi.getById.mockReturnValue(of(detailFixture()));
      const fixture = TestBed.createComponent(CompetitionWizardComponent);
      fixture.detectChanges();

      fixture.componentInstance['stepDirty'].set(true);
      fixture.detectChanges();

      const [step1Button] = stepButtons(fixture);
      step1Button.click();
      fixture.detectChanges();

      expect(fixture.componentInstance['currentStep']()).toBe(1);
      expect(fixture.nativeElement.querySelector('[role="alertdialog"]')).toBeFalsy();
    });
  });
});
