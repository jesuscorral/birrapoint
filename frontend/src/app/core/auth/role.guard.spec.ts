import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import type { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import type { AuthGuardData } from 'keycloak-angular';

import { isJudgeAllowed, isOrganizerAllowed } from './role.guard';

function authData(realmRoles: string[]): AuthGuardData {
  return {
    authenticated: true,
    grantedRoles: { realmRoles, resourceRoles: {} },
    keycloak: {} as AuthGuardData['keycloak'],
  };
}

describe('role guards', () => {
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    router = TestBed.inject(Router);
  });

  it('isOrganizerAllowed allows a caller with the ORGANIZER realm role', async () => {
    const result = await TestBed.runInInjectionContext(() =>
      isOrganizerAllowed(
        {} as ActivatedRouteSnapshot,
        {} as RouterStateSnapshot,
        authData(['ORGANIZER']),
      ),
    );

    expect(result).toBe(true);
  });

  it('isOrganizerAllowed redirects a JUDGE-only caller to root', async () => {
    const result = await TestBed.runInInjectionContext(() =>
      isOrganizerAllowed(
        {} as ActivatedRouteSnapshot,
        {} as RouterStateSnapshot,
        authData(['JUDGE']),
      ),
    );

    expect(result).toEqual(router.parseUrl('/'));
  });

  it('isJudgeAllowed allows a caller with the JUDGE realm role', async () => {
    const result = await TestBed.runInInjectionContext(() =>
      isJudgeAllowed({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot, authData(['JUDGE'])),
    );

    expect(result).toBe(true);
  });

  it('isJudgeAllowed redirects an ORGANIZER-only caller to root', async () => {
    const result = await TestBed.runInInjectionContext(() =>
      isJudgeAllowed(
        {} as ActivatedRouteSnapshot,
        {} as RouterStateSnapshot,
        authData(['ORGANIZER']),
      ),
    );

    expect(result).toEqual(router.parseUrl('/'));
  });
});
