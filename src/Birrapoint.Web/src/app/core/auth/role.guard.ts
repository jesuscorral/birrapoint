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

import { isActiveRole, resolveRoleLandingUrlTree } from './role-landing';

// `onLoad: 'check-sso'` (keycloak.providers.ts) doesn't force authentication, so an anonymous
// caller hitting /organizer/** or /judge/** directly falls through to root (WelcomeComponent, the
// public login/register landing) via the same resolveRoleLandingUrlTree(authData) || parseUrl('/')
// fallback used for an authenticated mismatch (e.g. a JUDGE hitting /organizer/** lands on
// /judge/tables, not just root); only an authenticated caller with neither role reaches root.
// isActiveRole (not a plain realm-role check) so a dual-role caller who picked JUDGE via
// /select-role can't bypass that choice by navigating straight to an /organizer/** URL, and vice
// versa — resolveRoleLandingUrlTree's fallback then sends them to their actual active workspace
// (or /select-role, if they haven't picked one yet this session).
export async function isOrganizerAllowed(
  _route: ActivatedRouteSnapshot,
  _state: RouterStateSnapshot,
  authData: AuthGuardData,
): Promise<boolean | UrlTree> {
  return (
    isActiveRole(authData, 'ORGANIZER') ||
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
    isActiveRole(authData, 'JUDGE') ||
    resolveRoleLandingUrlTree(authData) ||
    inject(Router).parseUrl('/')
  );
}

// /select-role only makes sense for a caller who actually holds both realm roles — anyone else
// (a bookmarked/shared link, browser Back after logout landing back on it, a single-role caller
// typing the URL) is bounced via the same resolveRoleLandingUrlTree(authData) || parseUrl('/')
// fallback the other two guards use. No redirect loop: resolveRoleLandingUrlTree only returns
// /select-role from its own dual-role branch, which this guard already short-circuits with `true`
// before ever reaching it.
export async function isRoleSelectAllowed(
  _route: ActivatedRouteSnapshot,
  _state: RouterStateSnapshot,
  authData: AuthGuardData,
): Promise<boolean | UrlTree> {
  const { realmRoles } = authData.grantedRoles;
  if (realmRoles.includes('ORGANIZER') && realmRoles.includes('JUDGE')) {
    return true;
  }
  return resolveRoleLandingUrlTree(authData) ?? inject(Router).parseUrl('/');
}

// /settings is role-agnostic identity info (Keycloak profile + realm roles), reachable by either
// role — an organizer or a judge, whichever this account holds (or both). Only an anonymous or
// no-role caller is bounced, to the public landing.
export async function isSettingsAllowed(
  _route: ActivatedRouteSnapshot,
  _state: RouterStateSnapshot,
  authData: AuthGuardData,
): Promise<boolean | UrlTree> {
  const { realmRoles } = authData.grantedRoles;
  if (realmRoles.includes('ORGANIZER') || realmRoles.includes('JUDGE')) {
    return true;
  }
  return inject(Router).parseUrl('/');
}

export const organizerGuard: CanActivateFn = createAuthGuard(isOrganizerAllowed);
export const judgeGuard: CanActivateFn = createAuthGuard(isJudgeAllowed);
export const roleSelectGuard: CanActivateFn = createAuthGuard(isRoleSelectAllowed);
export const settingsGuard: CanActivateFn = createAuthGuard(isSettingsAllowed);
