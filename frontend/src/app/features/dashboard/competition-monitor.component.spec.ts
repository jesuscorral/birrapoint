import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { signal } from '@angular/core';
import type { WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HubConnectionState } from '@microsoft/signalr';
import { of, Subject, throwError } from 'rxjs';

import { ApiError } from '../../core/api/api-error';
import { CompetitionsApiService } from '../../core/api/competitions-api.service';
import type { CompetitionDetail } from '../../core/api/competitions-api.service';
import { EntriesApiService } from '../../core/api/entries-api.service';
import type { EntryListItem } from '../../core/api/entries-api.service';
import { MonitoringApiService } from '../../core/api/monitoring-api.service';
import type {
  EntryEvaluationsResult,
  TableProgressSummary,
} from '../../core/api/monitoring-api.service';
import { CompetitionHubService } from '../../core/realtime/competition-hub.service';
import type {
  EvaluationCompletedEvent,
  TableClosedEvent,
  TableOrderFixedEvent,
} from '../../core/realtime/competition-hub.events';
import { CompetitionMonitorComponent } from './competition-monitor.component';

function competitionFixture(overrides: Partial<CompetitionDetail> = {}): CompetitionDetail {
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
    state: 'InEvaluation',
    ...overrides,
  };
}

function progressFixture(overrides: Partial<TableProgressSummary> = {}): TableProgressSummary {
  return {
    tableId: 't1',
    name: 'Mesa 1',
    state: 'Open',
    completed: 1,
    expected: 2,
    percent: 50,
    ...overrides,
  };
}

function entriesFixture(): EntryListItem[] {
  return [
    {
      id: 'e1',
      blindCode: 'AB12',
      styleCode: '4A',
      styleName: 'Munich Helles',
      abvLow: 4.5,
      abvHigh: 5.5,
      beerName: 'Golden Helles',
      notValidForBos: false,
      tastingTableId: 't1',
      tastingTableName: 'Mesa 1',
    },
    {
      id: 'e2',
      blindCode: 'CD34',
      styleCode: '21A',
      styleName: 'American IPA',
      abvLow: 6,
      abvHigh: 7.5,
      beerName: 'Hazy Dream',
      notValidForBos: false,
      tastingTableId: 't1',
      tastingTableName: 'Mesa 1',
    },
    {
      id: 'e3',
      blindCode: 'EF56',
      styleCode: '1A',
      styleName: 'American Light Lager',
      abvLow: 4,
      abvHigh: 4.5,
      beerName: 'Unassigned Lager',
      notValidForBos: false,
      tastingTableId: null,
      tastingTableName: null,
    },
  ];
}

function evaluationsResultFixture(
  overrides: Partial<EntryEvaluationsResult> = {},
): EntryEvaluationsResult {
  return {
    blindCode: 'AB12',
    evaluations: [
      {
        judgeDisplayName: 'Ada Lovelace',
        scores: { aroma: 10, appearance: 3, flavor: 18, mouthfeel: 4, overall: 9 },
        comments: {
          aroma: 'Clean malt aroma with light hop notes',
          appearance: 'Golden and clear',
          flavor: 'Balanced malt and hops',
          mouthfeel: 'Medium body, smooth',
          overall: 'Well made example of style',
        },
        total: 44,
        status: 'Submitted',
      },
    ],
    consolidatedMean: null,
    ...overrides,
  };
}

function buttonWithText(root: Element, text: string): HTMLButtonElement {
  const buttons = [...root.querySelectorAll('button')] as HTMLButtonElement[];
  const match = buttons.find((button) => button.textContent?.trim() === text);
  if (!match) {
    throw new Error(`No button with text "${text}" found`);
  }
  return match;
}

