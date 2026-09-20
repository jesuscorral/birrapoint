import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import Keycloak from 'keycloak-js';
import type { KeycloakProfile, KeycloakTokenParsed } from 'keycloak-js';

import { ActiveRoleService } from '../../core/auth/active-role.service';
import { UserSettingsComponent } from './user-settings.component';

function profileFixture(overrides: Partial<KeycloakProfile> = {}): KeycloakProfile {
  return {
    id: 'user-1',
    username: 'jane.organizer',
    email: 'jane@example.com',
    firstName: 'Jane',
    lastName: 'Organizer',
    enabled: true,
    emailVerified: true,
    totp: false,
    createdTimestamp: Date.UTC(2026, 0, 15),
    ...overrides,
  };
}

describe('UserSettingsComponent', () => {
  let fixture: ComponentFixture<UserSettingsComponent>;
  let keycloak: {
    loadUserProfile: jest.Mock;
    logout: jest.Mock;
    tokenParsed: KeycloakTokenParsed | undefined;
  };

  function createComponent(): ComponentFixture<UserSettingsComponent> {
    const created = TestBed.createComponent(UserSettingsComponent);
    created.detectChanges();
    return created;
  }

  beforeEach(() => {
    sessionStorage.clear();
    keycloak = {
      loadUserProfile: jest.fn().mockResolvedValue(profileFixture()),
      logout: jest.fn(),
      tokenParsed: { realm_access: { roles: ['ORGANIZER'] } },
    };

    TestBed.configureTestingModule({
      providers: [{ provide: Keycloak, useValue: keycloak }, provideRouter([])],
    });
  });

  it('renders the profile fields from the resolved Keycloak profile', async () => {
    fixture = createComponent();
    await fixture.whenStable();
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Jane');
    expect(text).toContain('Organizer');
    expect(text).toContain('jane@example.com');
    expect(text).toContain('jane.organizer');
    expect(text).toContain('ORGANIZER');
  });

  it('shows the verified badge when the email is verified', async () => {
    fixture = createComponent();
    await fixture.whenStable();
    fixture.detectChanges();

    const badge = fixture.nativeElement.querySelector('.verified-badge');
    expect(badge).not.toBeNull();
    expect(badge.textContent).toContain('Verificado');
  });

  it('does not show the verified badge when the email is not verified', async () => {
    keycloak.loadUserProfile.mockResolvedValue(profileFixture({ emailVerified: false }));
    fixture = createComponent();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.verified-badge')).toBeNull();
  });

  it('shows an alert message when loading the profile fails', async () => {
    keycloak.loadUserProfile.mockRejectedValue(new Error('network down'));
    fixture = createComponent();
    await fixture.whenStable();
    fixture.detectChanges();

    const alert = fixture.nativeElement.querySelector('[role="alert"]');
    expect(alert).not.toBeNull();
    expect(alert.textContent).toContain('No hemos podido cargar');
  });

  // Keycloak grants its own plumbing roles to every user alongside the app's. Verified against a
  // real token in the browser, which carried exactly these four.
  it("lists only BirraPoint's own realm roles, hiding Keycloak's internal ones", async () => {
    keycloak.tokenParsed = {
      realm_access: {
        roles: ['default-roles-birrapoint', 'offline_access', 'uma_authorization', 'ORGANIZER'],
      },
    };
    fixture = createComponent();
    await fixture.whenStable();
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('ORGANIZER');
    expect(text).not.toContain('default-roles-birrapoint');
    expect(text).not.toContain('offline_access');
    expect(text).not.toContain('uma_authorization');
  });

  it('falls back to a dash when the user holds no BirraPoint realm role', async () => {
    keycloak.tokenParsed = { realm_access: { roles: ['offline_access'] } };
    fixture = createComponent();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('offline_access');
  });

  // An anchor rather than a button, deliberately: it navigates, so it must behave like a link
  // (open-in-new-tab, link role) instead of firing a click handler.
  describe('back link (role-aware, Session 2026-09-20)', () => {
    it('points an ORGANIZER-only account back to the competitions dashboard', async () => {
      fixture = createComponent();
      await fixture.whenStable();
      fixture.detectChanges();

      const backLink = fixture.nativeElement.querySelector(
        'a[href="/organizer/dashboard"].back-to-list-link',
      ) as HTMLAnchorElement | null;
      expect(backLink).not.toBeNull();
      expect(backLink?.textContent).toContain('Volver a competiciones');
    });

    it('points a JUDGE-only account back to their tables', async () => {
      keycloak.tokenParsed = { realm_access: { roles: ['JUDGE'] } };
      fixture = createComponent();
      await fixture.whenStable();
      fixture.detectChanges();

      const backLink = fixture.nativeElement.querySelector(
        'a[href="/judge/tables"].back-to-list-link',
      ) as HTMLAnchorElement | null;
      expect(backLink).not.toBeNull();
      expect(backLink?.textContent).toContain('Volver a mis mesas');
    });

    it("follows the dual-role account's active choice (JUDGE)", async () => {
      keycloak.tokenParsed = { realm_access: { roles: ['ORGANIZER', 'JUDGE'] } };
      TestBed.inject(ActiveRoleService).setActiveRole('JUDGE');
      fixture = createComponent();
      await fixture.whenStable();
      fixture.detectChanges();

      const backLink = fixture.nativeElement.querySelector('a.back-to-list-link');
      expect(backLink?.getAttribute('href')).toBe('/judge/tables');
    });

    it("follows the dual-role account's active choice (ORGANIZER)", async () => {
      keycloak.tokenParsed = { realm_access: { roles: ['ORGANIZER', 'JUDGE'] } };
      TestBed.inject(ActiveRoleService).setActiveRole('ORGANIZER');
      fixture = createComponent();
      await fixture.whenStable();
      fixture.detectChanges();

      const backLink = fixture.nativeElement.querySelector('a.back-to-list-link');
      expect(backLink?.getAttribute('href')).toBe('/organizer/dashboard');
    });

    it('sends a dual-role account with no active choice yet to the role picker', async () => {
      keycloak.tokenParsed = { realm_access: { roles: ['ORGANIZER', 'JUDGE'] } };
      fixture = createComponent();
      await fixture.whenStable();
      fixture.detectChanges();

      const backLink = fixture.nativeElement.querySelector('a.back-to-list-link');
      expect(backLink?.getAttribute('href')).toBe('/select-role');
      expect(backLink?.textContent).toContain('Elegir rol');
    });
  });

  it('calls keycloak.logout with the app-root redirect when "Cerrar sesión" is clicked', async () => {
    fixture = createComponent();
    await fixture.whenStable();
    fixture.detectChanges();

    const buttons = Array.from(
      fixture.nativeElement.querySelectorAll('button'),
    ) as HTMLButtonElement[];
    const logoutButton = buttons.find((button) => button.textContent?.trim() === 'Cerrar sesión');
    expect(logoutButton).toBeDefined();
    logoutButton?.click();

    expect(keycloak.logout).toHaveBeenCalledWith({
      redirectUri: window.location.origin + '/',
    });
  });

  it("clears the session's chosen active role before logging out", async () => {
    TestBed.inject(ActiveRoleService).setActiveRole('JUDGE');
    fixture = createComponent();
    await fixture.whenStable();
    fixture.detectChanges();

    const buttons = Array.from(
      fixture.nativeElement.querySelectorAll('button'),
    ) as HTMLButtonElement[];
    buttons.find((button) => button.textContent?.trim() === 'Cerrar sesión')?.click();

    expect(TestBed.inject(ActiveRoleService).getActiveRole()).toBeNull();
  });
});
