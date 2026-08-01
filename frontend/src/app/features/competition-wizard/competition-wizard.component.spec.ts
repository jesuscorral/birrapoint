import { Location } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { CatalogApiService } from '../../core/api/catalog-api.service';
import { CompetitionsApiService } from '../../core/api/competitions-api.service';
import type { CompetitionDetail } from '../../core/api/competitions-api.service';
import { ImportApiService } from '../../core/api/import-api.service';
import { CompetitionWizardComponent } from './competition-wizard.component';
import { ImportStepComponent } from './steps/import-step.component';

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
    TestBed.configureTestingModule({
      providers: [
        { provide: CompetitionsApiService, useValue: fakeApi },
        { provide: CatalogApiService, useValue: fakeCatalogApi },
        { provide: ImportApiService, useValue: fakeImportApi },
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
    // formControlName attaches to the <bp-input> host element, not the native <input> nested
    // inside its own template — select by the id passed through to that native input instead.
    // bp-input's own [id] binding puts the same id on both, so scope the query to the nested
    // native element specifically or querySelector's document-order match returns the host.
    const nameInput = fixture.nativeElement.querySelector('#basics-name input') as HTMLInputElement;
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

  it('disables the step 2, 3 and 4 stepper buttons for a brand-new, unsaved competition', () => {
    configure(null);
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    const [step1Button, step2Button, step3Button, step4Button] = stepButtons(fixture);
    expect(step1Button.disabled).toBe(false);
    expect(step2Button.disabled).toBe(true);
    expect(step3Button.disabled).toBe(true);
    expect(step4Button.disabled).toBe(true);

    fixture.componentInstance['goToStep'](2);
    fixture.componentInstance['goToStep'](3);
    fixture.detectChanges();

    expect(fixture.componentInstance['currentStep']()).toBe(1);
    expect(fixture.nativeElement.querySelector('app-basics-step')).toBeTruthy();
  });

  it('enables the step 2, 3 and 4 stepper buttons once basics is saved, and jumps directly to step 3', () => {
    configure(null);
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    fixture.componentInstance['onBasicsSaved'](detailFixture({ id: 'new-id' }));
    fixture.detectChanges();

    const [, step2Button, step3Button, step4Button] = stepButtons(fixture);
    expect(step2Button.disabled).toBe(false);
    expect(step3Button.disabled).toBe(false);
    expect(step4Button.disabled).toBe(false);

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
    const nameInput = fixture.nativeElement.querySelector('#basics-name input') as HTMLInputElement;
    expect(nameInput.value).toBe('Resumed Cup');
  });

  it('enables all stepper buttons immediately when editing an existing competition, before any save', () => {
    configure('c1');
    fakeApi.getById.mockReturnValue(of(detailFixture()));
    const fixture = TestBed.createComponent(CompetitionWizardComponent);
    fixture.detectChanges();

    const [step1Button, step2Button, step3Button, step4Button] = stepButtons(fixture);
    expect(step1Button.disabled).toBe(false);
    expect(step2Button.disabled).toBe(false);
    expect(step3Button.disabled).toBe(false);
    expect(step4Button.disabled).toBe(false);

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
});
