import { Routes } from '@angular/router';

import { AuthPlaceholderComponent } from './core/auth/auth-placeholder.component';
import { judgeGuard, organizerGuard } from './core/auth/role.guard';

// Root '' is intentionally unmapped: T024 adds the post-login redirect-by-role landing there.
export const routes: Routes = [
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
];
