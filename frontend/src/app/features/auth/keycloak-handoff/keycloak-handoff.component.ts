import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import Keycloak from 'keycloak-js';
import { BpButtonComponent } from '../../../shared/components/bp-button/bp-button.component';

@Component({
  selector: 'app-keycloak-handoff',
  standalone: true,
  imports: [CommonModule, BpButtonComponent],
  template: `
    <div class="handoff">
      <div>
        <div class="logo" style="justify-content: center; margin-bottom: 1.5rem">
          <span class="logo__mark" aria-hidden="true">
            <svg
              width="22"
              height="22"
              viewBox="0 0 24 24"
              fill="none"
              stroke="#FBFAF6"
              stroke-width="2"
              stroke-linecap="round"
              stroke-linejoin="round"
            >
              <path d="M7 8h9v11a2 2 0 0 1-2 2H9a2 2 0 0 1-2-2z" />
              <path d="M16 10h2a2 2 0 0 1 2 2v3a2 2 0 0 1-2 2h-2" />
              <path
                d="M7 8a2.5 2.5 0 0 1 .6-3.6A2.5 2.5 0 0 1 11.5 5a2.5 2.5 0 0 1 4.4 1.2A2 2 0 0 1 16 8"
              />
            </svg>
          </span>
          BirraPoint
        </div>
        <span class="spinner" aria-hidden="true"></span>
        <p class="subtitle" style="margin: 1rem 0 0" role="status">Te llevamos al acceso seguro…</p>
        <p class="field__hint" style="max-width: 40ch; margin: 0 auto">
          Tu sesión se gestiona de forma segura. La app nunca ve tu contraseña.
        </p>
        <p style="margin-top: 2rem">
          <bp-button label="Continuar →" variant="ghost" (clicked)="onContinue()"></bp-button>
        </p>
      </div>
    </div>
  `,
  styles: [
    `
      :host {
        display: block;
        background: var(--color-bp-hueso-50);
        min-height: 100vh;
      }

      .handoff {
        min-height: 100vh;
        display: grid;
        place-items: center;
        text-align: center;
        padding: var(--spacing-12);
      }

      .logo {
        display: inline-flex;
        align-items: center;
        gap: var(--spacing-3);
        font-family: 'Fraunces', serif;
        font-weight: 700;
        font-size: 1.375rem;
        letter-spacing: -0.01em;
      }

      .logo__mark {
        width: 40px;
        height: 40px;
        flex: none;
        border-radius: 12px;
        background: linear-gradient(150deg, #d07a4c, #9a4b27);
        display: grid;
        place-items: center;
        box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.32);
      }

      .spinner {
        display: inline-block;
        width: 34px;
        height: 34px;
        border-radius: 50%;
        border: 3px solid var(--color-bp-cobre-500);
        border-top-color: transparent;
        opacity: 1;
        animation: spin 0.7s linear infinite;
      }

      @keyframes spin {
        to {
          transform: rotate(360deg);
        }
      }

      @media (prefers-reduced-motion: reduce) {
        .spinner {
          animation-duration: 2s;
        }
      }

      .subtitle {
        margin: 0;
        color: var(--color-bp-text-muted);
        font-size: 1rem;
      }

      .field__hint {
        display: block;
        color: var(--color-bp-text-muted);
        font-size: 0.875rem;
      }
    `,
  ],
})
export class KeycloakHandoffComponent implements OnInit {
  private keycloak = inject(Keycloak);
  private router = inject(Router);

  ngOnInit(): void {
    // Keycloak's redirect_uri lands back here with an existing SSO session (post-login callback,
    // or a page reload after auth) — route onward instead of calling login() again, which would
    // otherwise redirect straight back to Keycloak and loop.
    if (this.keycloak.authenticated) {
      this.router.navigateByUrl('/');
      return;
    }

    // Redirige automáticamente a Keycloak login después de un pequeño delay
    setTimeout(() => {
      this.onContinue();
    }, 1500);
  }

  onContinue(): void {
    // Without an explicit redirectUri, keycloak-js defaults to window.location.href — this page
    // itself — so a successful login would bounce back through /auth/handoff a second time before
    // ngOnInit's authenticated-check finally sends it to '/'. Redirecting straight to root skips
    // that extra hop.
    this.keycloak.login({ redirectUri: window.location.origin + '/' });
  }
}
