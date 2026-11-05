import { TestBed } from '@angular/core/testing';
import { UrlTree } from '@angular/router';
import type { AuthGuardData } from 'keycloak-angular';

import { ActiveRoleService } from './active-role.service';
import { isActiveRole, resolveRoleLandingUrlTree } from './role-landing';

function authData(realmRoles: string[]): AuthGuardData {
  return {
    authenticated: true,
    grantedRoles: { realmRoles, resourceRoles: {} },
    keycloak: {} as AuthGuardData['keycloak'],
  };
}

describe('resolveRoleLandingUrlTree', () => {
  beforeEach(() => {
    sessionStorage.clear();
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

  it('sends a dual-role caller with no chosen active role to /select-role', () => {
    const result = TestBed.runInInjectionContext(() =>
      resolveRoleLandingUrlTree(authData(['JUDGE', 'ORGANIZER'])),
    );

    expect(result?.toString()).toBe('/select-role');
  });

  it('resolves a dual-role caller who chose JUDGE to /judge/tables', () => {
    const result = TestBed.runInInjectionContext(() => {
      TestBed.inject(ActiveRoleService).setActiveRole('JUDGE');
      return resolveRoleLandingUrlTree(authData(['JUDGE', 'ORGANIZER']));
    });

    expect(result?.toString()).toBe('/judge/tables');
  });

  it('resolves a dual-role caller who chose ORGANIZER to /organizer/dashboard', () => {
    const result = TestBed.runInInjectionContext(() => {
      TestBed.inject(ActiveRoleService).setActiveRole('ORGANIZER');
      return resolveRoleLandingUrlTree(authData(['JUDGE', 'ORGANIZER']));
    });

    expect(result?.toString()).toBe('/organizer/dashboard');
  });
});

describe('isActiveRole', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({});
  });

  it('is true for a single-role caller holding that role', () => {
    const result = TestBed.runInInjectionContext(() => isActiveRole(authData(['JUDGE']), 'JUDGE'));

    expect(result).toBe(true);
  });

  it('is false for a single-role caller not holding that role', () => {
    const result = TestBed.runInInjectionContext(() =>
      isActiveRole(authData(['ORGANIZER']), 'JUDGE'),
    );

    expect(result).toBe(false);
  });

  it('for a dual-role caller with no choice made yet, is false for both roles', () => {
    const data = authData(['JUDGE', 'ORGANIZER']);
    const result = TestBed.runInInjectionContext(() => ({
      organizer: isActiveRole(data, 'ORGANIZER'),
      judge: isActiveRole(data, 'JUDGE'),
    }));

    expect(result).toEqual({ organizer: false, judge: false });
  });

  it('for a dual-role caller who chose JUDGE, is true only for JUDGE', () => {
    const data = authData(['JUDGE', 'ORGANIZER']);
    const result = TestBed.runInInjectionContext(() => {
      TestBed.inject(ActiveRoleService).setActiveRole('JUDGE');
      return { organizer: isActiveRole(data, 'ORGANIZER'), judge: isActiveRole(data, 'JUDGE') };
    });

    expect(result).toEqual({ organizer: false, judge: true });
  });
});
