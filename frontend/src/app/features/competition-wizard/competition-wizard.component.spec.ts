import Keycloak from 'keycloak-js';
import { Location } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';

import { CatalogApiService } from '../../core/api/catalog-api.service';
import { CompetitionsApiService } from '../../core/api/competitions-api.service';
import type { CompetitionDetail } from '../../core/api/competitions-api.service';
import { EntriesApiService } from '../../core/api/entries-api.service';
import { ImportApiService } from '../../core/api/import-api.service';
import { JudgeImportApiService } from '../../core/api/judge-import-api.service';
import { JudgeManagementApiService } from '../../core/api/judge-management-api.service';
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
  let fakeJudgeManagementApi: { getJudges: jest.Mock };

  function configure(id: string | null, queryParams: Record<string, string> = {}) {
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
    // Slice-B dependency of JudgeImportStepComponent's readOnly mode (T127/FR-061): it lists
    // already-registered judges instead of the upload flow, via JudgeManagementApiService.
    fakeJudgeManagementApi = { getJudges: jest.fn().mockReturnValue(of([])) };
    TestBed.configureTestingModule({
      providers: [
        {
          provide: Keycloak,
          useValue: { tokenParsed: { realm_access: { roles: ['ORGANIZER'] } }, logout: jest.fn() },
        },
        { provide: CompetitionsApiService, useValue: fakeApi },
        { provide: CatalogApiService, useValue: fakeCatalogApi },
        { provide: ImportApiService, useValue: fakeImportApi },
        { provide: EntriesApiService, useValue: fakeEntriesApi },
        { provide: JudgeImportApiService, useValue: fakeJudgeImportApi },
        { provide: TableManagementApiService, useValue: fakeTableManagementApi },
        { provide: JudgeManagementApiService, useValue: fakeJudgeManagementApi },
        provideRouter([]),
        // Must come after provideRouter([]) — it registers its own root ActivatedRoute, which
        // would otherwise win over this mock and silently drop the :id route param.
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: convertToParamMap(id ? { id } : {}),
              queryParamMap: convertToParamMap(queryParams),
            },
          },
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

  it('advances to step 2 and writes the URL exactly once after basics is saved for a brand-new competition', () => {
    configure(null);
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();
    const location = TestBed.inject(Location);
    const replaceStateSpy = jest.spyOn(location, 'replaceState');

    const detail = detailFixture({ id: 'new-id' });
    fixture.componentInstance['onBasicsSaved'](detail);
    fixture.detectChanges();

    // T127 code review (m1): onBasicsSaved used to call replaceState itself for this exact
    // case, racing the ?step= effect below and briefly exposing a query-less URL. It must now
    // leave the address bar to that one effect, which sees id and step together in a single
    // flush and writes the final `?step=2` URL directly.
    expect(replaceStateSpy).toHaveBeenCalledTimes(1);
    expect(replaceStateSpy.mock.calls[0]?.[0]).toBe('/organizer/competitions/new-id');
    expect(replaceStateSpy.mock.calls[0]?.[1]).toBe('step=2');
    expect(fixture.nativeElement.querySelector('app-details-step')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('app-basics-step')).toBeFalsy();
  });

  it('does not replace the URL with a fabricated id when basics is saved for an already-existing competition (only the step query is kept in sync)', () => {
    configure('c1');
    fakeApi.getById.mockReturnValue(of(detailFixture()));
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();
    const location = TestBed.inject(Location);
    const replaceStateSpy = jest.spyOn(location, 'replaceState');

    fixture.componentInstance['onBasicsSaved'](detailFixture({ name: 'Updated name' }));
    fixture.detectChanges();

    // T127: the URL is still kept in sync with the current step (see the "?step=N" describe
    // block below) — what this test pins is that onBasicsSaved itself never re-derives a URL
    // from the (already known) id the way it must for a brand-new competition above.
    expect(replaceStateSpy.mock.calls[0]?.[0]).toBe('/organizer/competitions/c1');
    expect(replaceStateSpy.mock.calls[0]?.[1]).toBe('step=2');
    expect(replaceStateSpy).not.toHaveBeenCalledWith('/organizer/competitions/c1');
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

  function buttonWithText(root: HTMLElement, text: string): HTMLButtonElement {
    const match = [...root.querySelectorAll('button')].find(
      (button) => button.textContent?.trim() === text,
    );
    if (!match) {
      throw new Error(`No button with text "${text}" found`);
    }
    return match as HTMLButtonElement;
  }

  function stepButtons(fixture: { nativeElement: HTMLElement }): HTMLButtonElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('.stepper__step'));
  }

  it('shows an uncoloured circle with just the number for a step never visited', () => {
    configure('c1');
    fakeApi.getById.mockReturnValue(of(detailFixture()));
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    const items = fixture.nativeElement.querySelectorAll('.stepper__item');
    const markers = fixture.nativeElement.querySelectorAll('.stepper__marker');
    // Step 5 has never been visited (the wizard is still on step 1) — no colour class, no check.
    expect(items[4].classList.contains('is-complete')).toBe(false);
    expect(items[4].classList.contains('is-partial')).toBe(false);
    expect(items[4].classList.contains('is-reached')).toBe(false);
    expect(markers[4].querySelector('svg')).toBeFalsy();
    expect(markers[4].textContent?.trim()).toBe('5');
  });

  it('marks a passed step complete (green check) once its required fields are all filled in', () => {
    configure('c1');
    fakeApi.getById.mockReturnValue(of(detailFixture()));
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    // Step 1's required fields (name, venue, startDate, endDate) are already satisfied by
    // detailFixture(), so moving past it should show it as complete rather than merely reached.
    fixture.componentInstance['onDetailsSaved'](detailFixture());
    fixture.detectChanges();

    const items = fixture.nativeElement.querySelectorAll('.stepper__item');
    const markers = fixture.nativeElement.querySelectorAll('.stepper__marker');
    expect(items[0].classList.contains('is-complete')).toBe(true);
    expect(items[0].classList.contains('is-partial')).toBe(false);
    expect(markers[0].querySelector('svg')).toBeTruthy();
  });

  it('keeps a passed step amber (partial) while a required field is still missing', () => {
    configure('c1');
    // getCategories() defaults to an empty list, so categories-step falls back to a single
    // unassigned "General" category — canFinish() is false until a style gets assigned to it.
    fakeApi.getById.mockReturnValue(of(detailFixture()));
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    fixture.componentInstance['onDetailsSaved'](detailFixture());
    fixture.detectChanges();
    fixture.componentInstance['onCategoriesSaved']();
    fixture.detectChanges();

    const items = fixture.nativeElement.querySelectorAll('.stepper__item');
    const markers = fixture.nativeElement.querySelectorAll('.stepper__marker');
    expect(items[2].classList.contains('is-partial')).toBe(true);
    expect(items[2].classList.contains('is-complete')).toBe(false);
    expect(markers[2].querySelector('svg')).toBeFalsy();
    expect(markers[2].textContent?.trim()).toBe('3');
  });

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

  it('advances to step 4 and renders the import step once categories is saved, marking step 3 reached', () => {
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
    expect(items[2].classList.contains('is-reached')).toBe(true);
  });

  it('advances to step 5 when the import step emits saved (real wiring, not a direct call)', () => {
    configure('c1');
    fakeApi.getById.mockReturnValue(of(detailFixture()));
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    fixture.componentInstance['onDetailsSaved'](detailFixture());
    fixture.detectChanges();
    fixture.componentInstance['onCategoriesSaved']();
    fixture.detectChanges();
    expect(fixture.componentInstance['currentStep']()).toBe(4);

    const importStepDebugEl = fixture.debugElement.query(By.directive(ImportStepComponent));
    importStepDebugEl.componentInstance.saved.emit();
    fixture.detectChanges();

    expect(fixture.componentInstance['currentStep']()).toBe(5);
    expect(fixture.nativeElement.querySelector('app-judge-import-step')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('app-import-step')).toBeFalsy();
  });

  it('renders the judge-import step when navigating to step 5 via the stepper, marking step 4 reached', () => {
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
    expect(items[3].classList.contains('is-reached')).toBe(true);
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

  it('advances to step 6 and renders app-tables-step when onJudgeImportSaved() runs, marking step 5 reached', () => {
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
    expect(items[4].classList.contains('is-reached')).toBe(true);
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
  // T125: "Volver al listado" was hoisted out of steps 1 and 3 into the wizard header, where it
  // exists once for all six steps and reuses the shell's own FR-007 prompt. None of that had any
  // coverage — these pin the exit path and its interaction with the step-jump path.
  describe('header exit (FR-007)', () => {
    function exitLink(fixture: { nativeElement: HTMLElement }): HTMLButtonElement {
      return fixture.nativeElement.querySelector('.back-to-list-link') as HTMLButtonElement;
    }

    it('leaves for the dashboard straight away when the step is clean', () => {
      configure('c1');
      fakeApi.getById.mockReturnValue(of(detailFixture()));
      const fixture = TestBed.createComponent(CompetitionWizardComponent);
      fixture.detectChanges();
      const navigateSpy = jest.spyOn(TestBed.inject(Router), 'navigateByUrl');

      exitLink(fixture).click();
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('[role="alertdialog"]')).toBeFalsy();
      expect(navigateSpy).toHaveBeenCalledWith('/organizer/dashboard');
    });

    it('prompts instead of leaving when the step has unsaved changes', () => {
      configure('c1');
      fakeApi.getById.mockReturnValue(of(detailFixture()));
      const fixture = TestBed.createComponent(CompetitionWizardComponent);
      fixture.detectChanges();
      const navigateSpy = jest.spyOn(TestBed.inject(Router), 'navigateByUrl');

      fixture.componentInstance['stepDirty'].set(true);
      fixture.detectChanges();
      exitLink(fixture).click();
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('[role="alertdialog"]')).toBeTruthy();
      expect(navigateSpy).not.toHaveBeenCalled();
    });

    it('leaves on "Descartar y continuar" after prompting', () => {
      configure('c1');
      fakeApi.getById.mockReturnValue(of(detailFixture()));
      const fixture = TestBed.createComponent(CompetitionWizardComponent);
      fixture.detectChanges();
      const navigateSpy = jest.spyOn(TestBed.inject(Router), 'navigateByUrl');

      fixture.componentInstance['stepDirty'].set(true);
      fixture.detectChanges();
      exitLink(fixture).click();
      fixture.detectChanges();

      buttonWithText(fixture.nativeElement, 'Descartar y continuar').click();
      fixture.detectChanges();

      expect(navigateSpy).toHaveBeenCalledWith('/organizer/dashboard');
      expect(fixture.componentInstance['stepDirty']()).toBe(false);
    });

    it('stays put and keeps the step dirty on "Seguir editando"', () => {
      configure('c1');
      fakeApi.getById.mockReturnValue(of(detailFixture()));
      const fixture = TestBed.createComponent(CompetitionWizardComponent);
      fixture.detectChanges();
      const navigateSpy = jest.spyOn(TestBed.inject(Router), 'navigateByUrl');

      fixture.componentInstance['stepDirty'].set(true);
      fixture.detectChanges();
      exitLink(fixture).click();
      fixture.detectChanges();

      buttonWithText(fixture.nativeElement, 'Seguir editando').click();
      fixture.detectChanges();

      expect(navigateSpy).not.toHaveBeenCalled();
      expect(fixture.componentInstance['stepDirty']()).toBe(true);
      expect(fixture.nativeElement.querySelector('[role="alertdialog"]')).toBeFalsy();
    });

    // Both paths share one dialog, so a pending exit must win over a pending step jump rather
    // than silently doing both (or neither).
    it('leaves rather than jumping when an exit is requested after a step jump', () => {
      configure('c1');
      fakeApi.getById.mockReturnValue(of(detailFixture()));
      const fixture = TestBed.createComponent(CompetitionWizardComponent);
      fixture.detectChanges();
      const navigateSpy = jest.spyOn(TestBed.inject(Router), 'navigateByUrl');

      fixture.componentInstance['stepDirty'].set(true);
      fixture.detectChanges();
      const [, step2Button] = stepButtons(fixture);
      step2Button.click();
      fixture.detectChanges();
      exitLink(fixture).click();
      fixture.detectChanges();

      buttonWithText(fixture.nativeElement, 'Descartar y continuar').click();
      fixture.detectChanges();

      expect(navigateSpy).toHaveBeenCalledWith('/organizer/dashboard');
      expect(fixture.componentInstance['currentStep']()).toBe(1);
    });

    it('describes the dialog with both its body and its save hint, in reading order', () => {
      configure('c1');
      fakeApi.getById.mockReturnValue(of(detailFixture()));
      const fixture = TestBed.createComponent(CompetitionWizardComponent);
      fixture.detectChanges();

      fixture.componentInstance['stepDirty'].set(true);
      fixture.detectChanges();
      exitLink(fixture).click();
      fixture.detectChanges();

      const dialog = fixture.nativeElement.querySelector('[role="alertdialog"]') as HTMLElement;
      expect(dialog.getAttribute('aria-describedby')).toBe(
        'unsaved-changes-body unsaved-changes-hint',
      );
      const hint = fixture.nativeElement.querySelector('#unsaved-changes-hint') as HTMLElement;
      const actions = fixture.nativeElement.querySelector('.modal-actions') as HTMLElement;
      // The hint explains the actions, so it has to precede them.
      expect(hint.compareDocumentPosition(actions) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });
  });

  // T127/FR-061: the wizard opens in every competition state, all 6 steps reachable, and the
  // current step is reflected in the URL as `?step=N` so a reload or a shared link lands back on
  // the same step.
  describe('deep link via ?step= (T127/FR-061)', () => {
    it('starts on step 4 when the route has :id and ?step=4', () => {
      configure('c1', { step: '4' });
      fakeApi.getById.mockReturnValue(of(detailFixture()));
      const fixture = TestBed.createComponent(CompetitionWizardComponent);
      fixture.detectChanges();

      expect(fixture.componentInstance['currentStep']()).toBe(4);
      expect(fixture.nativeElement.querySelector('app-import-step')).toBeTruthy();
    });

    it('falls back to step 1 when ?step is out of range', () => {
      configure('c1', { step: '9' });
      fakeApi.getById.mockReturnValue(of(detailFixture()));
      const fixture = TestBed.createComponent(CompetitionWizardComponent);
      fixture.detectChanges();

      expect(fixture.componentInstance['currentStep']()).toBe(1);
    });

    it('falls back to step 1 when ?step is not a number', () => {
      configure('c1', { step: 'abc' });
      fakeApi.getById.mockReturnValue(of(detailFixture()));
      const fixture = TestBed.createComponent(CompetitionWizardComponent);
      fixture.detectChanges();

      expect(fixture.componentInstance['currentStep']()).toBe(1);
    });

    it('ignores ?step for a brand-new competition (/new always starts at step 1)', () => {
      configure(null, { step: '4' });
      const fixture = TestBed.createComponent(CompetitionWizardComponent);
      fixture.detectChanges();

      expect(fixture.componentInstance['currentStep']()).toBe(1);
      expect(fixture.nativeElement.querySelector('app-basics-step')).toBeTruthy();
    });

    it('keeps the URL step query in sync with the current step when navigating via the stepper', () => {
      configure('c1');
      fakeApi.getById.mockReturnValue(of(detailFixture()));
      const fixture = TestBed.createComponent(CompetitionWizardComponent);
      fixture.detectChanges();
      const location = TestBed.inject(Location);
      const replaceStateSpy = jest.spyOn(location, 'replaceState');

      const [, , step3Button] = stepButtons(fixture);
      step3Button.click();
      fixture.detectChanges();

      expect(replaceStateSpy.mock.calls[0]?.[0]).toBe('/organizer/competitions/c1');
      expect(replaceStateSpy.mock.calls[0]?.[1]).toBe('step=3');
    });

    it('does not touch the URL for a brand-new competition still on step 1 (no id yet)', () => {
      configure(null);
      const fixture = TestBed.createComponent(CompetitionWizardComponent);
      fixture.detectChanges();
      const location = TestBed.inject(Location);
      const replaceStateSpy = jest.spyOn(location, 'replaceState');
      replaceStateSpy.mockClear();

      fixture.detectChanges();

      expect(replaceStateSpy).not.toHaveBeenCalled();
    });
  });

  // T127/FR-061 + clarification "Session 2026-09-19": the wizard is read-only once the
  // competition has moved past Active (InEvaluation/Finalized) — every step stays reachable for
  // review, but nothing can be modified.
  describe('read-only mode (T127/FR-061)', () => {
    it.each(['Draft', 'Active'] as const)(
      'is not read-only while the competition is %s',
      (state) => {
        configure('c1');
        fakeApi.getById.mockReturnValue(of(detailFixture({ state })));
        const fixture = TestBed.createComponent(CompetitionWizardComponent);
        fixture.detectChanges();

        expect(fixture.componentInstance['readOnly']()).toBe(false);
        expect(fixture.nativeElement.textContent).not.toContain('Modo consulta');
      },
    );

    it.each(['InEvaluation', 'Finalized'] as const)(
      'is read-only while the competition is %s, showing an info banner',
      (state) => {
        configure('c1');
        fakeApi.getById.mockReturnValue(of(detailFixture({ state })));
        const fixture = TestBed.createComponent(CompetitionWizardComponent);
        fixture.detectChanges();

        expect(fixture.componentInstance['readOnly']()).toBe(true);
        expect(fixture.nativeElement.textContent).toContain('Modo consulta');
        expect(fixture.nativeElement.textContent).toContain('Consultar competición');
      },
    );

    it('seeds every step as visited once a read-only competition loads, so the stepper shows real status', () => {
      configure('c1');
      fakeApi.getById.mockReturnValue(of(detailFixture({ state: 'Finalized' })));
      const fixture = TestBed.createComponent(CompetitionWizardComponent);
      fixture.detectChanges();

      expect(fixture.componentInstance['visitedSteps']()).toEqual(new Set([1, 2, 3, 4, 5, 6]));
    });

    it('code review M2: does not colour a pre-seeded-visited step amber until it has actually mounted and reported its status', () => {
      configure('c1');
      fakeApi.getById.mockReturnValue(of(detailFixture({ state: 'Finalized' })));
      const fixture = TestBed.createComponent(CompetitionWizardComponent);
      fixture.detectChanges();

      // visitedSteps is pre-seeded with all six (see above), but @switch only ever mounts
      // currentStep (1, since there's no ?step= here) — steps 3-6 never ran their own
      // completeness check, so they must render unmarked rather than a false 'partial'.
      const items = fixture.nativeElement.querySelectorAll('.stepper__item');
      expect(items[2].classList.contains('is-complete')).toBe(false);
      expect(items[2].classList.contains('is-partial')).toBe(false);
      expect(items[5].classList.contains('is-complete')).toBe(false);
      expect(items[5].classList.contains('is-partial')).toBe(false);

      // Once the organizer actually opens step 6, it mounts, reports its real status, and the
      // marker reflects it from then on — including after leaving the step again.
      fixture.componentInstance['goToStep'](6);
      fixture.detectChanges();
      fixture.componentInstance['goToStep'](1);
      fixture.detectChanges();

      const itemsAfter = fixture.nativeElement.querySelectorAll('.stepper__item');
      expect(
        itemsAfter[5].classList.contains('is-complete') ||
          itemsAfter[5].classList.contains('is-partial'),
      ).toBe(true);
    });

    it('passes readOnly() to the currently mounted step', () => {
      configure('c1');
      fakeApi.getById.mockReturnValue(of(detailFixture({ state: 'InEvaluation' })));
      const fixture = TestBed.createComponent(CompetitionWizardComponent);
      fixture.detectChanges();

      const basicsStepDebugEl = fixture.debugElement.query(By.css('app-basics-step'));
      expect(basicsStepDebugEl.componentInstance.readOnly()).toBe(true);
    });
  });
});
