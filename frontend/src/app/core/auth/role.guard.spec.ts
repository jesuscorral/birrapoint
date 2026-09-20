import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import type { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import type { AuthGuardData } from 'keycloak-angular';

import { ActiveRoleService } from './active-role.service';
import { isJudgeAllowed, isOrganizerAllowed, isRoleSelectAllowed } from './role.guard';

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
    sessionStorage.clear();
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

  it('isOrganizerAllowed redirects a JUDGE-only caller to their own landing', async () => {
    const result = await TestBed.runInInjectionContext(() =>
      isOrganizerAllowed(
        {} as ActivatedRouteSnapshot,
        {} as RouterStateSnapshot,
        authData(['JUDGE']),
      ),
    );

    expect(result).toEqual(router.parseUrl('/judge/tables'));
  });

  it('isOrganizerAllowed redirects a caller with neither role to root', async () => {
    const result = await TestBed.runInInjectionContext(() =>
      isOrganizerAllowed({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot, authData([])),
    );

    expect(result).toEqual(router.parseUrl('/'));
  });

  it('isJudgeAllowed allows a caller with the JUDGE realm role', async () => {
    const result = await TestBed.runInInjectionContext(() =>
      isJudgeAllowed({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot, authData(['JUDGE'])),
    );

    expect(result).toBe(true);
  });

  it('isJudgeAllowed redirects an ORGANIZER-only caller to their own landing', async () => {
    const result = await TestBed.runInInjectionContext(() =>
      isJudgeAllowed(
        {} as ActivatedRouteSnapshot,
        {} as RouterStateSnapshot,
        authData(['ORGANIZER']),
      ),
    );

    expect(result).toEqual(router.parseUrl('/organizer/dashboard'));
  });

  it('isJudgeAllowed redirects a caller with neither role to root', async () => {
    const result = await TestBed.runInInjectionContext(() =>
      isJudgeAllowed({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot, authData([])),
    );

    expect(result).toEqual(router.parseUrl('/'));
  });

  describe('dual-role caller (ORGANIZER + JUDGE)', () => {
    it('isOrganizerAllowed sends them to /select-role when no active role was chosen yet', async () => {
      const result = await TestBed.runInInjectionContext(() =>
        isOrganizerAllowed(
          {} as ActivatedRouteSnapshot,
          {} as RouterStateSnapshot,
          authData(['ORGANIZER', 'JUDGE']),
        ),
      );

      expect(result).toEqual(router.parseUrl('/select-role'));
    });

    it('isOrganizerAllowed allows them once they chose ORGANIZER', async () => {
      TestBed.inject(ActiveRoleService).setActiveRole('ORGANIZER');

      const result = await TestBed.runInInjectionContext(() =>
        isOrganizerAllowed(
          {} as ActivatedRouteSnapshot,
          {} as RouterStateSnapshot,
          authData(['ORGANIZER', 'JUDGE']),
        ),
      );

      expect(result).toBe(true);
    });

    it('isOrganizerAllowed bounces them to /judge/tables when they chose JUDGE', async () => {
      TestBed.inject(ActiveRoleService).setActiveRole('JUDGE');

      const result = await TestBed.runInInjectionContext(() =>
        isOrganizerAllowed(
          {} as ActivatedRouteSnapshot,
          {} as RouterStateSnapshot,
          authData(['ORGANIZER', 'JUDGE']),
        ),
      );

      expect(result).toEqual(router.parseUrl('/judge/tables'));
    });

    it('isJudgeAllowed bounces them to /organizer/dashboard when they chose ORGANIZER', async () => {
      TestBed.inject(ActiveRoleService).setActiveRole('ORGANIZER');

      const result = await TestBed.runInInjectionContext(() =>
        isJudgeAllowed(
          {} as ActivatedRouteSnapshot,
          {} as RouterStateSnapshot,
          authData(['ORGANIZER', 'JUDGE']),
        ),
      );

      expect(result).toEqual(router.parseUrl('/organizer/dashboard'));
    });

    it('isJudgeAllowed allows them once they chose JUDGE', async () => {
      TestBed.inject(ActiveRoleService).setActiveRole('JUDGE');

      const result = await TestBed.runInInjectionContext(() =>
        isJudgeAllowed(
          {} as ActivatedRouteSnapshot,
          {} as RouterStateSnapshot,
          authData(['ORGANIZER', 'JUDGE']),
        ),
      );

      expect(result).toBe(true);
    });
  });

  describe('isRoleSelectAllowed (/select-role)', () => {
    it('allows a genuinely dual-role caller', async () => {
      const result = await TestBed.runInInjectionContext(() =>
        isRoleSelectAllowed(
          {} as ActivatedRouteSnapshot,
          {} as RouterStateSnapshot,
          authData(['ORGANIZER', 'JUDGE']),
        ),
      );

      expect(result).toBe(true);
    });

    it('bounces an ORGANIZER-only caller to their own landing (e.g. a stale bookmark)', async () => {
      const result = await TestBed.runInInjectionContext(() =>
        isRoleSelectAllowed(
          {} as ActivatedRouteSnapshot,
          {} as RouterStateSnapshot,
          authData(['ORGANIZER']),
        ),
      );

      expect(result).toEqual(router.parseUrl('/organizer/dashboard'));
    });

    it('bounces a JUDGE-only caller to their own landing', async () => {
      const result = await TestBed.runInInjectionContext(() =>
        isRoleSelectAllowed(
          {} as ActivatedRouteSnapshot,
          {} as RouterStateSnapshot,
          authData(['JUDGE']),
        ),
      );

      expect(result).toEqual(router.parseUrl('/judge/tables'));
    });

    it('bounces an anonymous/no-role caller to the public landing (e.g. browser Back after logout)', async () => {
      const result = await TestBed.runInInjectionContext(() =>
        isRoleSelectAllowed({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot, authData([])),
      );

      expect(result).toEqual(router.parseUrl('/'));
    });
  });
});
