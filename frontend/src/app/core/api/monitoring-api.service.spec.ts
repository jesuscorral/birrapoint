import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { environment } from '../../../environments/environment';
import { MonitoringApiService } from './monitoring-api.service';
import type { EntryEvaluationsResult, TableProgressSummary } from './monitoring-api.service';

describe('MonitoringApiService', () => {
  let service: MonitoringApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(MonitoringApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getProgress() gets per-table progress for a competition', async () => {
    const progress: TableProgressSummary[] = [
      { tableId: 't1', name: 'Mesa 1', state: 'Open', completed: 2, expected: 5, percent: 40 },
    ];
    const result = firstValueFrom(service.getProgress('c1'));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/competitions/c1/progress`);
    expect(req.request.method).toBe('GET');
    req.flush(progress);

    expect(await result).toEqual(progress);
  });

  it('getEntryEvaluations() gets the per-judge audit for one entry', async () => {
    const evaluations: EntryEvaluationsResult = {
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
    };
    const result = firstValueFrom(service.getEntryEvaluations('c1', 'e1'));

    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/api/v1/competitions/c1/entries/e1/evaluations`,
    );
    expect(req.request.method).toBe('GET');
    req.flush(evaluations);

    expect(await result).toEqual(evaluations);
  });
});
