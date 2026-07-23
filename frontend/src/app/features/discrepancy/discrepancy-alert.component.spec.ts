import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';

import { ApiError } from '../../core/api/api-error';
import { CompetitionHubService } from '../../core/realtime/competition-hub.service';
import type {
  DiscrepancyRaisedEvent,
  DiscrepancyResolvedEvent,
} from '../../core/realtime/competition-hub.events';
import { DiscrepancyAlertComponent } from './discrepancy-alert.component';
import { DiscrepancyApiService } from './discrepancy-api.service';
import type { AdjustEvaluationResult, DiscrepancyView } from './discrepancy-api.service';

function alertFixture(overrides: Partial<DiscrepancyView> = {}): DiscrepancyView {
  return {
    alertId: 'a1',
    blindCode: 'AB12',
    totals: [
      { judgeDisplayName: 'Ada Lovelace', total: 40, isMine: true, evaluationId: 'e1' },
      { judgeDisplayName: 'Grace Hopper', total: 28, isMine: false, evaluationId: 'e2' },
    ],
    ...overrides,
  };
}

function validForm() {
  return {
    aromaScore: 10,
    aromaComment: 'Citrus and pine hop aroma, moderate intensity.',
    appearanceScore: 2,
    appearanceComment: 'Deep golden, persistent white head, brilliant.',
    flavorScore: 15,
    flavorComment: 'Balanced malt backbone with resinous hop finish.',
    mouthfeelScore: 4,
    mouthfeelComment: 'Medium body, lively carbonation, dry finish.',
    overallScore: 8,
    overallComment: 'A clean, well-executed example of the style.',
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

function findButtonWithText(root: Element, text: string): HTMLButtonElement | undefined {
  const buttons = [...root.querySelectorAll('button')] as HTMLButtonElement[];
  return buttons.find((button) => button.textContent?.trim() === text);
}

describe('DiscrepancyAlertComponent', () => {
  let fakeApi: { getDiscrepancies: jest.Mock; adjustEvaluation: jest.Mock };
  let fakeHub: {
    start: jest.Mock;
    joinTable: jest.Mock;
    leaveTable: jest.Mock;
    on: jest.Mock;
  };
  let discrepancyRaisedSubject: Subject<DiscrepancyRaisedEvent>;
  let discrepancyResolvedSubject: Subject<DiscrepancyResolvedEvent>;

  beforeEach(() => {
    discrepancyRaisedSubject = new Subject<DiscrepancyRaisedEvent>();
    discrepancyResolvedSubject = new Subject<DiscrepancyResolvedEvent>();
    fakeApi = {
      getDiscrepancies: jest.fn().mockReturnValue(of([alertFixture()])),
      adjustEvaluation: jest.fn(),
    };
    fakeHub = {
      start: jest.fn().mockResolvedValue(undefined),
      joinTable: jest.fn().mockResolvedValue(undefined),
      leaveTable: jest.fn().mockResolvedValue(undefined),
      on: jest.fn((event: string) => {
        if (event === 'DiscrepancyResolved') {
          return discrepancyResolvedSubject.asObservable();
        }
        return discrepancyRaisedSubject.asObservable();
      }),
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: DiscrepancyApiService, useValue: fakeApi },
        { provide: CompetitionHubService, useValue: fakeHub },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ tableId: 't1' }) } },
        },
      ],
    });
  });

  function createComponent() {
    const fixture = TestBed.createComponent(DiscrepancyAlertComponent);
    fixture.detectChanges();
    return fixture;
  }

  async function flush(): Promise<void> {
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
  }

  it('loads open discrepancies on init and renders the totals table', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    expect(fakeApi.getDiscrepancies).toHaveBeenCalledWith('t1');
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('AB12');
    expect(text).toContain('Ada Lovelace');
    expect(text).toContain('(you)');
    expect(text).toContain('Grace Hopper');
    expect(text).not.toContain('Grace Hopper (you)');
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

  it('shows the empty state with a link back to the table when there are no open alerts', async () => {
    fakeApi.getDiscrepancies.mockReturnValue(of([]));
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('No open discrepancies on this table.');
    expect(fixture.nativeElement.querySelector('a[href="/judge/tables/t1"]')).not.toBeNull();
  });

  it('surfaces a load error message when fetching discrepancies fails', async () => {
    fakeApi.getDiscrepancies.mockReturnValue(
      throwError(
        () => new ApiError({ status: 404, title: 'Not found', urn: null, detail: 'Not found.' }),
      ),
    );
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Not found.');
  });

  it('reveals an inline adjustment form on "Adjust my evaluation" and disables submit while invalid', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('form')).toBeNull();

    buttonWithText(fixture.nativeElement, 'Adjust my evaluation').click();
    fixture.detectChanges();

    const submitButton = fixture.nativeElement.querySelector(
      'button[type="submit"]',
    ) as HTMLButtonElement;
    expect(submitButton).not.toBeNull();
    expect(submitButton.disabled).toBe(true);
  });

  it('cancelling the inline form hides it again without calling adjustEvaluation', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    buttonWithText(fixture.nativeElement, 'Adjust my evaluation').click();
    fixture.detectChanges();
    buttonWithText(fixture.nativeElement, 'Cancel').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('form')).toBeNull();
    expect(fakeApi.adjustEvaluation).not.toHaveBeenCalled();
  });

  it("submits the adjustment for the caller's own evaluationId and shows a resolved confirmation when the discrepancy clears", async () => {
    const result: AdjustEvaluationResult = {
      evaluationId: 'e1',
      status: 'Confirmed',
      total: 39,
      discrepancy: null,
    };
    fakeApi.adjustEvaluation.mockReturnValue(of(result));

    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    buttonWithText(fixture.nativeElement, 'Adjust my evaluation').click();
    fixture.detectChanges();
    fixture.componentInstance.formFor('a1').setValue(validForm());
    fixture.detectChanges();

    buttonWithText(fixture.nativeElement, 'Submit adjustment').click();
    fixture.detectChanges();
    await flush();
    fixture.detectChanges();

    expect(fakeApi.adjustEvaluation).toHaveBeenCalledWith(
      't1',
      'e1',
      {
        aroma: 10,
        appearance: 2,
        flavor: 15,
        mouthfeel: 4,
        overall: 8,
      },
      {
        aroma: validForm().aromaComment,
        appearance: validForm().appearanceComment,
        flavor: validForm().flavorComment,
        mouthfeel: validForm().mouthfeelComment,
        overall: validForm().overallComment,
      },
    );
    expect(fixture.nativeElement.textContent).toContain('Resolved — your evaluation is confirmed.');
    expect(findButtonWithText(fixture.nativeElement, 'Adjust my evaluation')).toBeUndefined();
  });

  it('updates the totals table in place and collapses the form when still PendingConsensus', async () => {
    const updatedAlert = alertFixture({
      totals: [
        { judgeDisplayName: 'Ada Lovelace', total: 33, isMine: true, evaluationId: 'e1' },
        { judgeDisplayName: 'Grace Hopper', total: 28, isMine: false, evaluationId: 'e2' },
      ],
    });
    const result: AdjustEvaluationResult = {
      evaluationId: 'e1',
      status: 'PendingConsensus',
      total: 33,
      discrepancy: updatedAlert,
    };
    fakeApi.adjustEvaluation.mockReturnValue(of(result));

    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    buttonWithText(fixture.nativeElement, 'Adjust my evaluation').click();
    fixture.detectChanges();
    fixture.componentInstance.formFor('a1').setValue(validForm());
    fixture.detectChanges();

    buttonWithText(fixture.nativeElement, 'Submit adjustment').click();
    fixture.detectChanges();
    await flush();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('form')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('33');
    expect(fixture.nativeElement.textContent).not.toContain(
      'Resolved — your evaluation is confirmed.',
    );
    expect(buttonWithText(fixture.nativeElement, 'Adjust my evaluation')).not.toBeNull();
  });

  it('shows an evaluation-locked error and keeps the typed-in values on a 409', async () => {
    fakeApi.adjustEvaluation.mockReturnValue(
      throwError(
        () =>
          new ApiError({
            status: 409,
            title: 'Evaluation locked',
            urn: 'urn:birrapoint:evaluation-locked',
          }),
      ),
    );

    const fixture = createComponent();
    await flush();
    fixture.detectChanges();

    buttonWithText(fixture.nativeElement, 'Adjust my evaluation').click();
    fixture.detectChanges();
    fixture.componentInstance.formFor('a1').setValue(validForm());
    fixture.detectChanges();

    buttonWithText(fixture.nativeElement, 'Submit adjustment').click();
    fixture.detectChanges();
    await flush();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('no longer open for adjustment');
    expect(fixture.componentInstance.formFor('a1').getRawValue()).toMatchObject({
      aromaScore: 10,
      aromaComment: validForm().aromaComment,
    });
  });

  it('re-fetches discrepancies on a live DiscrepancyRaised event for this table', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();
    fakeApi.getDiscrepancies.mockClear();

    discrepancyRaisedSubject.next({
      alertId: 'a2',
      tableId: 't1',
      blindCode: 'CD34',
      involvedJudgeIds: ['j1', 'j2'],
    });
    await flush();

    expect(fakeApi.getDiscrepancies).toHaveBeenCalledWith('t1');
  });

  it('re-fetches discrepancies on a live DiscrepancyResolved event for this table', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();
    fakeApi.getDiscrepancies.mockClear();

    discrepancyResolvedSubject.next({ alertId: 'a1', tableId: 't1', blindCode: 'AB12' });
    await flush();

    expect(fakeApi.getDiscrepancies).toHaveBeenCalledWith('t1');
  });

  it('ignores a DiscrepancyRaised event for a different table', async () => {
    const fixture = createComponent();
    await flush();
    fixture.detectChanges();
    fakeApi.getDiscrepancies.mockClear();

    discrepancyRaisedSubject.next({
      alertId: 'a2',
      tableId: 'other-table',
      blindCode: 'CD34',
      involvedJudgeIds: ['j1', 'j2'],
    });
    await flush();

    expect(fakeApi.getDiscrepancies).not.toHaveBeenCalled();
  });
});
