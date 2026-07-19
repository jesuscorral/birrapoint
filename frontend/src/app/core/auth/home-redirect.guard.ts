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
// otherwise falls through to true so '' renders AuthPlaceholderComponent (no-access fallback).
export async function isHomeRedirectAllowed(
  _route: ActivatedRouteSnapshot,
  _state: RouterStateSnapshot,
  authData: AuthGuardData,
): Promise<boolean | UrlTree> {
  return resolveRoleLandingUrlTree(authData) ?? true;
}

export const homeRedirectGuard: CanActivateFn = createAuthGuard(isHomeRedirectAllowed);
