import { TestBed } from '@angular/core/testing';

import { ActiveRoleService } from './active-role.service';

describe('ActiveRoleService', () => {
  let service: ActiveRoleService;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({});
    service = TestBed.inject(ActiveRoleService);
  });

  it('has no active role by default', () => {
    expect(service.getActiveRole()).toBeNull();
  });

  it('overwrites a previously chosen role', () => {
    service.setActiveRole('ORGANIZER');
    service.setActiveRole('JUDGE');

    expect(service.getActiveRole()).toBe('JUDGE');
  });

  it('clears the chosen role', () => {
    service.setActiveRole('ORGANIZER');
    service.clearActiveRole();

    expect(service.getActiveRole()).toBeNull();
  });

  it('ignores a corrupted/unexpected stored value instead of throwing', () => {
    sessionStorage.setItem('birrapoint.activeRole', 'NOT_A_REAL_ROLE');
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});

    expect(TestBed.inject(ActiveRoleService).getActiveRole()).toBeNull();
  });

  it('survives a reload by re-seeding a fresh instance from sessionStorage', () => {
    service.setActiveRole('JUDGE');

    // Simulate a fresh page load: a new Angular injector (new service instance), same
    // sessionStorage — the in-memory signal alone would not survive this, only the mirror would.
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});
    const reloaded = TestBed.inject(ActiveRoleService);

    expect(reloaded.getActiveRole()).toBe('JUDGE');
  });

  it('seeds as unset when sessionStorage.getItem throws at construction (e.g. private browsing)', () => {
    const getSpy = jest.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('storage disabled');
    });

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});
    const freshService = TestBed.inject(ActiveRoleService);

    expect(freshService.getActiveRole()).toBeNull();
    getSpy.mockRestore();
  });

  // Regression (senior review on PR #43, B1): a sessionStorage failure must not strand the
  // caller — before this, setActiveRole silently no-opped and every later getActiveRole read
  // back null, so a dual-role caller in a storage-denied context (private browsing, quota,
  // partitioned storage) could click "Organizador"/"Juez" on /select-role forever and reach
  // neither workspace, since role.guard.ts's isActiveRole would always see no active role.
  it('keeps the chosen role usable for this tab when sessionStorage.setItem throws', () => {
    const setSpy = jest.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('storage disabled');
    });

    service.setActiveRole('JUDGE');

    expect(service.getActiveRole()).toBe('JUDGE');
    setSpy.mockRestore();
  });

  it('does not throw when clearActiveRole hits a storage error, and still clears in memory', () => {
    const removeSpy = jest.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
      throw new Error('storage disabled');
    });

    service.setActiveRole('ORGANIZER');
    expect(() => service.clearActiveRole()).not.toThrow();
    expect(service.getActiveRole()).toBeNull();

    removeSpy.mockRestore();
  });
});
