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

// `onLoad: 'check-sso'` (keycloak.providers.ts) doesn't force authentication, so an anonymous
// caller hitting /organizer/** or /judge/** directly falls through to root (WelcomeComponent, the
// public login/register landing) via the same resolveRoleLandingUrlTree(authData) || parseUrl('/')
// fallback used for an authenticated mismatch (e.g. a JUDGE hitting /organizer/** lands on
// /judge/tables, not just root); only an authenticated caller with neither role reaches root.
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
