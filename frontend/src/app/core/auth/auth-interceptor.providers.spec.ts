import { TestBed } from '@angular/core/testing';
import { INCLUDE_BEARER_TOKEN_INTERCEPTOR_CONFIG } from 'keycloak-angular';

import { environment } from '../../../environments/environment';
import { provideAuthBearerInterceptor } from './auth-interceptor.providers';

describe('provideAuthBearerInterceptor', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideAuthBearerInterceptor()] });
  });

  it('scopes the bearer token to the API base URL', () => {
    const [condition] = TestBed.inject(INCLUDE_BEARER_TOKEN_INTERCEPTOR_CONFIG);

    expect(condition.urlPattern.test(`${environment.apiBaseUrl}/api/v1/styles`)).toBe(true);
    expect(condition.urlPattern.test(environment.apiBaseUrl)).toBe(true);
  });

  it('never matches a third-party origin', () => {
    const [condition] = TestBed.inject(INCLUDE_BEARER_TOKEN_INTERCEPTOR_CONFIG);

    expect(condition.urlPattern.test('https://evil.example.com/steal')).toBe(false);
  });
});
