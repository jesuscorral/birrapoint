import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'bp-topbar',
  standalone: true,
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="topbar">
      <a class="topbar__brand" [routerLink]="homeLink()">
        <span class="topbar__logo" aria-hidden="true">
          <svg
            width="18"
            height="18"
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
      </a>

      @if (title()) {
        <span class="topbar__title">{{ title() }}</span>
      }

      <div class="topbar__actions">
        <ng-content></ng-content>
      </div>
    </header>
  `,
  styles: [
    `
      .topbar {
        display: flex;
        align-items: center;
        gap: var(--spacing-4);
        height: 64px;
        padding: 0 var(--spacing-6);
        background: var(--color-bp-surface);
        border-bottom: 1px solid var(--color-bp-border);
      }

      .topbar__brand {
        display: inline-flex;
        align-items: center;
        gap: var(--spacing-2);
        font-family: 'Fraunces', serif;
        font-weight: 700;
        font-size: 1.125rem;
        color: var(--color-bp-text);
        text-decoration: none;
        border-radius: 4px;
        flex: none;
      }

      .topbar__brand:focus-visible {
        outline: 2px solid var(--color-bp-cobre-500);
        outline-offset: 2px;
      }

      .topbar__logo {
        width: 30px;
        height: 30px;
        flex: none;
        border-radius: 8px;
        background: linear-gradient(150deg, #d07a4c, #9a4b27);
        display: grid;
        place-items: center;
        box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.32);
      }

      .topbar__title {
        font-size: 0.875rem;
        font-weight: 600;
        color: var(--color-bp-text-muted);
        padding-left: var(--spacing-4);
        border-left: 1px solid var(--color-bp-border);
      }

      .topbar__actions {
        margin-left: auto;
        display: flex;
        align-items: center;
        gap: var(--spacing-3);
      }

      @media (max-width: 640px) {
        .topbar {
          padding: 0 var(--spacing-4);
        }

        .topbar__title {
          display: none;
        }
      }
    `,
  ],
})
export class BpTopbarComponent {
  readonly title = input('');
  readonly homeLink = input('/organizer/dashboard');
}
