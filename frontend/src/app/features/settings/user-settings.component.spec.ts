import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import Keycloak from 'keycloak-js';
import type { KeycloakProfile, KeycloakTokenParsed } from 'keycloak-js';

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
    expect(badge.textContent).toContain('Verified');
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
    expect(alert.textContent).toContain('could not load');
  });

  it('calls keycloak.logout with the app-root redirect when "Log out" is clicked', async () => {
    fixture = createComponent();
    await fixture.whenStable();
    fixture.detectChanges();

    const buttons = Array.from(
      fixture.nativeElement.querySelectorAll('button'),
    ) as HTMLButtonElement[];
    const logoutButton = buttons.find((button) => button.textContent?.trim() === 'Log out');
    expect(logoutButton).toBeDefined();
    logoutButton?.click();

    expect(keycloak.logout).toHaveBeenCalledWith({
      redirectUri: window.location.origin + '/',
    });
  });
});
