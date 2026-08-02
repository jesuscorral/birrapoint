import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import Keycloak from 'keycloak-js';
import { BpButtonComponent } from '../../../shared/components/bp-button/bp-button.component';

@Component({
  selector: 'app-welcome',
  standalone: true,
  imports: [CommonModule, BpButtonComponent],
  template: `
    <div class="auth">
      <!-- Panel de marca -->
      <aside class="brand-panel">
        <svg class="brand-panel__bubbles" aria-hidden="true">
          <circle cx="14%" cy="72%" r="46" fill="rgba(255,255,255,.06)" />
          <circle cx="30%" cy="86%" r="22" fill="rgba(255,255,255,.05)" />
          <circle cx="78%" cy="24%" r="60" fill="rgba(255,255,255,.05)" />
          <circle cx="62%" cy="60%" r="14" fill="rgba(255,255,255,.07)" />
          <circle cx="88%" cy="70%" r="28" fill="rgba(255,255,255,.04)" />
        </svg>

        <div class="logo">
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

        <div>
          <h2 class="brand-panel__claim">Catas a ciegas, sin fricción.</h2>
          <p class="brand-panel__lead">
            Organiza tu concurso cervecero de principio a fin: inscripciones, mesas, jueces y
            resultados. Con anonimato garantizado y evaluación offline.
          </p>

          <ul class="feature-list">
            <li>
              <svg
                width="18"
                height="18"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="2.2"
                stroke-linecap="round"
                stroke-linejoin="round"
                aria-hidden="true"
              >
                <path d="M20 6 9 17l-5-5" />
              </svg>
              Importación de inscripciones validada contra el catálogo BJCP 2021
            </li>
            <li>
              <svg
                width="18"
                height="18"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="2.2"
                stroke-linecap="round"
                stroke-linejoin="round"
                aria-hidden="true"
              >
                <path d="M20 6 9 17l-5-5" />
              </svg>
              Mesas con control de conflicto de interés
            </li>
            <li>
              <svg
                width="18"
                height="18"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="2.2"
                stroke-linecap="round"
                stroke-linejoin="round"
                aria-hidden="true"
              >
                <path d="M20 6 9 17l-5-5" />
              </svg>
              Planillas offline que sincronizan solas al recuperar cobertura
            </li>
          </ul>
        </div>

        <div class="brand-panel__foot">
          <span>© 2026 BirraPoint</span>
          <span>Privacidad</span>
          <span>Términos</span>
        </div>
      </aside>

      <!-- Panel de formulario -->
      <main class="form-panel">
        <div class="form-panel__inner">
          <span class="eyebrow">Bienvenido</span>
          <h1 class="title">Entra en tu concurso</h1>
          <p class="subtitle">
            Accede con tu cuenta o crea una nueva para empezar a organizar tu primera competición.
          </p>

          <bp-button
            label="Iniciar sesión"
            variant="primary"
            size="lg"
            [block]="true"
            (clicked)="onLogin()"
          ></bp-button>

          <div class="divider">o</div>

          <div class="stack-3">
            <button class="role-card" (click)="onRegister()">
              <span class="role-card__icon" aria-hidden="true">
                <svg
                  width="22"
                  height="22"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="2"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                >
                  <path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" />
                  <circle cx="9" cy="7" r="4" />
                  <path d="M19 8v6M22 11h-6" />
                </svg>
              </span>
              <span>
                <span class="role-card__title">Crear cuenta de organizador</span>
                <span class="role-card__desc">
                  Da de alta competiciones, importa inscripciones e invita a tus jueces.
                </span>
              </span>
              <svg
                class="role-card__chev"
                width="20"
                height="20"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="2"
                stroke-linecap="round"
                stroke-linejoin="round"
                aria-hidden="true"
              >
                <path d="m9 18 6-6-6-6" />
              </svg>
            </button>

            <div class="role-card role-card--muted">
              <span class="role-card__icon" aria-hidden="true">
                <svg
                  width="22"
                  height="22"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="2"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                >
                  <path d="M12 3 4 7v5c0 4.4 3.4 8.3 8 9 4.6-.7 8-4.6 8-9V7z" />
                </svg>
              </span>
              <span>
                <span class="role-card__title"
                  >¿Eres juez? <span class="tag">Por invitación</span></span
                >
                <span class="role-card__desc">
                  El organizador te da de alta y recibirás un correo con tus credenciales.
                </span>
              </span>
            </div>
          </div>

          <p class="form-foot">
            ¿Problemas para acceder? <a href="mailto:support@birrapoint.local">Escríbenos</a>
          </p>
        </div>
      </main>
    </div>
  `,
  styles: [
    `
      :host {
        display: block;
        background: #d9e2dd;
        min-height: 100vh;
      }

      .auth {
        position: relative;
        display: grid;
        grid-template-columns: 1.05fr 1fr;
        min-height: 100vh;
      }

      @media (max-width: 768px) {
        .auth {
          grid-template-columns: 1fr;
          min-height: 0;
        }
      }

      /* --- Panel de marca --- */
      .brand-panel {
        position: relative;
        overflow: hidden;
        display: flex;
        flex-direction: column;
        justify-content: space-between;
        padding: var(--spacing-10);
        color: var(--color-bp-text-on-dark);
        background:
          radial-gradient(120% 90% at 12% 8%, rgba(184, 92, 51, 0.32) 0%, transparent 55%),
          radial-gradient(100% 80% at 88% 100%, rgba(46, 107, 87, 0.45) 0%, transparent 62%),
          linear-gradient(160deg, var(--color-bp-verde-600) 0%, var(--color-bp-verde-800) 100%);
      }

      @media (max-width: 768px) {
        .brand-panel {
          padding: var(--spacing-8) var(--spacing-6);
          gap: var(--spacing-6);
        }
      }

      .brand-panel__bubbles {
        position: absolute;
        inset: 0;
        opacity: 0.35;
        pointer-events: none;
      }

      .brand-panel > *:not(.brand-panel__bubbles) {
        position: relative;
        z-index: 1;
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

      .brand-panel__claim {
        font-family: 'Fraunces', serif;
        font-size: 3rem;
        line-height: 1.1;
        font-weight: 600;
        margin: 0 0 var(--spacing-4);
        letter-spacing: -0.02em;
        max-width: 14ch;
      }

      @media (max-width: 768px) {
        .brand-panel__claim {
          font-size: 1.75rem;
        }
      }

      .brand-panel__lead {
        margin: 0;
        max-width: 42ch;
        color: rgba(234, 242, 238, 0.8);
        font-size: 1.125rem;
      }

      @media (max-width: 768px) {
        .brand-panel__lead {
          font-size: 1rem;
        }
      }

      .feature-list {
        list-style: none;
        margin: var(--spacing-8) 0 0;
        padding: 0;
        display: grid;
        gap: var(--spacing-4);
      }

      .feature-list li {
        display: flex;
        gap: var(--spacing-3);
        align-items: flex-start;
        font-size: 0.875rem;
        color: rgba(234, 242, 238, 0.84);
      }

      .feature-list svg {
        flex: none;
        margin-top: 2px;
        color: var(--color-bp-cobre-300);
      }

      .brand-panel__foot {
        display: flex;
        gap: var(--spacing-6);
        font-size: 0.75rem;
        color: rgba(234, 242, 238, 0.52);
      }

      @media (max-width: 768px) {
        .brand-panel__foot,
        .feature-list {
          display: none;
        }
      }

      /* --- Panel de formulario --- */
      .form-panel {
        display: flex;
        flex-direction: column;
        justify-content: center;
        padding: var(--spacing-12) var(--spacing-10);
        background: var(--color-bp-surface);
      }

      @media (max-width: 768px) {
        .form-panel {
          padding: var(--spacing-8) var(--spacing-6) var(--spacing-10);
        }
      }

      .form-panel__inner {
        width: 100%;
        max-width: 27rem;
        margin: 0 auto;
      }

      .eyebrow {
        display: block;
        font-size: 0.75rem;
        font-weight: 600;
        letter-spacing: 0.09em;
        text-transform: uppercase;
        color: var(--color-bp-cobre-600);
        margin-bottom: var(--spacing-2);
      }

      .title {
        font-family: 'Fraunces', serif;
        font-size: 2.25rem;
        line-height: 1.15;
        font-weight: 600;
        letter-spacing: -0.02em;
        margin: 0 0 var(--spacing-3);
      }

      .subtitle {
        margin: 0 0 var(--spacing-8);
        color: var(--color-bp-text-muted);
        font-size: 1rem;
      }

      .divider {
        display: flex;
        align-items: center;
        gap: var(--spacing-4);
        margin: var(--spacing-6) 0;
        color: var(--color-bp-text-subtle);
        font-size: 0.875rem;
      }

      .divider::before,
      .divider::after {
        content: '';
        flex: 1;
        height: 1px;
        background: var(--color-bp-border);
      }

      .stack-3 > * + * {
        margin-top: var(--spacing-3);
      }

      .role-card {
        display: flex;
        gap: var(--spacing-4);
        width: 100%;
        text-align: left;
        padding: var(--spacing-5);
        background: var(--color-bp-surface);
        border: 1.5px solid var(--color-bp-border);
        border-radius: var(--radius-lg);
        cursor: pointer;
        transition:
          border-color 0.15s ease,
          box-shadow 0.15s ease,
          background 0.15s ease;
        appearance: none;
      }

      .role-card:hover {
        border-color: var(--color-bp-cobre-400);
        background: var(--color-bp-cobre-50);
        box-shadow: 0 4px 12px rgba(4, 23, 18, 0.09);
      }

      .role-card:focus-visible {
        outline: none;
        box-shadow:
          0 0 0 3px var(--color-bp-hueso-50),
          0 0 0 5px var(--color-bp-cobre-500);
      }

      .role-card__icon {
        flex: none;
        width: 44px;
        height: 44px;
        display: grid;
        place-items: center;
        border-radius: var(--radius-md);
        background: var(--color-bp-cobre-100);
        color: var(--color-bp-cobre-700);
      }

      .role-card__title {
        display: block;
        font-weight: 700;
        font-size: 1rem;
        color: var(--color-bp-text);
      }

      .role-card__desc {
        display: block;
        margin-top: 2px;
        font-size: 0.875rem;
        color: var(--color-bp-text-muted);
      }

      .role-card__chev {
        margin-left: auto;
        align-self: center;
        color: var(--color-bp-text-subtle);
        flex: none;
      }

      .role-card--muted {
        cursor: default;
        opacity: 0.75;
        background: var(--color-bp-hueso-100);
      }

      .role-card--muted:hover {
        border-color: var(--color-bp-border);
        background: var(--color-bp-hueso-100);
        box-shadow: none;
      }

      .role-card--muted .role-card__icon {
        background: var(--color-bp-verde-100);
        color: var(--color-bp-verde-500);
      }

      .tag {
        display: inline-block;
        margin-left: var(--spacing-2);
        padding: 1px 8px;
        font-size: 11px;
        font-weight: 700;
        letter-spacing: 0.04em;
        text-transform: uppercase;
        border-radius: var(--radius-full);
        background: var(--color-bp-verde-100);
        color: var(--color-bp-verde-500);
        vertical-align: 2px;
      }

      .form-foot {
        margin-top: var(--spacing-8);
        padding-top: var(--spacing-6);
        border-top: 1px solid var(--color-bp-border);
        font-size: 0.875rem;
        color: var(--color-bp-text-muted);
        text-align: center;
      }

      .form-foot a {
        color: var(--color-bp-cobre-600);
        font-weight: 600;
        text-decoration: none;
      }

      .form-foot a:hover {
        text-decoration: underline;
      }

      .form-foot a:focus-visible {
        outline: 2px solid var(--color-bp-cobre-500);
        outline-offset: 2px;
      }
    `,
  ],
})
export class WelcomeComponent {
  private router = inject(Router);
  private keycloak = inject(Keycloak);

  onLogin(): void {
    this.router.navigate(['/auth/handoff']);
  }

  onRegister(): void {
    // keycloak.register() builds the URL from the same client/realm config used by login() —
    // correct client_id, PKCE code_challenge (pkceMethod is set at keycloak.init() time), and the
    // right base path for whatever Keycloak version is actually deployed. Redirecting back to '/'
    // (rather than straight to /organizer/dashboard) lets homeRedirectGuard read the fresh token
    // and route by role — the realm grants ORGANIZER by default to every self-registered user
    // (infra/keycloak/birrapoint-realm.json defaultRole composite), but routing through the guard
    // keeps this the single place that decides where an authenticated caller lands.
    this.keycloak.register({ redirectUri: window.location.origin + '/' });
  }
}
