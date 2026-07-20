import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { environment } from '../../../environments/environment';
import { JudgeManagementApiService } from './judge-management-api.service';
import type { JudgeProfile, RegisterJudgesResult } from './judge-management-api.service';

describe('JudgeManagementApiService', () => {
  let service: JudgeManagementApiService;
  let httpMock: HttpTestingController;

  const judges: JudgeProfile[] = [
    {
      id: 'j1',
      email: 'ada@example.com',
      displayName: 'Ada Lovelace',
      invitationStatus: 'Sent',
      attempts: 1,
      lastError: null,
      sentAt: '2026-07-20T10:00:00Z',
    },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(JudgeManagementApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('registerJudges() posts the email list', async () => {
    const registerResult: RegisterJudgesResult = {
      created: [{ id: 'j1', email: 'ada@example.com' }],
      skipped: [{ email: 'grace@example.com', reason: 'already-registered' }],
    };
    const result = firstValueFrom(
      service.registerJudges('c1', ['ada@example.com', 'grace@example.com']),
    );

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/competitions/c1/judges`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ emails: ['ada@example.com', 'grace@example.com'] });
    req.flush(registerResult);

    expect(await result).toEqual(registerResult);
  });

  it('getJudges() gets the delivery status list', async () => {
    const result = firstValueFrom(service.getJudges('c1'));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/competitions/c1/judges`);
    expect(req.request.method).toBe('GET');
    req.flush(judges);

    expect(await result).toEqual(judges);
  });

  it('updateJudgeEmail() puts the corrected email', async () => {
    const updated: JudgeProfile = { ...judges[0], email: 'ada2@example.com' };
    const result = firstValueFrom(service.updateJudgeEmail('c1', 'j1', 'ada2@example.com'));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/v1/competitions/c1/judges/j1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ email: 'ada2@example.com' });
    req.flush(updated);

    expect(await result).toEqual(updated);
  });

  it('resendInvitation() posts to the invitation endpoint', async () => {
    const result = firstValueFrom(service.resendInvitation('c1', 'j1'));

    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/api/v1/competitions/c1/judges/j1/invitation`,
    );
    expect(req.request.method).toBe('POST');
    req.flush({ status: 'Pending' });

    expect(await result).toEqual({ status: 'Pending' });
  });
});
