import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { APP_CONFIG } from '../config/app-config.model';
import { TEST_APP_CONFIG } from '../config/app-config.testing';
import { TablesApiService } from './tables-api.service';
import type { TableSummary } from './tables-api.service';

describe('TablesApiService', () => {
  let service: TablesApiService;
  let httpMock: HttpTestingController;

  const table: TableSummary = {
    id: 't1',
    name: 'Mesa 1',
    judges: [
      { id: 'j1', email: 'ada@example.com', displayName: 'Ada Lovelace' },
      { id: 'j2', email: 'grace@example.com', displayName: 'Grace Hopper' },
    ],
    samples: [{ beerEntryId: 'e1' }, { beerEntryId: 'e2' }],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: APP_CONFIG, useValue: TEST_APP_CONFIG },
      ],
    });
    service = TestBed.inject(TablesApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getTables() gets the competition tables', async () => {
    const result = firstValueFrom(service.getTables('c1'));

    const req = httpMock.expectOne(`${TEST_APP_CONFIG.apiBaseUrl}/api/v1/competitions/c1/tables`);
    expect(req.request.method).toBe('GET');
    req.flush([table]);

    expect(await result).toEqual([table]);
  });

  it('removeJudge() PUTs the table without the judge (full replace)', async () => {
    const result = firstValueFrom(service.removeJudge('c1', table, 'j1'));

    const req = httpMock.expectOne(
      `${TEST_APP_CONFIG.apiBaseUrl}/api/v1/competitions/c1/tables/t1`,
    );
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({
      name: 'Mesa 1',
      judgeIds: ['j2'],
      beerEntryIds: ['e1', 'e2'],
    });
    req.flush({ ...table, judges: [table.judges[1]], bosFlaggedEntryIds: [] });

    expect((await result).judges.map((j) => j.id)).toEqual(['j2']);
  });
});
