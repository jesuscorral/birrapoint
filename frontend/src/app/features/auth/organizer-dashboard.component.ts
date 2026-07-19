import { ChangeDetectionStrategy, Component } from '@angular/core';

// Post-login landing for the ORGANIZER role (T024). Real dashboard content lands in Phase 11
// (US9, monitoring dashboard) — this only proves the routing/guard wiring for quickstart scenario 1.
@Component({
  selector: 'app-organizer-dashboard',
  template: `<h1>Organizer dashboard</h1>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrganizerDashboardComponent {}
