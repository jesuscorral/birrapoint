import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { environment } from '../../../environments/environment';
import { ImportApiService } from './import-api.service';
import type { EditImportRowRequest, ImportBatch, ImportRow } from './import-api.service';

describe('ImportApiService', () => {
  let service: ImportApiService;
  let httpMock: HttpTestingController;

  const batch: ImportBatch = {
    importId: 'i1',
    rows: [
      {
        rowNumber: 1,
        status: 'Valid',
        data: {
          participantName: 'José Deza Prieto',
          participantEmail: 'dezaprieto@gmail.com',
          acceMemberNumber: '1423',
          dateOfBirth: null,
          phone: '699989612',
          category: 'Estilos clásicos',
          competitionCategoryId: 'cat-1',
          style: '21C. Hazy IPA',
          resolvedStyleCode: '21C',
          submittedAt: '2025-09-01T09:21:16Z',
          abvPercent: 7.6,
          brewDate: '2025-08-12',
          bottlingDate: '2025-08-28',
          malts: 'Pale Ale, Trigo',
          hops: 'Citra, Mosaic',
          yeast: 'White Lab WL-001-P',
          otherIngredients: null,
          entryInstructions: null,
          beerName: null,
        },
        error: null,
      },
    ],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ImportApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('upload() posts a multipart FormData with the file under the "file" field', async () => {
    const file = new File(['data'], 'entries.xlsx');
    const result = firstValueFrom(service.upload('c1', file));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/competitions/c1/imports`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeInstanceOf(FormData);
    expect((req.request.body as FormData).get('file')).toBe(file);
    req.flush(batch);

    expect(await result).toEqual(batch);
  });

  it('getImport() gets the current row states', async () => {
    const result = firstValueFrom(service.getImport('c1', 'i1'));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/competitions/c1/imports/i1`);
    expect(req.request.method).toBe('GET');
    req.flush(batch);

    expect(await result).toEqual(batch);
  });

  it('editRow() puts the full-replace row body', async () => {
    const body: EditImportRowRequest = {
      participantName: 'José Deza Prieto',
      participantEmail: 'dezaprieto@gmail.com',
      acceMemberNumber: '1423',
      dateOfBirth: null,
      phone: '699989612',
      competitionCategoryId: 'cat-1',
      styleCode: '21C',
      submittedAt: '2025-09-01T09:21:16Z',
      abvPercent: 7.6,
      brewDate: '2025-08-12',
      bottlingDate: '2025-08-28',
      malts: 'Pale Ale, Trigo',
      hops: 'Citra, Mosaic',
      yeast: 'White Lab WL-001-P',
      otherIngredients: null,
      entryInstructions: null,
      beerName: null,
    };
    const updatedRow: ImportRow = { ...batch.rows[0] };
    const result = firstValueFrom(service.editRow('c1', 'i1', 1, body));

    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/api/v1/competitions/c1/imports/i1/rows/1`,
    );
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(body);
    req.flush(updatedRow);

    expect(await result).toEqual(updatedRow);
  });

  it('excludeRow() posts to the exclude action with no body', async () => {
    const updatedRow: ImportRow = { ...batch.rows[0], status: 'Excluded' };
    const result = firstValueFrom(service.excludeRow('c1', 'i1', 1));

    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/api/v1/competitions/c1/imports/i1/rows/1/exclude`,
    );
    expect(req.request.method).toBe('POST');
    req.flush(updatedRow);

    expect(await result).toEqual(updatedRow);
  });

  it('consolidate() posts to the consolidate endpoint', async () => {
    const consolidateResult = {
      imported: 1,
      excluded: 0,
      entries: [{ id: 'e1', blindCode: 'AB12', styleCode: '21C' }],
    };
    const result = firstValueFrom(service.consolidate('c1', 'i1'));

    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/api/v1/competitions/c1/imports/i1/consolidate`,
    );
    expect(req.request.method).toBe('POST');
    req.flush(consolidateResult);

    expect(await result).toEqual(consolidateResult);
  });

  it('revalidate() posts to the revalidate endpoint with no body', async () => {
    const result = firstValueFrom(service.revalidate('c1', 'i1'));

    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/api/v1/competitions/c1/imports/i1/revalidate`,
    );
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({});
    req.flush(batch);

    expect(await result).toEqual(batch);
  });
});
