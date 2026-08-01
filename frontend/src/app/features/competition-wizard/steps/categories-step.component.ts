import { CdkTrapFocus } from '@angular/cdk/a11y';
import type { OnInit } from '@angular/core';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { Router } from '@angular/router';
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
  imports: [CdkTrapFocus, BpButtonComponent, BpInputComponent, BpAlertComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (loading()) {
      <p class="step-lead" role="status">Cargando…</p>
    } @else {
      <p class="step-lead">
        Crea las categorías de tu competición y asigna los estilos BJCP que quieras incluir. Un
        estilo solo puede pertenecer a una categoría; los que no asignes a ninguna quedan fuera de
        la competición.
      </p>

      <section class="categories-list" aria-label="Categorías de la competición">
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
                (onClick)="stopEditing()"
              ></bp-button>
            } @else {
              <span class="category-row__name">{{ categoryLabel(row, i) }}</span>
              <div class="category-row__actions">
                <bp-button
                  type="button"
                  label="Editar"
                  variant="ghost"
                  [attr.aria-label]="'Editar ' + categoryLabel(row, i)"
                  (onClick)="startEditing(i)"
                ></bp-button>
                <bp-button
                  type="button"
                  label="Eliminar"
                  variant="ghost"
                  [disabled]="categories().length <= 1"
                  [attr.aria-label]="'Eliminar ' + categoryLabel(row, i)"
                  (onClick)="removeCategory(i)"
                ></bp-button>
              </div>
            }
          </div>
        }

        <bp-button
          type="button"
          label="+ Añadir categoría"
          variant="secondary"
          (onClick)="addCategory()"
        ></bp-button>
      </section>

      <section class="style-catalog" aria-label="Catálogo de estilos BJCP">
        <h3 class="section-heading">Asignar estilos a las categorías</h3>
        <p class="style-catalog__lead">
          Elige para cada estilo BJCP a cuál de tus categorías de arriba pertenece, o déjalo "Sin
          asignar" para excluirlo del concurso.
        </p>
        @for (group of groupedCatalog(); track group.categoryName) {
          <fieldset class="style-group">
            <legend>{{ group.categoryName }}</legend>
            <div class="style-group__bulk">
              <span class="style-group__bulk-label">Asignar todo el grupo a</span>
              <select
                #groupSelect
                class="style-group__bulk-select"
                [attr.aria-label]="'Asignar todos los estilos de ' + group.categoryName + ' a'"
                (change)="onBulkAssignGroup(group, groupSelect.value); groupSelect.value = ''"
              >
                <option value="" disabled selected>Asignar todo el grupo a…</option>
                <option value="unassign">Sin asignar</option>
                @for (catRow of categories(); track $index; let ci = $index) {
                  <option [value]="ci">{{ categoryLabel(catRow, ci) }}</option>
                }
              </select>
            </div>
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
          </fieldset>
        }
      </section>

      @if (bannerError(); as message) {
        <bp-alert type="error" title="No hemos podido guardar">{{ message }}</bp-alert>
      }

      <div class="step-actions">
        <div class="step-actions__left">
          <bp-button
            type="button"
            label="← Volver"
            variant="ghost"
            (onClick)="back.emit()"
          ></bp-button>
          <button type="button" class="back-to-list-link" (click)="onRequestBack()">
            ← Volver al listado
          </button>
        </div>
        <bp-button
          type="submit"
          label="Continuar"
          variant="primary"
          [loading]="submitting()"
          [disabled]="!canFinish()"
          (onClick)="onFinish()"
        ></bp-button>
      </div>
    }

    @if (confirmingBack()) {
      <div class="modal-backdrop" role="presentation" (click)="onCancelBackConfirm()">
        <div
          role="alertdialog"
          aria-modal="true"
          aria-label="Cambios sin guardar"
          class="modal-panel"
          cdkTrapFocus
          cdkTrapFocusAutoCapture
          (click)="$event.stopPropagation()"
          (keydown.escape)="onCancelBackConfirm()"
        >
          <h2>¿Guardar los cambios?</h2>
          <p>
            Puedes guardar esta competición como borrador antes de salir, o descartar los cambios y
            volver al listado de competiciones.
          </p>
          @if (!canFinish()) {
            <p class="modal-hint">
              Asigna al menos un estilo a una categoría con nombre para poder guardar como borrador.
            </p>
          }
          <div class="modal-actions">
            <bp-button
              type="button"
              label="Guardar borrador"
              variant="primary"
              [loading]="submitting()"
              [disabled]="!canFinish()"
              (onClick)="onSaveAndLeave()"
            ></bp-button>
            <bp-button
              type="button"
              label="Descartar cambios"
              variant="secondary"
              [disabled]="submitting()"
              (onClick)="onDiscardAndLeave()"
            ></bp-button>
            <bp-button
              type="button"
              label="Cancelar"
              variant="ghost"
              [disabled]="submitting()"
              (onClick)="onCancelBackConfirm()"
            ></bp-button>
          </div>
        </div>
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

      .categories-list {
        display: flex;
        flex-direction: column;
        gap: var(--spacing-2);
        margin-bottom: var(--spacing-4);
      }

      .category-row {
        display: flex;
        align-items: center;
        gap: var(--spacing-3);
        padding: var(--spacing-2) var(--spacing-3);
        border: 1px solid var(--color-bp-border);
        border-radius: var(--radius-md);
      }

      .category-row bp-input {
        flex: 1 1 auto;
      }

      .category-row__name {
        flex: 1 1 auto;
        min-height: 44px;
        display: flex;
        align-items: center;
        font-weight: 600;
        color: var(--color-bp-text);
      }

      .category-row__actions {
        display: flex;
        gap: var(--spacing-2);
      }

      .style-catalog {
        margin-top: var(--spacing-6);
      }

      .style-catalog__lead {
        margin: 0 0 var(--spacing-4);
        color: var(--color-bp-text-muted);
        font-size: 0.875rem;
      }

      .style-group {
        border: 1px solid var(--color-bp-border);
        border-radius: var(--radius-md);
        padding: var(--spacing-4);
        margin-bottom: var(--spacing-4);
      }

      .style-group legend {
        font-weight: 600;
        color: var(--color-bp-text);
        padding: 0 var(--spacing-2);
      }

      .style-row {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: var(--spacing-3);
        padding: var(--spacing-2) 0;
      }

      .style-row__label {
        color: var(--color-bp-text);
      }

      .style-row__select {
        min-height: 44px;
        padding: 0 var(--spacing-3);
        border: 1.5px solid var(--color-bp-border-strong);
        border-radius: var(--radius-md);
        background: var(--color-bp-surface);
        color: var(--color-bp-text);
      }

      .style-group__bulk {
        display: flex;
        align-items: center;
        gap: var(--spacing-2);
        margin-bottom: var(--spacing-3);
      }

      .style-group__bulk-label {
        color: var(--color-bp-text-muted);
        font-size: 0.875rem;
      }

      .style-group__bulk-select {
        min-height: 44px;
        padding: 0 var(--spacing-3);
        border: 1.5px solid var(--color-bp-border-strong);
        border-radius: var(--radius-md);
        background: var(--color-bp-surface);
        color: var(--color-bp-text);
      }

      .step-actions {
        display: flex;
        justify-content: space-between;
        align-items: center;
        flex-wrap: wrap;
        gap: var(--spacing-3);
        margin-top: var(--spacing-6);
        padding-top: var(--spacing-6);
        border-top: 1px solid var(--color-bp-border);
      }

      .step-actions__left {
        display: flex;
        align-items: center;
        gap: var(--spacing-2);
      }

      .back-to-list-link {
        display: inline-flex;
        align-items: center;
        min-height: 44px;
        padding: 0 var(--spacing-3);
        border-radius: var(--radius-md);
        color: var(--color-bp-text-muted);
        font-weight: 600;
        text-decoration: none;
        transition:
          background 0.15s ease,
          color 0.15s ease;
      }

      .back-to-list-link:hover {
        background: var(--color-bp-hueso-100);
        color: var(--color-bp-text);
      }

      .back-to-list-link:focus-visible {
        outline: 2px solid var(--color-bp-cobre-500);
        outline-offset: 2px;
      }

      .modal-backdrop {
        position: fixed;
        inset: 0;
        background: rgba(4, 23, 18, 0.45);
        display: flex;
        align-items: center;
        justify-content: center;
        padding: var(--spacing-4);
        z-index: 10;
      }

      .modal-panel {
        background: var(--color-bp-surface);
        border-radius: var(--radius-lg);
        box-shadow: var(--shadow-lg);
        padding: var(--spacing-6);
        max-width: 26rem;
      }

      .modal-panel h2 {
        font-family: 'Fraunces', serif;
        font-size: 1.25rem;
        margin: 0 0 var(--spacing-3);
        color: var(--color-bp-text);
      }

      .modal-panel p {
        color: var(--color-bp-text-muted);
        margin: 0 0 var(--spacing-4);
      }

      .modal-hint {
        color: var(--color-bp-danger-600) !important;
        font-size: 0.875rem;
      }

      .modal-actions {
        display: flex;
        flex-wrap: wrap;
        gap: var(--spacing-3);
        margin-top: var(--spacing-6);
      }
    `,
  ],
})
export class CategoriesStepComponent implements OnInit {
  private readonly catalogApi = inject(CatalogApiService);
  private readonly competitionsApi = inject(CompetitionsApiService);
  private readonly router = inject(Router);

  readonly competitionId = input.required<string>();
  readonly saved = output<void>();
  readonly back = output<void>();

  protected readonly loading = signal(false);
  protected readonly catalog = signal<StyleSummary[]>([]);
  protected readonly categories = signal<CategoryRow[]>([]);
  protected readonly submitting = signal(false);
  protected readonly apiError = signal<ApiError | null>(null);
  protected readonly confirmingBack = signal(false);
  protected readonly editingIndex = signal<number | null>(null);

  protected readonly groupedCatalog = computed<StyleGroup[]>(() => {
    const groups = new Map<string, StyleSummary[]>();
    for (const style of this.catalog()) {
      const list = groups.get(style.categoryName) ?? [];
      list.push(style);
      groups.set(style.categoryName, list);
    }
    return Array.from(groups.entries()).map(([categoryName, styles]) => ({
      categoryName,
      styles,
    }));
  });

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
    if (selectedValue === '') {
      return;
    }
    // '' is the bulk control's own "no action chosen" placeholder, distinct from onAssignStyle's
    // '' (its "unassign" sentinel) — hence the bulk control uses 'unassign' for that case instead.
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

  protected onFinish(): void {
    if (!this.canFinish() || this.submitting()) {
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

  protected onRequestBack(): void {
    this.confirmingBack.set(true);
  }

  protected onCancelBackConfirm(): void {
    this.confirmingBack.set(false);
  }

  protected onDiscardAndLeave(): void {
    this.confirmingBack.set(false);
    this.router.navigateByUrl('/organizer/dashboard');
  }

  protected onSaveAndLeave(): void {
    if (!this.canFinish() || this.submitting()) {
      return;
    }

    this.confirmingBack.set(false);
    this.submitting.set(true);
    this.apiError.set(null);

    this.competitionsApi.setCategories(this.competitionId(), this.buildPayload()).subscribe({
      next: () => {
        this.submitting.set(false);
        this.router.navigateByUrl('/organizer/dashboard');
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
