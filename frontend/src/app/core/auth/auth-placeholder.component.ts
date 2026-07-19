import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { map } from 'rxjs';

// Fallback render target for '/' (T024's homeRedirectGuard): an authenticated caller who holds
// neither ORGANIZER nor JUDGE is routed here instead of a role-specific landing. Originally the
// render target for /organizer and /judge too (T019) before real landing pages existed.
@Component({
  selector: 'app-auth-placeholder',
  template: `<p>{{ label() }}: this account has no recognized workspace role.</p>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuthPlaceholderComponent {
  private readonly route = inject(ActivatedRoute);

  protected readonly label = toSignal(
    this.route.data.pipe(map((data) => (data['label'] as string | undefined) ?? '')),
    { initialValue: '' },
  );
}
