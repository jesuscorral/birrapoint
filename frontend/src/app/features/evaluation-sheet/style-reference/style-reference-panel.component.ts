import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';

import { ApiError } from '../../../core/api/api-error';
import { CatalogApiService } from '../../../core/api/catalog-api.service';
import type { StyleDetail } from '../../../core/api/catalog-api.service';

function toGenericApiError(error: unknown): ApiError {
  return error instanceof ApiError
    ? error
    : new ApiError({ status: 0, title: 'An unexpected error occurred.', urn: null });
}

function errorMessage(error: ApiError): string {
  return error.detail ?? error.title;
}

// T060B/FR-049: collapsible, read-only BJCP 2021 guide-description panel inside the evaluation
// sheet. Public reference data, not competition- or entrant-specific — no BR-01/FR-019 anonymity
// concern (unlike everything else on this screen, which must never render an entrant field).
//
// Caching is deliberately two-layered and both layers are intentionally minimal, not a persistent
// Dexie-backed cache (that's a bigger architectural commitment than this task calls for):
// CatalogApiService itself caches per style code in memory for its own (root, session-long)
// lifetime; this component additionally never calls it more than once per instance (`detail`
// signal gates the fetch), so toggling the panel closed and back open is always instant and never
// re-subscribes, whether or not the underlying service call would have hit the network again.
@Component({
  selector: 'app-style-reference-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="style-reference-panel">
      <button
        type="button"
        class="style-reference-toggle"
        [attr.aria-expanded]="expanded()"
        aria-controls="style-reference-content"
        (click)="toggle()"
      >
        {{ expanded() ? 'Hide' : 'Show' }} style guide: {{ styleName() }} ({{ styleCode() }})
      </button>

      @if (expanded()) {
        <div id="style-reference-content" class="style-reference-content">
          @if (loading()) {
            <p>Loading style guide…</p>
          }
          @if (loadError(); as message) {
            <p role="alert">{{ message }}</p>
          }
          @if (detail(); as style) {
            <dl class="vital-statistics">
              <dt>OG</dt>
              <dd>{{ style.vitalStatistics.ogLow }} – {{ style.vitalStatistics.ogHigh }}</dd>
              <dt>FG</dt>
              <dd>{{ style.vitalStatistics.fgLow }} – {{ style.vitalStatistics.fgHigh }}</dd>
              <dt>IBU</dt>
              <dd>{{ style.vitalStatistics.ibuLow }} – {{ style.vitalStatistics.ibuHigh }}</dd>
              <dt>SRM</dt>
              <dd>{{ style.vitalStatistics.srmLow }} – {{ style.vitalStatistics.srmHigh }}</dd>
              <dt>ABV</dt>
              <dd>{{ style.vitalStatistics.abvLow }}% – {{ style.vitalStatistics.abvHigh }}%</dd>
            </dl>

            <h3>Overall Impression</h3>
            <p>{{ style.description.overallImpression }}</p>
            <h3>Aroma</h3>
            <p>{{ style.description.aroma }}</p>
            <h3>Appearance</h3>
            <p>{{ style.description.appearance }}</p>
            <h3>Flavor</h3>
            <p>{{ style.description.flavor }}</p>
            <h3>Mouthfeel</h3>
            <p>{{ style.description.mouthfeel }}</p>
            <h3>Comments</h3>
            <p>{{ style.description.comments }}</p>
            <h3>History</h3>
            <p>{{ style.description.history }}</p>
            <h3>Characteristic Ingredients</h3>
            <p>{{ style.description.characteristicIngredients }}</p>
            <h3>Style Comparison</h3>
            <p>{{ style.description.styleComparison }}</p>
            @if (style.description.entryInstructions) {
              <h3>Entry Instructions</h3>
              <p>{{ style.description.entryInstructions }}</p>
            }
            @if (style.description.commercialExamples.length > 0) {
              <h3>Commercial Examples</h3>
              <ul>
                @for (example of style.description.commercialExamples; track example) {
                  <li>{{ example }}</li>
                }
              </ul>
            }
            @if (style.description.tags.length > 0) {
              <p class="style-tags">Tags: {{ style.description.tags.join(', ') }}</p>
            }
          }
        </div>
      }
    </div>
  `,
  styles: `
    .style-reference-panel {
      margin: 1rem 0;
      border: 1px solid #d1d5db;
      border-radius: 0.5rem;
    }

    .style-reference-toggle {
      width: 100%;
      text-align: left;
      padding: 0.75rem 1rem;
      background: #f9fafb;
      border: none;
      border-radius: 0.5rem;
      font-weight: 600;
      cursor: pointer;
    }

    .style-reference-content {
      padding: 0 1rem 1rem;
    }

    .vital-statistics {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 0.25rem 0.75rem;
    }

    .vital-statistics dt {
      font-weight: 600;
    }
  `,
})
export class StyleReferencePanelComponent {
  private readonly catalog = inject(CatalogApiService);

  readonly styleCode = input.required<string>();
  readonly styleName = input.required<string>();

  protected readonly expanded = signal(false);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly detail = signal<StyleDetail | null>(null);

  protected toggle(): void {
    this.expanded.update((value) => !value);
    if (this.expanded() && !this.detail() && !this.loading()) {
      this.fetchDetail();
    }
  }

  private fetchDetail(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.catalog.getStyleDetail(this.styleCode()).subscribe({
      next: (detail) => {
        this.loading.set(false);
        this.detail.set(detail);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(errorMessage(toGenericApiError(error)));
      },
    });
  }
}
