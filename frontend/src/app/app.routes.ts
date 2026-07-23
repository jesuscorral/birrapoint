import { Routes } from '@angular/router';

import { AuthPlaceholderComponent } from './core/auth/auth-placeholder.component';
import { homeRedirectGuard } from './core/auth/home-redirect.guard';
import { judgeGuard, organizerGuard } from './core/auth/role.guard';
import { CompetitionMonitorComponent } from './features/dashboard/competition-monitor.component';
import { CompetitionWizardComponent } from './features/competition-wizard/competition-wizard.component';
import { DiscrepancyAlertComponent } from './features/discrepancy/discrepancy-alert.component';
import { EntryImportComponent } from './features/entry-import/entry-import.component';
import { EvaluationSheetComponent } from './features/evaluation-sheet/evaluation-sheet.component';
import { JudgeManagementComponent } from './features/judge-management/judge-management.component';
import { JudgeTableOrderComponent } from './features/judge-tables/judge-table-order.component';
import { JudgeTablesListComponent } from './features/judge-tables/judge-tables-list.component';
import { OrganizerDashboardComponent } from './features/dashboard/organizer-dashboard.component';
import { ResultsDispatchComponent } from './features/results-dispatch/results-dispatch.component';
import { TableManagementComponent } from './features/table-management/table-management.component';

export const routes: Routes = [
  // No component renders here for a recognized role — homeRedirectGuard (T024) always resolves
  // to a UrlTree for ORGANIZER/JUDGE. AuthPlaceholderComponent is the fallback for a caller with
  // neither realm role (shouldn't happen given the backend's deny-by-default policy, but the
  // frontend must still render *something* rather than loop or blank).
  {
    path: '',
    pathMatch: 'full',
    canActivate: [homeRedirectGuard],
    component: AuthPlaceholderComponent,
    data: { label: 'No access' },
  },
  {
    path: 'organizer',
    canActivate: [organizerGuard],
    children: [
      { path: 'dashboard', component: OrganizerDashboardComponent },
      { path: 'competitions/new', component: CompetitionWizardComponent },
      { path: 'competitions/:id', component: CompetitionWizardComponent },
      { path: 'competitions/:id/import', component: EntryImportComponent },
      { path: 'competitions/:id/judges', component: JudgeManagementComponent },
      { path: 'competitions/:id/tables', component: TableManagementComponent },
      { path: 'competitions/:id/monitor', component: CompetitionMonitorComponent },
      { path: 'competitions/:id/dispatch', component: ResultsDispatchComponent },
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
    ],
  },
  {
    path: 'judge',
    canActivate: [judgeGuard],
    children: [
      { path: 'tables', component: JudgeTablesListComponent },
      { path: 'tables/:tableId', component: JudgeTableOrderComponent },
      {
        path: 'tables/:tableId/samples/:beerEntryId',
        component: EvaluationSheetComponent,
      },
      { path: 'tables/:tableId/discrepancies', component: DiscrepancyAlertComponent },
      { path: '', pathMatch: 'full', redirectTo: 'tables' },
    ],
  },
  { path: '**', redirectTo: '' },
];
