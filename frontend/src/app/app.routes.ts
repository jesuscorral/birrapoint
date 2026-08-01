import { Routes } from '@angular/router';

import { homeRedirectGuard } from './core/auth/home-redirect.guard';
import { judgeGuard, organizerGuard } from './core/auth/role.guard';
import { CompetitionMonitorComponent } from './features/dashboard/competition-monitor.component';
import { CompetitionWizardComponent } from './features/competition-wizard/competition-wizard.component';
import { DiscrepancyAlertComponent } from './features/discrepancy/discrepancy-alert.component';
import { EvaluationSheetComponent } from './features/evaluation-sheet/evaluation-sheet.component';
import { JudgeManagementComponent } from './features/judge-management/judge-management.component';
import { JudgeTableOrderComponent } from './features/judge-tables/judge-table-order.component';
import { JudgeTablesListComponent } from './features/judge-tables/judge-tables-list.component';
import { OrganizerDashboardComponent } from './features/dashboard/organizer-dashboard.component';
import { ResultsDispatchComponent } from './features/results-dispatch/results-dispatch.component';
import { TableManagementComponent } from './features/table-management/table-management.component';
import { WelcomeComponent } from './features/auth/welcome/welcome.component';
import { KeycloakHandoffComponent } from './features/auth/keycloak-handoff/keycloak-handoff.component';

export const routes: Routes = [
  // Keycloak handoff (visual transition, then redirect). Public but only used in browser after click.
  {
    path: 'auth/handoff',
    component: KeycloakHandoffComponent,
    data: { label: 'Acceso seguro' },
  },
  // Root: public login/register landing (WelcomeComponent) for unauthenticated callers.
  // homeRedirectGuard redirects an authenticated caller to their role-specific workspace when
  // recognized (ORGANIZER -> /organizer/dashboard, JUDGE -> /judge/tables); an authenticated
  // caller with neither role falls through and sees the same landing (shouldn't happen given the
  // backend's deny-by-default policy).
  {
    path: '',
    pathMatch: 'full',
    canActivate: [homeRedirectGuard],
    component: WelcomeComponent,
  },
  {
    path: 'organizer',
    canActivate: [organizerGuard],
    children: [
      { path: 'dashboard', component: OrganizerDashboardComponent },
      { path: 'competitions/new', component: CompetitionWizardComponent },
      { path: 'competitions/:id', component: CompetitionWizardComponent },
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
