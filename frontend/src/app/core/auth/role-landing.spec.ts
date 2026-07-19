import { TestBed } from '@angular/core/testing';
import { UrlTree } from '@angular/router';
import type { AuthGuardData } from 'keycloak-angular';

import { resolveRoleLandingUrlTree } from './role-landing';

function authData(realmRoles: string[]): AuthGuardData {
  return {
    authenticated: true,
    grantedRoles: { realmRoles, resourceRoles: {} },
    keycloak: {} as AuthGuardData['keycloak'],
  };
}

describe('resolveRoleLandingUrlTree', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({});
  });

  it('resolves an ORGANIZER caller to /organizer/dashboard', () => {
    const result = TestBed.runInInjectionContext(() =>
      resolveRoleLandingUrlTree(authData(['ORGANIZER'])),
    );

    expect(result).toBeInstanceOf(UrlTree);
    expect(result?.toString()).toBe('/organizer/dashboard');
  });

  it('resolves a JUDGE caller to /judge/tables', () => {
    const result = TestBed.runInInjectionContext(() =>
      resolveRoleLandingUrlTree(authData(['JUDGE'])),
    );

    expect(result).toBeInstanceOf(UrlTree);
    expect(result?.toString()).toBe('/judge/tables');
  });

  it('resolves a caller with neither role to null', () => {
    const result = TestBed.runInInjectionContext(() => resolveRoleLandingUrlTree(authData([])));

    expect(result).toBeNull();
  });

  it('prefers ORGANIZER when a caller somehow holds both roles', () => {
    const result = TestBed.runInInjectionContext(() =>
      resolveRoleLandingUrlTree(authData(['JUDGE', 'ORGANIZER'])),
    );

    expect(result?.toString()).toBe('/organizer/dashboard');
  });
});
