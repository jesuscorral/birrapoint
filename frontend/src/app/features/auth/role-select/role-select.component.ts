import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router } from '@angular/router';

import type { AppRole } from '../../../core/auth/active-role.service';
import { ActiveRoleService } from '../../../core/auth/active-role.service';
import { landingPathFor } from '../../../core/auth/role-landing';
import { BpButtonComponent } from '../../../shared/components/bp-button/bp-button.component';

// Shown once per session to an account holding both ORGANIZER and JUDGE realm roles
// (resolveRoleLandingUrlTree redirects here whenever ActiveRoleService has no choice yet) — lets
// them pick which workspace to enter. Purely a frontend UX partition: the choice is session-scoped
// (ActiveRoleService/sessionStorage) and never affects backend authorization, which still enforces
// the caller's real Keycloak roles on every endpoint regardless (Principle VII).
@Component({
  selector: 'app-role-select',
  imports: [BpButtonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="role-select">
      <h1>¿Cómo querés entrar?</h1>
      <p class="role-select__lead">
        Tu cuenta tiene acceso como organizador y como juez. Puedes cambiar de vista más adelante.
      </p>

      <div class="role-select__options">
        <bp-button
          type="button"
          label="Organizador"
          variant="primary"
          (clicked)="chooseRole('ORGANIZER')"
        ></bp-button>
        <bp-button
          type="button"
          label="Juez"
          variant="secondary"
          (clicked)="chooseRole('JUDGE')"
        ></bp-button>
      </div>
    </div>
  `,
  styles: `
    .role-select {
      min-height: 100vh;
      display: grid;
      place-items: center;
      text-align: center;
      padding: var(--spacing-12);
      background: var(--color-bp-hueso-50);
    }

    .role-select__lead {
      color: var(--color-bp-text-muted);
      max-width: 40ch;
      margin: 0 0 var(--spacing-6);
    }

    .role-select__options {
      display: flex;
      gap: var(--spacing-4);
      justify-content: center;
      flex-wrap: wrap;
    }
  `,
})
export class RoleSelectComponent {
  private readonly activeRole = inject(ActiveRoleService);
  private readonly router = inject(Router);

  protected chooseRole(role: AppRole): void {
    this.activeRole.setActiveRole(role);
    void this.router.navigateByUrl(landingPathFor(role));
  }
}
