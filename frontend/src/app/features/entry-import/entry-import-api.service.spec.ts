import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { environment } from '../../../environments/environment';
import { EntryImportApiService } from './entry-import-api.service';
import type { ImportBatch, ImportRow, StyleSummary } from './entry-import-api.service';

describe('EntryImportApiService', () => {
  let service: EntryImportApiService;
  let httpMock: HttpTestingController;

  const batch: ImportBatch = {
    importId: 'i1',
    rows: [
      {
        rowNumber: 1,
        status: 'Valid',
        data: {
          participantName: 'Ada Lovelace',
          participantEmail: 'ada@example.com',
          beerName: 'Golden Helles',
          style: '4A',
          collaborators: [],
          resolvedStyleCode: '4A',
        },
        error: null,
      },
    ],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(EntryImportApiService);
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

  it('resolveRow() puts an assign-style action with the style code', async () => {
    const updatedRow: ImportRow = { ...batch.rows[0], status: 'Valid' };
    const result = firstValueFrom(service.resolveRow('c1', 'i1', 2, 'assign-style', '4A'));

    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/api/v1/competitions/c1/imports/i1/rows/2`,
    );
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ action: 'assign-style', styleCode: '4A' });
    req.flush(updatedRow);

    expect(await result).toEqual(updatedRow);
  });

  it('resolveRow() puts an exclude action without a style code', async () => {
    const updatedRow: ImportRow = { ...batch.rows[0], status: 'Excluded' };
    const result = firstValueFrom(service.resolveRow('c1', 'i1', 3, 'exclude'));

    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/api/v1/competitions/c1/imports/i1/rows/3`,
    );
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ action: 'exclude', styleCode: undefined });
    req.flush(updatedRow);

    expect(await result).toEqual(updatedRow);
  });

  it('consolidate() posts to the consolidate endpoint', async () => {
    const consolidateResult = {
      imported: 1,
      excluded: 0,
      entries: [{ id: 'e1', blindCode: 'AB12', styleCode: '4A' }],
    };
    const result = firstValueFrom(service.consolidate('c1', 'i1'));

    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/api/v1/competitions/c1/imports/i1/consolidate`,
    );
    expect(req.request.method).toBe('POST');
    req.flush(consolidateResult);

    expect(await result).toEqual(consolidateResult);
  });

  it('getStyles() gets the BJCP catalog', async () => {
    const styles: StyleSummary[] = [
      {
        code: '4A',
        name: 'Munich Helles',
        categoryNumber: '4',
        categoryName: 'Pale Malty European Lager',
      },
    ];
    const result = firstValueFrom(service.getStyles());

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/styles`);
    expect(req.request.method).toBe('GET');
    req.flush(styles);

    expect(await result).toEqual(styles);
  });
});
