import type {
  ActivatedRouteSnapshot,
  CanActivateFn,
  RouterStateSnapshot,
  UrlTree,
} from '@angular/router';
import type { AuthGuardData } from 'keycloak-angular';
import { createAuthGuard } from 'keycloak-angular';

import { resolveRoleLandingUrlTree } from './role-landing';

// Post-login landing for '/': redirects to the caller's role-specific workspace when recognized,
// otherwise falls through to true so '' renders WelcomeComponent — the unauthenticated landing
// (onLoad: 'check-sso' doesn't force login), or the no-access fallback for an authenticated caller
// with neither role (shouldn't happen given the backend's deny-by-default policy).
export async function isHomeRedirectAllowed(
  _route: ActivatedRouteSnapshot,
  _state: RouterStateSnapshot,
  authData: AuthGuardData,
): Promise<boolean | UrlTree> {
  return resolveRoleLandingUrlTree(authData) ?? true;
}

export const homeRedirectGuard: CanActivateFn = createAuthGuard(isHomeRedirectAllowed);
