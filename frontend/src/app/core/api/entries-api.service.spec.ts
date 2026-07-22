import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { environment } from '../../../environments/environment';
import { EntriesApiService } from './entries-api.service';
import type { EntryListItem } from './entries-api.service';

describe('EntriesApiService', () => {
  let service: EntriesApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
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
        abvLow: 6,
        abvHigh: 7.5,
        beerName: 'Hazy Dream',
        notValidForBos: false,
        tastingTableId: null,
        tastingTableName: null,
      },
    ];
    const result = firstValueFrom(service.getEntries('c1'));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/competitions/c1/entries`);
    expect(req.request.method).toBe('GET');
    req.flush(entries);

    expect(await result).toEqual(entries);
  });
});
