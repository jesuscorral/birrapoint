import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { environment } from '../../../environments/environment';
import { CatalogApiService } from './catalog-api.service';
import type { StyleDetail } from './catalog-api.service';

describe('CatalogApiService', () => {
  let service: CatalogApiService;
  let httpMock: HttpTestingController;

  const detail: StyleDetail = {
    code: '21A',
    name: 'American IPA',
    categoryNumber: '21',
    categoryName: 'IPA',
    vitalStatistics: {
      ogLow: 1.056,
      ogHigh: 1.07,
      fgLow: 1.008,
      fgHigh: 1.014,
      ibuLow: 40,
      ibuHigh: 70,
      srmLow: 6,
      srmHigh: 14,
      abvLow: 5.5,
      abvHigh: 7.5,
    },
    description: {
      overallImpression: 'A decidedly hoppy and bitter, moderately strong American pale ale.',
      aroma: 'Hop aroma is medium to strong intensity.',
      appearance: 'Color ranges from medium gold to light reddish amber.',
      flavor: 'Similar to aroma; a hop-forward beer.',
      mouthfeel: 'Medium-light to medium body.',
      comments: 'Hop character should be evident.',
      history: 'An American development of the historical English style.',
      characteristicIngredients: 'Pale ale malt, American or New World hops.',
      styleComparison: 'Bigger, hoppier, and stronger than an American Pale Ale.',
      entryInstructions: null,
      commercialExamples: ['Russian River Blind Pig IPA'],
      tags: ['strong', 'hoppy'],
    },
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CatalogApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getStyleDetail() gets the full BJCP style detail by code', async () => {
    const result = firstValueFrom(service.getStyleDetail('21A'));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/styles/21A`);
    expect(req.request.method).toBe('GET');
    req.flush(detail);

    expect(await result).toEqual(detail);
  });

  it('caches a fetched style detail in memory and does not refetch it on a later call', async () => {
    const firstResult = firstValueFrom(service.getStyleDetail('21A'));
    httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/styles/21A`).flush(detail);
    expect(await firstResult).toEqual(detail);

    const secondResult = firstValueFrom(service.getStyleDetail('21A'));
    httpMock.expectNone(`${environment.apiBaseUrl}/api/v1/styles/21A`);
    expect(await secondResult).toEqual(detail);
  });

  it('fetches a different code independently of a cached one', async () => {
    const firstResult = firstValueFrom(service.getStyleDetail('21A'));
    httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/styles/21A`).flush(detail);
    expect(await firstResult).toEqual(detail);

    const otherDetail = { ...detail, code: '4A', name: 'Munich Helles' };
    const otherResult = firstValueFrom(service.getStyleDetail('4A'));
    httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/styles/4A`).flush(otherDetail);

    expect((await otherResult).code).toBe('4A');
  });
});
