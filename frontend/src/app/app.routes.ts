import { Routes } from '@angular/router';

import { homeRedirectGuard } from './core/auth/home-redirect.guard';
import { judgeGuard, organizerGuard, roleSelectGuard } from './core/auth/role.guard';
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
import { UserSettingsComponent } from './features/settings/user-settings.component';
import { WelcomeComponent } from './features/auth/welcome/welcome.component';
import { KeycloakHandoffComponent } from './features/auth/keycloak-handoff/keycloak-handoff.component';
import { RoleSelectComponent } from './features/auth/role-select/role-select.component';

export const routes: Routes = [
  // Keycloak handoff (visual transition, then redirect). Public but only used in browser after click.
  {
    path: 'auth/handoff',
    component: KeycloakHandoffComponent,
    data: { label: 'Acceso seguro' },
  },
  // Reached via resolveRoleLandingUrlTree (role-landing.ts) whenever a dual-role (ORGANIZER +
  // JUDGE) caller has no ActiveRoleService choice yet this session, and via the "switch role"
  // actions on the organizer/judge shells. roleSelectGuard bounces anyone who isn't actually a
  // dual-role caller (an anonymous visitor via a bookmarked/shared link, browser Back after
  // logout, a single-role caller typing the URL) to their own real landing — this route's copy
  // ("Tu cuenta tiene acceso como organizador y como juez") would otherwise be shown, wrongly, to
  // any of those.
  {
    path: 'select-role',
    canActivate: [roleSelectGuard],
    component: RoleSelectComponent,
    data: { label: 'Elegir rol' },
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
      { path: 'settings', component: UserSettingsComponent },
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
