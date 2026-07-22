import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { ApiError } from '../../core/api/api-error';
import { CatalogApiService } from '../../core/api/catalog-api.service';
import { SyncService } from '../../core/offline/sync.service';
import type { EvaluationComments, EvaluationScores } from '../../core/offline/db';
import { EvaluationSheetComponent } from './evaluation-sheet.component';
import { TastingOrderApiService } from '../judge-tables/tasting-order-api.service';
import type { JudgeSample } from '../judge-tables/tasting-order-api.service';

function sampleFixture(overrides: Partial<JudgeSample> = {}): JudgeSample {
  return {
    beerEntryId: 'e1',
    blindCode: 'AB12',
    styleCode: '21A',
    styleName: 'American IPA',
    sequenceOrder: 1,
    evaluationStatus: 'NotStarted',
    ...overrides,
  };
}

function validScores(): EvaluationScores {
  return { aroma: 10, appearance: 2, flavor: 15, mouthfeel: 4, overall: 8 };
}

function validComments(): EvaluationComments {
  return {
    aroma: 'Citrus and pine hop aroma, moderate intensity.',
    appearance: 'Deep golden, persistent white head, brilliant.',
    flavor: 'Balanced malt backbone with resinous hop finish.',
    mouthfeel: 'Medium body, lively carbonation, dry finish.',
    overall: 'A clean, well-executed example of the style.',
  };
}

