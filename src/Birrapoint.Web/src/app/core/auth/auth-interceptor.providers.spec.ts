import { TestBed } from '@angular/core/testing';
import { INCLUDE_BEARER_TOKEN_INTERCEPTOR_CONFIG } from 'keycloak-angular';

import { TEST_APP_CONFIG } from '../config/app-config.testing';
import { buildBearerUrlPattern, provideAuthBearerInterceptor } from './auth-interceptor.providers';

describe('buildBearerUrlPattern', () => {
  it('matches API requests built against an absolute base URL', () => {
    const pattern = buildBearerUrlPattern('http://localhost:5121');

    expect(pattern.test('http://localhost:5121/api/v1/styles')).toBe(true);
    expect(pattern.test('http://localhost:5121/api')).toBe(true);
  });

  it('never matches a third-party origin', () => {
    const pattern = buildBearerUrlPattern('http://localhost:5121');

    expect(pattern.test('https://evil.example.com/steal')).toBe(false);
  });

  it('never matches a look-alike prefix of the API base URL', () => {
    const pattern = buildBearerUrlPattern('http://localhost:5121');

    expect(pattern.test('http://localhost:5121.evil.com/api/steal')).toBe(false);
    expect(pattern.test('http://localhost:5121@evil.com/api/steal')).toBe(false);
    expect(pattern.test('http://localhost:51210/api/steal')).toBe(false);
  });

  it('never matches a non-API path on the same origin', () => {
    const pattern = buildBearerUrlPattern('http://localhost:5121');

    expect(pattern.test('http://localhost:5121/hubs/competition')).toBe(false);
    expect(pattern.test('http://localhost:5121/health')).toBe(false);
  });

  // Same-origin production deployment (FR-043): apiBaseUrl is '' and every request is relative.
  describe('with an empty (same-origin) apiBaseUrl', () => {
    it('matches relative /api/... requests', () => {
      const pattern = buildBearerUrlPattern('');

      expect(pattern.test('/api/v1/styles')).toBe(true);
      expect(pattern.test('/api')).toBe(true);
    });

    it("never matches Keycloak's own absolute URL", () => {
      const pattern = buildBearerUrlPattern('');

      expect(
        pattern.test('http://localhost:8081/realms/birrapoint/protocol/openid-connect/token'),
      ).toBe(false);
    });

    it('never matches an unrelated relative path (hub negotiate, static assets)', () => {
      const pattern = buildBearerUrlPattern('');

      expect(pattern.test('/hubs/competition')).toBe(false);
      expect(pattern.test('/manifest.webmanifest')).toBe(false);
    });

    it('never matches a protocol-relative look-alike', () => {
      const pattern = buildBearerUrlPattern('');

      expect(pattern.test('//evil.com/api/steal')).toBe(false);
    });
  });
});

describe('provideAuthBearerInterceptor', () => {
  it('wires the built pattern into INCLUDE_BEARER_TOKEN_INTERCEPTOR_CONFIG', () => {
    TestBed.configureTestingModule({ providers: [provideAuthBearerInterceptor(TEST_APP_CONFIG)] });

    const [condition] = TestBed.inject(INCLUDE_BEARER_TOKEN_INTERCEPTOR_CONFIG);

    expect(condition.urlPattern.test(`${TEST_APP_CONFIG.apiBaseUrl}/api/v1/styles`)).toBe(true);
    expect(condition.urlPattern.test('https://evil.example.com/steal')).toBe(false);
  });
});
