import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';
import type { CdkDragDrop } from '@angular/cdk/drag-drop';

import { ApiError } from '../../core/api/api-error';
import { CompetitionHubService } from '../../core/realtime/competition-hub.service';
import type {
  TableClosedEvent,
  TableOrderFixedEvent,
} from '../../core/realtime/competition-hub.events';
import { JudgeTableOrderComponent } from './judge-table-order.component';
import { TastingOrderApiService } from './tasting-order-api.service';
import type { JudgeSample, JudgeTableSummary } from './tasting-order-api.service';

function tableFixture(overrides: Partial<JudgeTableSummary> = {}): JudgeTableSummary {
  return {
    tableId: 't1',
    name: 'Table 1',
    competitionState: 'Active',
    tableState: 'Open',
    orderFixed: false,
    orderFixedBy: null,
    ...overrides,
  };
}

function sampleFixture(overrides: Partial<JudgeSample> = {}): JudgeSample {
  return {
    beerEntryId: 'e1',
    blindCode: 'AB12',
    styleCode: '4A',
    styleName: 'Munich Helles',
    sequenceOrder: null,
    evaluationStatus: 'NotStarted',
    ...overrides,
  };
}

function samplesFixture(): JudgeSample[] {
  return [
    sampleFixture({ beerEntryId: 'e1', blindCode: 'AB12' }),
    sampleFixture({
      beerEntryId: 'e2',
      blindCode: 'CD34',
      styleCode: '21A',
      styleName: 'American IPA',
    }),
    sampleFixture({ beerEntryId: 'e3', blindCode: 'EF56' }),
  ];
}

// All samples fixed (sequenceOrder assigned) and fully evaluated — the state a table must be in
// before "Close table" becomes available.
function doneSamplesFixture(): JudgeSample[] {
  return samplesFixture().map((sample, i) => ({
    ...sample,
    sequenceOrder: i + 1,
    evaluationStatus: 'Submitted' as const,
  }));
}

function buttonWithLabel(root: Element, label: string): HTMLButtonElement {
  const buttons = [...root.querySelectorAll('button')] as HTMLButtonElement[];
  const match = buttons.find((button) => button.getAttribute('aria-label') === label);
  if (!match) {
    throw new Error(`No button with aria-label "${label}" found`);
  }
  return match;
}

function buttonWithText(root: Element, text: string): HTMLButtonElement {
  const buttons = [...root.querySelectorAll('button')] as HTMLButtonElement[];
  const match = buttons.find((button) => button.textContent?.trim() === text);
  if (!match) {
    throw new Error(`No button with text "${text}" found`);
  }
  return match;
}

function findButtonWithText(root: Element, text: string): HTMLButtonElement | undefined {
  const buttons = [...root.querySelectorAll('button')] as HTMLButtonElement[];
  return buttons.find((button) => button.textContent?.trim() === text);
}

