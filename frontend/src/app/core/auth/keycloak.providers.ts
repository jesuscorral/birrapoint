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

// `check-sso` silently checks if there's an active session but doesn't force login.
// The public /welcome landing (Angular) is guarded por role-based redirects at the route level,
// so authenticated users land on their dashboard automatically (homeRedirectGuard).
// `pkceMethod: 'S256'` is required client-side even though the realm client already
// mandates it (infra/keycloak/birrapoint-realm.json), per R-11.
// NOTE: This change enables a public landing per design decision (Botella y cobre auth flow).
// FR-001 updated: "redirect *authenticated* users to dashboard, *unauthenticated* to welcome".
export const keycloakInitOptions: KeycloakInitOptions = {
  onLoad: 'check-sso',
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
