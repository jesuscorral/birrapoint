import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { environment } from '../../../environments/environment';
import { DispatchApiService } from './dispatch-api.service';
import type { DispatchStatusRow } from './dispatch-api.service';

describe('DispatchApiService', () => {
  let service: DispatchApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(DispatchApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getDispatchStatus() gets the per-participant email status list', async () => {
    const rows: DispatchStatusRow[] = [
      {
        participantId: 'p1',
        email: 'a@example.com',
        status: 'Completed',
        attempts: 1,
        lastError: null,
      },
      {
        participantId: 'p2',
        email: 'b@example.com',
        status: 'Failed',
        attempts: 3,
        lastError: 'SMTP timeout',
      },
    ];
    const result = firstValueFrom(service.getDispatchStatus('c1'));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/competitions/c1/dispatch`);
    expect(req.request.method).toBe('GET');
    req.flush(rows);

    expect(await result).toEqual(rows);
  });

  it('retryDispatch() posts the participant ids to re-queue', async () => {
    const result = firstValueFrom(service.retryDispatch('c1', ['p2']));

    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/api/v1/competitions/c1/dispatch/retries`,
    );
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ participantIds: ['p2'] });
    req.flush(null);

    await result;
  });

  describe('downloadResultsArchive()', () => {
    it('resolves ready: true with the ZIP blob on a 200', async () => {
      const result = firstValueFrom(service.downloadResultsArchive('c1'));

      const req = httpMock.expectOne(
        `${environment.apiBaseUrl}/api/v1/competitions/c1/results/archive`,
      );
      expect(req.request.method).toBe('GET');
      req.flush(new Blob(['zip-bytes'], { type: 'application/zip' }));

      const outcome = await result;
      expect(outcome.ready).toBe(true);
      if (outcome.ready) {
        expect(outcome.blob).toBeInstanceOf(Blob);
      }
    });

    it('resolves ready: false with the in-progress status on a 202', async () => {
      const result = firstValueFrom(service.downloadResultsArchive('c1'));

      const req = httpMock.expectOne(
        `${environment.apiBaseUrl}/api/v1/competitions/c1/results/archive`,
      );
      req.flush(new Blob([JSON.stringify({ status: 'Running' })], { type: 'application/json' }), {
        status: 202,
        statusText: 'Accepted',
      });

      const outcome = await result;
      expect(outcome).toEqual({ ready: false, status: 'Running' });
    });

    it('falls back to "Unknown" if the 202 body is not parseable JSON', async () => {
      const result = firstValueFrom(service.downloadResultsArchive('c1'));

      const req = httpMock.expectOne(
        `${environment.apiBaseUrl}/api/v1/competitions/c1/results/archive`,
      );
      req.flush(new Blob(['not json'], { type: 'text/plain' }), {
        status: 202,
        statusText: 'Accepted',
      });

      const outcome = await result;
      expect(outcome).toEqual({ ready: false, status: 'Unknown' });
    });
  });
});
