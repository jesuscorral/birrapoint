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

  it('persists the chosen role across service instances (session-scoped)', () => {
    service.setActiveRole('JUDGE');

    const other = TestBed.inject(ActiveRoleService);
    expect(other.getActiveRole()).toBe('JUDGE');
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

    expect(service.getActiveRole()).toBeNull();
  });

  it('degrades to no persisted role when sessionStorage throws (e.g. private browsing)', () => {
    const getSpy = jest.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('storage disabled');
    });

    expect(service.getActiveRole()).toBeNull();

    getSpy.mockRestore();
  });

  it('does not throw when setActiveRole/clearActiveRole hit a storage error', () => {
    const setSpy = jest.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('storage disabled');
    });
    const removeSpy = jest.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
      throw new Error('storage disabled');
    });

    expect(() => service.setActiveRole('ORGANIZER')).not.toThrow();
    expect(() => service.clearActiveRole()).not.toThrow();

    setSpy.mockRestore();
    removeSpy.mockRestore();
  });
});