describe('JudgeTableOrderComponent', () => {
  let fakeApi: {
    getMyTables: jest.Mock;
    getTableSamples: jest.Mock;
    fixOrder: jest.Mock;
    closeTable: jest.Mock;
  };
  let fakeHub: {
    start: jest.Mock;
    joinTable: jest.Mock;
    leaveTable: jest.Mock;
    on: jest.Mock;
  };
  let orderFixedSubject: Subject<TableOrderFixedEvent>;
  let tableClosedSubject: Subject<TableClosedEvent>;

  beforeEach(() => {
    orderFixedSubject = new Subject<TableOrderFixedEvent>();
    tableClosedSubject = new Subject<TableClosedEvent>();
    fakeApi = {
      getMyTables: jest.fn().mockReturnValue(of([tableFixture()])),
      getTableSamples: jest.fn().mockReturnValue(of(samplesFixture())),
      fixOrder: jest.fn(),
      closeTable: jest.fn(),
    };
    fakeHub = {
      start: jest.fn().mockResolvedValue(undefined),
      joinTable: jest.fn().mockResolvedValue(undefined),
      leaveTable: jest.fn().mockResolvedValue(undefined),
      on: jest.fn((event: string) => {
        if (event === 'TableClosed') {
          return tableClosedSubject.asObservable();
        }
        return orderFixedSubject.asObservable();
      }),
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: TastingOrderApiService, useValue: fakeApi },
        { provide: CompetitionHubService, useValue: fakeHub },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ tableId: 't1' }) } },
        },
      ],
    });
  });

  function createComponent() {
    const fixture = TestBed.createComponent(JudgeTableOrderComponent);
    fixture.detectChanges();
    return fixture;
  }

  async function flush() {
    await Promise.resolve();
    await Promise.resolve();
  }

  it('loads samples and the table summary on init', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    expect(fakeApi.getTableSamples).toHaveBeenCalledWith('t1');
    expect(fakeApi.getMyTables).toHaveBeenCalled();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Table 1');
    expect(text).toContain('AB12');
    expect(text).toContain('CD34');
    expect(text).toContain('EF56');
  });

  it('joins the table SignalR group on init and leaves it on destroy', async () => {
    const fixture = createComponent();
    await flush();

    expect(fakeHub.start).toHaveBeenCalled();
    expect(fakeHub.joinTable).toHaveBeenCalledWith('t1');

    fixture.destroy();
    await flush();

    expect(fakeHub.leaveTable).toHaveBeenCalledWith('t1');
  });

  it('reorders the local sample list on a CDK drop event', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    const dropEvent = { previousIndex: 0, currentIndex: 2 } as CdkDragDrop<unknown>;
    fixture.componentInstance.onDrop(dropEvent);
    fixture.detectChanges();

    expect(fixture.componentInstance.samples().map((s) => s.beerEntryId)).toEqual([
      'e2',
      'e3',
      'e1',
    ]);
  });

  it('reorders the local sample list with the keyboard move-down control', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    buttonWithLabel(fixture.nativeElement, 'Move AB12 down').click();
    fixture.detectChanges();

    expect(fixture.componentInstance.samples().map((s) => s.beerEntryId)).toEqual([
      'e2',
      'e1',
      'e3',
    ]);
  });

  it('reorders the local sample list with the keyboard move-up control', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    buttonWithLabel(fixture.nativeElement, 'Move CD34 up').click();
    fixture.detectChanges();

    expect(fixture.componentInstance.samples().map((s) => s.beerEntryId)).toEqual([
      'e2',
      'e1',
      'e3',
    ]);
  });

  it('disables move-up for the first row and move-down for the last row', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    expect(buttonWithLabel(fixture.nativeElement, 'Move AB12 up').disabled).toBe(true);
    expect(buttonWithLabel(fixture.nativeElement, 'Move EF56 down').disabled).toBe(true);
  });

  it('fixes the order only after the confirm step, then locks the UI', async () => {
    fakeApi.fixOrder.mockReturnValue(
      of(samplesFixture().map((s, i) => ({ ...s, sequenceOrder: i + 1 }))),
    );
    fakeApi.getMyTables
      .mockReturnValueOnce(of([tableFixture()]))
      .mockReturnValue(of([tableFixture({ orderFixed: true, orderFixedBy: 'Ada Lovelace' })]));
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    buttonWithText(fixture.nativeElement, 'Fix order').click();
    fixture.detectChanges();

    expect(fakeApi.fixOrder).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('cannot be undone');

    buttonWithText(fixture.nativeElement, 'Confirm fix order').click();
    fixture.detectChanges();
    await flush();
    fixture.detectChanges();

    expect(fakeApi.fixOrder).toHaveBeenCalledWith('t1', ['e1', 'e2', 'e3']);
    expect(fixture.componentInstance.orderFixed()).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Ada Lovelace');
    expect(fixture.nativeElement.querySelector('button[aria-label="Move AB12 up"]')).toBeNull();
  });

  it('cancelling the confirm step does not call fixOrder', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    buttonWithText(fixture.nativeElement, 'Fix order').click();
    fixture.detectChanges();
    buttonWithText(fixture.nativeElement, 'Cancel').click();
    fixture.detectChanges();

    expect(fakeApi.fixOrder).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).not.toContain('cannot be undone');
  });

  it('locks the UI on a live TableOrderFixed event without calling fixOrder', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    orderFixedSubject.next({
      tableId: 't1',
      orderedSamples: [
        { beerEntryId: 'e2', blindCode: 'CD34', sequenceOrder: 1 },
        { beerEntryId: 'e1', blindCode: 'AB12', sequenceOrder: 2 },
        { beerEntryId: 'e3', blindCode: 'EF56', sequenceOrder: 3 },
      ],
      fixedByDisplayName: 'Grace Hopper',
    });
    fixture.detectChanges();

    expect(fakeApi.fixOrder).not.toHaveBeenCalled();
    expect(fixture.componentInstance.orderFixed()).toBe(true);
    expect(fixture.componentInstance.samples().map((s) => s.beerEntryId)).toEqual([
      'e2',
      'e1',
      'e3',
    ]);
    expect(fixture.nativeElement.textContent).toContain('Grace Hopper');
  });

  it('ignores a TableOrderFixed event for a different table', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    orderFixedSubject.next({
      tableId: 'other-table',
      orderedSamples: [{ beerEntryId: 'e1', blindCode: 'AB12', sequenceOrder: 1 }],
      fixedByDisplayName: 'Someone Else',
    });
    fixture.detectChanges();

    expect(fixture.componentInstance.orderFixed()).toBe(false);
  });

  it('shows "already fixed by {fixedBy}" and reconciles the order on a 409 order-already-fixed race', async () => {
    fakeApi.fixOrder.mockReturnValue(
      throwError(
        () =>
          new ApiError({
            status: 409,
            title: 'Tasting order already fixed',
            urn: 'urn:birrapoint:order-already-fixed',
            extensions: { fixedBy: 'Grace Hopper' },
          }),
      ),
    );
    const reconciled = samplesFixture().map((s, i) => ({ ...s, sequenceOrder: i + 1 }));
    fakeApi.getTableSamples
      .mockReturnValueOnce(of(samplesFixture()))
      .mockReturnValue(of(reconciled));
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    buttonWithText(fixture.nativeElement, 'Fix order').click();
    fixture.detectChanges();
    buttonWithText(fixture.nativeElement, 'Confirm fix order').click();
    fixture.detectChanges();
    await flush();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Order already fixed by Grace Hopper');
    expect(fixture.componentInstance.orderFixed()).toBe(true);
  });

  it('shows a plain message on a 409 invalid-state-transition', async () => {
    fakeApi.fixOrder.mockReturnValue(
      throwError(
        () =>
          new ApiError({
            status: 409,
            title: 'Invalid state transition',
            urn: 'urn:birrapoint:invalid-state-transition',
          }),
      ),
    );
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    buttonWithText(fixture.nativeElement, 'Fix order').click();
    fixture.detectChanges();
    buttonWithText(fixture.nativeElement, 'Confirm fix order').click();
    fixture.detectChanges();
    await flush();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('not open for ordering yet');
  });

  it('surfaces a generic error message on other fixOrder failures', async () => {
    fakeApi.fixOrder.mockReturnValue(
      throwError(
        () =>
          new ApiError({
            status: 400,
            title: 'Invalid request',
            urn: 'urn:birrapoint:validation',
            detail: 'orderedBeerEntryIds must be an exact permutation.',
          }),
      ),
    );
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    buttonWithText(fixture.nativeElement, 'Fix order').click();
    fixture.detectChanges();
    buttonWithText(fixture.nativeElement, 'Confirm fix order').click();
    fixture.detectChanges();
    await flush();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'orderedBeerEntryIds must be an exact permutation.',
    );
  });

  it('shows no evaluation entry point while the order is not fixed', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('a[href*="/samples/"]')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Evaluate');
  });

  it('shows an Evaluate link only for the first NotStarted sample once the order is fixed', async () => {
    fakeApi.fixOrder.mockReturnValue(
      of(samplesFixture().map((s, i) => ({ ...s, sequenceOrder: i + 1 }))),
    );
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();
    buttonWithText(fixture.nativeElement, 'Fix order').click();
    fixture.detectChanges();
    buttonWithText(fixture.nativeElement, 'Confirm fix order').click();
    fixture.detectChanges();
    await flush();
    fixture.detectChanges();

    const evaluateLink = fixture.nativeElement.querySelector(
      'a[href="/judge/tables/t1/samples/e1"]',
    ) as HTMLAnchorElement | null;
    expect(evaluateLink).not.toBeNull();
    expect(evaluateLink?.textContent).toContain('Evaluate');

    // e2/e3 are NotStarted too but not first in sequence — locked, not linked.
    expect(fixture.nativeElement.querySelector('a[href="/judge/tables/t1/samples/e2"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('a[href="/judge/tables/t1/samples/e3"]')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Locked');
  });

  it('shows a read-only "Submitted" label (no link) for an already-submitted sample', async () => {
    fakeApi.fixOrder.mockReturnValue(
      of(
        samplesFixture().map((s, i) => ({
          ...s,
          sequenceOrder: i + 1,
          evaluationStatus: s.beerEntryId === 'e1' ? 'Submitted' : s.evaluationStatus,
        })),
      ),
    );
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();
    buttonWithText(fixture.nativeElement, 'Fix order').click();
    fixture.detectChanges();
    buttonWithText(fixture.nativeElement, 'Confirm fix order').click();
    fixture.detectChanges();
    await flush();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('a[href="/judge/tables/t1/samples/e1"]')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Submitted');
    // e2 is now the first NotStarted sample and becomes reachable.
    expect(
      fixture.nativeElement.querySelector('a[href="/judge/tables/t1/samples/e2"]'),
    ).not.toBeNull();
  });

  it('shows a read-only "Pending consensus" label (no link) for a sample under discrepancy review', async () => {
    fakeApi.fixOrder.mockReturnValue(
      of(
        samplesFixture().map((s, i) => ({
          ...s,
          sequenceOrder: i + 1,
          evaluationStatus: s.beerEntryId === 'e1' ? 'PendingConsensus' : s.evaluationStatus,
        })),
      ),
    );
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();
    buttonWithText(fixture.nativeElement, 'Fix order').click();
    fixture.detectChanges();
    buttonWithText(fixture.nativeElement, 'Confirm fix order').click();
    fixture.detectChanges();
    await flush();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('a[href="/judge/tables/t1/samples/e1"]')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Pending consensus');
  });

  it('surfaces a load error message when fetching samples fails', async () => {
    fakeApi.getTableSamples.mockReturnValue(
      throwError(
        () => new ApiError({ status: 404, title: 'Not found', urn: null, detail: 'Not found.' }),
      ),
    );
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Not found.');
  });

  describe('closing the table (T066)', () => {
    it('shows a Close table button once the order is fixed and every sample is done', async () => {
      fakeApi.getMyTables.mockReturnValue(
        of([tableFixture({ orderFixed: true, orderFixedBy: 'Ada Lovelace' })]),
      );
      fakeApi.getTableSamples.mockReturnValue(of(doneSamplesFixture()));
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      expect(buttonWithText(fixture.nativeElement, 'Close table')).not.toBeNull();
    });

    it('hides the Close table button while any sample is still NotStarted', async () => {
      fakeApi.getMyTables.mockReturnValue(
        of([tableFixture({ orderFixed: true, orderFixedBy: 'Ada Lovelace' })]),
      );
      fakeApi.getTableSamples.mockReturnValue(
        of(
          doneSamplesFixture().map((sample) =>
            sample.beerEntryId === 'e2'
              ? { ...sample, evaluationStatus: 'NotStarted' as const }
              : sample,
          ),
        ),
      );
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      expect(findButtonWithText(fixture.nativeElement, 'Close table')).toBeUndefined();
    });

    it('hides the Close table button while the order is not fixed yet, even if samples are done', async () => {
      fakeApi.getMyTables.mockReturnValue(of([tableFixture({ orderFixed: false })]));
      fakeApi.getTableSamples.mockReturnValue(of(doneSamplesFixture()));
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      expect(findButtonWithText(fixture.nativeElement, 'Close table')).toBeUndefined();
    });

    it('closes the table only after the confirm step, then locks the UI', async () => {
      fakeApi.getMyTables.mockReturnValue(
        of([tableFixture({ orderFixed: true, orderFixedBy: 'Ada Lovelace' })]),
      );
      fakeApi.getTableSamples.mockReturnValue(of(doneSamplesFixture()));
      fakeApi.closeTable.mockReturnValue(
        of({ consolidatedScores: [{ blindCode: 'AB12', mean: 32.5 }] }),
      );
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      buttonWithText(fixture.nativeElement, 'Close table').click();
      fixture.detectChanges();

      expect(fakeApi.closeTable).not.toHaveBeenCalled();
      expect(fixture.nativeElement.textContent).toContain('cannot be undone');

      buttonWithText(fixture.nativeElement, 'Confirm close table').click();
      fixture.detectChanges();
      await flush();
      fixture.detectChanges();

      expect(fakeApi.closeTable).toHaveBeenCalledWith('t1');
      expect(fixture.nativeElement.textContent).toContain('Table closed');
      expect(findButtonWithText(fixture.nativeElement, 'Close table')).toBeUndefined();
    });

    it('cancelling the confirm step does not call closeTable', async () => {
      fakeApi.getMyTables.mockReturnValue(
        of([tableFixture({ orderFixed: true, orderFixedBy: 'Ada Lovelace' })]),
      );
      fakeApi.getTableSamples.mockReturnValue(of(doneSamplesFixture()));
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      buttonWithText(fixture.nativeElement, 'Close table').click();
      fixture.detectChanges();
      buttonWithText(fixture.nativeElement, 'Cancel').click();
      fixture.detectChanges();

      expect(fakeApi.closeTable).not.toHaveBeenCalled();
      expect(fixture.nativeElement.textContent).not.toContain('cannot be undone');
      expect(fixture.nativeElement.textContent).not.toContain('Table closed');
    });

    it('shows the missing blind codes on a 409 evaluations-incomplete', async () => {
      fakeApi.getMyTables.mockReturnValue(
        of([tableFixture({ orderFixed: true, orderFixedBy: 'Ada Lovelace' })]),
      );
      fakeApi.getTableSamples.mockReturnValue(of(doneSamplesFixture()));
      fakeApi.closeTable.mockReturnValue(
        throwError(
          () =>
            new ApiError({
              status: 409,
              title: 'Evaluations incomplete',
              urn: 'urn:birrapoint:evaluations-incomplete',
              extensions: { missing: ['AB12', 'EF56'] },
            }),
        ),
      );
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      buttonWithText(fixture.nativeElement, 'Close table').click();
      fixture.detectChanges();
      buttonWithText(fixture.nativeElement, 'Confirm close table').click();
      fixture.detectChanges();
      await flush();
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain('AB12');
      expect(fixture.nativeElement.textContent).toContain('EF56');
      expect(fixture.nativeElement.textContent).not.toContain('Table closed');
    });

    it('shows the affected blind codes on a 409 discrepancy-open', async () => {
      fakeApi.getMyTables.mockReturnValue(
        of([tableFixture({ orderFixed: true, orderFixedBy: 'Ada Lovelace' })]),
      );
      fakeApi.getTableSamples.mockReturnValue(of(doneSamplesFixture()));
      fakeApi.closeTable.mockReturnValue(
        throwError(
          () =>
            new ApiError({
              status: 409,
              title: 'Discrepancy open',
              urn: 'urn:birrapoint:discrepancy-open',
              extensions: { blindCodes: ['CD34'] },
            }),
        ),
      );
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      buttonWithText(fixture.nativeElement, 'Close table').click();
      fixture.detectChanges();
      buttonWithText(fixture.nativeElement, 'Confirm close table').click();
      fixture.detectChanges();
      await flush();
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain('CD34');
      expect(fixture.nativeElement.textContent).not.toContain('Table closed');
    });

    it('treats a 409 table-closed race as success: shows the closed banner, no error', async () => {
      fakeApi.getMyTables.mockReturnValue(
        of([tableFixture({ orderFixed: true, orderFixedBy: 'Ada Lovelace' })]),
      );
      fakeApi.getTableSamples.mockReturnValue(of(doneSamplesFixture()));
      fakeApi.closeTable.mockReturnValue(
        throwError(
          () =>
            new ApiError({
              status: 409,
              title: 'Table already closed',
              urn: 'urn:birrapoint:table-closed',
            }),
        ),
      );
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      buttonWithText(fixture.nativeElement, 'Close table').click();
      fixture.detectChanges();
      buttonWithText(fixture.nativeElement, 'Confirm close table').click();
      fixture.detectChanges();
      await flush();
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain('Table closed');
      expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
      expect(findButtonWithText(fixture.nativeElement, 'Close table')).toBeUndefined();
    });

    it('flips to the closed banner on a live TableClosed event without calling the API', async () => {
      fakeApi.getMyTables.mockReturnValue(
        of([tableFixture({ orderFixed: true, orderFixedBy: 'Ada Lovelace' })]),
      );
      fakeApi.getTableSamples.mockReturnValue(of(doneSamplesFixture()));
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      tableClosedSubject.next({ tableId: 't1' });
      fixture.detectChanges();

      expect(fakeApi.closeTable).not.toHaveBeenCalled();
      expect(fixture.nativeElement.textContent).toContain('Table closed');
      expect(findButtonWithText(fixture.nativeElement, 'Close table')).toBeUndefined();
    });

    it('ignores a TableClosed event for a different table', async () => {
      fakeApi.getMyTables.mockReturnValue(
        of([tableFixture({ orderFixed: true, orderFixedBy: 'Ada Lovelace' })]),
      );
      fakeApi.getTableSamples.mockReturnValue(of(doneSamplesFixture()));
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      tableClosedSubject.next({ tableId: 'other-table' });
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).not.toContain('Table closed');
    });

    it('shows the closed banner immediately when loading an already-closed table', async () => {
      fakeApi.getMyTables.mockReturnValue(
        of([
          tableFixture({
            tableState: 'Closed',
            orderFixed: true,
            orderFixedBy: 'Ada Lovelace',
          }),
        ]),
      );
      fakeApi.getTableSamples.mockReturnValue(of(doneSamplesFixture()));
      const fixture = createComponent();
      await flush();
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain('Table closed');
      expect(findButtonWithText(fixture.nativeElement, 'Close table')).toBeUndefined();
    });
  });
});
