import { Routes } from '@angular/router';

import { AuthPlaceholderComponent } from './core/auth/auth-placeholder.component';
import { judgeGuard, organizerGuard } from './core/auth/role.guard';

export const routes: Routes = [
  // Placeholder, not a redirect-by-role: T024 replaces this entry with the real post-login
  // landing. Without it, a role mismatch below (or landing on '/' after login) rendered a bare
  // <h1>BirraPoint</h1> shell with no route matched (T019 review).
  { path: '', pathMatch: 'full', component: AuthPlaceholderComponent, data: { label: 'Home' } },
  {
    path: 'organizer',
    canActivate: [organizerGuard],
    component: AuthPlaceholderComponent,
    data: { label: 'Organizer' },
  },
  {
    path: 'judge',
    canActivate: [judgeGuard],
    component: AuthPlaceholderComponent,
    data: { label: 'Judge' },
  },
  { path: '**', redirectTo: '' },
];
