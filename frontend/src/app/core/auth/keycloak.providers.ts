import type { EnvironmentProviders } from '@angular/core';
import {
  AutoRefreshTokenService,
  provideKeycloak,
  UserActivityService,
  withAutoRefreshToken,
} from 'keycloak-angular';
import type { KeycloakConfig, KeycloakInitOptions } from 'keycloak-js';

import { environment } from '../../../environments/environment';

export const keycloakConfig: KeycloakConfig = environment.keycloak;

// `login-required` blocks the whole app pre-render until authenticated (FR-001) — there is no
// public/anonymous section of this PWA, so no extra route-level auth guard is needed on top of
// this. `pkceMethod: 'S256'` is required client-side even though the realm client already
// mandates it (infra/keycloak/birrapoint-realm.json), per R-11.
export const keycloakInitOptions: KeycloakInitOptions = {
  onLoad: 'login-required',
  pkceMethod: 'S256',
};

export function provideAppKeycloak(): EnvironmentProviders {
  return provideKeycloak({
    config: keycloakConfig,
    initOptions: keycloakInitOptions,
    // Silent token refresh (R-11) driven by user activity, logs out on prolonged inactivity.
    features: [withAutoRefreshToken()],
    // keycloak-angular 20.1.0 doesn't mark these `providedIn: 'root'`, so withAutoRefreshToken's
    // `inject(AutoRefreshTokenService)` fails without them registered explicitly here.
    providers: [AutoRefreshTokenService, UserActivityService],
  });
}
