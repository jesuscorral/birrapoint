import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import type { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import type { AuthGuardData } from 'keycloak-angular';

import { isHomeRedirectAllowed } from './home-redirect.guard';

function authData(realmRoles: string[]): AuthGuardData {
  return {
    authenticated: true,
    grantedRoles: { realmRoles, resourceRoles: {} },
    keycloak: {} as AuthGuardData['keycloak'],
  };
}

describe('isHomeRedirectAllowed', () => {
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    router = TestBed.inject(Router);
  });

  it('redirects an ORGANIZER caller to /organizer/dashboard', async () => {
    const result = await TestBed.runInInjectionContext(() =>
      isHomeRedirectAllowed(
        {} as ActivatedRouteSnapshot,
        {} as RouterStateSnapshot,
        authData(['ORGANIZER']),
      ),
    );

    expect(result).toEqual(router.parseUrl('/organizer/dashboard'));
  });

  it('redirects a JUDGE caller to /judge/tables', async () => {
    const result = await TestBed.runInInjectionContext(() =>
      isHomeRedirectAllowed(
        {} as ActivatedRouteSnapshot,
        {} as RouterStateSnapshot,
        authData(['JUDGE']),
      ),
    );

    expect(result).toEqual(router.parseUrl('/judge/tables'));
  });

  it('falls through to true for a caller with neither role', async () => {
    const result = await TestBed.runInInjectionContext(() =>
      isHomeRedirectAllowed({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot, authData([])),
    );

    expect(result).toBe(true);
  });
});
