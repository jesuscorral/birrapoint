import { CdkDrag } from '@angular/cdk/drag-drop';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

import { ClickVsDragDirective } from './click-vs-drag.directive';

// Minimal shape both TableSample (seated) and EntryListItem (unassigned) satisfy.
export interface BeerTokenData {
  id: string;
  blindCode: string;
  // T125c: BJCP style code (e.g. "21A") — the mini variant's visible label, since the pool
  // (where blindCode is how the organizer searches/picks) already shows blindCode.
  styleCode: string;
  notValidForBos: boolean;
  styleName: string;
  abvPercent: number;
  // T124: organizer-defined competition category (wizard step 3); null outside the import flow.
  competitionCategoryName: string | null;
  // T124: BJCP taxonomy category — an independent axis from competitionCategoryName.
  bjcpCategoryNumber: string | null;
  bjcpCategoryName: string | null;
  // T125c: resolved from category-color.ts's buildCategoryColorMap by the caller — always a
  // concrete CSS color, never null (UNCATEGORIZED_COLOR is the caller's own fallback).
  categoryColor: string;
}

// `full` is the "Unassigned" column's card: a beer-glass icon plus everything the organizer needs
// to decide which table a sample belongs on (style, competition category, real ABV). `mini` is the
// seated form inside a MesaCard, where the table's own aggregate stats already carry the balance
// picture and each token only has to stay identifiable and draggable (T124).
export type BeerTokenVariant = 'full' | 'mini';

