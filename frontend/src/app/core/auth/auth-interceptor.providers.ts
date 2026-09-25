import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { makeEnvironmentProviders } from '@angular/core';
import type { EnvironmentProviders } from '@angular/core';
import {
  createInterceptorCondition,
  includeBearerTokenInterceptor,
  INCLUDE_BEARER_TOKEN_INTERCEPTOR_CONFIG,
} from 'keycloak-angular';
import type { IncludeBearerTokenCondition } from 'keycloak-angular';

import type { AppConfig } from '../config/app-config.model';

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

// Scoped to `${apiBaseUrl}/api` — exactly the prefix ApiClient builds every request against
// (api-client.service.ts) — rather than the whole origin, so a same-origin apiBaseUrl of ''
// (production, FR-043) still only matches relative /api/... requests and not, say, the SignalR
// hub negotiate call or a static asset also served from that origin.
export function buildBearerUrlPattern(apiBaseUrl: string): RegExp {
  return new RegExp(`^${escapeRegExp(apiBaseUrl)}/api(/.*)?$`);
}

// Explicit URL-pattern scoping (keycloak-angular's own security note on
// includeBearerTokenInterceptor): the access token must never leak to a third-party origin.
export function provideAuthBearerInterceptor(config: AppConfig): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideHttpClient(withInterceptors([includeBearerTokenInterceptor])),
    {
      provide: INCLUDE_BEARER_TOKEN_INTERCEPTOR_CONFIG,
      useValue: [
        createInterceptorCondition<IncludeBearerTokenCondition>({
          urlPattern: buildBearerUrlPattern(config.apiBaseUrl),
        }),
      ],
    },
  ]);
}
