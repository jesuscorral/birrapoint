import { TestBed } from '@angular/core/testing';
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
    jest.useFakeTimers();
    fakeApi = { create: jest.fn(), update: jest.fn(), getById: jest.fn() };
    TestBed.configureTestingModule({
      providers: [{ provide: CompetitionsApiService, useValue: fakeApi }],
    });
  });

  afterEach(() => {
    jest.useRealTimers();
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

    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
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

    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(button.disabled).toBe(true);
  });

  it('disables Save Draft when registrationEnd is before registrationStart', () => {
    const fixture = createComponent();
    fixture.componentInstance.form.patchValue({
      registrationStart: '2026-07-01',
      registrationEnd: '2026-06-01',
    });
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(button.disabled).toBe(true);
  });

  it('shows a Saved confirmation after a successful save, cleared after a few seconds', () => {
    fakeApi.update.mockReturnValue(of(detailFixture()));
    const fixture = createComponent();

    fixture.componentInstance.onSaveDraft();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Saved');

    jest.advanceTimersByTime(3000);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('Saved');
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

  it('surfaces an ApiError banner without showing a Saved confirmation', () => {
    fakeApi.update.mockReturnValue(
      throwError(() => new ApiError({ status: 500, title: 'Something went wrong', urn: null })),
    );
    const fixture = createComponent();

    fixture.componentInstance.onSaveDraft();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Something went wrong');
    expect(fixture.nativeElement.textContent).not.toContain('Saved');
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
});
