import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { makeEnvironmentProviders } from '@angular/core';
import type { EnvironmentProviders } from '@angular/core';
import {
  createInterceptorCondition,
  includeBearerTokenInterceptor,
  INCLUDE_BEARER_TOKEN_INTERCEPTOR_CONFIG,
} from 'keycloak-angular';
import type { IncludeBearerTokenCondition } from 'keycloak-angular';

import { environment } from '../../../environments/environment';

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

// Explicit URL-pattern scoping (keycloak-angular's own security note on
// includeBearerTokenInterceptor): the access token must never leak to a third-party origin.
export function provideAuthBearerInterceptor(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideHttpClient(withInterceptors([includeBearerTokenInterceptor])),
    {
      provide: INCLUDE_BEARER_TOKEN_INTERCEPTOR_CONFIG,
      useValue: [
        createInterceptorCondition<IncludeBearerTokenCondition>({
          urlPattern: new RegExp(`^${escapeRegExp(environment.apiBaseUrl)}(/.*)?$`),
        }),
      ],
    },
  ]);
}
