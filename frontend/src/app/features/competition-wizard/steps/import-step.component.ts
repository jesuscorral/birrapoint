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
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';

import { ApiError } from '../../../core/api/api-error';
import { CatalogApiService } from '../../../core/api/catalog-api.service';
import type { StyleSummary } from '../../../core/api/catalog-api.service';
import { CompetitionsApiService } from '../../../core/api/competitions-api.service';
import type { CompetitionCategory } from '../../../core/api/competitions-api.service';
import { EntriesApiService } from '../../../core/api/entries-api.service';
import type { EntryListItem } from '../../../core/api/entries-api.service';
import { ImportApiService } from '../../../core/api/import-api.service';
import type {
  ConsolidateResult,
  EditImportRowRequest,
  ImportBatch,
  ImportRow,
  ImportRowData,
  ImportRowStatus,
} from '../../../core/api/import-api.service';
import { BpAlertComponent } from '../../../shared/components/bp-alert/bp-alert.component';
import { BpButtonComponent } from '../../../shared/components/bp-button/bp-button.component';
import { BpInputComponent } from '../../../shared/components/bp-input/bp-input.component';
import { BpTextareaComponent } from '../../../shared/components/bp-textarea/bp-textarea.component';
import { StylePickerComponent } from '../../entry-import/style-picker.component';

interface RowDraft {
  participantName: string;
  participantEmail: string;
  acceMemberNumber: string;
  dateOfBirth: string;
  phone: string;
  competitionCategoryId: string;
  styleCode: string;
  submittedAt: string;
  abvPercent: string;
  brewDate: string;
  bottlingDate: string;
  malts: string;
  hops: string;
  yeast: string;
  otherIngredients: string;
  entryInstructions: string;
  beerName: string;
}

const UNRESOLVED_STATUSES = new Set<ImportRowStatus>([
  'StyleMismatch',
  'CategoryMismatch',
  'CategoryStyleMismatch',
  'Invalid',
]);

const STATUS_LABELS: Record<ImportRowStatus, string> = {
  Valid: 'Válida',
  StyleMismatch: 'Estilo no reconocido',
  CategoryMismatch: 'Categoría no reconocida',
  CategoryStyleMismatch: 'Estilo no asignado a la categoría',
  Invalid: 'Incompleta',
  Excluded: 'Excluida',
};

function toGenericApiError(error: unknown): ApiError {
  return error instanceof ApiError
    ? error
    : new ApiError({ status: 0, title: 'An unexpected error occurred.', urn: null });
}

function toDraft(data: ImportRowData): RowDraft {
  return {
    participantName: data.participantName ?? '',
    participantEmail: data.participantEmail ?? '',
    acceMemberNumber: data.acceMemberNumber ?? '',
    dateOfBirth: data.dateOfBirth ?? '',
    phone: data.phone ?? '',
    competitionCategoryId: data.competitionCategoryId ?? '',
    styleCode: data.resolvedStyleCode ?? '',
    submittedAt: data.submittedAt ?? '',
    abvPercent:
      data.abvPercent !== null && data.abvPercent !== undefined ? String(data.abvPercent) : '',
    brewDate: data.brewDate ?? '',
    bottlingDate: data.bottlingDate ?? '',
    malts: data.malts ?? '',
    hops: data.hops ?? '',
    yeast: data.yeast ?? '',
    otherIngredients: data.otherIngredients ?? '',
    entryInstructions: data.entryInstructions ?? '',
    beerName: data.beerName ?? '',
  };
}

function toEditRequest(draft: RowDraft): EditImportRowRequest {
  return {
    participantName: draft.participantName.trim(),
    participantEmail: draft.participantEmail.trim() || null,
    acceMemberNumber: draft.acceMemberNumber.trim() || null,
    dateOfBirth: draft.dateOfBirth || null,
    phone: draft.phone.trim() || null,
    competitionCategoryId: draft.competitionCategoryId || null,
    styleCode: draft.styleCode || null,
    submittedAt: draft.submittedAt,
    abvPercent: Number(draft.abvPercent) || 0,
    brewDate: draft.brewDate || null,
    bottlingDate: draft.bottlingDate || null,
    malts: draft.malts.trim() || null,
    hops: draft.hops.trim() || null,
    yeast: draft.yeast.trim() || null,
    otherIngredients: draft.otherIngredients.trim() || null,
    entryInstructions: draft.entryInstructions.trim() || null,
    beerName: draft.beerName.trim() || null,
  };
}