// T048A/T048B/T048C: a beer draggable used both seated on a MesaCard and in the "Unassigned"
// column — one shared implementation so the click-vs-drag disambiguation and target sizing live in
// exactly one place.
@Component({
  selector: 'app-beer-token',
  standalone: true,
  imports: [CdkDrag, ClickVsDragDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      cdkDrag
      [cdkDragData]="beer().id"
      class="beer-token"
      [class.beer-token--full]="variant() === 'full'"
      [class.beer-token--mini]="variant() === 'mini'"
      [class.beer-token--bos-flagged]="beer().notValidForBos"
      [style.background]="beer().categoryColor"
      [attr.data-entry-id]="beer().id"
      role="button"
      tabindex="0"
      [attr.aria-label]="'Beer ' + beer().blindCode + ' — view details'"
      [attr.aria-describedby]="describedBy()"
      appClickVsDrag
      (appClickVsDrag)="activated.emit()"
    >
      @if (variant() === 'full') {
        <!-- Decorative: everything it depicts is already in the accessible name/description. -->
        <svg class="beer-token__icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
          <path
            d="M6 3h9v3h1.5A3.5 3.5 0 0 1 20 9.5v3a3.5 3.5 0 0 1-3.5 3.5H15v3a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2V3Zm9 5v6h1.5a1.5 1.5 0 0 0 1.5-1.5v-3A1.5 1.5 0 0 0 16.5 8H15Z"
            fill="currentColor"
          />
          <path d="M8.5 8.5v9M11 8.5v9" stroke="currentColor" stroke-width="1.2" opacity="0.5" />
        </svg>
      }

      <span class="beer-token__body">
        <span class="beer-token__code">
          @if (variant() === 'full') {
            {{ beer().blindCode }}
          } @else {
            {{ beer().styleCode }}
          }
          @if (beer().notValidForBos) {
            <span class="bos-marker" aria-hidden="true">&#9888;</span>
          }
        </span>

        @if (variant() === 'full') {
          <span class="beer-token__style" aria-hidden="true">{{ beer().styleName }}</span>
          <span class="beer-token__meta" aria-hidden="true">
            @if (beer().competitionCategoryName; as category) {
              <span class="beer-token__chip beer-token__chip--category">{{ category }}</span>
            }
            @if (bjcpCategoryLabel(); as bjcp) {
              <span class="beer-token__chip">{{ bjcp }}</span>
            }
            <span class="beer-token__chip beer-token__chip--abv">{{ abvLabel() }}</span>
          </span>
        } @else {
          <!-- Seated on a table the organizer balances mean alcohol, so the graduation stays
               visible — bare percent, no "ABV" suffix, since the pill has no room for a unit the
               number already implies. The screen-reader description below still spells out the
               full "X% ABV" via srDescription(). -->
          <span class="beer-token__abv-mini" aria-hidden="true">{{ abvPercentLabel() }}</span>
        }
      </span>
    </div>

    <!-- WCAG 1.4.1 + 1.3.1: the style/category/ABV shown visually (and the BOS ring) must reach a
         screen reader too. aria-label is E2E-locked to "Beer {code} — view details" across eight
         specs, so this information is attached as a *description* instead of folded into the name. -->
    <span [id]="detailsNoteId()" class="sr-only">{{ srDescription() }}</span>
    @if (beer().notValidForBos) {
      <span [id]="bosNoteId()" class="sr-only">Not valid for Best of Show</span>
    }
  `,
  styles: `
    :host {
      display: contents;
    }

    .beer-token {
      position: relative;
      display: flex;
      align-items: center;
      gap: var(--spacing-2);
      border-radius: var(--radius-md);
      /* Background is the per-category pastel tint set per instance via [style.background]
         (category-color.ts's buildCategoryColorMap, or UNCATEGORIZED_COLOR). Ink stays this one
         dark color against every tint -- computed contrast ranges 11.47:1-13.68:1 across the 8
         palette tints, comfortably clear of WCAG AA's 4.5:1 for this small bold label. */
      color: var(--color-bp-text);
      font-size: 0.8rem;
      font-weight: 700;
      cursor: grab;
      user-select: none;
      padding: var(--spacing-2);
    }

    /* "Unassigned" column card: full width of the panel, ~64px tall so the drag target keeps the
       same generous hit area the 64px square token had. */
    .beer-token--full {
      width: 100%;
      min-height: 64px;
      padding: var(--spacing-2) var(--spacing-3);
      text-align: left;
    }

    /* Seated on a MesaCard: a compact pill, so a table with a dozen samples still fits in the
       board grid without the card growing taller than the viewport. */
    .beer-token--mini {
      min-height: 32px;
      padding: var(--spacing-1) var(--spacing-2);
      font-size: 0.75rem;
      gap: var(--spacing-1);
    }

    .beer-token__icon {
      flex: 0 0 auto;
      width: 22px;
      height: 22px;
    }

    .beer-token__body {
      display: flex;
      flex-direction: column;
      gap: 2px;
      min-width: 0;
    }

    .beer-token--mini .beer-token__body {
      flex-direction: row;
      align-items: baseline;
      gap: var(--spacing-1);
    }

    .beer-token__code {
      font-weight: 700;
      letter-spacing: 0.02em;
      white-space: nowrap;
    }

    .beer-token__style {
      font-weight: 500;
      font-size: 0.75rem;
      line-height: 1.2;
      /* This token now sits on a light pastel category tint, not a dark cobre-700 backdrop --
         text-muted is the same secondary-ink color already used against light surfaces
         elsewhere in this file, and clears AA at this size against every palette tint. */
      color: var(--color-bp-text-muted);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .beer-token__meta {
      display: flex;
      flex-wrap: wrap;
      gap: 3px;
      margin-top: 2px;
    }

    /* Opaque, not white-with-alpha: composited over --color-bp-cobre-700 the alpha versions came
       out at 4.23:1 and 3.15:1, under the 4.5:1 AA threshold that applies at this 10px size.
       aria-hidden does not exempt them — axe's color-contrast rule evaluates any text visible on
       screen. Opaque fills also keep the ratio fixed if the token background ever changes. */
    .beer-token__chip {
      font-size: 0.625rem;
      font-weight: 600;
      line-height: 1.4;
      padding: 0 5px;
      border-radius: var(--radius-full);
      background: var(--color-bp-cobre-100);
      color: var(--color-bp-cobre-700);
      white-space: nowrap;
      max-width: 9rem;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .beer-token__chip--category {
      background: #fff;
      color: var(--color-bp-cobre-700);
      font-weight: 700;
    }

    /* Overriding only the background here left this chip inheriting cobre-700 text from
       .beer-token__chip, i.e. #9a4b27 on #1f2320 — 2.58:1. Every modifier that changes the fill
       must restate the ink. */
    .beer-token__chip--abv {
      background: var(--color-bp-text);
      color: var(--color-bp-text-on-dark);
    }

    /* Secondary to the style code beside it: lighter weight and muted ink, so the BJCP code still
       reads first. text-muted clears AA against every category tint (same basis as
       .beer-token__style above). */
    .beer-token__abv-mini {
      font-weight: 600;
      font-size: 0.6875rem;
      color: var(--color-bp-text-muted);
      white-space: nowrap;
    }

    .beer-token--bos-flagged {
      /* --color-bp-cobre-700 (#9a4b27) against every palette tint measures 4.44:1-5.29:1 --
         comfortably above WCAG 1.4.11's 3:1 non-text contrast minimum. */
      box-shadow: 0 0 0 2px var(--color-bp-cobre-700) inset;
    }

    .beer-token:focus-visible {
      outline: 2px solid var(--color-bp-cobre-700);
      outline-offset: 2px;
    }

    .bos-marker {
      display: inline-block;
      margin-left: 2px;
      /* --color-bp-cobre-700 (#9a4b27) against every palette tint measures 4.44:1-5.29:1 --
         same ring color/rationale as .beer-token--bos-flagged above. */
      color: var(--color-bp-cobre-700);
      font-size: 0.7rem;
    }
  `,
})
export class BeerTokenComponent {
  readonly beer = input.required<BeerTokenData>();
  readonly variant = input<BeerTokenVariant>('mini');
  readonly activated = output<void>();

  protected readonly bosNoteId = computed(() => `bos-note-${this.beer().id}`);
  protected readonly detailsNoteId = computed(() => `beer-note-${this.beer().id}`);

  // WCAG 1.4.1 (Use of Color): the BOS-flagged state must not be conveyed by the ring color alone
  // (this describedby, plus the visible aria-hidden marker glyph in the template) — the base
  // aria-label deliberately stays exactly "Beer {code} — view details" regardless of flag state,
  // since several E2E specs (us5/us6/us9-tables/-order/-dashboard) locate a flagged beer by that
  // exact accessible name; describedby adds information without changing the name they match on.
  protected readonly describedBy = computed(() =>
    this.beer().notValidForBos
      ? `${this.detailsNoteId()} ${this.bosNoteId()}`
      : this.detailsNoteId(),
  );

  protected readonly abvLabel = computed(() => `${this.beer().abvPercent}% ABV`);

  // Mini (seated) variant only: the bare graduation, no unit suffix — see the template comment.
  protected readonly abvPercentLabel = computed(() => `${this.beer().abvPercent}%`);

  // "21 · IPA" when the catalog row resolved, "IPA"/"21" when only one half is known, null when
  // the style code has no catalog row at all (the DTO's documented null case).
  protected readonly bjcpCategoryLabel = computed(() => {
    const { bjcpCategoryNumber, bjcpCategoryName } = this.beer();
    if (bjcpCategoryNumber && bjcpCategoryName) {
      return `${bjcpCategoryNumber} · ${bjcpCategoryName}`;
    }
    return bjcpCategoryName ?? bjcpCategoryNumber ?? null;
  });

  protected readonly srDescription = computed(() => {
    const beer = this.beer();
    const parts = [beer.styleName];
    if (beer.competitionCategoryName) {
      parts.push(`categoría ${beer.competitionCategoryName}`);
    }
    const bjcp = this.bjcpCategoryLabel();
    if (bjcp) {
      parts.push(`BJCP ${bjcp}`);
    }
    parts.push(this.abvLabel());
    return parts.join(', ');
  });
}
