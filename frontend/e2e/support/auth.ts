import type { Page } from '@playwright/test';

// The app's entry point stopped auto-redirecting to Keycloak: `/` now renders the welcome screen
// ("Entra en tu concurso") and the redirect happens on "Iniciar sesión". Every spec had its own
// `page.goto('/')` + `waitForURL(keycloak)` pair, so all of them hung on the welcome screen until
// the test timed out. That flow lives here now.
//
// This predates T124/T125 — it came in with the FR-001 welcome redesign — but it blocks every
// spec, so nothing else in e2e/ can be verified without it.

export const KEYCLOAK_ORIGIN = 'http://localhost:8081';

/**
 * Fills and submits the Keycloak-hosted login form. Assumes the page is already on Keycloak.
 *
 * Selectors are scoped to #kc-form-login and structural rather than id-based, because the
 * birrapoint theme (infra/keycloak/themes/birrapoint) replaced the stock markup: its submit is a
 * plain `input[type=submit]` with no `#kc-login` id, and on the "restart login" path it renders a
 * second, hidden `#username`, which makes a bare `#username` locator ambiguous under strict mode.
 */
export async function submitKeycloakLogin(
  page: Page,
  username: string,
  password: string,
): Promise<void> {
  const form = page.locator('#kc-form-login');
  await form.locator('input[name="username"]:not([type="hidden"])').fill(username);
  await form.locator('input[name="password"]').fill(password);
  await form.locator('input[type="submit"]').click();
}

/** Welcome screen -> Keycloak. Leaves the page on the Keycloak login form. */
export async function goToLogin(page: Page): Promise<void> {
  await page.goto('/');
  await page.getByRole('button', { name: 'Iniciar sesión' }).click();
  await page.waitForURL(new RegExp(`^${KEYCLOAK_ORIGIN}/`));
}

/** Full login, landing on the role's post-login route. */
export async function login(
  page: Page,
  username: string,
  password: string,
  landing: string,
): Promise<void> {
  await goToLogin(page);
  await submitKeycloakLogin(page, username, password);
  await page.waitForURL(landing);
}

/** Organizer login, landing on the dashboard. */
export async function loginAsOrganizer(
  page: Page,
  username = 'organizer',
  password = 'organizer',
): Promise<void> {
  await login(page, username, password, '**/organizer/dashboard');
}
