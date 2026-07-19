import { ChangeDetectionStrategy, Component } from '@angular/core';

// Post-login landing for the JUDGE role (T024). Real tasting-order/evaluation content lands in
// Phase 8 (US6) — this only proves the routing/guard wiring for quickstart scenario 1.
@Component({
  selector: 'app-judge-tables',
  template: `<h1>Judge tables</h1>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class JudgeTablesComponent {}
