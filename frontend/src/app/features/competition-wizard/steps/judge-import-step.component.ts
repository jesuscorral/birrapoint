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

import { ApiError } from '../../../core/api/api-error';
import { JudgeImportApiService } from '../../../core/api/judge-import-api.service';
import type {
  EditJudgeImportRowRequest,
  JudgeImportBatch,
  JudgeImportConsolidateResult,
  JudgeImportRowData,
  JudgeImportRowStatus,
} from '../../../core/api/judge-import-api.service';
import { BpAlertComponent } from '../../../shared/components/bp-alert/bp-alert.component';
import { BpButtonComponent } from '../../../shared/components/bp-button/bp-button.component';
import { BpInputComponent } from '../../../shared/components/bp-input/bp-input.component';
import { BpTextareaComponent } from '../../../shared/components/bp-textarea/bp-textarea.component';

interface RowDraft {
  name: string;
  email: string;
  bjcpRank: string;
  bjcpId: string;
  preferredCategory: string;
  preferences: string;
}

const UNRESOLVED_STATUSES = new Set<JudgeImportRowStatus>(['Invalid']);

const STATUS_LABELS: Record<JudgeImportRowStatus, string> = {
  Valid: 'Válida',
  Invalid: 'Incompleta',
  Excluded: 'Excluida',
};

function toGenericApiError(error: unknown): ApiError {
  return error instanceof ApiError
    ? error
    : new ApiError({ status: 0, title: 'An unexpected error occurred.', urn: null });
}

function toDraft(data: JudgeImportRowData): RowDraft {
  return {
    name: data.name ?? '',
    email: data.email ?? '',
    bjcpRank: data.bjcpRank ?? '',
    bjcpId: data.bjcpId ?? '',
    preferredCategory: data.preferredCategory ?? '',
    preferences: data.preferences ?? '',
  };
}

function toEditRequest(draft: RowDraft): EditJudgeImportRowRequest {
  return {
    name: draft.name.trim() || null,
    email: draft.email.trim() || null,
    bjcpRank: draft.bjcpRank.trim() || null,
    bjcpId: draft.bjcpId.trim() || null,
    preferredCategory: draft.preferredCategory.trim() || null,
    preferences: draft.preferences.trim() || null,
  };
}

