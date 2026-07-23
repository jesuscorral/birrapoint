import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { environment } from '../../../environments/environment';
import { ApiError } from './api-error';
import { ApiClient } from './api-client.service';

describe('ApiClient', () => {
  let client: ApiClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    client = TestBed.inject(ApiClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('builds the request against apiBaseUrl + /api/v1', async () => {
    const result = firstValueFrom(client.get<{ code: string }[]>('/styles'));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/styles`);
    expect(req.request.method).toBe('GET');
    req.flush([{ code: '21A' }]);

    expect(await result).toEqual([{ code: '21A' }]);
  });

  it('sends a POST body and any extra headers through untouched', async () => {
    const result = firstValueFrom(client.post('/competitions/1/tables', { name: 'Table 1' }));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/competitions/1/tables`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Table 1' });
    req.flush({ id: 't1' });

    expect(await result).toEqual({ id: 't1' });
  });

  it('rejects with an ApiError built from the ProblemDetails response body', async () => {
    const result = firstValueFrom(client.get('/competitions/missing'));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/competitions/missing`);
    req.flush(
      { type: 'urn:birrapoint:validation', title: 'Validation failed', errors: { id: ['bad'] } },
      { status: 400, statusText: 'Bad Request' },
    );

    await expect(result).rejects.toBeInstanceOf(ApiError);
    const error = (await result.catch((e: ApiError) => e)) as ApiError;
    expect(error.status).toBe(400);
    expect(error.urn).toBe('urn:birrapoint:validation');
  });

  it('rejects with a generic ApiError on a network failure (status 0, no body)', async () => {
    const result = firstValueFrom(client.get('/competitions/missing'));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/competitions/missing`);
    req.error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });

    const error = (await result.catch((e: HttpErrorResponse | ApiError) => e)) as ApiError;
    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(0);
    expect(error.urn).toBeNull();
  });

  describe('getBlob()', () => {
    it('resolves the full HttpResponse with a 200 blob body', async () => {
      const result = firstValueFrom(client.getBlob('/competitions/c1/results/archive'));

      const req = httpMock.expectOne(
        `${environment.apiBaseUrl}/api/v1/competitions/c1/results/archive`,
      );
      expect(req.request.method).toBe('GET');
      expect(req.request.responseType).toBe('blob');
      req.flush(new Blob(['zip-bytes'], { type: 'application/zip' }));

      const response = await result;
      expect(response.status).toBe(200);
      expect(response.body).toBeInstanceOf(Blob);
    });

    it('resolves the full HttpResponse for a non-200 status (e.g. 202) without throwing', async () => {
      const result = firstValueFrom(client.getBlob('/competitions/c1/results/archive'));

      const req = httpMock.expectOne(
        `${environment.apiBaseUrl}/api/v1/competitions/c1/results/archive`,
      );
      req.flush(new Blob([JSON.stringify({ status: 'Running' })], { type: 'application/json' }), {
        status: 202,
        statusText: 'Accepted',
      });

      const response = await result;
      expect(response.status).toBe(202);
      expect(response.body).toBeInstanceOf(Blob);
    });

    it('rejects with an ApiError parsed from the blob-wrapped ProblemDetails body on a 4xx/5xx', async () => {
      const result = firstValueFrom(client.getBlob('/competitions/c1/results/archive'));

      const req = httpMock.expectOne(
        `${environment.apiBaseUrl}/api/v1/competitions/c1/results/archive`,
      );
      req.flush(
        new Blob([JSON.stringify({ type: 'urn:birrapoint:validation', title: 'Bad request' })], {
          type: 'application/problem+json',
        }),
        { status: 400, statusText: 'Bad Request' },
      );

      const error = (await result.catch((e: unknown) => e)) as ApiError;
      expect(error).toBeInstanceOf(ApiError);
      expect(error.status).toBe(400);
      expect(error.urn).toBe('urn:birrapoint:validation');
    });
  });
});
