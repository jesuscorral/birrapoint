import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import Keycloak from 'keycloak-js';

import { ActiveRoleService } from '../../../core/auth/active-role.service';
import { BpTopbarComponent } from '../bp-topbar/bp-topbar.component';

// T127/FR-061: shared page chrome (topbar + main landmark + gutter rules) extracted from the
// wizard shell so every organizer screen gets the same breathing room and a single `main` per
// page, instead of each feature re-declaring its own copy of the gutter media queries.
//
// Session 2026-09-20: "Cambiar rol" / "Ajustes" / "Cerrar sesión" moved in here, fixed, instead of
// each consumer projecting its own copy into [bpTopbarActions] (OrganizerDashboardComponent and
// JudgeTablesListComponent used to each carry a byte-for-byte duplicate of this logic — a real
// finding from PR #43's review). Every screen that wraps its content in <bp-page-shell> now gets
// these for free, organizer or judge alike.
@Component({
  selector: 'bp-page-shell',
  standalone: true,
  imports: [BpTopbarComponent, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <bp-topbar [homeLink]="homeLink()" [title]="title()">
      <!-- Static select-based projection: it matches the source template node, so wrap any
           conditional content in its own [bpTopbarActions] host (e.g. an @if inside the
           ng-container) rather than putting the @if around the ng-container itself — an @if
           wrapping it falls through to the default (page-body) slot instead of this one. Screen-
           specific actions (if any) render first, the identity actions below always trail last. -->
      <ng-content select="[bpTopbarActions]"></ng-content>

      @if (hasDualRole()) {
        <button type="button" class="topbar-action topbar-action--button" (click)="onSwitchRole()">
          Cambiar rol
        </button>
      }
      <a routerLink="/settings" class="topbar-action topbar-action--link">Ajustes</a>
      <button type="button" class="topbar-action topbar-action--button" (click)="onLogout()">
        Cerrar sesión
      </button>
    </bp-topbar>
    <main class="page-main">
      <div class="page-container">
        <ng-content></ng-content>
      </div>
    </main>
  `,
  styles: `
    :host {
      display: block;
      min-height: 100vh;
      background: var(--color-bp-hueso-50);
    }

    /* Same gutter rules as the wizard shell it was extracted from: the shell spans the viewport
       but content never runs up against it — the gutter widens with the screen instead of the
       card growing to fill every last pixel. */
    .page-main {
      display: flex;
      justify-content: center;
      padding: var(--spacing-10) var(--spacing-8) var(--spacing-16);
    }

    @media (min-width: 1280px) {
      .page-main {
        padding-inline: var(--spacing-12);
      }
    }

    @media (min-width: 1800px) {
      .page-main {
        padding-inline: var(--spacing-16);
      }
    }

    @media (max-width: 640px) {
      .page-main {
        padding: var(--spacing-8) var(--spacing-4) var(--spacing-12);
      }
    }

    .page-container {
      width: 100%;
      max-width: 88rem;
      min-width: 0;
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

    .topbar-action--link {
      color: var(--color-bp-cobre-700);
    }

    .topbar-action--link:hover {
      background: var(--color-bp-cobre-50);
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
  `,
})
export class BpPageShellComponent {
  private readonly keycloak = inject(Keycloak);
  private readonly activeRole = inject(ActiveRoleService);
  private readonly router = inject(Router);

  readonly homeLink = input('/organizer/dashboard');
  readonly title = input('');

  // "Cambiar rol" only makes sense for an account that actually holds both realm roles — a
  // single-role account never picked a role to begin with, so there's nothing to switch away from.
  protected hasDualRole(): boolean {
    const roles = this.keycloak.tokenParsed?.realm_access?.roles ?? [];
    return roles.includes('ORGANIZER') && roles.includes('JUDGE');
  }

  // Clears the session's chosen workspace and sends the caller back to the picker — does not log
  // them out, and doesn't touch backend authorization (Principle VII), only ActiveRoleService's
  // frontend-only partition (role-landing.ts).
  protected onSwitchRole(): void {
    this.activeRole.clearActiveRole();
    void this.router.navigateByUrl('/select-role');
  }

  protected onLogout(): void {
    // Don't leak this tab's chosen workspace into whoever logs in next on it.
    this.activeRole.clearActiveRole();
    this.keycloak.logout({ redirectUri: window.location.origin + '/' });
  }
}
