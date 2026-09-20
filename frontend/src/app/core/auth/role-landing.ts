import { Router } from '@angular/router';
import type { UrlTree } from '@angular/router';
import type { AuthGuardData } from 'keycloak-angular';
import { inject } from '@angular/core';

import { ActiveRoleService } from './active-role.service';
import type { AppRole } from './active-role.service';

// The one ORGANIZER → /organizer/dashboard, JUDGE → /judge/tables mapping — used by
// resolveRoleLandingUrlTree below and by RoleSelectComponent, so the two never drift apart.
export function landingPathFor(role: AppRole): string {
  return role === 'ORGANIZER' ? '/organizer/dashboard' : '/judge/tables';
}

// Shared by home-redirect.guard.ts (post-login landing) and role.guard.ts (mismatch redirect). A
// caller holding both realm roles is ambiguous on its own — resolved via ActiveRoleService's
// session-scoped choice, prompting for one (/select-role) the first time there isn't one yet.
//
// Calls `inject()` (via ActiveRoleService), so — like `isActiveRole` below — this must only be
// called synchronously within an active injection context (a guard's synchronous prefix, a
// component's field initializer/constructor, or `TestBed.runInInjectionContext` in tests); an
// `await` before the call, or any other deferral, throws NG0203.
export function resolveRoleLandingUrlTree(authData: AuthGuardData): UrlTree | null {
  const { realmRoles } = authData.grantedRoles;
  const router = inject(Router);
  const hasOrganizer = realmRoles.includes('ORGANIZER');
  const hasJudge = realmRoles.includes('JUDGE');

  if (hasOrganizer && hasJudge) {
    const activeRole = inject(ActiveRoleService).getActiveRole();
    return activeRole
      ? router.parseUrl(landingPathFor(activeRole))
      : router.parseUrl('/select-role');
  }
  if (hasOrganizer) {
    return router.parseUrl(landingPathFor('ORGANIZER'));
  }
  if (hasJudge) {
    return router.parseUrl(landingPathFor('JUDGE'));
  }
  return null;
}

// Whether `role` is the caller's currently-relevant workspace for the purpose of deciding which
// frontend routes/landing to show — trivially true for a single-role caller who holds it, but for
// a dual-role caller only the session's chosen ActiveRoleService value counts. Used by
// role.guard.ts so a caller who picked JUDGE can't bypass the picker by navigating straight to an
// /organizer/** URL (and vice versa).
//
// This is a frontend UX partition, never an authorization check: every backend endpoint keeps
// enforcing the caller's real Keycloak realm roles regardless of this value (Principle VII) — see
// ActiveRoleService's own doc comment. Same injection-context requirement as
// resolveRoleLandingUrlTree above (calls `inject()`, must run synchronously within one).
export function isActiveRole(authData: AuthGuardData, role: AppRole): boolean {
  const { realmRoles } = authData.grantedRoles;
  const hasOrganizer = realmRoles.includes('ORGANIZER');
  const hasJudge = realmRoles.includes('JUDGE');

  if (hasOrganizer && hasJudge) {
    return inject(ActiveRoleService).getActiveRole() === role;
  }
  return realmRoles.includes(role);
}
