import { Injectable, signal } from '@angular/core';

export type AppRole = 'ORGANIZER' | 'JUDGE';

const STORAGE_KEY = 'birrapoint.activeRole';

function readStoredRole(): AppRole | null {
  try {
    const stored = sessionStorage.getItem(STORAGE_KEY);
    return stored === 'ORGANIZER' || stored === 'JUDGE' ? stored : null;
  } catch {
    // Private browsing / storage disabled: no persisted choice to seed from.
    return null;
  }
}

// Session-scoped UI partition for an account holding both ORGANIZER and JUDGE realm roles —
// which workspace the login/role-select flow sends them to. Never a security boundary: every
// backend endpoint still enforces the caller's real Keycloak role regardless of this value
// (Principle VII) — this only decides which frontend routes/landing a dual-role caller sees.
//
// The in-memory signal is the source of truth for this tab's lifetime — exactly the scope this
// choice is meant to have. sessionStorage is only a best-effort mirror so the choice survives a
// reload; when it's unavailable (private browsing, quota, a blocked/partitioned store) the
// in-memory value still holds, so the caller reaches their workspace instead of bouncing back to
// /select-role on every navigation attempt (a real dead end a storage failure would otherwise
// cause, since setActiveRole would silently no-op and every later getActiveRole would read back
// null).
@Injectable({ providedIn: 'root' })
export class ActiveRoleService {
  private readonly active = signal<AppRole | null>(readStoredRole());

  readonly activeRole = this.active.asReadonly();

  getActiveRole(): AppRole | null {
    return this.active();
  }

  setActiveRole(role: AppRole): void {
    this.active.set(role);
    try {
      sessionStorage.setItem(STORAGE_KEY, role);
    } catch {
      // Best-effort: a reload will re-prompt, but navigation within this tab still works off
      // the in-memory signal above.
    }
  }

  clearActiveRole(): void {
    this.active.set(null);
    try {
      sessionStorage.removeItem(STORAGE_KEY);
    } catch {
      // Best-effort: see setActiveRole above.
    }
  }
}
