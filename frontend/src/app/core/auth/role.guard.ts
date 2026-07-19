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

import { resolveRoleLandingUrlTree } from './role-landing';

function hasRealmRole(authData: AuthGuardData, role: string): boolean {
  return authData.grantedRoles.realmRoles.includes(role);
}

// `login-required` (keycloak.providers.ts) already forces authentication before any route
// activates, so these only need to branch on role, not on `authData.authenticated`. A mismatch
// redirects to the caller's own role landing (e.g. a JUDGE hitting /organizer/** lands on
// /judge/tables, not just root) via resolveRoleLandingUrlTree; only a caller with neither role
// falls back to root.
export async function isOrganizerAllowed(
  _route: ActivatedRouteSnapshot,
  _state: RouterStateSnapshot,
  authData: AuthGuardData,
): Promise<boolean | UrlTree> {
  return (
    hasRealmRole(authData, 'ORGANIZER') ||
    resolveRoleLandingUrlTree(authData) ||
    inject(Router).parseUrl('/')
  );
}

export async function isJudgeAllowed(
  _route: ActivatedRouteSnapshot,
  _state: RouterStateSnapshot,
  authData: AuthGuardData,
): Promise<boolean | UrlTree> {
  return (
    hasRealmRole(authData, 'JUDGE') ||
    resolveRoleLandingUrlTree(authData) ||
    inject(Router).parseUrl('/')
  );
}

export const organizerGuard: CanActivateFn = createAuthGuard(isOrganizerAllowed);
export const judgeGuard: CanActivateFn = createAuthGuard(isJudgeAllowed);
