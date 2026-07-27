import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { environment } from '../../../environments/environment';
import { TablesApiService } from './tables-api.service';
import type { TableSummary } from './tables-api.service';

describe('TablesApiService', () => {
  let service: TablesApiService;
  let httpMock: HttpTestingController;

  const table: TableSummary = {
    id: 't1',
    name: 'Mesa 1',
    judges: [{ id: 'j1', email: 'ada@example.com', displayName: 'Ada Lovelace' }],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(TablesApiService);
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

  it('removeJudge() deletes the judge from the table', async () => {
    const result = firstValueFrom(service.removeJudge('c1', 't1', 'j1'));

    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/api/v1/competitions/c1/tables/t1/judges/j1`,
    );
    expect(req.request.method).toBe('DELETE');
    req.flush({ tableId: 't1', judgeId: 'j1' });

    expect(await result).toEqual({ tableId: 't1', judgeId: 'j1' });
  });
});
