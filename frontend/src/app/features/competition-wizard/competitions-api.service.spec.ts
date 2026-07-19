import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { environment } from '../../../environments/environment';
import { CompetitionsApiService } from './competitions-api.service';
import type { CompetitionDetail, CompetitionPayload } from './competitions-api.service';

describe('CompetitionsApiService', () => {
  let service: CompetitionsApiService;
  let httpMock: HttpTestingController;

  const detail: CompetitionDetail = {
    id: 'c1',
    name: 'Golden Ale Cup',
    venue: 'Town Hall',
    startDate: '2026-08-01',
    endDate: '2026-08-02',
    description: null,
    logoUrl: null,
    entryLimit: null,
    registrationStart: null,
    registrationEnd: null,
    state: 'Draft',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CompetitionsApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('create() posts basics to /competitions', async () => {
    const payload: CompetitionPayload = {
      name: 'Golden Ale Cup',
      venue: 'Town Hall',
      startDate: '2026-08-01',
      endDate: '2026-08-02',
    };
    const result = firstValueFrom(service.create(payload));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/competitions`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    req.flush(detail);

    expect(await result).toEqual(detail);
  });

  it('update() puts the full payload to /competitions/{id}', async () => {
    const payload: CompetitionPayload = {
      name: 'Golden Ale Cup',
      venue: 'Town Hall',
      startDate: '2026-08-01',
      endDate: '2026-08-02',
      description: 'A friendly local competition',
    };
    const result = firstValueFrom(service.update('c1', payload));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/competitions/c1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(payload);
    req.flush({ ...detail, description: payload.description ?? null });

    expect(await result).toEqual({ ...detail, description: payload.description });
  });

  it('getById() gets /competitions/{id}', async () => {
    const result = firstValueFrom(service.getById('c1'));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/competitions/c1`);
    expect(req.request.method).toBe('GET');
    req.flush(detail);

    expect(await result).toEqual(detail);
  });
});
