import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { APP_CONFIG } from '../config/app-config.model';
import { TEST_APP_CONFIG } from '../config/app-config.testing';
import { EntriesApiService } from './entries-api.service';
import type { EntryListItem } from './entries-api.service';

describe('EntriesApiService', () => {
  let service: EntriesApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: APP_CONFIG, useValue: TEST_APP_CONFIG },
      ],
    });
    service = TestBed.inject(EntriesApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getEntries() gets the competition entries', async () => {
    const entries: EntryListItem[] = [
      {
        id: 'e2',
        blindCode: 'CD34',
        styleCode: '21A',
        styleName: 'American IPA',
        abvPercent: 6.8,
        abvLow: 6,
        abvHigh: 7.5,
        beerName: 'Hazy Dream',
        notValidForBos: false,
        competitionCategoryName: null,
        bjcpCategoryNumber: null,
        bjcpCategoryName: null,
        tastingTableId: null,
        tastingTableName: null,
      },
    ];
    const result = firstValueFrom(service.getEntries('c1'));

    const req = httpMock.expectOne(`${TEST_APP_CONFIG.apiBaseUrl}/api/v1/competitions/c1/entries`);
    expect(req.request.method).toBe('GET');
    req.flush(entries);

    expect(await result).toEqual(entries);
  });
});
