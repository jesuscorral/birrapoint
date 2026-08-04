import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';

import { ApiError } from '../../../core/api/api-error';
import { CompetitionsApiService } from '../../../core/api/competitions-api.service';
import type { CompetitionDetail } from '../../../core/api/competitions-api.service';
import { BasicsStepComponent } from './basics-step.component';

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

describe('BasicsStepComponent', () => {
  let fakeApi: { create: jest.Mock; update: jest.Mock; getById: jest.Mock };

  beforeEach(() => {
    fakeApi = { create: jest.fn(), update: jest.fn(), getById: jest.fn() };
    TestBed.configureTestingModule({
      providers: [{ provide: CompetitionsApiService, useValue: fakeApi }, provideRouter([])],
    });
  });

  function createComponent() {
    const fixture = TestBed.createComponent(BasicsStepComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('disables Next while required fields are empty or invalid', () => {
    const fixture = createComponent();
    const button = fixture.nativeElement.querySelector(
      'button[type="submit"]',
    ) as HTMLButtonElement;
    expect(button.disabled).toBe(true);

    fixture.componentInstance.form.setValue({
      name: 'Golden Ale Cup',
      venue: 'Town Hall',
      startDate: '2026-08-02',
      endDate: '2026-08-01', // before startDate
    });
    fixture.detectChanges();
    expect(button.disabled).toBe(true);
  });

  it('enables Next once all fields are valid and endDate is on/after startDate', () => {
    const fixture = createComponent();
    fixture.componentInstance.form.setValue({
      name: 'Golden Ale Cup',
      venue: 'Town Hall',
      startDate: '2026-08-01',
      endDate: '2026-08-02',
    });
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector(
      'button[type="submit"]',
    ) as HTMLButtonElement;
    expect(button.disabled).toBe(false);
  });

  it('calls create() with only the basics fields when there is no competition id yet', () => {
    fakeApi.create.mockReturnValue(of(detailFixture()));
    const fixture = createComponent();
    fixture.componentInstance.form.setValue({
      name: 'Golden Ale Cup',
      venue: 'Town Hall',
      startDate: '2026-08-01',
      endDate: '2026-08-02',
    });

    fixture.componentInstance.onNext();

    expect(fakeApi.create).toHaveBeenCalledWith({
      name: 'Golden Ale Cup',
      venue: 'Town Hall',
      startDate: '2026-08-01',
      endDate: '2026-08-02',
    });
    expect(fakeApi.update).not.toHaveBeenCalled();
  });

  it('calls update() merging previously-saved step-2 fields when editing an existing competition', () => {
    fakeApi.update.mockReturnValue(of(detailFixture()));
    const fixture = TestBed.createComponent(BasicsStepComponent);
    fixture.componentRef.setInput('competitionId', 'c1');
    fixture.componentRef.setInput(
      'initialValue',
      detailFixture({ description: 'A friendly local competition', entryLimit: 40 }),
    );
    fixture.detectChanges();

    fixture.componentInstance.onNext();

    expect(fakeApi.update).toHaveBeenCalledWith('c1', {
      name: 'Golden Ale Cup',
      venue: 'Town Hall',
      startDate: '2026-08-01',
      endDate: '2026-08-02',
      description: 'A friendly local competition',
      logoUrl: undefined,
      entryLimit: 40,
      registrationStart: undefined,
      registrationEnd: undefined,
    });
  });

  it('emits saved with the API response on success', () => {
    const detail = detailFixture({ id: 'new-id' });
    fakeApi.create.mockReturnValue(of(detail));
    const fixture = createComponent();
    fixture.componentInstance.form.setValue({
      name: 'Golden Ale Cup',
      venue: 'Town Hall',
      startDate: '2026-08-01',
      endDate: '2026-08-02',
    });
    const emitted: CompetitionDetail[] = [];
    fixture.componentInstance.saved.subscribe((value) => emitted.push(value));

    fixture.componentInstance.onNext();

    expect(emitted).toEqual([detail]);
  });

  it('surfaces a field-level ApiError inline instead of advancing', () => {
    fakeApi.create.mockReturnValue(
      throwError(
        () =>
          new ApiError({
            status: 400,
            title: 'Validation failed',
            urn: 'urn:birrapoint:validation',
            errors: { name: ['Name is already in use'] },
          }),
      ),
    );
    const fixture = createComponent();
    fixture.componentInstance.form.setValue({
      name: 'Golden Ale Cup',
      venue: 'Town Hall',
      startDate: '2026-08-01',
      endDate: '2026-08-02',
    });
    const emitted: CompetitionDetail[] = [];
    fixture.componentInstance.saved.subscribe((value) => emitted.push(value));

    fixture.componentInstance.onNext();
    fixture.detectChanges();

    expect(emitted).toEqual([]);
    expect(fixture.nativeElement.textContent).toContain('Name is already in use');
  });

  it('opens a save-or-discard dialog instead of navigating immediately on "Volver al listado"', () => {
    const fixture = createComponent();

    const backButton = fixture.nativeElement.querySelector(
      '.back-to-list-link',
    ) as HTMLButtonElement;
    backButton.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="alertdialog"]')).toBeTruthy();
  });

  it('discards without saving and navigates to the organizer dashboard', () => {
    const fixture = createComponent();
    const router = TestBed.inject(Router);
    const navigateSpy = jest.spyOn(router, 'navigateByUrl');

    fixture.componentInstance['onRequestBack']();
    fixture.componentInstance['onDiscardAndLeave']();
    fixture.detectChanges();

    expect(fakeApi.create).not.toHaveBeenCalled();
    expect(fakeApi.update).not.toHaveBeenCalled();
    expect(navigateSpy).toHaveBeenCalledWith('/organizer/dashboard');
    expect(fixture.nativeElement.querySelector('[role="alertdialog"]')).toBeFalsy();
  });

  it('closes the dialog without navigating on "Cancelar"', () => {
    const fixture = createComponent();
    const router = TestBed.inject(Router);
    const navigateSpy = jest.spyOn(router, 'navigateByUrl');

    fixture.componentInstance['onRequestBack']();
    fixture.componentInstance['onCancelBackConfirm']();
    fixture.detectChanges();

    expect(navigateSpy).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[role="alertdialog"]')).toBeFalsy();
  });

  it('saves as a draft and navigates to the organizer dashboard on "Guardar borrador"', () => {
    fakeApi.create.mockReturnValue(of(detailFixture()));
    const fixture = createComponent();
    fixture.componentInstance.form.setValue({
      name: 'Golden Ale Cup',
      venue: 'Town Hall',
      startDate: '2026-08-01',
      endDate: '2026-08-02',
    });
    const router = TestBed.inject(Router);
    const navigateSpy = jest.spyOn(router, 'navigateByUrl');

    fixture.componentInstance['onRequestBack']();
    fixture.componentInstance['onSaveAndLeave']();

    expect(fakeApi.create).toHaveBeenCalledWith({
      name: 'Golden Ale Cup',
      venue: 'Town Hall',
      startDate: '2026-08-01',
      endDate: '2026-08-02',
    });
    expect(navigateSpy).toHaveBeenCalledWith('/organizer/dashboard');
  });

  it('does not save or navigate on "Guardar borrador" while required fields are missing', () => {
    const fixture = createComponent();
    const router = TestBed.inject(Router);
    const navigateSpy = jest.spyOn(router, 'navigateByUrl');

    fixture.componentInstance['onRequestBack']();
    fixture.componentInstance['onSaveAndLeave']();

    expect(fakeApi.create).not.toHaveBeenCalled();
    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('prefills the form from initialValue (resume-with-data)', () => {
    const fixture = TestBed.createComponent(BasicsStepComponent);
    fixture.componentRef.setInput('initialValue', detailFixture());
    fixture.detectChanges();

    expect(fixture.componentInstance.form.getRawValue()).toEqual({
      name: 'Golden Ale Cup',
      venue: 'Town Hall',
      startDate: '2026-08-01',
      endDate: '2026-08-02',
    });
  });

  it('emits dirtyChange(false) when the form is populated from initialValue (patchValue never marks a control dirty)', () => {
    const emitted: boolean[] = [];
    const fixture = TestBed.createComponent(BasicsStepComponent);
    fixture.componentInstance.dirtyChange.subscribe((value) => emitted.push(value));
    fixture.componentRef.setInput('initialValue', detailFixture());
    fixture.detectChanges();

    expect(emitted).toEqual([false]);
  });

  it('emits dirtyChange(true) after the organizer edits a field', () => {
    const fixture = createComponent();
    const emitted: boolean[] = [];
    fixture.componentInstance.dirtyChange.subscribe((value) => emitted.push(value));

    const nameInput = fixture.nativeElement.querySelector('input#basics-name') as HTMLInputElement;
    nameInput.value = 'A new name';
    nameInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(emitted).toEqual([true]);
    expect(fixture.componentInstance.form.dirty).toBe(true);
  });
});