describe('CompetitionMonitorComponent', () => {
  let fakeCompetitionsApi: { getById: jest.Mock };
  let fakeMonitoringApi: { getProgress: jest.Mock; getEntryEvaluations: jest.Mock };
  let fakeEntriesApi: { getEntries: jest.Mock };
  let fakeHub: {
    start: jest.Mock;
    joinCompetitionAsOrganizer: jest.Mock;
    leaveCompetition: jest.Mock;
    on: jest.Mock;
    state: WritableSignal<HubConnectionState>;
  };
  let evaluationCompletedSubject: Subject<EvaluationCompletedEvent>;
  let tableClosedSubject: Subject<TableClosedEvent>;
  let tableOrderFixedSubject: Subject<TableOrderFixedEvent>;

  beforeEach(() => {
    evaluationCompletedSubject = new Subject<EvaluationCompletedEvent>();
    tableClosedSubject = new Subject<TableClosedEvent>();
    tableOrderFixedSubject = new Subject<TableOrderFixedEvent>();

    fakeCompetitionsApi = { getById: jest.fn().mockReturnValue(of(competitionFixture())) };
    fakeMonitoringApi = {
      getProgress: jest.fn().mockReturnValue(of([progressFixture()])),
      getEntryEvaluations: jest.fn().mockReturnValue(of(evaluationsResultFixture())),
    };
    fakeEntriesApi = { getEntries: jest.fn().mockReturnValue(of(entriesFixture())) };
    fakeHub = {
      start: jest.fn().mockResolvedValue(undefined),
      joinCompetitionAsOrganizer: jest.fn().mockResolvedValue(undefined),
      leaveCompetition: jest.fn().mockResolvedValue(undefined),
      on: jest.fn((event: string) => {
        if (event === 'TableClosed') {
          return tableClosedSubject.asObservable();
        }
        if (event === 'TableOrderFixed') {
          return tableOrderFixedSubject.asObservable();
        }
        return evaluationCompletedSubject.asObservable();
      }),
      // Starts Connected: the effect that watches this signal runs once immediately on creation
      // and must treat that first observation as "already connected," not a reconnect — same as
      // the real hub having finished start()/joinCompetitionAsOrganizer() by the time this
      // component's effect is set up in practice.
      state: signal(HubConnectionState.Connected),
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: CompetitionsApiService, useValue: fakeCompetitionsApi },
        { provide: MonitoringApiService, useValue: fakeMonitoringApi },
        { provide: EntriesApiService, useValue: fakeEntriesApi },
        { provide: CompetitionHubService, useValue: fakeHub },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ id: 'c1' }) } },
        },
      ],
    });
  });

  function createComponent() {
    const fixture = TestBed.createComponent(CompetitionMonitorComponent);
    fixture.detectChanges();
    return fixture;
  }

  async function flush() {
    await Promise.resolve();
    await Promise.resolve();
  }

  it('loads the competition header and per-table progress rows', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    expect(fakeCompetitionsApi.getById).toHaveBeenCalledWith('c1');
    expect(fakeMonitoringApi.getProgress).toHaveBeenCalledWith('c1');
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Golden Ale Cup');
    expect(text).toContain('Town Hall');
    expect(text).toContain('Mesa 1');
    expect(text).toContain('1 / 2');
  });

  it('shows a link to the Results & Dispatch screen once the competition is Finalized', async () => {
    fakeCompetitionsApi.getById.mockReturnValue(of(competitionFixture({ state: 'Finalized' })));
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    const link = fixture.nativeElement.querySelector(
      'a[href="/organizer/competitions/c1/dispatch"]',
    );
    expect(link?.textContent).toContain('Results & Dispatch');
  });

  it('does not show the Results & Dispatch link while the competition is not yet Finalized', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    const link = fixture.nativeElement.querySelector(
      'a[href="/organizer/competitions/c1/dispatch"]',
    );
    expect(link).toBeNull();
  });

  it('groups samples under their assigned table, leaving unassigned samples out', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('AB12');
    expect(text).toContain('CD34');
    expect(text).not.toContain('EF56');
  });

  it('joins the competition SignalR group on init and leaves it on destroy', async () => {
    const fixture = createComponent();
    await flush();

    expect(fakeHub.start).toHaveBeenCalled();
    expect(fakeHub.joinCompetitionAsOrganizer).toHaveBeenCalledWith('c1');

    fixture.destroy();
    await flush();

    expect(fakeHub.leaveCompetition).toHaveBeenCalledWith('c1');
  });

  it('re-fetches on a reconnect (state Connected -> Disconnected -> Connected), but not on the initial connect', async () => {
    const fixture = createComponent();
    await flush();

    // Initial load: one call each, from the constructor's loadAll() — the effect's first
    // observation of "Connected" must not trigger a second fetch.
    expect(fakeMonitoringApi.getProgress).toHaveBeenCalledTimes(1);

    fakeHub.state.set(HubConnectionState.Reconnecting);
    fixture.detectChanges();
    fakeHub.state.set(HubConnectionState.Connected);
    fixture.detectChanges();
    await flush();

    expect(fakeMonitoringApi.getProgress).toHaveBeenCalledTimes(2);
    expect(fakeCompetitionsApi.getById).toHaveBeenCalledTimes(2);
    expect(fakeEntriesApi.getEntries).toHaveBeenCalledTimes(2);
  });

  it('updates a table row progress in place on a live EvaluationCompleted event, without reloading', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();
    fakeMonitoringApi.getProgress.mockClear();

    evaluationCompletedSubject.next({
      tableId: 't1',
      blindCode: 'AB12',
      judgeDisplayName: 'Ada Lovelace',
      tableProgress: { completed: 2, expected: 2, percent: 100 },
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('2 / 2');
    expect(fakeMonitoringApi.getProgress).not.toHaveBeenCalled();
  });

  it('ignores an EvaluationCompleted event for an unknown table', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    evaluationCompletedSubject.next({
      tableId: 'other-table',
      blindCode: 'ZZ99',
      judgeDisplayName: 'Someone Else',
      tableProgress: { completed: 5, expected: 5, percent: 100 },
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('1 / 2');
  });

  it('marks a table row Closed on a live TableClosed event', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    tableClosedSubject.next({ tableId: 't1' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Closed');
  });

  it('ignores a TableClosed event for a different table', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    tableClosedSubject.next({ tableId: 'other-table' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Open');
    expect(fixture.nativeElement.textContent).not.toContain('Closed');
  });

  it('shows a transient note on a live TableOrderFixed event', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    tableOrderFixedSubject.next({
      tableId: 't1',
      orderedSamples: [{ beerEntryId: 'e1', blindCode: 'AB12', sequenceOrder: 1 }],
      fixedByDisplayName: 'Grace Hopper',
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Order fixed by Grace Hopper');
  });

  it('drills down into a sample: fetches and renders every judge score, comment, total, and status', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('[data-entry-id="e1"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await flush();
    fixture.detectChanges();

    expect(fakeMonitoringApi.getEntryEvaluations).toHaveBeenCalledWith('c1', 'e1');
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Ada Lovelace');
    expect(text).toContain('10');
    expect(text).toContain('Clean malt aroma with light hop notes');
    expect(text).toContain('44');
    expect(text).toContain('Submitted');
  });

  it('shows "not yet closed" wording when the consolidated mean is null', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('[data-entry-id="e1"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await flush();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('not yet closed');
  });

  it('shows the consolidated mean once present', async () => {
    fakeMonitoringApi.getEntryEvaluations.mockReturnValue(
      of(evaluationsResultFixture({ consolidatedMean: 42.5 })),
    );
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('[data-entry-id="e1"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await flush();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('42.5');
  });

  it('closes the drill-down panel and returns to the table list', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('[data-entry-id="e1"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await flush();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Ada Lovelace');

    buttonWithText(fixture.nativeElement, 'Close').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('Ada Lovelace');
  });

  it('surfaces a load error message when any initial request fails', async () => {
    fakeMonitoringApi.getProgress.mockReturnValue(
      throwError(
        () => new ApiError({ status: 500, title: 'An unexpected error occurred.', urn: null }),
      ),
    );
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    const alert = fixture.nativeElement.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('An unexpected error occurred.');
  });
});
