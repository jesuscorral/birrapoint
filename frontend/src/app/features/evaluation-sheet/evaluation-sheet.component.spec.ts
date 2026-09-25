import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import Keycloak from 'keycloak-js';
import { of, Subject, throwError } from 'rxjs';

import { ApiError } from '../../core/api/api-error';
import { CatalogApiService } from '../../core/api/catalog-api.service';
import { SyncService } from '../../core/offline/sync.service';
import type { EvaluationComments, EvaluationScores } from '../../core/offline/db';
import { CompetitionHubService } from '../../core/realtime/competition-hub.service';
import type { JudgeRemovedEvent } from '../../core/realtime/competition-hub.events';
import { EvaluationSheetComponent } from './evaluation-sheet.component';
import { TastingOrderApiService } from '../judge-tables/tasting-order-api.service';
import type { JudgeSample, JudgeTableSummary } from '../judge-tables/tasting-order-api.service';

function sampleFixture(overrides: Partial<JudgeSample> = {}): JudgeSample {
  return {
    beerEntryId: 'e1',
    blindCode: 'AB12',
    styleCode: '21A',
    styleName: 'American IPA',
    abvPercent: 6.2,
    sequenceOrder: 1,
    evaluationStatus: 'NotStarted',
    ...overrides,
  };
}