describe('EvaluationSheetComponent', () => {
  let fakeTastingOrderApi: { getTableSamples: jest.Mock };
  let fakeSync: { loadDraft: jest.Mock; saveDraft: jest.Mock; submit: jest.Mock };
  let fakeCatalog: { getStyleDetail: jest.Mock };
  let navigateSpy: jest.SpiedFunction<Router['navigate']>;

  beforeEach(() => {
    Object.defineProperty(navigator, 'onLine', { value: true, configurable: true });

    fakeTastingOrderApi = { getTableSamples: jest.fn().mockReturnValue(of([sampleFixture()])) };
    fakeSync = {
      loadDraft: jest.fn().mockResolvedValue(undefined),
      saveDraft: jest.fn().mockResolvedValue(undefined),
      submit: jest.fn().mockResolvedValue({ status: 'confirmed' }),
    };
    fakeCatalog = { getStyleDetail: jest.fn().mockReturnValue(of(null)) };

    TestBed.configureTestingModule({
      providers: [
        { provide: TastingOrderApiService, useValue: fakeTastingOrderApi },
        { provide: SyncService, useValue: fakeSync },
        { provide: CatalogApiService, useValue: fakeCatalog },
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: convertToParamMap({ tableId: 't1', beerEntryId: 'e1' }) },
          },
        },
      ],
    });
  });

  function createComponent() {
    const fixture = TestBed.createComponent(EvaluationSheetComponent);
    navigateSpy = jest.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture.detectChanges();
    return fixture;
  }

  async function flush(): Promise<void> {
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
  }

  it('loads the sample and shows its blind code and style', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    expect(fakeTastingOrderApi.getTableSamples).toHaveBeenCalledWith('t1');
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('AB12');
    expect(text).toContain('American IPA');
  });

  it('shows a load error when the sample cannot be found in this table', async () => {
    fakeTastingOrderApi.getTableSamples.mockReturnValue(
      of([sampleFixture({ beerEntryId: 'other' })]),
    );
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('not found');
  });

  it('shows a load error when fetching samples fails', async () => {
    fakeTastingOrderApi.getTableSamples.mockReturnValue(
      throwError(() => new ApiError({ status: 404, title: 'Not found', urn: null })),
    );
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Not found');
  });

  it('falls back to the last cached sample when a restart-while-offline fetch cannot reach the server (status 0)', async () => {
    // First mount: online, the live fetch succeeds -> populates the offline-restart fallback
    // cache with this exact sample.
    const firstMount = createComponent();
    await flush();
    firstMount.detectChanges();
    expect(firstMount.nativeElement.textContent).toContain('AB12');

    // Second mount (e.g. the judge's browser restarting while still offline): the live fetch
    // never reaches the server at all (status 0) -- a real 404/403 the server actively returned
    // must still show the load error as before (see the preceding test), only a genuine
    // connectivity failure falls back to the cache.
    fakeTastingOrderApi.getTableSamples.mockReturnValue(
      throwError(
        () => new ApiError({ status: 0, title: 'An unexpected error occurred.', urn: null }),
      ),
    );
    const secondMount = createComponent();
    await flush();
    secondMount.detectChanges();

    expect(secondMount.nativeElement.textContent).toContain('AB12');
    expect(secondMount.nativeElement.textContent).toContain('American IPA');
    expect(secondMount.nativeElement.querySelector('form')).not.toBeNull();
  });

  it('shows a read-only notice instead of the form for an already-submitted sample', async () => {
    fakeTastingOrderApi.getTableSamples.mockReturnValue(
      of([sampleFixture({ evaluationStatus: 'Submitted' })]),
    );
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('already been evaluated');
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
  });

  it('hydrates the form from an existing draft (offline-resume)', async () => {
    fakeSync.loadDraft.mockResolvedValue({
      beerEntryId: 'e1',
      tastingTableId: 't1',
      scores: validScores(),
      comments: validComments(),
      updatedAt: new Date().toISOString(),
    });
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    expect(fakeSync.loadDraft).toHaveBeenCalledWith('e1');
    expect(fixture.componentInstance.form.getRawValue()).toMatchObject({
      aromaScore: 10,
      aromaComment: validComments().aroma,
      overallScore: 8,
    });
  });

  it('starts blank when there is no existing draft', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    expect(fixture.componentInstance.form.getRawValue()).toMatchObject({
      aromaScore: null,
      aromaComment: '',
    });
  });

  it('computes a read-only total from the current scores', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();
    fixture.componentInstance.form.patchValue(
      { aromaScore: 10, appearanceScore: 2, flavorScore: 15, mouthfeelScore: 4, overallScore: 8 },
      { emitEvent: false },
    );

    expect(fixture.componentInstance.total()).toBe(39);
  });

  it('shows the remaining characters needed to reach the 20-char comment minimum', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();
    fixture.componentInstance.form.patchValue({ aromaComment: 'Too short' }, { emitEvent: false });

    expect(fixture.componentInstance.remainingChars('aroma')).toBe(20 - 'Too short'.length);
  });

  it('disables submit while any score exceeds its cap or any comment is under 20 chars', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    const submitButton = fixture.nativeElement.querySelector(
      'button[type="submit"]',
    ) as HTMLButtonElement;
    expect(submitButton.disabled).toBe(true);

    fixture.componentInstance.form.setValue({
      aromaScore: 13, // exceeds the 12 cap
      aromaComment: validComments().aroma,
      appearanceScore: validScores().appearance,
      appearanceComment: validComments().appearance,
      flavorScore: validScores().flavor,
      flavorComment: validComments().flavor,
      mouthfeelScore: validScores().mouthfeel,
      mouthfeelComment: validComments().mouthfeel,
      overallScore: validScores().overall,
      overallComment: validComments().overall,
    });
    fixture.detectChanges();
    expect(submitButton.disabled).toBe(true);
  });

  it('enables submit once every score is within cap and every comment reaches 20 chars', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    fixture.componentInstance.form.setValue({
      aromaScore: validScores().aroma,
      aromaComment: validComments().aroma,
      appearanceScore: validScores().appearance,
      appearanceComment: validComments().appearance,
      flavorScore: validScores().flavor,
      flavorComment: validComments().flavor,
      mouthfeelScore: validScores().mouthfeel,
      mouthfeelComment: validComments().mouthfeel,
      overallScore: validScores().overall,
      overallComment: validComments().overall,
    });
    fixture.detectChanges();

    const submitButton = fixture.nativeElement.querySelector(
      'button[type="submit"]',
    ) as HTMLButtonElement;
    expect(submitButton.disabled).toBe(false);
  });

  it('saves a debounced draft (delegated to SyncService) on every field change', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    fixture.componentInstance.form.patchValue({ aromaScore: 5 });

    expect(fakeSync.saveDraft).toHaveBeenCalledWith(
      'e1',
      't1',
      expect.objectContaining({ aroma: 5 }),
      expect.any(Object),
    );
  });

  it('submits with the {tableId}:{beerEntryId} idempotency key and navigates back on confirmed success', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();
    fixture.componentInstance.form.setValue({
      aromaScore: validScores().aroma,
      aromaComment: validComments().aroma,
      appearanceScore: validScores().appearance,
      appearanceComment: validComments().appearance,
      flavorScore: validScores().flavor,
      flavorComment: validComments().flavor,
      mouthfeelScore: validScores().mouthfeel,
      mouthfeelComment: validComments().mouthfeel,
      overallScore: validScores().overall,
      overallComment: validComments().overall,
    });

    await fixture.componentInstance.onSubmit();

    expect(fakeSync.submit).toHaveBeenCalledWith(
      't1:e1',
      't1',
      'e1',
      validScores(),
      validComments(),
    );
    expect(navigateSpy).toHaveBeenCalledWith(['/judge', 'tables', 't1']);
  });

  it('navigates back even when submit() only enqueues (offline/deferred), not just on confirmed', async () => {
    fakeSync.submit.mockResolvedValue({ status: 'enqueued' });
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();
    fixture.componentInstance.form.setValue({
      aromaScore: validScores().aroma,
      aromaComment: validComments().aroma,
      appearanceScore: validScores().appearance,
      appearanceComment: validComments().appearance,
      flavorScore: validScores().flavor,
      flavorComment: validComments().flavor,
      mouthfeelScore: validScores().mouthfeel,
      mouthfeelComment: validComments().mouthfeel,
      overallScore: validScores().overall,
      overallComment: validComments().overall,
    });

    await fixture.componentInstance.onSubmit();

    expect(navigateSpy).toHaveBeenCalledWith(['/judge', 'tables', 't1']);
  });

  it('maps a 409 out-of-sequence rejection to a plain message and does not navigate', async () => {
    fakeSync.submit.mockRejectedValue(
      new ApiError({
        status: 409,
        title: 'Out of sequence',
        urn: 'urn:birrapoint:out-of-sequence',
      }),
    );
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();
    fixture.componentInstance.form.setValue({
      aromaScore: validScores().aroma,
      aromaComment: validComments().aroma,
      appearanceScore: validScores().appearance,
      appearanceComment: validComments().appearance,
      flavorScore: validScores().flavor,
      flavorComment: validComments().flavor,
      mouthfeelScore: validScores().mouthfeel,
      mouthfeelComment: validComments().mouthfeel,
      overallScore: validScores().overall,
      overallComment: validComments().overall,
    });

    await fixture.componentInstance.onSubmit();
    fixture.detectChanges();

    expect(navigateSpy).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('not the next one');
  });

  it('shows a distinct message for a non-ApiError (local storage) submit failure', async () => {
    fakeSync.submit.mockRejectedValue(new Error('QuotaExceededError'));
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();
    fixture.componentInstance.form.setValue({
      aromaScore: validScores().aroma,
      aromaComment: validComments().aroma,
      appearanceScore: validScores().appearance,
      appearanceComment: validComments().appearance,
      flavorScore: validScores().flavor,
      flavorComment: validComments().flavor,
      mouthfeelScore: validScores().mouthfeel,
      mouthfeelComment: validComments().mouthfeel,
      overallScore: validScores().overall,
      overallComment: validComments().overall,
    });

    await fixture.componentInstance.onSubmit();
    fixture.detectChanges();

    expect(navigateSpy).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain("couldn't be saved locally");
  });

  it('shows the offline badge when navigator.onLine is false, and reacts live to online/offline events', async () => {
    Object.defineProperty(navigator, 'onLine', { value: false, configurable: true });
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Offline mode — data protected locally');

    window.dispatchEvent(new Event('online'));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('Offline mode');

    window.dispatchEvent(new Event('offline'));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Offline mode — data protected locally');
  });

  it('does not show the offline badge when online', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('Offline mode');
  });
});
