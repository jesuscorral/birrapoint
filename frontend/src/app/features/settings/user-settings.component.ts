import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import Keycloak from 'keycloak-js';
import type { KeycloakProfile } from 'keycloak-js';

import { ActiveRoleService } from '../../core/auth/active-role.service';
import { BpTopbarComponent } from '../../shared/components/bp-topbar/bp-topbar.component';

// The two realm roles BirraPoint assigns meaning to — the same pair core/auth's route guards
// branch on (role.guard.ts, role-landing.ts). Keycloak also grants every user its own plumbing
// roles (`default-roles-<realm>`, `offline_access`, `uma_authorization`), which are an
// implementation detail of the identity provider rather than facts about the person, so the
// account screen filters to this list. An allowlist rather than a denylist deliberately:
// `default-roles-<realm>` is named after the realm, and a denylist would leak any built-in
// Keycloak adds in a future version. Doubles as a stable display order, since the token's own
// role order is arbitrary.
const APP_REALM_ROLES = ['ORGANIZER', 'JUDGE'] as const;

// T126: a settings page for the identity data the app actually has access to. Identity is
// Keycloak-only (constitution Principle VII — no custom login/user-data backend), so every field
// here comes straight from keycloak-js: loadUserProfile() for the profile attributes, and
// tokenParsed.realm_access.roles for the realm role(s). There are no custom Keycloak user
// attributes configured in this realm (infra/keycloak/birrapoint-realm.json), so this is the
// complete, honest set of "everything we have" about the user — not a generic attributes dump.
//
// Session 2026-09-20: now reachable from both /organizer/** and /judge/** (top-level /settings,
// settingsGuard) — the "back" link/label adapts to the caller's effective workspace instead of
// always pointing at the organizer dashboard.
@Component({
  selector: 'app-user-settings',
  imports: [BpTopbarComponent, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="settings-shell">
      <bp-topbar>
        <button type="button" class="topbar-action topbar-action--button" (click)="onLogout()">
          Cerrar sesión
        </button>
      </bp-topbar>

      <main class="settings-main">
        <!-- An anchor, not a button: this is navigation, so it gets open-in-new-tab, the link role
             and Enter-to-follow for free. The wizard's own "back to list" is a button only because
             it has to run the FR-007 unsaved-changes guard first; this screen is read-only. -->
        <a [routerLink]="backLink().href" class="back-to-list-link">
          <span aria-hidden="true">←</span> {{ backLink().label }}
        </a>

        <h1>Tu cuenta</h1>

        @if (loadError(); as message) {
          <p role="alert">{{ message }}</p>
        }

        @if (profile(); as user) {
          <dl class="profile-details">
            <div>
              <dt>Nombre</dt>
              <dd>{{ user.firstName || '—' }}</dd>
            </div>
            <div>
              <dt>Apellidos</dt>
              <dd>{{ user.lastName || '—' }}</dd>
            </div>
            <div>
              <dt>Correo electrónico</dt>
              <dd>
                {{ user.email || '—' }}
                @if (user.emailVerified) {
                  <span class="verified-badge">Verificado</span>
                }
              </dd>
            </div>
            <div>
              <dt>Usuario</dt>
              <dd>{{ user.username || '—' }}</dd>
            </div>
            <div>
              <dt>Rol(es)</dt>
              <dd>{{ rolesLabel() }}</dd>
            </div>
            @if (memberSinceLabel(); as since) {
              <div>
                <dt>Miembro desde</dt>
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

    /* Same treatment as the wizard's "Volver al listado", so the one way back out of a screen
       looks the same wherever the organizer meets it. */
    .back-to-list-link {
      display: inline-block;
      padding: var(--spacing-2) 0;
      font-size: 0.875rem;
      font-weight: 600;
      color: var(--color-bp-text-muted);
      text-decoration: underline;
      text-underline-offset: 3px;
    }

    .back-to-list-link:hover {
      color: var(--color-bp-text);
    }

    .back-to-list-link:focus-visible {
      outline: 2px solid var(--color-bp-cobre-500);
      outline-offset: 2px;
      border-radius: var(--radius-sm);
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
  private readonly activeRole = inject(ActiveRoleService);

  protected readonly profile = signal<KeycloakProfile | null>(null);
  protected readonly loadError = signal<string | null>(null);

  constructor() {
    this.keycloak
      .loadUserProfile()
      .then((profile) => this.profile.set(profile))
      .catch(() =>
        this.loadError.set(
          'No hemos podido cargar los datos de tu cuenta. Vuelve a intentarlo en unos instantes.',
        ),
      );
  }

  protected rolesLabel(): string {
    const granted = this.keycloak.tokenParsed?.realm_access?.roles ?? [];
    const appRoles = APP_REALM_ROLES.filter((role) => granted.includes(role));
    return appRoles.length > 0 ? appRoles.join(', ') : '—';
  }

  protected memberSinceLabel(): string | null {
    const timestamp = this.profile()?.createdTimestamp;
    return timestamp ? new Date(timestamp).toLocaleDateString() : null;
  }

  // A single-role account always goes back to its one workspace. A dual-role account goes back
  // to whichever it's currently using (ActiveRoleService) — defaulting to the organizer dashboard
  // if, somehow, neither has been chosen yet this session.
  protected backLink(): { href: string; label: string } {
    const roles = this.keycloak.tokenParsed?.realm_access?.roles ?? [];
    const hasOrganizer = roles.includes('ORGANIZER');
    const hasJudge = roles.includes('JUDGE');
    const effectiveJudge =
      hasJudge && (!hasOrganizer || this.activeRole.getActiveRole() === 'JUDGE');

    return effectiveJudge
      ? { href: '/judge/tables', label: 'Volver a mis mesas' }
      : { href: '/organizer/dashboard', label: 'Volver a competiciones' };
  }

  protected onLogout(): void {
    // Don't leak this tab's chosen workspace into whoever logs in next on it — see
    // OrganizerDashboardComponent.onLogout for the same reasoning.
    this.activeRole.clearActiveRole();
    this.keycloak.logout({ redirectUri: window.location.origin + '/' });
  }
}
