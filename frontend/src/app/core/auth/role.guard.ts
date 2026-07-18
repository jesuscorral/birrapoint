import { inject } from '@angular/core';
import { Router } from '@angular/router';
import type {
  ActivatedRouteSnapshot,
  CanActivateFn,
  RouterStateSnapshot,
  UrlTree,
} from '@angular/router';
import type { AuthGuardData } from 'keycloak-angular';
import { createAuthGuard } from 'keycloak-angular';

function hasRealmRole(authData: AuthGuardData, role: string): boolean {
  return authData.grantedRoles.realmRoles.includes(role);
}

// `login-required` (keycloak.providers.ts) already forces authentication before any route
// activates, so these only need to branch on role, not on `authData.authenticated`. A mismatch
// redirects to root: T024 wires that path to the caller's real landing page by role.
export async function isOrganizerAllowed(
  _route: ActivatedRouteSnapshot,
  _state: RouterStateSnapshot,
  authData: AuthGuardData,
): Promise<boolean | UrlTree> {
  return hasRealmRole(authData, 'ORGANIZER') || inject(Router).parseUrl('/');
}

export async function isJudgeAllowed(
  _route: ActivatedRouteSnapshot,
  _state: RouterStateSnapshot,
  authData: AuthGuardData,
): Promise<boolean | UrlTree> {
  return hasRealmRole(authData, 'JUDGE') || inject(Router).parseUrl('/');
}

export const organizerGuard: CanActivateFn = createAuthGuard(isOrganizerAllowed);
export const judgeGuard: CanActivateFn = createAuthGuard(isJudgeAllowed);
