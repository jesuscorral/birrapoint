import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';

import { ApiError } from '../../../core/api/api-error';
import { CompetitionsApiService } from '../../../core/api/competitions-api.service';
import type { CompetitionDetail } from '../../../core/api/competitions-api.service';
import { DetailsStepComponent } from './details-step.component';

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

describe('DetailsStepComponent', () => {
  let fakeApi: { create: jest.Mock; update: jest.Mock; getById: jest.Mock };

  beforeEach(() => {
    fakeApi = { create: jest.fn(), update: jest.fn(), getById: jest.fn() };
    TestBed.configureTestingModule({
      providers: [{ provide: CompetitionsApiService, useValue: fakeApi }, provideRouter([])],
    });
  });

  function createComponent(initialValue: CompetitionDetail | null = detailFixture()) {
    const fixture = TestBed.createComponent(DetailsStepComponent);
    fixture.componentRef.setInput('competitionId', 'c1');
    fixture.componentRef.setInput('initialValue', initialValue);
    fixture.detectChanges();
    return fixture;
  }

  it('calls update() with the full accumulated form state (basics + details) on Save Draft', () => {
    fakeApi.update.mockReturnValue(of(detailFixture()));
    const fixture = createComponent();
    fixture.componentInstance.form.setValue({
      description: 'A friendly local competition',
      logoUrl: 'https://example.com/logo.png',
      entryLimit: 40,
      registrationStart: '2026-06-01',
      registrationEnd: '2026-07-01',
    });

    fixture.componentInstance.onSaveDraft();

    expect(fakeApi.update).toHaveBeenCalledWith('c1', {
      name: 'Golden Ale Cup',
      venue: 'Town Hall',
      startDate: '2026-08-01',
      endDate: '2026-08-02',
      description: 'A friendly local competition',
      logoUrl: 'https://example.com/logo.png',
      entryLimit: 40,
      registrationStart: '2026-06-01',
      registrationEnd: '2026-07-01',
    });
  });

  it('allows Save Draft with all optional fields blank', () => {
    fakeApi.update.mockReturnValue(of(detailFixture()));
    const fixture = createComponent();

    // The step now also renders a "Volver" (back) button before the submit button — target the
    // submit button specifically rather than the first <button> in DOM order.
    const button = fixture.nativeElement.querySelector(
      'button[type="submit"]',
    ) as HTMLButtonElement;
    expect(button.disabled).toBe(false);

    fixture.componentInstance.onSaveDraft();

    expect(fakeApi.update).toHaveBeenCalledWith('c1', {
      name: 'Golden Ale Cup',
      venue: 'Town Hall',
      startDate: '2026-08-01',
      endDate: '2026-08-02',
      description: undefined,
      logoUrl: undefined,
      entryLimit: undefined,
      registrationStart: undefined,
      registrationEnd: undefined,
    });
  });

  it('disables Save Draft when entryLimit is not positive', () => {
    const fixture = createComponent();
    fixture.componentInstance.form.patchValue({ entryLimit: 0 });
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector(
      'button[type="submit"]',
    ) as HTMLButtonElement;
    expect(button.disabled).toBe(true);
  });

  it('disables Save Draft when registrationEnd is before registrationStart', () => {
    const fixture = createComponent();
    fixture.componentInstance.form.patchValue({
      registrationStart: '2026-07-01',
      registrationEnd: '2026-06-01',
    });
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector(
      'button[type="submit"]',
    ) as HTMLButtonElement;
    expect(button.disabled).toBe(true);
  });

  it('emits saved instead of navigating directly — the wizard shell now owns advancing past this step', () => {
    fakeApi.update.mockReturnValue(of(detailFixture()));
    const fixture = createComponent();
    const router = TestBed.inject(Router);
    const navigateSpy = jest.spyOn(router, 'navigateByUrl');

    fixture.componentInstance.onSaveDraft();

    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('emits saved with the API response on success', () => {
    const detail = detailFixture({ description: 'A friendly local competition' });
    fakeApi.update.mockReturnValue(of(detail));
    const fixture = createComponent();
    const emitted: CompetitionDetail[] = [];
    fixture.componentInstance.saved.subscribe((value) => emitted.push(value));

    fixture.componentInstance.onSaveDraft();

    expect(emitted).toEqual([detail]);
  });

  it('surfaces an ApiError banner and stays on the page instead of navigating away', () => {
    fakeApi.update.mockReturnValue(
      throwError(() => new ApiError({ status: 500, title: 'Something went wrong', urn: null })),
    );
    const fixture = createComponent();
    const router = TestBed.inject(Router);
    const navigateSpy = jest.spyOn(router, 'navigateByUrl');

    fixture.componentInstance.onSaveDraft();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Something went wrong');
    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('prefills the form from initialValue (resume-with-data)', () => {
    const fixture = createComponent(
      detailFixture({
        description: 'A friendly local competition',
        logoUrl: 'https://example.com/logo.png',
        entryLimit: 40,
        registrationStart: '2026-06-01',
        registrationEnd: '2026-07-01',
      }),
    );

    expect(fixture.componentInstance.form.getRawValue()).toEqual({
      description: 'A friendly local competition',
      logoUrl: 'https://example.com/logo.png',
      entryLimit: 40,
      registrationStart: '2026-06-01',
      registrationEnd: '2026-07-01',
    });
  });

  it('emits dirtyChange(false) when the form is populated from initialValue (patchValue never marks a control dirty)', () => {
    const emitted: boolean[] = [];
    const fixture = TestBed.createComponent(DetailsStepComponent);
    fixture.componentInstance.dirtyChange.subscribe((value) => emitted.push(value));
    fixture.componentRef.setInput('competitionId', 'c1');
    fixture.componentRef.setInput('initialValue', detailFixture());
    fixture.detectChanges();

    expect(emitted).toEqual([false]);
  });

  it('emits dirtyChange(true) after the organizer edits a field', () => {
    const fixture = createComponent();
    const emitted: boolean[] = [];
    fixture.componentInstance.dirtyChange.subscribe((value) => emitted.push(value));

    const logoInput = fixture.nativeElement.querySelector('input#details-logo') as HTMLInputElement;
    logoInput.value = 'https://example.com/new-logo.png';
    logoInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(emitted).toEqual([true]);
    expect(fixture.componentInstance.form.dirty).toBe(true);
  });
});
