import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { ApiClient } from '../../core/api/api-client.service';
import type { EvaluationComments, EvaluationScores } from '../../core/offline/db';
import { DiscrepancyApiService } from './discrepancy-api.service';
import type { AdjustEvaluationResult, DiscrepancyView } from './discrepancy-api.service';

describe('DiscrepancyApiService', () => {
  let fakeApiClient: { get: jest.Mock; put: jest.Mock };
  let service: DiscrepancyApiService;

  beforeEach(() => {
    fakeApiClient = { get: jest.fn(), put: jest.fn() };
    TestBed.configureTestingModule({
      providers: [{ provide: ApiClient, useValue: fakeApiClient }],
    });
    service = TestBed.inject(DiscrepancyApiService);
  });

  it('getDiscrepancies calls GET /me/tables/{tableId}/discrepancies', () => {
    const response: DiscrepancyView[] = [
      {
        alertId: 'a1',
        blindCode: 'AB12',
        totals: [
          { judgeDisplayName: 'Ada Lovelace', total: 40, isMine: true, evaluationId: 'e1' },
          { judgeDisplayName: 'Grace Hopper', total: 30, isMine: false, evaluationId: 'e2' },
        ],
      },
    ];
    fakeApiClient.get.mockReturnValue(of(response));

    let result: DiscrepancyView[] | undefined;
    service.getDiscrepancies('t1').subscribe((value) => (result = value));

    expect(fakeApiClient.get).toHaveBeenCalledWith('/me/tables/t1/discrepancies');
    expect(result).toEqual(response);
  });

  it('adjustEvaluation calls PUT /me/tables/{tableId}/evaluations/{evaluationId} with scores and comments', () => {
    const scores: EvaluationScores = {
      aroma: 10,
      appearance: 2,
      flavor: 15,
      mouthfeel: 4,
      overall: 8,
    };
    const comments: EvaluationComments = {
      aroma: 'Citrus and pine hop aroma, moderate intensity.',
      appearance: 'Deep golden, persistent white head, brilliant.',
      flavor: 'Balanced malt backbone with resinous hop finish.',
      mouthfeel: 'Medium body, lively carbonation, dry finish.',
      overall: 'A clean, well-executed example of the style.',
    };
    const response: AdjustEvaluationResult = {
      evaluationId: 'e1',
      status: 'Confirmed',
      total: 39,
      discrepancy: null,
    };
    fakeApiClient.put.mockReturnValue(of(response));

    let result: AdjustEvaluationResult | undefined;
    service.adjustEvaluation('t1', 'e1', scores, comments).subscribe((value) => (result = value));

    expect(fakeApiClient.put).toHaveBeenCalledWith('/me/tables/t1/evaluations/e1', {
      scores,
      comments,
    });
    expect(result).toEqual(response);
  });
});
