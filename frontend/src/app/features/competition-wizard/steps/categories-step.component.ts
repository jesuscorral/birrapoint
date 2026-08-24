import type { OnInit } from '@angular/core';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { forkJoin } from 'rxjs';

import { ApiError } from '../../../core/api/api-error';
import { CatalogApiService } from '../../../core/api/catalog-api.service';
import type { StyleSummary } from '../../../core/api/catalog-api.service';
import { CompetitionsApiService } from '../../../core/api/competitions-api.service';
import type { CompetitionCategoryPayload } from '../../../core/api/competitions-api.service';
import { BpButtonComponent } from '../../../shared/components/bp-button/bp-button.component';
import { BpInputComponent } from '../../../shared/components/bp-input/bp-input.component';
import { BpAlertComponent } from '../../../shared/components/bp-alert/bp-alert.component';

interface CategoryRow {
  id: string | null;
  name: string;
  displayOrder: number;
  styleCodes: string[];
}

interface StyleGroup {
  categoryName: string;
  styles: StyleSummary[];
}

function toGenericApiError(error: unknown): ApiError {
  return error instanceof ApiError
    ? error
    : new ApiError({ status: 0, title: 'An unexpected error occurred.', urn: null });
}

@Component({
  selector: 'app-categories-step',
  imports: [BpButtonComponent, BpInputComponent, BpAlertComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (loading()) {
      <p class="step-lead" role="status">Cargando…</p>
    } @else {
      <p class="step-lead">
        Crea las categorías de tu competición y asigna a cada una los estilos BJCP que quieras
        incluir. Un estilo solo puede pertenecer a una categoría.
      </p>

      <div class="styles-layout">
        <section class="categories-panel" aria-label="Categorías de la competición">
          <h3 class="section-heading">Categorías del concurso</h3>
          @for (row of categories(); track $index; let i = $index) {
            <div class="category-row">
              @if (editingIndex() === i) {
                <bp-input
                  [id]="'category-name-' + i"
                  label="Nombre de la categoría"
                  [value]="row.name"
                  (valueChange)="updateCategoryName(i, $event)"
                ></bp-input>
                <bp-button
                  type="button"
                  label="Listo"
                  variant="secondary"
                  (clicked)="stopEditing()"
                ></bp-button>
              } @else {
                <span class="category-row__name">{{ categoryLabel(row, i) }}</span>
                <span class="category-row__count">{{ row.styleCodes.length }}</span>
                <div class="category-row__actions">
                  <bp-button
                    type="button"
                    label="Editar"
                    variant="ghost"
                    [ariaLabel]="'Editar ' + categoryLabel(row, i)"
                    (clicked)="startEditing(i)"
                  ></bp-button>
                  <bp-button
                    type="button"
                    label="Eliminar"
                    variant="ghost"
                    [disabled]="categories().length <= 1"
                    [ariaLabel]="'Eliminar ' + categoryLabel(row, i)"
                    (clicked)="removeCategory(i)"
                  ></bp-button>
                </div>
              }
            </div>
          }

          <bp-button
            type="button"
            label="+ Añadir categoría"
            variant="secondary"
            (clicked)="addCategory()"
          ></bp-button>
        </section>

        <section class="style-catalog" aria-label="Catálogo de estilos BJCP">
          <div class="style-catalog__toolbar">
            <div>
              <h3 class="section-heading">Asignar estilos a las categorías</h3>
              <p class="style-catalog__summary">
                {{ assignedTotal() }} de {{ catalog().length }} estilos asignados. Los que dejes
                "Sin asignar" quedan fuera del concurso.
              </p>
            </div>
            <div class="style-catalog__controls">
              <input
                type="search"
                class="style-catalog__search"
                placeholder="Buscar por código o nombre"
                aria-label="Buscar estilo por código o nombre"
                (input)="filter.set($any($event.target).value)"
              />
              <bp-button
                type="button"
                [label]="expandAll() ? 'Plegar todo' : 'Expandir todo'"
                variant="ghost"
                (clicked)="expandAll.set(!expandAll())"
              ></bp-button>
            </div>
          </div>

          <div class="style-groups">
            @for (group of groupedCatalog(); track group.categoryName; let gi = $index) {
              <div class="style-group" [class.is-open]="isGroupOpen(group)">
                <div class="style-group__header">
                  <button
                    type="button"
                    class="style-group__toggle"
                    [attr.aria-expanded]="isGroupOpen(group)"
                    [attr.aria-controls]="'style-group-rows-' + gi"
                    (click)="toggleGroup(group)"
                  >
                    <span class="style-group__chevron" aria-hidden="true">
                      <svg
                        width="12"
                        height="12"
                        viewBox="0 0 24 24"
                        fill="none"
                        stroke="currentColor"
                        stroke-width="3"
                        stroke-linecap="round"
                        stroke-linejoin="round"
                      >
                        <path d="m9 18 6-6-6-6" />
                      </svg>
                    </span>
                    <span class="style-group__name">{{ group.categoryName }}</span>
                    <span class="style-group__count"
                      >{{ assignedInGroup(group) }}/{{ group.styles.length }}</span
                    >
                  </button>
                  <select
                    class="style-group__bulk-select"
                    [attr.aria-label]="'Asignar todos los estilos de ' + group.categoryName + ' a'"
                    (change)="onBulkAssignGroup(group, $any($event.target).value)"
                  >
                    @if (groupCategoryIndex(group) === 'mixed') {
                      <option value="mixed" disabled [selected]="true">Varias categorías</option>
                    }
                    <option value="" [selected]="groupCategoryIndex(group) === -1">
                      Sin asignar
                    </option>
                    @for (catRow of categories(); track $index; let ci = $index) {
                      <option [value]="ci" [selected]="groupCategoryIndex(group) === ci">
                        {{ categoryLabel(catRow, ci) }}
                      </option>
                    }
                  </select>
                </div>
                <div
                  class="style-group__rows"
                  [id]="'style-group-rows-' + gi"
                  [hidden]="!isGroupOpen(group)"
                >
                  @for (style of group.styles; track style.code) {
                    <div class="style-row">
                      <span class="style-row__label">{{ style.code }} — {{ style.name }}</span>
                      <select
                        class="style-row__select"
                        [attr.aria-label]="'Categoría para ' + style.code + ' ' + style.name"
                        (change)="onAssignStyle(style.code, $any($event.target).value)"
                      >
                        <option value="" [selected]="styleCategoryIndex(style.code) === -1">
                          Sin asignar
                        </option>
                        @for (catRow of categories(); track $index; let ci = $index) {
                          <option [value]="ci" [selected]="styleCategoryIndex(style.code) === ci">
                            {{ categoryLabel(catRow, ci) }}
                          </option>
                        }
                      </select>
                    </div>
                  }
                </div>
              </div>
            } @empty {
              <p class="style-catalog__empty">Ningún estilo coincide con la búsqueda.</p>
            }
          </div>
        </section>
      </div>

      @if (bannerError(); as message) {
        <bp-alert type="error" title="No hemos podido guardar">{{ message }}</bp-alert>
      }

      <!-- Same two-slot "Atrás" / "Siguiente" bottom bar as every other wizard step (see
           details-step.component.ts) — "Siguiente" is never blocked on canFinish(): when it's
           false there's nothing valid to persist yet, so the step simply advances without saving
           and the stepper marker for it stays amber. -->
      <div class="step-actions">
        <bp-button type="button" label="Atrás" variant="ghost" (clicked)="back.emit()"></bp-button>
        <bp-button
          type="submit"
          label="Siguiente"
          variant="primary"
          [loading]="submitting()"
          (clicked)="onFinish()"
        ></bp-button>
      </div>
    }
  `,
  styles: [
    `
      .step-lead {
        margin: 0 0 var(--spacing-6);
        color: var(--color-bp-text-muted);
        font-size: 0.9375rem;
      }

      .section-heading {
        font-family: 'Fraunces', serif;
        font-size: 1rem;
        font-weight: 600;
        color: var(--color-bp-text);
        margin: 0 0 var(--spacing-3);
      }

      /* Organizers create competitions on a desktop, so the catalog gets a persistent
         two-column layout: categories stay visible while styles are assigned on the right. */
      .styles-layout {
        display: grid;
        grid-template-columns: minmax(15rem, 18rem) 1fr;
        gap: var(--spacing-6);
        align-items: start;
      }

      @media (max-width: 900px) {
        .styles-layout {
          grid-template-columns: 1fr;
        }
      }

      .categories-panel {
        display: flex;
        flex-direction: column;
        gap: var(--spacing-2);
        position: sticky;
        top: var(--spacing-4);
      }

      @media (max-width: 900px) {
        .categories-panel {
          position: static;
        }
      }

      .category-row {
        display: flex;
        align-items: center;
        gap: var(--spacing-2);
        padding: 0 var(--spacing-3);
        border: 1px solid var(--color-bp-border);
        border-radius: var(--radius-md);
      }

      .category-row__count {
        flex: none;
        font-size: 0.75rem;
        font-weight: 700;
        color: var(--color-bp-text-muted);
        background: var(--color-bp-hueso-100);
        border-radius: var(--radius-full);
        padding: 2px var(--spacing-2);
      }

      .category-row bp-input {
        flex: 1 1 auto;
      }

      .category-row__name {
        flex: 1 1 auto;
        min-height: 40px;
        display: flex;
        align-items: center;
        font-weight: 600;
        color: var(--color-bp-text);
      }

      .category-row__actions {
        display: flex;
        gap: var(--spacing-2);
      }

      .style-catalog__toolbar {
        display: flex;
        align-items: flex-start;
        justify-content: space-between;
        flex-wrap: wrap;
        gap: var(--spacing-3);
        margin-bottom: var(--spacing-4);
      }

      .style-catalog__summary {
        margin: 0;
        max-width: 32rem;
        color: var(--color-bp-text-muted);
        font-size: 0.875rem;
      }

      .style-catalog__controls {
        display: flex;
        align-items: center;
        gap: var(--spacing-2);
      }

      .style-catalog__search {
        min-height: 40px;
        min-width: 16rem;
        padding: 0 var(--spacing-3);
        border: 1.5px solid var(--color-bp-border-strong);
        border-radius: var(--radius-md);
        background: var(--color-bp-surface);
        color: var(--color-bp-text);
      }

      .style-catalog__empty {
        color: var(--color-bp-text-muted);
        font-size: 0.875rem;
      }

      /* Collapsed groups tile into columns so the ~35 BJCP groups read as a compact index;
         an open group takes the full row so its style rows keep room to breathe. */
      .style-groups {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(18rem, 1fr));
        gap: var(--spacing-2);
        align-items: start;
      }

      .style-group {
        border: 1px solid var(--color-bp-border);
        border-radius: var(--radius-md);
      }

      .style-group.is-open {
        grid-column: 1 / -1;
      }

      .style-group__header {
        display: flex;
        align-items: center;
        gap: var(--spacing-2);
        padding: var(--spacing-2) var(--spacing-3);
      }

      .style-group__toggle {
        display: flex;
        align-items: center;
        gap: var(--spacing-2);
        flex: 1 1 auto;
        min-width: 0;
        min-height: 36px;
        border: none;
        background: none;
        padding: 0;
        font: inherit;
        font-weight: 600;
        text-align: left;
        color: var(--color-bp-text);
        cursor: pointer;
      }

      .style-group__toggle:focus-visible {
        outline: 2px solid var(--color-bp-cobre-500);
        outline-offset: 2px;
      }

      .style-group__chevron {
        display: grid;
        place-items: center;
        flex: none;
        color: var(--color-bp-text-muted);
        transition: transform 0.15s ease;
      }

      .style-group.is-open .style-group__chevron {
        transform: rotate(90deg);
      }

      .style-group__name {
        flex: 1 1 auto;
        min-width: 0;
      }

      .style-group__count {
        font-size: 0.75rem;
        font-weight: 700;
        color: var(--color-bp-text-muted);
        background: var(--color-bp-hueso-100);
        border-radius: var(--radius-full);
        padding: 2px var(--spacing-2);
      }

      .style-group__rows {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(19rem, 1fr));
        gap: var(--spacing-2) var(--spacing-4);
        padding: var(--spacing-2) var(--spacing-3) var(--spacing-3);
        border-top: 1px solid var(--color-bp-border);
      }

      /* [hidden] loses to the display:grid above, so the collapsed state needs its own rule.
         The rows stay in the DOM (rather than behind an @if) so the header's aria-controls
         target always exists. */
      .style-group__rows[hidden] {
        display: none;
      }

      .style-row {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: var(--spacing-3);
      }

      .style-row__label {
        color: var(--color-bp-text);
        font-size: 0.875rem;
      }

      .style-row__select {
        min-height: 36px;
        max-width: 12rem;
        padding: 0 var(--spacing-2);
        border: 1.5px solid var(--color-bp-border-strong);
        border-radius: var(--radius-md);
        background: var(--color-bp-surface);
        color: var(--color-bp-text);
        font-size: 0.875rem;
      }

      .style-group__bulk-select {
        flex: none;
        width: 9rem;
        min-height: 36px;
        padding: 0 var(--spacing-2);
        border: 1.5px solid var(--color-bp-border-strong);
        border-radius: var(--radius-md);
        background: var(--color-bp-surface);
        color: var(--color-bp-text);
        font-size: 0.875rem;
      }

      .step-actions {
        display: flex;
        justify-content: space-between;
        align-items: center;
        flex-wrap: wrap;
        gap: var(--spacing-3);
        margin: 0 calc(-1 * var(--spacing-8)) calc(-1 * var(--spacing-8));
        padding: var(--spacing-4) var(--spacing-8) var(--spacing-6);
        border-top: 1px solid var(--color-bp-border);
        position: sticky;
        bottom: 0;
        background: var(--color-bp-surface);
        z-index: 1;
      }

      @media (max-width: 640px) {
        .step-actions {
          margin: 0 calc(-1 * var(--spacing-6)) calc(-1 * var(--spacing-6));
          padding: var(--spacing-4) var(--spacing-6) var(--spacing-6);
        }
      }
    `,
  ],
})
export class CategoriesStepComponent implements OnInit {
  private readonly catalogApi = inject(CatalogApiService);
  private readonly competitionsApi = inject(CompetitionsApiService);

  readonly competitionId = input.required<string>();
  readonly saved = output<void>();
  readonly back = output<void>();
  // See basics-step.component.ts for why the wizard shell needs this (FR-007 stay-or-discard
  // prompt on Back/stepper navigation). There's no reactive form here, so dirtiness is tracked by
  // diffing the current categories() against a snapshot taken right after the initial load.
  readonly dirtyChange = output<boolean>();
  // Drives the stepper marker colour in the wizard shell: green once every BJCP style in the
  // catalog has been assigned to a category, orange while any remain "Sin asignar".
  readonly statusChange = output<'complete' | 'partial'>();

  protected readonly loading = signal(false);
  protected readonly catalog = signal<StyleSummary[]>([]);
  protected readonly categories = signal<CategoryRow[]>([]);
  protected readonly submitting = signal(false);
  protected readonly apiError = signal<ApiError | null>(null);
  protected readonly editingIndex = signal<number | null>(null);
  protected readonly filter = signal('');
  protected readonly expandAll = signal(false);
  private readonly loadedSnapshot = signal<string>('');

  // Groups start collapsed so the ~30 BJCP groups read as a short overview instead of a
  // page-long list of selects; an active search always reveals what it matched. Assigning a whole
  // group never needs the detail open — that select lives in the always-visible header.
  protected readonly openGroups = signal<ReadonlySet<string>>(new Set());

  protected readonly assignedTotal = computed(() =>
    this.categories().reduce((total, row) => total + row.styleCodes.length, 0),
  );

  protected readonly isDirty = computed(
    () => JSON.stringify(this.categories()) !== this.loadedSnapshot(),
  );

  // Mirrors canFinish() — the same "at least one named category with at least one style
  // assigned" gate that already governs whether this step's data is worth persisting — rather
  // than requiring every catalog style to be assigned, since leaving styles unassigned is a
  // valid, deliberate choice (FR-052) and not a missing required field.
  protected readonly categoryStatus = computed<'complete' | 'partial'>(() =>
    this.canFinish() ? 'complete' : 'partial',
  );

  protected readonly groupedCatalog = computed<StyleGroup[]>(() => {
    const query = this.filter().trim().toLowerCase();
    const groups = new Map<string, StyleSummary[]>();
    for (const style of this.catalog()) {
      if (
        query &&
        !style.code.toLowerCase().includes(query) &&
        !style.name.toLowerCase().includes(query)
      ) {
        continue;
      }
      const list = groups.get(style.categoryName) ?? [];
      list.push(style);
      groups.set(style.categoryName, list);
    }
    return Array.from(groups.entries()).map(([categoryName, styles]) => ({
      categoryName,
      styles,
    }));
  });

  constructor() {
    effect(() => {
      this.dirtyChange.emit(this.isDirty());
    });
    effect(() => {
      this.statusChange.emit(this.categoryStatus());
    });
  }

  ngOnInit(): void {
    this.loading.set(true);
    forkJoin({
      catalog: this.catalogApi.getStyles(),
      categoriesResponse: this.competitionsApi.getCategories(this.competitionId()),
    }).subscribe({
      next: ({ catalog, categoriesResponse }) => {
        this.catalog.set(catalog);
        const loaded = categoriesResponse.categories.map((category) => ({
          id: category.id,
          name: category.name,
          displayOrder: category.displayOrder,
          styleCodes: [...category.styleCodes],
        }));
        // FR-052: the step always has at least one category — a brand-new competition starts with
        // a default "General" one (organizer can rename/remove it like any other, as long as at
        // least one remains) rather than an empty list with no obvious first move.
        this.categories.set(
          loaded.length > 0
            ? loaded
            : [{ id: null, name: 'General', displayOrder: 0, styleCodes: [] }],
        );
        this.loadedSnapshot.set(JSON.stringify(this.categories()));
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.apiError.set(toGenericApiError(error));
      },
    });
  }

  protected categoryLabel(row: CategoryRow, index: number): string {
    return row.name.trim() || `Categoría ${index + 1}`;
  }

  protected styleCategoryIndex(code: string): number {
    return this.categories().findIndex((row) => row.styleCodes.includes(code));
  }

  protected assignedInGroup(group: StyleGroup): number {
    return group.styles.filter((style) => this.styleCategoryIndex(style.code) !== -1).length;
  }

  protected isGroupOpen(group: StyleGroup): boolean {
    return (
      this.expandAll() ||
      this.filter().trim().length > 0 ||
      this.openGroups().has(group.categoryName)
    );
  }

  protected toggleGroup(group: StyleGroup): void {
    const next = new Set(this.openGroups());
    if (this.isGroupOpen(group)) {
      next.delete(group.categoryName);
      // A group opened only because "Expandir todo" is on can't be closed while it stays on.
      this.expandAll.set(false);
    } else {
      next.add(group.categoryName);
    }
    this.openGroups.set(next);
  }

  // The group's own <select> doubles as the at-a-glance state of the whole group: a category
  // index when every style in it shares one, -1 when none is assigned, 'mixed' otherwise.
  protected groupCategoryIndex(group: StyleGroup): number | 'mixed' {
    const [first, ...rest] = group.styles.map((style) => this.styleCategoryIndex(style.code));
    if (first === undefined) {
      return -1;
    }
    return rest.every((index) => index === first) ? first : 'mixed';
  }

  protected styleSelectValue(code: string): string {
    const index = this.styleCategoryIndex(code);
    return index === -1 ? '' : index.toString();
  }

  protected onAssignStyle(code: string, selectedIndex: string): void {
    const target = selectedIndex === '' ? -1 : Number(selectedIndex);
    this.categories.update((rows) =>
      rows.map((row, i) => {
        const withoutCode = row.styleCodes.filter((c) => c !== code);
        if (i === target) {
          return { ...row, styleCodes: [...withoutCode, code] };
        }
        return withoutCode.length === row.styleCodes.length
          ? row
          : { ...row, styleCodes: withoutCode };
      }),
    );
  }

  protected onBulkAssignGroup(group: StyleGroup, selectedValue: string): void {
    // 'mixed' is the read-only marker for a partially-assigned group, never an action.
    if (selectedValue === 'mixed') {
      return;
    }
    // Both '' and the legacy 'unassign' sentinel mean "clear the whole group" — '' is what the
    // rendered "Sin asignar" option carries, matching the per-style select's own semantics.
    for (const style of group.styles) {
      this.onAssignStyle(style.code, selectedValue === 'unassign' ? '' : selectedValue);
    }
  }

  protected addCategory(): void {
    this.categories.update((rows) => [
      ...rows,
      { id: null, name: '', displayOrder: rows.length, styleCodes: [] },
    ]);
    this.editingIndex.set(this.categories().length - 1);
  }

  protected removeCategory(index: number): void {
    // FR-052: the step always has at least one category — never remove the last one.
    if (this.categories().length <= 1) {
      return;
    }
    this.categories.update((rows) => rows.filter((_, i) => i !== index));
    if (this.editingIndex() === index) {
      this.editingIndex.set(null);
    }
  }

  protected updateCategoryName(index: number, name: string): void {
    this.categories.update((rows) => rows.map((row, i) => (i === index ? { ...row, name } : row)));
  }

  protected startEditing(index: number): void {
    this.editingIndex.set(index);
  }

  protected stopEditing(): void {
    this.editingIndex.set(null);
  }

  protected canFinish(): boolean {
    const rows = this.categories();
    const hasStyleAssigned = rows.some((row) => row.styleCodes.length > 0);
    const hasNamedCategory = rows.some((row) => row.name.trim().length > 0);
    return hasStyleAssigned && hasNamedCategory;
  }

  protected bannerError(): string | null {
    const error = this.apiError();
    if (!error) {
      return null;
    }
    if (error.errors) {
      const firstMessage = Object.values(error.errors)[0]?.[0];
      return firstMessage ?? error.detail ?? error.title;
    }
    return error.detail ?? error.title;
  }

  // "Siguiente": persists the categories/style assignments when there's something valid to save
  // (canFinish()), then advances either way — an incomplete assignment isn't a reason to block
  // navigation, it just leaves this step's stepper marker amber until the organizer comes back.
  protected onFinish(): void {
    if (this.submitting()) {
      return;
    }

    if (!this.canFinish()) {
      this.saved.emit();
      return;
    }

    this.submitting.set(true);
    this.apiError.set(null);

    this.competitionsApi.setCategories(this.competitionId(), this.buildPayload()).subscribe({
      next: () => {
        this.submitting.set(false);
        this.saved.emit();
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.apiError.set(toGenericApiError(error));
      },
    });
  }

  private buildPayload(): CompetitionCategoryPayload[] {
    return this.categories()
      .filter((row) => row.name.trim().length > 0 || row.styleCodes.length > 0)
      .map((row, i) => ({
        name: row.name.trim() || `Categoría ${i + 1}`,
        displayOrder: i,
        styleCodes: row.styleCodes,
      }));
  }
}