@Component({
  selector: 'app-judge-import-step',
  imports: [
    FormsModule,
    BpButtonComponent,
    BpInputComponent,
    BpTextareaComponent,
    BpAlertComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (loading()) {
      <p class="step-lead" role="status">Cargando…</p>
    } @else {
      <p class="step-lead">
        Sube el listado de jueces del club (formato .xlsx) para dar de alta sus perfiles en esta
        competición. El envío de la invitación es una acción aparte, disponible después desde la
        gestión de jueces.
      </p>

      @if (loadError(); as err) {
        <bp-alert type="error" title="No hemos podido cargar los datos">{{
          bannerMessage(err)
        }}</bp-alert>
      }

      @if (!importBatch()) {
        <form (ngSubmit)="onUpload()">
          <label class="upload-label" for="judge-import-file">Listado de jueces (.xlsx)</label>
          <input
            id="judge-import-file"
            type="file"
            accept=".xlsx"
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
              [disabled]="!selectedFile() || uploading()"
            ></bp-button>
          </div>
        </form>
      } @else {
        <section class="judge-import-rows" aria-label="Jueces importados">
          @for (row of importBatch()!.rows; track row.rowNumber; let i = $index) {
            <div class="judge-import-row">
              @if (editingIndex() === i) {
                <div class="judge-import-row__editor">
                  <div class="judge-import-row__grid">
                    <bp-input
                      [id]="'judge-row-name-' + i"
                      label="Nombre y apellidos"
                      [required]="true"
                      [value]="editDraft()?.name ?? ''"
                      (valueChange)="updateDraft('name', $event)"
                    ></bp-input>
                    <bp-input
                      [id]="'judge-row-email-' + i"
                      label="Correo electrónico"
                      type="email"
                      [required]="true"
                      [value]="editDraft()?.email ?? ''"
                      (valueChange)="updateDraft('email', $event)"
                    ></bp-input>
                    <bp-input
                      [id]="'judge-row-rank-' + i"
                      label="Rango BJCP"
                      [value]="editDraft()?.bjcpRank ?? ''"
                      (valueChange)="updateDraft('bjcpRank', $event)"
                    ></bp-input>
                    <bp-input
                      [id]="'judge-row-bjcpid-' + i"
                      label="BJCP ID"
                      [value]="editDraft()?.bjcpId ?? ''"
                      (valueChange)="updateDraft('bjcpId', $event)"
                    ></bp-input>
                    <bp-input
                      [id]="'judge-row-category-' + i"
                      label="Categoría preferida"
                      [value]="editDraft()?.preferredCategory ?? ''"
                      (valueChange)="updateDraft('preferredCategory', $event)"
                    ></bp-input>
                  </div>

                  <bp-textarea
                    [id]="'judge-row-preferences-' + i"
                    label="Preferencias"
                    [value]="editDraft()?.preferences ?? ''"
                    (valueChange)="updateDraft('preferences', $event)"
                  ></bp-textarea>

                  @if (rowError(); as err) {
                    <bp-alert type="error" title="No hemos podido guardar la fila">{{
                      bannerMessage(err)
                    }}</bp-alert>
                  }

                  <div class="judge-import-row__editor-actions">
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
                <div class="judge-import-row__summary">
                  <span class="judge-import-row__number">#{{ row.rowNumber }}</span>
                  <span class="judge-import-row__name">{{ row.data.name || '(sin nombre)' }}</span>
                  <span class="judge-import-row__email">{{ row.data.email || 'Sin correo' }}</span>
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
                    <span class="judge-import-row__excluded-label">Fila excluida</span>
                  }
                </div>
                @if (row.status !== 'Valid' && row.status !== 'Excluded' && row.error) {
                  <p class="judge-import-row__error">{{ row.error }}</p>
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
            Creados: {{ result.created.length }}. Actualizados: {{ result.updated.length }}.
            Excluidos: {{ result.excluded }}.
            @if (result.skipped.length > 0) {
              Omitidos: {{ result.skipped.length }} (correos duplicados en el archivo).
            }
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

      .judge-import-rows {
        display: flex;
        flex-direction: column;
        gap: var(--spacing-3);
        margin-bottom: var(--spacing-4);
      }

      .judge-import-row {
        border: 1px solid var(--color-bp-border);
        border-radius: var(--radius-md);
        padding: var(--spacing-3) var(--spacing-4);
      }

      .judge-import-row__summary {
        display: flex;
        align-items: center;
        gap: var(--spacing-3);
        flex-wrap: wrap;
      }

      .judge-import-row__number {
        color: var(--color-bp-text-muted);
        font-weight: 600;
      }

      .judge-import-row__name {
        flex: 1 1 auto;
        font-weight: 600;
        color: var(--color-bp-text);
      }

      .judge-import-row__email {
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

      .status-badge--invalid {
        background: var(--color-bp-danger-50);
        color: #7b1e17;
      }

      .judge-import-row__excluded-label {
        color: var(--color-bp-text-muted);
        font-size: 0.875rem;
      }

      .judge-import-row__error {
        margin: var(--spacing-2) 0 0;
        font-size: 0.8125rem;
        font-weight: 500;
        color: var(--color-bp-danger-600);
      }

      .judge-import-row__grid {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: 0 var(--spacing-4);
      }

      @media (max-width: 480px) {
        .judge-import-row__grid {
          grid-template-columns: 1fr;
        }
      }

      .judge-import-row__editor-actions {
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
export class JudgeImportStepComponent implements OnInit {
  private readonly judgeImportApi = inject(JudgeImportApiService);
  private readonly router = inject(Router);

  readonly competitionId = input.required<string>();
  // Hoisted onto the wizard (see competition-wizard.component.ts) so a pending judge-roster
  // import batch survives navigating to another wizard step and back, same FR-054-style
  // survive-navigation reasoning as importId for step 4 — this step is destroyed and recreated on
  // every wizard step navigation, so it cannot hold that state locally. There is no revalidate
  // endpoint here (no field resolves against data that can change out from under the batch), so
  // returning to this step simply re-fetches the batch's current state.
  readonly judgeImportId = input<string | null>(null);
  readonly judgeImportIdChange = output<string>();
  readonly back = output<void>();
  readonly dirtyChange = output<boolean>();

  protected readonly loading = signal(false);
  protected readonly loadError = signal<ApiError | null>(null);

  protected readonly selectedFile = signal<File | null>(null);
  protected readonly uploading = signal(false);
  protected readonly uploadError = signal<ApiError | null>(null);

  protected readonly importBatch = signal<JudgeImportBatch | null>(null);

  protected readonly editingIndex = signal<number | null>(null);
  protected readonly editDraft = signal<RowDraft | null>(null);
  protected readonly rowSaving = signal(false);
  protected readonly rowError = signal<ApiError | null>(null);

  protected readonly consolidating = signal(false);
  protected readonly consolidateError = signal<ApiError | null>(null);
  protected readonly consolidateResult = signal<JudgeImportConsolidateResult | null>(null);

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
    const judgeImportId = this.judgeImportId();
    if (!judgeImportId) {
      return;
    }

    this.loading.set(true);
    this.judgeImportApi.getImport(this.competitionId(), judgeImportId).subscribe({
      next: (batch) => {
        this.loading.set(false);
        this.importBatch.set(batch);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(toGenericApiError(error));
      },
    });
  }

  protected statusLabel(status: JudgeImportRowStatus): string {
    return STATUS_LABELS[status];
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
    if (!file || this.uploading()) {
      return;
    }

    this.uploading.set(true);
    this.uploadError.set(null);

    this.judgeImportApi.upload(this.competitionId(), file).subscribe({
      next: (batch) => {
        this.uploading.set(false);
        this.importBatch.set(batch);
        this.consolidateResult.set(null);
        this.judgeImportIdChange.emit(batch.importId);
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

    this.judgeImportApi
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

    this.judgeImportApi.excludeRow(this.competitionId(), batch.importId, row.rowNumber).subscribe({
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

    this.judgeImportApi.consolidate(this.competitionId(), batch.importId).subscribe({
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
