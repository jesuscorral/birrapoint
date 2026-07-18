import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { map } from 'rxjs';

// Temporary render target for the guarded /organizer and /judge route shells (T019). Replaced by
// the real dashboard/tables landing pages in T024.
@Component({
  selector: 'app-auth-placeholder',
  template: `<p>{{ label() }} workspace — placeholder, see T024.</p>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuthPlaceholderComponent {
  private readonly route = inject(ActivatedRoute);

  protected readonly label = toSignal(
    this.route.data.pipe(map((data) => (data['label'] as string | undefined) ?? '')),
    { initialValue: '' },
  );
}
