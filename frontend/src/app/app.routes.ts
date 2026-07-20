import { Routes } from '@angular/router';

import { AuthPlaceholderComponent } from './core/auth/auth-placeholder.component';
import { homeRedirectGuard } from './core/auth/home-redirect.guard';
import { judgeGuard, organizerGuard } from './core/auth/role.guard';
import { CompetitionWizardComponent } from './features/competition-wizard/competition-wizard.component';
import { EntryImportComponent } from './features/entry-import/entry-import.component';
import { JudgeTablesComponent } from './features/auth/judge-tables.component';
import { OrganizerDashboardComponent } from './features/auth/organizer-dashboard.component';

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
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
    ],
  },
  {
    path: 'judge',
    canActivate: [judgeGuard],
    children: [
      { path: 'tables', component: JudgeTablesComponent },
      { path: '', pathMatch: 'full', redirectTo: 'tables' },
    ],
  },
  { path: '**', redirectTo: '' },
];