function tableSummaryFixture(overrides: Partial<JudgeTableSummary> = {}): JudgeTableSummary {
  return {
    tableId: 't1',
    name: 'Table 3',
    competitionState: 'InEvaluation',
    tableState: 'Open',
    orderFixed: true,
    orderFixedBy: 'Jane',
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
  let fakeTastingOrderApi: { getTableSamples: jest.Mock; getMyTables: jest.Mock };
  let fakeSync: {
    loadDraft: jest.Mock;
    saveDraft: jest.Mock;
    submit: jest.Mock;
    rejectOutboxForTable: jest.Mock;
  };
  let fakeCatalog: { getStyleDetail: jest.Mock };
  let fakeHub: { start: jest.Mock; joinTable: jest.Mock; leaveTable: jest.Mock; on: jest.Mock };
  let judgeRemovedSubject: Subject<JudgeRemovedEvent>;
  let navigateSpy: jest.SpiedFunction<Router['navigate']>;

  beforeEach(() => {
    Object.defineProperty(navigator, 'onLine', { value: true, configurable: true });

    fakeTastingOrderApi = {
      getTableSamples: jest.fn().mockReturnValue(of([sampleFixture()])),
      getMyTables: jest.fn().mockReturnValue(of([tableSummaryFixture()])),
    };
    fakeSync = {
      loadDraft: jest.fn().mockResolvedValue(undefined),
      saveDraft: jest.fn().mockResolvedValue(undefined),
      submit: jest.fn().mockResolvedValue({ status: 'confirmed' }),
      rejectOutboxForTable: jest.fn().mockResolvedValue([]),
    };
    fakeCatalog = { getStyleDetail: jest.fn().mockReturnValue(of(null)) };
    judgeRemovedSubject = new Subject<JudgeRemovedEvent>();
    fakeHub = {
      start: jest.fn().mockResolvedValue(undefined),
      joinTable: jest.fn().mockResolvedValue(undefined),
      leaveTable: jest.fn().mockResolvedValue(undefined),
      on: jest.fn(() => judgeRemovedSubject.asObservable()),
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: TastingOrderApiService, useValue: fakeTastingOrderApi },
        { provide: SyncService, useValue: fakeSync },
        { provide: CatalogApiService, useValue: fakeCatalog },
        { provide: CompetitionHubService, useValue: fakeHub },
        {
          provide: Keycloak,
          useValue: { tokenParsed: { realm_access: { roles: ['JUDGE'] } }, logout: jest.fn() },
        },
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

  // The submit action (and submitError()) only render on the Resumen tab (Session 2026-09-21) —
  // tests that set the whole form programmatically and then need the submit button/error visible
  // must jump there first, same as a judge who filled every section then opened Resumen.
  function goToReviewStep(fixture: ReturnType<typeof createComponent>): void {
    fixture.componentInstance.activeTab.set('summary');
    fixture.detectChanges();
  }

  function fillField(root: Element, id: string, value: string): void {
    const field = root.querySelector(`#${id}`) as HTMLInputElement | HTMLTextAreaElement;
    field.value = value;
    field.dispatchEvent(new Event('input'));
  }

  // Unlike buttonWithText's exact match, a nav-bar item's own text grows a trailing "✓" once its
  // section becomes valid (see the component's [sectionValid] check) — match by prefix so the
  // same lookup keeps working before and after that happens.
  function sectionNavButton(root: Element, label: string): HTMLButtonElement {
    const buttons = [...root.querySelectorAll('.section-nav__item')] as HTMLButtonElement[];
    const match = buttons.find((button) => button.textContent?.trim().startsWith(label));
    if (!match) {
      throw new Error(`No section-nav button starting with "${label}" found`);
    }
    return match;
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

  it('shows the ABV of the sample (Session 2026-09-20)', async () => {
    fakeTastingOrderApi.getTableSamples.mockReturnValue(of([sampleFixture({ abvPercent: 7.1 })]));
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('7.1% ABV');
  });

  it('shows a load error when the sample cannot be found in this table', async () => {
    fakeTastingOrderApi.getTableSamples.mockReturnValue(
      of([sampleFixture({ beerEntryId: 'other' })]),
    );
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('No se ha encontrado');
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

    expect(fixture.nativeElement.textContent).toContain('ya ha sido evaluada');
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
  });

  it('shows a discrepancy notice with a link to resolve it for a PendingConsensus sample', async () => {
    fakeTastingOrderApi.getTableSamples.mockReturnValue(
      of([sampleFixture({ evaluationStatus: 'PendingConsensus' })]),
    );
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('discrepancia de puntuación');
    expect(fixture.nativeElement.textContent).not.toContain('ya ha sido evaluada');
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
    expect(
      fixture.nativeElement.querySelector('a[href="/judge/tables/t1/discrepancies"]'),
    ).not.toBeNull();
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
      feedback: '',
    });
    goToReviewStep(fixture);

    const submitButton = fixture.nativeElement.querySelector(
      'button[type="submit"]',
    ) as HTMLButtonElement;
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
      feedback: '',
    });
    goToReviewStep(fixture);

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
      expect.any(Object),
      expect.any(String),
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
      feedback: '',
    });

    await fixture.componentInstance.onSubmit();

    expect(fakeSync.submit).toHaveBeenCalledWith(
      't1:e1',
      't1',
      'e1',
      validScores(),
      validComments(),
      expect.any(Object),
      '',
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
      feedback: '',
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
      feedback: '',
    });

    await fixture.componentInstance.onSubmit();
    goToReviewStep(fixture);

    expect(navigateSpy).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('no es la siguiente');
  });

  it('ejects directly on a 404 submit rejection (removed from the table mid-session), without waiting for the hub event', async () => {
    fakeSync.submit.mockRejectedValue(new ApiError({ status: 404, title: 'Not found', urn: null }));
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
      feedback: '',
    });

    await fixture.componentInstance.onSubmit();

    expect(fakeSync.rejectOutboxForTable).toHaveBeenCalledWith('t1');
    expect(navigateSpy).toHaveBeenCalledWith(['/judge', 'tables'], {
      state: { ejected: true, tableName: 'Table 3' },
    });
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
      feedback: '',
    });

    await fixture.componentInstance.onSubmit();
    goToReviewStep(fixture);

    expect(navigateSpy).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain(
      'No hemos podido guardar esta evaluación localmente',
    );
  });

  it('shows the offline badge when navigator.onLine is false, and reacts live to online/offline events', async () => {
    Object.defineProperty(navigator, 'onLine', { value: false, configurable: true });
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'Modo sin conexión — datos protegidos localmente',
    );

    window.dispatchEvent(new Event('online'));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('Offline mode');

    window.dispatchEvent(new Event('offline'));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(
      'Modo sin conexión — datos protegidos localmente',
    );
  });

  it('does not show the offline badge when online', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('Offline mode');
  });

  describe('live judge removal (T087/US12)', () => {
    it('joins the table SignalR group on init and leaves it on destroy', async () => {
      const fixture = createComponent();
      await flush();

      expect(fakeHub.joinTable).toHaveBeenCalledWith('t1');

      fixture.destroy();
      await flush();

      expect(fakeHub.leaveTable).toHaveBeenCalledWith('t1');
    });

    it('when this judge is the one removed: purges the outbox and navigates away, mid-sheet', async () => {
      fakeTastingOrderApi.getTableSamples
        .mockReturnValueOnce(of([sampleFixture()]))
        .mockReturnValue(
          throwError(() => new ApiError({ status: 404, title: 'Not found', urn: null })),
        );
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      judgeRemovedSubject.next({ tableId: 't1', judgeId: 'some-judge' });
      await flush();

      expect(fakeSync.rejectOutboxForTable).toHaveBeenCalledWith('t1');
      expect(navigateSpy).toHaveBeenCalledWith(['/judge', 'tables'], {
        state: { ejected: true, tableName: 'Table 3' },
      });
    });

    it('falls back to a generic table name when the table summary could not be loaded', async () => {
      fakeTastingOrderApi.getMyTables.mockReturnValue(throwError(() => new Error('network down')));
      fakeTastingOrderApi.getTableSamples
        .mockReturnValueOnce(of([sampleFixture()]))
        .mockReturnValue(
          throwError(() => new ApiError({ status: 404, title: 'Not found', urn: null })),
        );
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      judgeRemovedSubject.next({ tableId: 't1', judgeId: 'some-judge' });
      await flush();

      expect(navigateSpy).toHaveBeenCalledWith(['/judge', 'tables'], {
        state: { ejected: true, tableName: 'Mesa' },
      });
    });

    it('when a different judge at this table is removed: this session stays put, no-op', async () => {
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      judgeRemovedSubject.next({ tableId: 't1', judgeId: 'some-other-judge' });
      await flush();

      expect(fakeSync.rejectOutboxForTable).not.toHaveBeenCalled();
      expect(navigateSpy).not.toHaveBeenCalled();
    });

    it('ignores a JudgeRemoved event for a different table', async () => {
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      judgeRemovedSubject.next({ tableId: 'other-table', judgeId: 'some-judge' });
      await flush();

      expect(fakeSync.rejectOutboxForTable).not.toHaveBeenCalled();
      expect(navigateSpy).not.toHaveBeenCalled();
    });
  });

  describe('free-navigation section sheet (Session 2026-09-21)', () => {
    it('starts on the Apariencia section, with every section freely reachable via the nav bar', async () => {
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      const text = fixture.nativeElement.textContent as string;
      expect(text).toContain('Apariencia');
      expect(fixture.nativeElement.querySelector('#appearance-score')).not.toBeNull();

      sectionNavButton(fixture.nativeElement, 'Aroma').click();
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('#aroma-score')).not.toBeNull();

      sectionNavButton(fixture.nativeElement, 'Sabor').click();
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('#flavor-score')).not.toBeNull();
    });

    it('jumping to an incomplete section is never blocked, unlike the old gated wizard', async () => {
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      // Never touched Apariencia's own fields — jumping straight to Resumen must still work.
      sectionNavButton(fixture.nativeElement, 'Resumen').click();
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain('Revisa tu evaluación');
    });

    it('navigating away and back to a section preserves whatever was typed', async () => {
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      fillField(fixture.nativeElement, 'appearance-score', String(validScores().appearance));
      fillField(fixture.nativeElement, 'appearance-comment', validComments().appearance);
      fixture.detectChanges();

      sectionNavButton(fixture.nativeElement, 'Aroma').click();
      fixture.detectChanges();
      sectionNavButton(fixture.nativeElement, 'Apariencia').click();
      fixture.detectChanges();

      const appearanceScoreInput = fixture.nativeElement.querySelector(
        '#appearance-score',
      ) as HTMLInputElement;
      expect(appearanceScoreInput.value).toBe(String(validScores().appearance));
    });

    it('the nav bar marks a section done once its score and comment are both valid', async () => {
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      expect(sectionNavButton(fixture.nativeElement, 'Apariencia').textContent).not.toContain('✓');

      fillField(fixture.nativeElement, 'appearance-score', String(validScores().appearance));
      fillField(fixture.nativeElement, 'appearance-comment', validComments().appearance);
      fixture.detectChanges();

      expect(sectionNavButton(fixture.nativeElement, 'Apariencia').textContent).toContain('✓');
    });

    it('binds each section tab to its own score and comment, so every section gets its own ✓', async () => {
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      const tabs = [
        { label: 'Apariencia', key: 'appearance' },
        { label: 'Aroma', key: 'aroma' },
        { label: 'Sabor', key: 'flavor' },
        { label: 'Sensación en boca', key: 'mouthfeel' },
        { label: 'Impresión general', key: 'overall' },
      ] as const;

      for (const tab of tabs) {
        sectionNavButton(fixture.nativeElement, tab.label).click();
        fixture.detectChanges();
        fillField(fixture.nativeElement, `${tab.key}-score`, String(validScores()[tab.key]));
        fillField(fixture.nativeElement, `${tab.key}-comment`, validComments()[tab.key]);
        fixture.detectChanges();
      }

      const form = fixture.componentInstance.form;
      for (const tab of tabs) {
        expect(form.get(`${tab.key}Score`)?.value).toBe(validScores()[tab.key]);
        expect(form.get(`${tab.key}Comment`)?.value).toBe(validComments()[tab.key]);
        expect(sectionNavButton(fixture.nativeElement, tab.label).textContent).toContain('✓');
      }

      // Going back to an earlier tab shows that tab's own comment, not the last one typed.
      sectionNavButton(fixture.nativeElement, 'Aroma').click();
      fixture.detectChanges();
      const aromaComment = fixture.nativeElement.querySelector(
        '#aroma-comment',
      ) as HTMLTextAreaElement;
      expect(aromaComment.value).toBe(validComments().aroma);
    });

    it('reaches Resumen showing every score, comment and the total, submit button included', async () => {
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
        feedback: '',
      });
      fixture.detectChanges();
      sectionNavButton(fixture.nativeElement, 'Resumen').click();
      fixture.detectChanges();

      const text = fixture.nativeElement.textContent as string;
      expect(text).toContain('Revisa tu evaluación');
      expect(text).toContain(`${validScores().aroma} / 12`);
      expect(text).toContain(validComments().flavor);
      expect(text).toContain('Total');
      expect(text).toContain('Descriptores de defecto detectados');
      expect(fixture.nativeElement.querySelector('button[type="submit"]')).not.toBeNull();
    });

    it('setting a discrete intensity descriptor updates component state', async () => {
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      sectionNavButton(fixture.nativeElement, 'Aroma').click();
      fixture.detectChanges();
      const maltSlider = fixture.nativeElement.querySelector('#aroma-malt') as HTMLInputElement;
      maltSlider.value = '3';
      maltSlider.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      expect(fixture.componentInstance.descriptors().aroma.malt).toBe(3);
    });

    // senior-review B1: an earlier version of this component defaulted every slider's *state*
    // field to a concrete number (0/50), so an untouched slider silently submitted a fabricated
    // rating — indistinguishable from a judge who deliberately rated it "Nada"/neutral. Fixed by
    // keeping the state `null` until the judge actually drags the slider; the template alone
    // supplies a display-only fallback for where the handle sits. These two tests prove that fix
    // holds all the way out to what SyncService actually receives, not just component state.
    it('an untouched slider reaches submit() as null, not a fabricated default', async () => {
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      // Touch only Aroma's malt slider — every other slider (appearance.retention,
      // aroma.hops/fermentation, flavor.*, mouthfeel.*, overall.*) is left completely untouched.
      sectionNavButton(fixture.nativeElement, 'Aroma').click();
      fixture.detectChanges();
      const maltSlider = fixture.nativeElement.querySelector('#aroma-malt') as HTMLInputElement;
      maltSlider.value = '3';
      maltSlider.dispatchEvent(new Event('input'));

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
        feedback: '',
      });

      await fixture.componentInstance.onSubmit();

      const [, , , , , descriptorsArg] = fakeSync.submit.mock.calls[0] as [
        string,
        string,
        string,
        unknown,
        unknown,
        {
          aroma: { malt: number | null; hops: number | null; fermentation: number | null };
          appearance: { retention: number | null };
          overall: { classicExample: number | null };
        },
      ];
      expect(descriptorsArg.aroma.malt).toBe(3);
      expect(descriptorsArg.aroma.hops).toBeNull();
      expect(descriptorsArg.aroma.fermentation).toBeNull();
      expect(descriptorsArg.appearance.retention).toBeNull();
      expect(descriptorsArg.overall.classicExample).toBeNull();
    });

    it('an untouched slider still shows its neutral display position, without that reaching submit', async () => {
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      sectionNavButton(fixture.nativeElement, 'Aroma').click();
      fixture.detectChanges();
      const maltSlider = fixture.nativeElement.querySelector('#aroma-malt') as HTMLInputElement;
      // Never touched — the rendered handle position is a display-only fallback (Nada = 0),
      // never written into component state (verified above) or, therefore, ever submitted.
      expect(maltSlider.value).toBe('0');
      expect(fixture.componentInstance.descriptors().aroma.malt).toBeNull();
    });

    it('toggling an off-flavor descriptor on the Resumen tab tracks it', async () => {
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      sectionNavButton(fixture.nativeElement, 'Resumen').click();
      fixture.detectChanges();

      const diacetylCheckbox = Array.from(
        fixture.nativeElement.querySelectorAll('.off-flavor-option'),
      )
        .find((label) => (label as HTMLElement).textContent?.includes('Diacetil'))
        ?.querySelector('input') as HTMLInputElement;
      diacetylCheckbox.click();
      fixture.detectChanges();

      expect(fixture.componentInstance.descriptors().offFlavors.has('Diacetyl')).toBe(true);
    });

    // Organizer follow-up (Session 2026-09-21): every descriptor now has its own "Inapropiado"
    // flag — not just the handful that had one in the first pass (e.g. Sabor's attributes and
    // Impresión general's had none at all before).
    it('every slider on every section carries its own Inapropiado checkbox', async () => {
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      const expectedSliderIdsBySection: Record<string, string[]> = {
        Apariencia: ['appearance-retention'],
        Aroma: ['aroma-malt', 'aroma-hops', 'aroma-fermentation'],
        Sabor: [
          'flavor-malt',
          'flavor-hops',
          'flavor-bitterness',
          'flavor-fermentation',
          'flavor-balance',
          'flavor-finish',
        ],
        'Sensación en boca': [
          'mouthfeel-body',
          'mouthfeel-carbonation',
          'mouthfeel-alcohol-warmth',
          'mouthfeel-creaminess',
          'mouthfeel-astringency',
        ],
        'Impresión general': ['overall-classic-example', 'overall-defects', 'overall-vitality'],
      };

      for (const [sectionLabel, sliderIds] of Object.entries(expectedSliderIdsBySection)) {
        sectionNavButton(fixture.nativeElement, sectionLabel).click();
        fixture.detectChanges();

        for (const sliderId of sliderIds) {
          const slider = fixture.nativeElement
            .querySelector(`#${sliderId}`)
            ?.closest('bp-discrete-slider, bp-bipolar-slider');
          expect(slider?.querySelector('input[type="checkbox"]')).not.toBeNull();
        }
      }
    });

    it('the Color/Claridad/Espuma selects on Apariencia each carry their own Inapropiado checkbox', async () => {
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      const fields = fixture.nativeElement.querySelectorAll('.descriptor-field');
      expect(fields.length).toBe(3); // Color, Claridad, Espuma
      for (const field of Array.from(fields)) {
        expect((field as HTMLElement).querySelector('input[type="checkbox"]')).not.toBeNull();
        expect((field as HTMLElement).textContent).toContain('Inapropiado');
      }
    });

    it('renders Puntuación and Comentario after the descriptors, not before', async () => {
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      sectionNavButton(fixture.nativeElement, 'Aroma').click();
      fixture.detectChanges();

      const fieldset = fixture.nativeElement.querySelector('fieldset.evaluation-section');
      const descriptorGroup = fieldset.querySelector('.descriptor-group');
      const scoreGroup = fieldset.querySelector('.score-group');
      expect(descriptorGroup).not.toBeNull();
      expect(scoreGroup).not.toBeNull();

      const position = descriptorGroup.compareDocumentPosition(scoreGroup);
      expect(position & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      expect(scoreGroup.querySelector('#aroma-score')).not.toBeNull();
      expect(scoreGroup.querySelector('#aroma-comment')).not.toBeNull();
    });
  });
});
