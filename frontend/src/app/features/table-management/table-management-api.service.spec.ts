import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { environment } from '../../../environments/environment';
import { TableManagementApiService } from './table-management-api.service';
import type {
  JudgeListItem,
  TableMutationResult,
  TableSummary,
} from './table-management-api.service';

describe('TableManagementApiService', () => {
  let service: TableManagementApiService;
  let httpMock: HttpTestingController;

  const table: TableSummary = {
    id: 't1',
    name: 'Mesa 1',
    state: 'Open',
    judges: [{ id: 'j1', email: 'ada@example.com', displayName: 'Ada Lovelace' }],
    samples: [
      {
        beerEntryId: 'e1',
        blindCode: 'AB12',
        styleCode: '4A',
        styleName: 'Munich Helles',
        abvLow: 4.5,
        abvHigh: 5.5,
        notValidForBos: false,
      },
    ],
    progress: { submitted: 0, total: 1 },
    stats: { meanAbv: 5, styleCount: 1, styles: ['Munich Helles'] },
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(TableManagementApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getTables() gets the competition tables', async () => {
    const result = firstValueFrom(service.getTables('c1'));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/competitions/c1/tables`);
    expect(req.request.method).toBe('GET');
    req.flush([table]);

    expect(await result).toEqual([table]);
  });

  it('getJudges() gets the competition judges', async () => {
    const judges: JudgeListItem[] = [
      { id: 'j1', email: 'ada@example.com', displayName: 'Ada Lovelace' },
    ];
    const result = firstValueFrom(service.getJudges('c1'));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/competitions/c1/judges`);
    expect(req.request.method).toBe('GET');
    req.flush(judges);

    expect(await result).toEqual(judges);
  });

  it('createTable() posts the full desired judge/entry set', async () => {
    const mutationResult: TableMutationResult = { ...table, bosFlaggedEntryIds: [] };
    const result = firstValueFrom(
      service.createTable('c1', { name: 'Mesa 1', judgeIds: ['j1'], beerEntryIds: ['e1'] }),
    );

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/competitions/c1/tables`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Mesa 1', judgeIds: ['j1'], beerEntryIds: ['e1'] });
    req.flush(mutationResult);

    expect(await result).toEqual(mutationResult);
  });

  it('updateTable() puts the full desired judge/entry set to the table id', async () => {
    const mutationResult: TableMutationResult = { ...table, bosFlaggedEntryIds: ['e1'] };
    const result = firstValueFrom(
      service.updateTable('c1', 't1', { name: 'Mesa 1', judgeIds: [], beerEntryIds: [] }),
    );

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/competitions/c1/tables/t1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ name: 'Mesa 1', judgeIds: [], beerEntryIds: [] });
    req.flush(mutationResult);

    expect(await result).toEqual(mutationResult);
  });
});
