import { ChangeDetectionStrategy, Component, input, model, signal } from '@angular/core';

// Shared drag-and-drop file picker. Extracted from the entries-import step so every .xlsx upload
// in the wizard (entries, judge roster) presents the same control instead of one step showing a
// styled dropzone and the next a bare native file input.
@Component({
  selector: 'bp-file-dropzone',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <label
      class="dropzone"
      [attr.for]="inputId()"
      [class.is-disabled]="disabled()"
      [class.is-dragging]="dragging()"
      (dragover)="onDragOver($event)"
      (dragleave)="dragging.set(false)"
      (drop)="onDrop($event)"
    >
      <input
        [id]="inputId()"
        class="dropzone__input"
        type="file"
        [accept]="accept()"
        [attr.aria-label]="ariaLabel()"
        [disabled]="disabled()"
        (change)="onFileSelected($event)"
      />
      <svg
        class="dropzone__icon"
        aria-hidden="true"
        width="40"
        height="40"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="1.6"
        stroke-linecap="round"
        stroke-linejoin="round"
      >
        <path d="M14 3v4a1 1 0 0 0 1 1h4" />
        <path d="M17 21H7a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h7l5 5v11a2 2 0 0 1-2 2Z" />
        <path d="M12 11v6" />
        <path d="m9 14 3-3 3 3" />
      </svg>
      @if (file(); as selected) {
        <span class="dropzone__filename">{{ selected.name }}</span>
        <span class="dropzone__hint">{{ sizeLabel(selected) }} · Clic para cambiarlo</span>
      } @else {
        <span class="dropzone__title">{{ prompt() }}</span>
        @if (hint()) {
          <span class="dropzone__hint">{{ hint() }}</span>
        }
      }
    </label>
  `,
  styles: [
    `
      .dropzone {
        position: relative;
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: var(--spacing-2);
        padding: var(--spacing-8) var(--spacing-6);
        border: 2px dashed var(--color-bp-border-strong);
        border-radius: var(--radius-lg);
        background: var(--color-bp-hueso-50);
        text-align: center;
        cursor: pointer;
      }

      .dropzone:hover,
      .dropzone.is-dragging {
        border-color: var(--color-bp-cobre-500);
        background: var(--color-bp-hueso-100);
      }

      /* Clipped rather than hidden (no display:none / opacity:0): the native input keeps its
         keyboard focus and its exposure to assistive tech, which is what makes the dropzone
         operable without a pointer. */
      .dropzone__input {
        position: absolute;
        width: 1px;
        height: 1px;
        margin: -1px;
        padding: 0;
        border: 0;
        overflow: hidden;
        clip-path: inset(50%);
        white-space: nowrap;
      }

      .dropzone:focus-within {
        outline: 2px solid var(--color-bp-cobre-500);
        outline-offset: 2px;
      }

      .dropzone.is-disabled {
        cursor: not-allowed;
        opacity: 0.6;
      }

      .dropzone.is-disabled:hover {
        border-color: var(--color-bp-border-strong);
        background: var(--color-bp-hueso-50);
      }

      .dropzone__icon {
        color: var(--color-bp-cobre-700);
      }

      .dropzone__title,
      .dropzone__filename {
        font-weight: 600;
        color: var(--color-bp-text);
      }

      .dropzone__filename {
        word-break: break-all;
      }

      .dropzone__hint {
        color: var(--color-bp-text-muted);
        font-size: 0.875rem;
      }
    `,
  ],
})
export class BpFileDropzoneComponent {
  /** Id of the inner <input type="file">; E2E and label[for] association both target it. */
  readonly inputId = input.required<string>();
  /** Accessible name of the file input — the only label this control exposes. */
  readonly ariaLabel = input.required<string>();
  readonly accept = input('.xlsx');
  readonly prompt = input('Arrastra el archivo aquí o haz clic para elegirlo');
  readonly hint = input('');
  readonly disabled = input(false);

  readonly file = model<File | null>(null);

  protected readonly dragging = signal(false);

  protected sizeLabel(file: File): string {
    return `${Math.max(1, Math.round(file.size / 1024))} KB`;
  }

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.file.set(input.files?.[0] ?? null);
  }

  protected onDragOver(event: DragEvent): void {
    if (this.disabled()) {
      return;
    }
    event.preventDefault();
    this.dragging.set(true);
  }

  protected onDrop(event: DragEvent): void {
    if (this.disabled()) {
      return;
    }
    event.preventDefault();
    this.dragging.set(false);
    const dropped = event.dataTransfer?.files?.[0];
    if (dropped) {
      this.file.set(dropped);
    }
  }
}
