import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';

import { ApiError } from '../../core/api/api-error';
import { EntryImportApiService } from './entry-import-api.service';
import type { ConsolidateResult, ImportBatch, StyleSummary } from './entry-import-api.service';
import { StylePickerComponent } from './style-picker.component';

function toGenericApiError(error: unknown): ApiError {
  return error instanceof ApiError
    ? error
    : new ApiError({ status: 0, title: 'An unexpected error occurred.', urn: null });
}

function errorMessage(error: ApiError): string {
  return error.detail ?? error.title;
}

const UNRESOLVED_STATUSES = new Set(['StyleMismatch', 'Invalid']);

// T036: upload -> per-row results with Mapping & Correction -> consolidate summary, as one
// signal-driven view swap rather than a full stepper (the three views never need independent
// navigation/back-and-forth beyond the natural upload-once, correct-until-clean, consolidate-once
// flow).
@Component({
  selector: 'app-entry-import',
  imports: [FormsModule, StylePickerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>Import beer entries</h1>

    @if (consolidateResult(); as result) {
      <section aria-label="Consolidation summary">
        <p>Imported: {{ result.imported }}</p>
        <p>Excluded: {{ result.excluded }}</p>
        <table>
          <caption>
            Created entries
          </caption>
          <thead>
            <tr>
              <th scope="col">Blind code</th>
              <th scope="col">Style</th>
            </tr>
          </thead>
          <tbody>
            @for (entry of result.entries; track entry.id) {
              <tr>
                <td>{{ entry.blindCode }}</td>
                <td>{{ entry.styleCode }}</td>
              </tr>
            }
          </tbody>
        </table>
      </section>
    } @else if (importBatch(); as batch) {
      <section aria-label="Import results">
        @if (actionError(); as message) {
          <p role="alert">{{ message }}</p>
        }
        <table>
          <thead>
            <tr>
              <th scope="col">Row</th>
              <th scope="col">Participant</th>
              <th scope="col">Beer</th>
              <th scope="col">Style</th>
              <th scope="col">Status</th>
              <th scope="col">Correction</th>
            </tr>
          </thead>
          <tbody>
            @for (row of batch.rows; track row.rowNumber) {
              <tr [attr.data-row-number]="row.rowNumber">
                <td>{{ row.rowNumber }}</td>
                <td>{{ row.data.participantName }}</td>
                <td>{{ row.data.beerName }}</td>
                <td>{{ row.data.style }}</td>
                <td>
                  {{ row.status }}
                  @if (row.error) {
                    — {{ row.error }}
                  }
                </td>
                <td>
                  @if (row.status === 'StyleMismatch') {
                    <app-style-picker
                      [styles]="styles()"
                      (assign)="onAssignStyle(row.rowNumber, $event)"
                    />
                    <button type="button" (click)="onExclude(row.rowNumber)">Exclude</button>
                  } @else if (row.status === 'Invalid') {
                    <button type="button" (click)="onExclude(row.rowNumber)">Exclude</button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>

        <p>{{ unresolvedCount() }} row(s) need correction before you can consolidate.</p>
        @if (consolidateError(); as message) {
          <p role="alert">{{ message }}</p>
        }
        <button
          type="button"
          [disabled]="unresolvedCount() > 0 || consolidating()"
          (click)="onConsolidate()"
        >
          Consolidate
        </button>
      </section>
    } @else {
      <form (ngSubmit)="onUpload()">
        <label>
          Entries file (.xlsx)
          <input type="file" accept=".xlsx" (change)="onFileSelected($event)" />
        </label>
        @if (uploadError(); as message) {
          <p role="alert">{{ message }}</p>
        }
        <button type="submit" [disabled]="!selectedFile() || uploading()">Upload</button>
      </form>
    }
  `,
})
export class EntryImportComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(EntryImportApiService);

  private readonly competitionId = this.route.snapshot.paramMap.get('id')!;

  protected readonly selectedFile = signal<File | null>(null);
  protected readonly uploading = signal(false);
  protected readonly uploadError = signal<string | null>(null);

  protected readonly importBatch = signal<ImportBatch | null>(null);
  protected readonly styles = signal<StyleSummary[]>([]);
  protected readonly actionError = signal<string | null>(null);

  protected readonly consolidating = signal(false);
  protected readonly consolidateError = signal<string | null>(null);
  protected readonly consolidateResult = signal<ConsolidateResult | null>(null);

  protected readonly unresolvedCount = computed(
    () => this.importBatch()?.rows.filter((row) => UNRESOLVED_STATUSES.has(row.status)).length ?? 0,
  );

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

    this.api.upload(this.competitionId, file).subscribe({
      next: (batch) => {
        this.uploading.set(false);
        this.importBatch.set(batch);
        this.loadStyles();
      },
      error: (error: unknown) => {
        this.uploading.set(false);
        this.uploadError.set(errorMessage(toGenericApiError(error)));
      },
    });
  }

  private loadStyles(): void {
    this.api.getStyles().subscribe({
      next: (styles) => this.styles.set(styles),
      error: () => this.actionError.set('Unable to load the BJCP catalog for style correction.'),
    });
  }

  protected onAssignStyle(rowNumber: number, styleCode: string): void {
    this.resolveRow(rowNumber, 'assign-style', styleCode);
  }

  protected onExclude(rowNumber: number): void {
    this.resolveRow(rowNumber, 'exclude');
  }

  private resolveRow(
    rowNumber: number,
    action: 'assign-style' | 'exclude',
    styleCode?: string,
  ): void {
    const batch = this.importBatch();
    if (!batch) {
      return;
    }

    this.actionError.set(null);

    this.api
      .resolveRow(this.competitionId, batch.importId, rowNumber, action, styleCode)
      .subscribe({
        next: (updatedRow) => {
          this.importBatch.set({
            ...batch,
            rows: batch.rows.map((row) => (row.rowNumber === rowNumber ? updatedRow : row)),
          });
        },
        error: (error: unknown) => {
          this.actionError.set(errorMessage(toGenericApiError(error)));
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

    this.api.consolidate(this.competitionId, batch.importId).subscribe({
      next: (result) => {
        this.consolidating.set(false);
        this.consolidateResult.set(result);
      },
      error: (error: unknown) => {
        this.consolidating.set(false);
        this.consolidateError.set(errorMessage(toGenericApiError(error)));
      },
    });
  }
}
