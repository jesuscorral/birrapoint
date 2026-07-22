import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { environment } from '../../../environments/environment';
import { TastingOrderApiService } from './tasting-order-api.service';
import type { JudgeSample, JudgeTableSummary } from './tasting-order-api.service';

describe('TastingOrderApiService', () => {
  let service: TastingOrderApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(TastingOrderApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getMyTables() gets the judge-assigned tables', async () => {
    const tables: JudgeTableSummary[] = [
      {
        tableId: 't1',
        name: 'Table 1',
        competitionState: 'Active',
        tableState: 'Open',
        orderFixed: false,
        orderFixedBy: null,
      },
    ];
    const result = firstValueFrom(service.getMyTables());

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/me/tables`);
    expect(req.request.method).toBe('GET');
    req.flush(tables);

    expect(await result).toEqual(tables);
  });

  it('getTableSamples() gets the blind sample list for a table', async () => {
    const samples: JudgeSample[] = [
      {
        beerEntryId: 'e1',
        blindCode: 'AB12',
        styleCode: '4A',
        styleName: 'Munich Helles',
        sequenceOrder: null,
        evaluationStatus: 'NotStarted',
      },
    ];
    const result = firstValueFrom(service.getTableSamples('t1'));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/me/tables/t1/samples`);
    expect(req.request.method).toBe('GET');
    req.flush(samples);

    expect(await result).toEqual(samples);
  });

  it('fixOrder() posts the ordered beer entry ids and returns the updated samples', async () => {
    const samples: JudgeSample[] = [
      {
        beerEntryId: 'e1',
        blindCode: 'AB12',
        styleCode: '4A',
        styleName: 'Munich Helles',
        sequenceOrder: 1,
        evaluationStatus: 'NotStarted',
      },
    ];
    const result = firstValueFrom(service.fixOrder('t1', ['e1']));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/me/tables/t1/order`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ orderedBeerEntryIds: ['e1'] });
    req.flush(samples);

    expect(await result).toEqual(samples);
  });

  it('closeTable() posts with no body and returns the consolidated scores', async () => {
    const response = { consolidatedScores: [{ blindCode: 'AB12', mean: 32.5 }] };
    const result = firstValueFrom(service.closeTable('t1'));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/me/tables/t1/close`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    req.flush(response);

    expect(await result).toEqual(response);
  });
});
