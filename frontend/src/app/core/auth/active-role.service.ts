import { Injectable } from '@angular/core';

export type AppRole = 'ORGANIZER' | 'JUDGE';

const STORAGE_KEY = 'birrapoint.activeRole';

// Session-scoped (sessionStorage, not shared across tabs/devices, cleared on tab close) UI
// partition for an account holding both ORGANIZER and JUDGE realm roles — which workspace the
// login/role-select flow sends them to. Never a security boundary: every backend endpoint still
// enforces the caller's real Keycloak role regardless of this value (Principle VII) — this only
// decides which frontend routes/landing a dual-role caller sees.
@Injectable({ providedIn: 'root' })
export class ActiveRoleService {
  getActiveRole(): AppRole | null {
    try {
      const stored = sessionStorage.getItem(STORAGE_KEY);
      return stored === 'ORGANIZER' || stored === 'JUDGE' ? stored : null;
    } catch {
      // Private browsing / storage disabled: behaves as if no choice were ever made — the
      // caller sees the role picker again on every navigation instead of a hard failure.
      return null;
    }
  }

  setActiveRole(role: AppRole): void {
    try {
      sessionStorage.setItem(STORAGE_KEY, role);
    } catch {
      // Best-effort: see getActiveRole above.
    }
  }

  clearActiveRole(): void {
    try {
      sessionStorage.removeItem(STORAGE_KEY);
    } catch {
      // Best-effort: see getActiveRole above.
    }
  }
}