@Component({
  selector: 'app-import-step',
  imports: [
    FormsModule,
    BpButtonComponent,
    BpInputComponent,
    BpTextareaComponent,
    BpAlertComponent,
    StylePickerComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (loading()) {
      <p class="step-lead" role="status">Cargando…</p>
    } @else {
      <p class="step-lead">
        Sube el archivo de inscripciones (formato ACCE, .xlsx) para crear las cervezas de esta
        competición a partir de las categorías definidas en el paso anterior.
      </p>

      @if (loadError(); as err) {
        <bp-alert type="error" title="No hemos podido cargar los datos">{{
          bannerMessage(err)
        }}</bp-alert>
      }

      @if (categories().length === 0) {
        <bp-alert type="info" title="Falta al menos una categoría">
          Antes de importar cervezas, crea al menos una categoría en el paso 3.
        </bp-alert>
      }

      @if (!importBatch()) {
        @if (alreadyImportedEntries().length > 0) {
          <section class="existing-entries" aria-label="Cervezas ya importadas">
            <h2 class="existing-entries__title">
              Cervezas ya importadas ({{ alreadyImportedEntries().length }})
            </h2>
            <ul class="existing-entries__list">
              @for (entry of alreadyImportedEntries(); track entry.id) {
                <li>
                  <span class="existing-entries__code">{{ entry.blindCode }}</span>
                  <span class="existing-entries__style"
                    >{{ entry.styleCode }} — {{ entry.styleName }}</span
                  >
                  @if (entry.beerName) {
                    <span class="existing-entries__name">{{ entry.beerName }}</span>
                  }
                </li>
              }
            </ul>
            <div class="step-actions">
              <bp-button
                type="button"
                label="Ir al panel de organizador"
                variant="primary"
                (clicked)="goToDashboard()"
              ></bp-button>
            </div>
          </section>
        }
        <form (ngSubmit)="onUpload()">
          <label class="upload-label" for="import-file">Archivo de inscripciones (.xlsx)</label>
          <input
            id="import-file"
            type="file"
            accept=".xlsx"
            [disabled]="categories().length === 0"
            (change)="onFileSelected($event)"
          />

          @if (uploadError(); as err) {
            <bp-alert type="error" title="No hemos podido subir el archivo">{{
              bannerMessage(err)
            }}</bp-alert>
          }

          <div class="step-actions">
            <bp-button
              type="button"
              label="← Volver"
              variant="ghost"
              (clicked)="back.emit()"
            ></bp-button>
            <bp-button
              type="submit"
              label="Subir archivo"
              variant="primary"
              [loading]="uploading()"
              [disabled]="!selectedFile() || uploading() || categories().length === 0"
            ></bp-button>
          </div>
        </form>
      } @else {
        <section class="import-rows" aria-label="Filas importadas">
          @for (row of importBatch()!.rows; track row.rowNumber; let i = $index) {
            <div class="import-row">
              @if (editingIndex() === i) {
                <div class="import-row__editor">
                  <div class="import-row__grid">
                    <bp-input
                      [id]="'row-name-' + i"
                      label="Nombre del participante"
                      [required]="true"
                      [value]="editDraft()?.participantName ?? ''"
                      (valueChange)="updateDraft('participantName', $event)"
                    ></bp-input>
                    <bp-input
                      [id]="'row-email-' + i"
                      label="Correo electrónico"
                      type="email"
                      [value]="editDraft()?.participantEmail ?? ''"
                      (valueChange)="updateDraft('participantEmail', $event)"
                    ></bp-input>
                    <bp-input
                      [id]="'row-acce-' + i"
                      label="Número de socio ACCE"
                      [value]="editDraft()?.acceMemberNumber ?? ''"
                      (valueChange)="updateDraft('acceMemberNumber', $event)"
                    ></bp-input>
                    <bp-input
                      [id]="'row-phone-' + i"
                      label="Teléfono"
                      type="tel"
                      [value]="editDraft()?.phone ?? ''"
                      (valueChange)="updateDraft('phone', $event)"
                    ></bp-input>
                    <bp-input
                      [id]="'row-dob-' + i"
                      label="Fecha de nacimiento"
                      type="date"
                      [value]="editDraft()?.dateOfBirth ?? ''"
                      (valueChange)="updateDraft('dateOfBirth', $event)"
                    ></bp-input>
                    <bp-input
                      [id]="'row-submitted-' + i"
                      label="Fecha de envío"
                      [required]="true"
                      hint="Formato ISO, p. ej. 2025-09-01T09:21:16Z"
                      [value]="editDraft()?.submittedAt ?? ''"
                      (valueChange)="updateDraft('submittedAt', $event)"
                    ></bp-input>
                    <bp-input
                      [id]="'row-abv-' + i"
                      label="Grado alcohólico (%)"
                      type="number"
                      [required]="true"
                      [value]="editDraft()?.abvPercent ?? ''"
                      (valueChange)="updateDraft('abvPercent', $event)"
                    ></bp-input>
                    <bp-input
                      [id]="'row-beername-' + i"
                      label="Nombre de la cerveza"
                      [value]="editDraft()?.beerName ?? ''"
                      (valueChange)="updateDraft('beerName', $event)"
                    ></bp-input>
                    <bp-input
                      [id]="'row-brewdate-' + i"
                      label="Fecha de elaboración"
                      type="date"
                      [value]="editDraft()?.brewDate ?? ''"
                      (valueChange)="updateDraft('brewDate', $event)"
                    ></bp-input>
                    <bp-input
                      [id]="'row-bottlingdate-' + i"
                      label="Fecha de embotellado"
                      type="date"
                      [value]="editDraft()?.bottlingDate ?? ''"
                      (valueChange)="updateDraft('bottlingDate', $event)"
                    ></bp-input>
                  </div>

                  <bp-textarea
                    [id]="'row-malts-' + i"
                    label="Maltas"
                    [value]="editDraft()?.malts ?? ''"
                    (valueChange)="updateDraft('malts', $event)"
                  ></bp-textarea>
                  <bp-textarea
                    [id]="'row-hops-' + i"
                    label="Lúpulos"
                    [value]="editDraft()?.hops ?? ''"
                    (valueChange)="updateDraft('hops', $event)"
                  ></bp-textarea>
                  <bp-textarea
                    [id]="'row-yeast-' + i"
                    label="Levadura"
                    [value]="editDraft()?.yeast ?? ''"
                    (valueChange)="updateDraft('yeast', $event)"
                  ></bp-textarea>
                  <bp-textarea
                    [id]="'row-other-' + i"
                    label="Otros ingredientes"
                    [value]="editDraft()?.otherIngredients ?? ''"
                    (valueChange)="updateDraft('otherIngredients', $event)"
                  ></bp-textarea>
                  <bp-textarea
                    [id]="'row-instructions-' + i"
                    label="Instrucciones de entrada"
                    [value]="editDraft()?.entryInstructions ?? ''"
                    (valueChange)="updateDraft('entryInstructions', $event)"
                  ></bp-textarea>

                  <div class="import-row__field">
                    <label [for]="'row-category-' + i">Categoría</label>
                    <select
                      [id]="'row-category-' + i"
                      (change)="updateDraft('competitionCategoryId', $any($event.target).value)"
                    >
                      <option value="" [selected]="!editDraft()?.competitionCategoryId">
                        Sin asignar
                      </option>
                      @for (category of categories(); track category.id) {
                        <option
                          [value]="category.id"
                          [selected]="editDraft()?.competitionCategoryId === category.id"
                        >
                          {{ category.name }}
                        </option>
                      }
                    </select>
                    <p class="import-row__raw-hint">
                      Texto original: {{ row.data.category || '—' }}
                    </p>
                  </div>

                  <div class="import-row__field">
                    <span class="import-row__field-label">Estilo</span>
                    <app-style-picker
                      [styles]="styles()"
                      (assign)="updateDraft('styleCode', $event)"
                    ></app-style-picker>
                    <p class="import-row__raw-hint">
                      Texto original: {{ row.data.style || '—' }}. Estilo asignado:
                      {{ editDraft()?.styleCode || 'sin asignar' }}
                    </p>
                  </div>

                  @if (rowError(); as err) {
                    <bp-alert type="error" title="No hemos podido guardar la fila">{{
                      bannerMessage(err)
                    }}</bp-alert>
                  }

                  <div class="import-row__editor-actions">
                    <bp-button
                      type="button"
                      label="Cancelar"
                      variant="ghost"
                      [disabled]="rowSaving()"
                      (clicked)="stopEditing()"
                    ></bp-button>
                    <bp-button
                      type="button"
                      label="Guardar fila"
                      variant="primary"
                      [loading]="rowSaving()"
                      (clicked)="saveRow(i)"
                    ></bp-button>
                  </div>
                </div>
              } @else {
                <div class="import-row__summary">
                  <span class="import-row__number">#{{ row.rowNumber }}</span>
                  <span class="import-row__name">{{
                    row.data.participantName || '(sin nombre)'
                  }}</span>
                  <span class="import-row__category">{{ categoryDisplay(row) }}</span>
                  <span class="import-row__style">{{ styleDisplay(row) }}</span>
                  <span class="status-badge" [class]="'status-badge--' + row.status.toLowerCase()">
                    {{ statusLabel(row.status) }}
                  </span>
                  @if (row.status !== 'Excluded') {
                    <bp-button
                      type="button"
                      label="Editar"
                      variant="ghost"
                      (clicked)="startEditing(i)"
                    ></bp-button>
                    <bp-button
                      type="button"
                      label="Excluir"
                      variant="secondary"
                      [ariaLabel]="'Excluir fila #' + row.rowNumber"
                      [loading]="rowSaving()"
                      (clicked)="excludeRow(i)"
                    ></bp-button>
                  } @else {
                    <span class="import-row__excluded-label">Fila excluida</span>
                  }
                </div>
                @if (row.status !== 'Valid' && row.status !== 'Excluded' && row.error) {
                  <p class="import-row__error">{{ row.error }}</p>
                }
              }
            </div>
          }
        </section>

        <p class="import-unresolved-hint">
          {{ unresolvedCount() }} fila(s) necesitan corrección antes de poder consolidar.
        </p>

        @if (consolidateError(); as err) {
          <bp-alert type="error" title="No hemos podido consolidar">{{
            bannerMessage(err)
          }}</bp-alert>
        }

        @if (consolidateResult(); as result) {
          <bp-alert type="success" title="Importación consolidada">
            Importadas: {{ result.imported }}. Excluidas: {{ result.excluded }}.
          </bp-alert>

          <div class="step-actions">
            <bp-button
              type="button"
              label="Ir al panel de organizador"
              variant="primary"
              (clicked)="goToDashboard()"
            ></bp-button>
          </div>
        } @else {
          <div class="step-actions">
            <bp-button
              type="button"
              label="← Volver"
              variant="ghost"
              (clicked)="back.emit()"
            ></bp-button>
            <bp-button
              type="button"
              label="Consolidar"
              variant="primary"
              [loading]="consolidating()"
              [disabled]="unresolvedCount() > 0 || consolidating()"
              (clicked)="onConsolidate()"
            ></bp-button>
          </div>
        }
      }
    }
  `,
  styles: [
    `
      .step-lead {
        margin: 0 0 var(--spacing-6);
        color: var(--color-bp-text-muted);
        font-size: 0.9375rem;
      }

      form {
        display: flex;
        flex-direction: column;
        gap: var(--spacing-4);
      }

      .upload-label {
        font-weight: 600;
        color: var(--color-bp-text);
      }

      input[type='file'] {
        min-height: 44px;
      }

      .import-rows {
        display: flex;
        flex-direction: column;
        gap: var(--spacing-3);
        margin-bottom: var(--spacing-4);
      }

      .existing-entries {
        margin-bottom: var(--spacing-6);
        padding-bottom: var(--spacing-4);
        border-bottom: 1px solid var(--color-bp-border);
      }

      .existing-entries__title {
        font-size: 1rem;
        font-weight: 600;
        color: var(--color-bp-text);
        margin: 0 0 var(--spacing-3);
      }

      .existing-entries__list {
        list-style: none;
        margin: 0 0 var(--spacing-4);
        padding: 0;
        display: flex;
        flex-direction: column;
        gap: var(--spacing-2);
      }

      .existing-entries__list li {
        display: flex;
        align-items: center;
        gap: var(--spacing-3);
        flex-wrap: wrap;
        font-size: 0.875rem;
      }

      .existing-entries__code {
        font-weight: 600;
        color: var(--color-bp-text);
      }

      .existing-entries__style,
      .existing-entries__name {
        color: var(--color-bp-text-muted);
      }

      .import-row {
        border: 1px solid var(--color-bp-border);
        border-radius: var(--radius-md);
        padding: var(--spacing-3) var(--spacing-4);
      }

      .import-row__summary {
        display: flex;
        align-items: center;
        gap: var(--spacing-3);
        flex-wrap: wrap;
      }

      .import-row__number {
        color: var(--color-bp-text-muted);
        font-weight: 600;
      }

      .import-row__name {
        flex: 1 1 auto;
        font-weight: 600;
        color: var(--color-bp-text);
      }

      .import-row__category,
      .import-row__style {
        color: var(--color-bp-text-muted);
        font-size: 0.875rem;
      }

      .status-badge {
        font-size: 0.75rem;
        font-weight: 700;
        padding: 2px var(--spacing-2);
        border-radius: var(--radius-sm);
        background: var(--color-bp-hueso-200);
        color: var(--color-bp-text);
      }

      .status-badge--valid {
        background: var(--color-bp-exito-50);
        color: #245a3b;
      }

      .status-badge--excluded {
        background: var(--color-bp-hueso-200);
      }

      .status-badge--invalid,
      .status-badge--stylemismatch,
      .status-badge--categorymismatch,
      .status-badge--categorystylemismatch {
        background: var(--color-bp-danger-50);
        color: #7b1e17;
      }

      .import-row__excluded-label {
        color: var(--color-bp-text-muted);
        font-size: 0.875rem;
      }

      .import-row__error {
        margin: var(--spacing-2) 0 0;
        font-size: 0.8125rem;
        font-weight: 500;
        color: var(--color-bp-danger-600);
      }

      .import-row__grid {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: 0 var(--spacing-4);
      }

      @media (max-width: 480px) {
        .import-row__grid {
          grid-template-columns: 1fr;
        }
      }

      .import-row__field {
        margin-bottom: var(--spacing-5);
      }

      .import-row__field label,
      .import-row__field-label {
        display: block;
        font-size: 0.875rem;
        font-weight: 600;
        color: var(--color-bp-text);
        margin-bottom: var(--spacing-2);
      }

      .import-row__field select {
        min-height: 44px;
        width: 100%;
        padding: 0 var(--spacing-3);
        border: 1.5px solid var(--color-bp-border-strong);
        border-radius: var(--radius-md);
        background: var(--color-bp-surface);
        color: var(--color-bp-text);
      }

      .import-row__raw-hint {
        margin: var(--spacing-2) 0 0;
        font-size: 0.8125rem;
        color: var(--color-bp-text-subtle);
      }

      .import-row__editor-actions {
        display: flex;
        justify-content: flex-end;
        gap: var(--spacing-2);
        margin-top: var(--spacing-4);
      }

      .import-unresolved-hint {
        color: var(--color-bp-text-muted);
        font-size: 0.875rem;
        margin: 0 0 var(--spacing-4);
      }

      .step-actions {
        display: flex;
        justify-content: space-between;
        align-items: center;
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
export class ImportStepComponent implements OnInit {
  private readonly importApi = inject(ImportApiService);
  private readonly catalogApi = inject(CatalogApiService);
  private readonly competitionsApi = inject(CompetitionsApiService);
  private readonly entriesApi = inject(EntriesApiService);
  private readonly router = inject(Router);

  readonly competitionId = input.required<string>();
  // Hoisted onto the wizard (see competition-wizard.component.ts) so a pending import batch
  // survives navigating to another wizard step and back (FR-054) — this step itself is destroyed
  // and recreated on every navigation, so it cannot hold that state locally.
  readonly importId = input<string | null>(null);
  readonly importIdChange = output<string>();
  readonly back = output<void>();
  // See basics-step.component.ts for why the wizard shell needs this (FR-007 stay-or-discard
  // prompt on Back/stepper navigation). Unsaved-edit here means either an open row editor whose
  // draft hasn't been saved, or a chosen file not yet uploaded.
  readonly dirtyChange = output<boolean>();

  protected readonly loading = signal(false);
  protected readonly categories = signal<CompetitionCategory[]>([]);
  protected readonly styles = signal<StyleSummary[]>([]);
  protected readonly loadError = signal<ApiError | null>(null);

  protected readonly selectedFile = signal<File | null>(null);
  protected readonly uploading = signal(false);
  protected readonly uploadError = signal<ApiError | null>(null);

  protected readonly importBatch = signal<ImportBatch | null>(null);
  // Consolidated entries already persisted for this competition, independent of any pending
  // import batch — fetched by competitionId alone so they're still visible after reopening the
  // wizard (importId is in-memory-only and does not survive leaving/reloading it).
  protected readonly alreadyImportedEntries = signal<EntryListItem[]>([]);

  protected readonly editingIndex = signal<number | null>(null);
  protected readonly editDraft = signal<RowDraft | null>(null);
  protected readonly rowSaving = signal(false);
  protected readonly rowError = signal<ApiError | null>(null);

  protected readonly consolidating = signal(false);
  protected readonly consolidateError = signal<ApiError | null>(null);
  protected readonly consolidateResult = signal<ConsolidateResult | null>(null);

  protected readonly unresolvedCount = computed(
    () => this.importBatch()?.rows.filter((row) => UNRESOLVED_STATUSES.has(row.status)).length ?? 0,
  );

  protected readonly isDirty = computed(
    () =>
      this.editingIndex() !== null || (this.selectedFile() !== null && this.importBatch() === null),
  );

  constructor() {
    effect(() => {
      this.dirtyChange.emit(this.isDirty());
    });
  }

  ngOnInit(): void {
    this.loading.set(true);
    forkJoin({
      categoriesResponse: this.competitionsApi.getCategories(this.competitionId()),
      styles: this.catalogApi.getStyles(),
      // Purely decorative (shows an "already imported" panel) — a transient failure here must not
      // block the primary category/style load that the rest of the step depends on.
      entries: this.entriesApi.getEntries(this.competitionId()).pipe(catchError(() => of([]))),
    }).subscribe({
      next: ({ categoriesResponse, styles, entries }) => {
        this.categories.set(categoriesResponse.categories);
        this.styles.set(styles);
        this.alreadyImportedEntries.set(entries);

        const importId = this.importId();
        if (importId) {
          // FR-054: returning to this step (e.g. after fixing category/style assignments in
          // wizard step 3) re-validates the pending batch against current data instead of
          // showing an empty upload form again.
          this.importApi.revalidate(this.competitionId(), importId).subscribe({
            next: (batch) => {
              this.loading.set(false);
              this.importBatch.set(batch);
            },
            error: (error: unknown) => {
              this.loading.set(false);
              this.loadError.set(toGenericApiError(error));
            },
          });
        } else {
          this.loading.set(false);
        }
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(toGenericApiError(error));
      },
    });
  }

  protected statusLabel(status: ImportRowStatus): string {
    return STATUS_LABELS[status];
  }

  protected categoryDisplay(row: ImportRow): string {
    const id = row.data.competitionCategoryId;
    if (id) {
      const match = this.categories().find((category) => category.id === id);
      if (match) {
        return match.name;
      }
    }
    return row.data.category ? `${row.data.category} (pendiente)` : 'Sin categoría';
  }

  protected styleDisplay(row: ImportRow): string {
    const code = row.data.resolvedStyleCode;
    if (code) {
      const match = this.styles().find((style) => style.code === code);
      return match ? `${match.code} — ${match.name}` : code;
    }
    return row.data.style ? `${row.data.style} (pendiente)` : 'Sin estilo';
  }

  protected bannerMessage(error: ApiError): string {
    if (error.errors) {
      return Object.values(error.errors)[0]?.[0] ?? error.detail ?? error.title;
    }
    return error.detail ?? error.title;
  }

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile.set(input.files?.[0] ?? null);
  }

  protected onUpload(): void {
    const file = this.selectedFile();
    if (!file || this.uploading() || this.categories().length === 0) {
      return;
    }

    this.uploading.set(true);
    this.uploadError.set(null);

    this.importApi.upload(this.competitionId(), file).subscribe({
      next: (batch) => {
        this.uploading.set(false);
        this.importBatch.set(batch);
        this.consolidateResult.set(null);
        this.importIdChange.emit(batch.importId);
      },
      error: (error: unknown) => {
        this.uploading.set(false);
        this.uploadError.set(toGenericApiError(error));
      },
    });
  }

  protected startEditing(index: number): void {
    const batch = this.importBatch();
    if (!batch) {
      return;
    }
    this.editingIndex.set(index);
    this.editDraft.set(toDraft(batch.rows[index].data));
    this.rowError.set(null);
  }

  protected stopEditing(): void {
    this.editingIndex.set(null);
    this.editDraft.set(null);
    this.rowError.set(null);
  }

  protected updateDraft<K extends keyof RowDraft>(field: K, value: string): void {
    this.editDraft.update((draft) => (draft ? { ...draft, [field]: value } : draft));
  }

  protected saveRow(index: number): void {
    const batch = this.importBatch();
    const draft = this.editDraft();
    if (!batch || !draft || this.rowSaving()) {
      return;
    }

    const row = batch.rows[index];
    this.rowSaving.set(true);
    this.rowError.set(null);

    this.importApi
      .editRow(this.competitionId(), batch.importId, row.rowNumber, toEditRequest(draft))
      .subscribe({
        next: (updatedRow) => {
          this.rowSaving.set(false);
          this.importBatch.set({
            ...batch,
            rows: batch.rows.map((r, i) => (i === index ? updatedRow : r)),
          });
          this.stopEditing();
        },
        error: (error: unknown) => {
          this.rowSaving.set(false);
          this.rowError.set(toGenericApiError(error));
        },
      });
  }

  protected excludeRow(index: number): void {
    const batch = this.importBatch();
    if (!batch || this.rowSaving()) {
      return;
    }

    const row = batch.rows[index];
    this.rowSaving.set(true);
    this.rowError.set(null);

    this.importApi.excludeRow(this.competitionId(), batch.importId, row.rowNumber).subscribe({
      next: (updatedRow) => {
        this.rowSaving.set(false);
        this.importBatch.set({
          ...batch,
          rows: batch.rows.map((r, i) => (i === index ? updatedRow : r)),
        });
        this.stopEditing();
      },
      error: (error: unknown) => {
        this.rowSaving.set(false);
        this.rowError.set(toGenericApiError(error));
      },
    });
  }

  protected onConsolidate(): void {
    const batch = this.importBatch();
    if (!batch || this.unresolvedCount() > 0 || this.consolidating()) {
      return;
    }

    this.consolidating.set(true);
    this.consolidateError.set(null);

    this.importApi.consolidate(this.competitionId(), batch.importId).subscribe({
      next: (result) => {
        this.consolidating.set(false);
        this.consolidateResult.set(result);
      },
      error: (error: unknown) => {
        this.consolidating.set(false);
        this.consolidateError.set(toGenericApiError(error));
      },
    });
  }

  protected goToDashboard(): void {
    this.router.navigateByUrl('/organizer/dashboard');
  }
}
