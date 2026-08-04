import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { environment } from '../../../environments/environment';
import { JudgeImportApiService } from './judge-import-api.service';
import type {
  EditJudgeImportRowRequest,
  JudgeImportBatch,
  JudgeImportRow,
} from './judge-import-api.service';

describe('JudgeImportApiService', () => {
  let service: JudgeImportApiService;
  let httpMock: HttpTestingController;

  const batch: JudgeImportBatch = {
    importId: 'i1',
    rows: [
      {
        rowNumber: 1,
        status: 'Valid',
        data: {
          name: 'Ana García Ruiz',
          email: 'rebeca@example.com',
          bjcpRank: 'Certificado',
          bjcpId: 'E4612',
          preferredCategory: 'Estilos Clásicos',
          preferences: null,
        },
        error: null,
      },
    ],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(JudgeImportApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('upload() posts a multipart FormData with the file under the "file" field', async () => {
    const file = new File(['data'], 'roster.xlsx');
    const result = firstValueFrom(service.upload('c1', file));

    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/api/v1/competitions/c1/judge-imports`,
    );
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeInstanceOf(FormData);
    expect((req.request.body as FormData).get('file')).toBe(file);
    req.flush(batch);

    expect(await result).toEqual(batch);
  });

  it('getImport() gets the current row states', async () => {
    const result = firstValueFrom(service.getImport('c1', 'i1'));

    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/api/v1/competitions/c1/judge-imports/i1`,
    );
    expect(req.request.method).toBe('GET');
    req.flush(batch);

    expect(await result).toEqual(batch);
  });

  it('editRow() puts the full-replace row body', async () => {
    const body: EditJudgeImportRowRequest = {
      name: 'Ana García Ruiz',
      email: 'rebeca@example.com',
      bjcpRank: 'Certificado',
      bjcpId: 'E4612',
      preferredCategory: 'Estilos Clásicos',
      preferences: null,
    };
    const updatedRow: JudgeImportRow = { ...batch.rows[0] };
    const result = firstValueFrom(service.editRow('c1', 'i1', 1, body));

    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/api/v1/competitions/c1/judge-imports/i1/rows/1`,
    );
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(body);
    req.flush(updatedRow);

    expect(await result).toEqual(updatedRow);
  });

  it('excludeRow() posts to the exclude action with no body', async () => {
    const updatedRow: JudgeImportRow = { ...batch.rows[0], status: 'Excluded' };
    const result = firstValueFrom(service.excludeRow('c1', 'i1', 1));

    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/api/v1/competitions/c1/judge-imports/i1/rows/1/exclude`,
    );
    expect(req.request.method).toBe('POST');
    req.flush(updatedRow);

    expect(await result).toEqual(updatedRow);
  });

  it('consolidate() posts to the consolidate endpoint', async () => {
    const consolidateResult = {
      created: [{ id: 'j1', email: 'rebeca@example.com' }],
      updated: [],
      excluded: 0,
      skipped: [],
    };
    const result = firstValueFrom(service.consolidate('c1', 'i1'));

    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/api/v1/competitions/c1/judge-imports/i1/consolidate`,
    );
    expect(req.request.method).toBe('POST');
    req.flush(consolidateResult);

    expect(await result).toEqual(consolidateResult);
  });
});
