import { ChangeDetectionStrategy, Component, input } from '@angular/core';

import { BpTopbarComponent } from '../bp-topbar/bp-topbar.component';

// T127/FR-061: shared page chrome (topbar + main landmark + gutter rules) extracted from the
// wizard shell so every organizer screen gets the same breathing room and a single `main` per
// page, instead of each feature re-declaring its own copy of the gutter media queries.
@Component({
  selector: 'bp-page-shell',
  standalone: true,
  imports: [BpTopbarComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <bp-topbar [homeLink]="homeLink()" [title]="title()"></bp-topbar>
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
  `,
})
export class BpPageShellComponent {
  readonly homeLink = input('/organizer/dashboard');
  readonly title = input('');
}
