import { Router } from '@angular/router';
import type { UrlTree } from '@angular/router';
import type { AuthGuardData } from 'keycloak-angular';
import { inject } from '@angular/core';

// Shared by home-redirect.guard.ts (post-login landing) and role.guard.ts (mismatch redirect), so
// both branch off the same ORGANIZER → /organizer/dashboard, JUDGE → /judge/tables mapping.
export function resolveRoleLandingUrlTree(authData: AuthGuardData): UrlTree | null {
  const { realmRoles } = authData.grantedRoles;
  const router = inject(Router);

  if (realmRoles.includes('ORGANIZER')) {
    return router.parseUrl('/organizer/dashboard');
  }
  if (realmRoles.includes('JUDGE')) {
    return router.parseUrl('/judge/tables');
  }
  return null;
}
