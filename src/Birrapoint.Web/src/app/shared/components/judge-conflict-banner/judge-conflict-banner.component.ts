import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

// shared/ never imports core/ (ADR-0014): the host maps the hub event onto this shape.
export interface JudgeConflictBannerItem {
  tableId: string;
  judgeDisplayName: string;
  // null when the host screen does not know the table's name.
  tableName: string | null;
}

// FR-082: tells the organizer that a judge's email correction made them the owner of entries at a
// table where they are seated. One banner per table: a later item for the same table replaces
// the earlier one.
@Component({
  selector: 'bp-judge-conflict-banner',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (conflict of banners(); track conflict.tableId) {
      <div
        role="alert"
        class="mb-4 flex items-start gap-3 rounded-md border border-[#efc9c5] bg-bp-danger-50 p-4 text-sm text-[#7b1e17]"
        [attr.data-table-id]="conflict.tableId"
        (keydown.escape)="dismissed.emit(conflict.tableId)"
      >
        <span class="flex-1">
          <strong class="block font-bold">Conflicto de interés</strong>
          {{ conflict.judgeDisplayName }} ahora es propietario de cervezas en
          {{ conflict.tableName ?? 'una mesa' }}, donde está asignado como juez. Corrige la mesa.
        </span>
        <button
          type="button"
          class="rounded px-2 py-1 font-semibold underline focus-visible:outline-2 focus-visible:outline-offset-2"
          [attr.aria-label]="'Descartar aviso de ' + (conflict.tableName ?? 'una mesa')"
          (click)="dismissed.emit(conflict.tableId)"
        >
          Descartar
        </button>
      </div>
    }
  `,
})
export class JudgeConflictBannerComponent {
  readonly conflicts = input.required<readonly JudgeConflictBannerItem[]>();
  readonly dismissed = output<string>();

  protected readonly banners = computed(() => {
    const byTable = new Map<string, JudgeConflictBannerItem>();
    for (const conflict of this.conflicts()) {
      byTable.delete(conflict.tableId);
      byTable.set(conflict.tableId, conflict);
    }
    return [...byTable.values()];
  });
}
