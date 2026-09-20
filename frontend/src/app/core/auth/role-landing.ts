import { Router } from '@angular/router';
import type { UrlTree } from '@angular/router';
import type { AuthGuardData } from 'keycloak-angular';
import { inject } from '@angular/core';

import { ActiveRoleService } from './active-role.service';
import type { AppRole } from './active-role.service';

// Shared by home-redirect.guard.ts (post-login landing) and role.guard.ts (mismatch redirect), so
// both branch off the same ORGANIZER → /organizer/dashboard, JUDGE → /judge/tables mapping. A
// caller holding both realm roles is ambiguous on its own — resolved via ActiveRoleService's
// session-scoped choice, prompting for one (/select-role) the first time there isn't one yet.
export function resolveRoleLandingUrlTree(authData: AuthGuardData): UrlTree | null {
  const { realmRoles } = authData.grantedRoles;
  const router = inject(Router);
  const hasOrganizer = realmRoles.includes('ORGANIZER');
  const hasJudge = realmRoles.includes('JUDGE');

  if (hasOrganizer && hasJudge) {
    const activeRole = inject(ActiveRoleService).getActiveRole();
    if (activeRole === 'JUDGE') {
      return router.parseUrl('/judge/tables');
    }
    if (activeRole === 'ORGANIZER') {
      return router.parseUrl('/organizer/dashboard');
    }
    return router.parseUrl('/select-role');
  }
  if (hasOrganizer) {
    return router.parseUrl('/organizer/dashboard');
  }
  if (hasJudge) {
    return router.parseUrl('/judge/tables');
  }
  return null;
}

// Whether `role` is the caller's currently-relevant workspace: trivially true for a single-role
// caller who holds it, but for a dual-role caller only the session's chosen ActiveRoleService
// value counts — used by role.guard.ts so a caller who picked JUDGE can't bypass the picker by
// navigating straight to an /organizer/** URL (and vice versa).
export function isActiveRole(authData: AuthGuardData, role: AppRole): boolean {
  const { realmRoles } = authData.grantedRoles;
  const hasOrganizer = realmRoles.includes('ORGANIZER');
  const hasJudge = realmRoles.includes('JUDGE');

  if (hasOrganizer && hasJudge) {
    return inject(ActiveRoleService).getActiveRole() === role;
  }
  return realmRoles.includes(role);
}
