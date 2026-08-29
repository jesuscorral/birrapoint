import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import Keycloak from 'keycloak-js';
import type { KeycloakProfile } from 'keycloak-js';

import { BpTopbarComponent } from '../../shared/components/bp-topbar/bp-topbar.component';

// T126: a settings page for the identity data the app actually has access to. Identity is
// Keycloak-only (constitution Principle VII — no custom login/user-data backend), so every field
// here comes straight from keycloak-js: loadUserProfile() for the profile attributes, and
// tokenParsed.realm_access.roles for the realm role(s). There are no custom Keycloak user
// attributes configured in this realm (infra/keycloak/birrapoint-realm.json), so this is the
// complete, honest set of "everything we have" about the user — not a generic attributes dump.
@Component({
  selector: 'app-user-settings',
  imports: [BpTopbarComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="settings-shell">
      <bp-topbar>
        <button type="button" class="topbar-action topbar-action--button" (click)="onLogout()">
          Log out
        </button>
      </bp-topbar>

      <main class="settings-main">
        <h1>Your account</h1>

        @if (loadError(); as message) {
          <p role="alert">{{ message }}</p>
        }

        @if (profile(); as user) {
          <dl class="profile-details">
            <div>
              <dt>First name</dt>
              <dd>{{ user.firstName || '—' }}</dd>
            </div>
            <div>
              <dt>Last name</dt>
              <dd>{{ user.lastName || '—' }}</dd>
            </div>
            <div>
              <dt>Email</dt>
              <dd>
                {{ user.email || '—' }}
                @if (user.emailVerified) {
                  <span class="verified-badge">Verified</span>
                }
              </dd>
            </div>
            <div>
              <dt>Username</dt>
              <dd>{{ user.username || '—' }}</dd>
            </div>
            <div>
              <dt>Role(s)</dt>
              <dd>{{ rolesLabel() }}</dd>
            </div>
            @if (memberSinceLabel(); as since) {
              <div>
                <dt>Member since</dt>
                <dd>{{ since }}</dd>
              </div>
            }
          </dl>
        }
      </main>
    </div>
  `,
  styles: `
    :host {
      display: block;
      min-height: 100vh;
      background: var(--color-bp-hueso-50);
    }

    .settings-main {
      padding: var(--spacing-8) var(--spacing-6);
      max-width: 40rem;
      margin: 0 auto;
    }

    h1 {
      font-family: 'Fraunces', serif;
      font-size: 1.75rem;
      font-weight: 600;
      letter-spacing: -0.02em;
      color: var(--color-bp-text);
      margin: 0 0 var(--spacing-6);
    }

    .topbar-action {
      display: inline-flex;
      align-items: center;
      min-height: 40px;
      padding: 0 var(--spacing-4);
      border-radius: var(--radius-md);
      font-weight: 600;
      font-size: 0.875rem;
      text-decoration: none;
      cursor: pointer;
      background: transparent;
      font-family: inherit;
    }

    .topbar-action--button {
      color: var(--color-bp-text-muted);
      border: 1.5px solid var(--color-bp-border-strong);
    }

    .topbar-action--button:hover {
      background: var(--color-bp-hueso-100);
    }

    .topbar-action:focus-visible {
      outline: none;
      box-shadow:
        0 0 0 3px var(--color-bp-surface),
        0 0 0 5px var(--color-bp-cobre-500);
    }

    .profile-details {
      background: var(--color-bp-surface);
      border: 1px solid var(--color-bp-border);
      border-radius: var(--radius-lg);
      padding: var(--spacing-6);
      margin: 0;
      display: grid;
      gap: var(--spacing-4);
    }

    .profile-details dt {
      font-size: 0.8125rem;
      font-weight: 600;
      color: var(--color-bp-text-muted);
    }

    .profile-details dd {
      margin: 0;
      color: var(--color-bp-text);
    }

    .verified-badge {
      display: inline-flex;
      align-items: center;
      margin-left: var(--spacing-2);
      padding: 1px 8px;
      font-size: 0.75rem;
      font-weight: 700;
      letter-spacing: 0.02em;
      border-radius: var(--radius-full);
      background: var(--color-bp-exito-50);
      color: var(--color-bp-exito-600);
    }

    [role='alert'] {
      color: var(--color-bp-danger-600);
      background: var(--color-bp-danger-50);
      border: 1px solid #efc9c5;
      border-radius: var(--radius-md);
      padding: var(--spacing-3) var(--spacing-4);
      margin-bottom: var(--spacing-4);
    }
  `,
})
export class UserSettingsComponent {
  private readonly keycloak = inject(Keycloak);

  protected readonly profile = signal<KeycloakProfile | null>(null);
  protected readonly loadError = signal<string | null>(null);

  constructor() {
    this.keycloak
      .loadUserProfile()
      .then((profile) => this.profile.set(profile))
      .catch(() =>
        this.loadError.set('We could not load your account details. Try again shortly.'),
      );
  }

  protected rolesLabel(): string {
    const roles = this.keycloak.tokenParsed?.realm_access?.roles ?? [];
    return roles.length > 0 ? roles.join(', ') : '—';
  }

  protected memberSinceLabel(): string | null {
    const timestamp = this.profile()?.createdTimestamp;
    return timestamp ? new Date(timestamp).toLocaleDateString() : null;
  }

  protected onLogout(): void {
    this.keycloak.logout({ redirectUri: window.location.origin + '/' });
  }
}
