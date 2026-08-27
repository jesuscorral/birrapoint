import { test, expect, Page } from '@playwright/test';
import { createJudgeUser, deleteUser, ProvisionedJudge } from './support/keycloak-admin';
import { KEYCLOAK_ORIGIN, goToLogin, submitKeycloakLogin } from './support/auth';

// quickstart.md scenario 1 / spec.md US1 (FR-001–FR-003). Asserts the TARGET nested landing URLs
// (/organizer/dashboard, /judge/tables) that T024 introduces — today's app only has flat
// /organizer and /judge placeholder routes, so this spec is expected to fail until T024 lands.

const ORGANIZER_USERNAME = 'organizer';
const ORGANIZER_PASSWORD = 'organizer';

test.describe('US1 — secure access with role-based entry', () => {
  let judge: ProvisionedJudge;

  test.beforeAll(async () => {
    judge = await createJudgeUser(`judge-${Date.now()}-${crypto.randomUUID()}@birrapoint.local`);
  });

  test.afterAll(async () => {
    // Guard against beforeAll having thrown before `judge` was assigned (e.g. Keycloak down) —
    // an unguarded `judge.id` here would mask the real failure with a TypeError in afterAll.
    if (judge?.id) {
      await deleteUser(judge.id);
    }
  });

  // `/` is the public welcome screen now (FR-001), so it no longer redirects on its own — the
  // hand-off happens on "Iniciar sesión". The PKCE parameters are what this test is really about,
  // and they are unchanged.
  test('login hands off to the Keycloak hosted form with PKCE', async ({ page }) => {
    await goToLogin(page);

    await page.waitForURL(
      new RegExp(`^${KEYCLOAK_ORIGIN}/realms/birrapoint/protocol/openid-connect/auth`),
    );

    const redirectUrl = new URL(page.url());
    expect(redirectUrl.searchParams.get('client_id')).toBe('birrapoint-spa');
    expect(redirectUrl.searchParams.get('code_challenge_method')).toBe('S256');
    expect(redirectUrl.searchParams.get('code_challenge')).toBeTruthy();
  });

  // The welcome screen being public must not mean the app is. keycloak.providers.ts uses
  // `onLoad: 'check-sso'`, which deliberately does not force authentication, so an anonymous deep
  // link falls through to the public welcome screen (see role.guard.ts) rather than bouncing to
  // Keycloak. What matters is that the guarded screen itself never renders.
  test('an unauthenticated deep link into a guarded route never renders it', async ({ page }) => {
    await page.goto('/organizer/dashboard');

    await expect(page).toHaveURL(/localhost:4200\/$/);
    await expect(page.getByRole('heading', { name: 'Entra en tu concurso' })).toBeVisible();
    await expect(page.locator('app-organizer-dashboard')).not.toBeAttached();
  });

  test('organizer login lands on /organizer/dashboard', async ({ page }) => {
    await goToLogin(page);

    await submitKeycloakLogin(page, ORGANIZER_USERNAME, ORGANIZER_PASSWORD);

    await page.waitForURL('**/organizer/dashboard');
    await expect(page).toHaveURL(/\/organizer\/dashboard$/);
  });

  test('judge with a temporary credential is forced through password change with no deep-link bypass, then lands on /judge/tables', async ({
    page,
    context,
  }) => {
    // Login originates from '/' so the OAuth redirect_uri Keycloak completes the flow against stays
    // '/', and the eventual landing on /judge/tables can only be produced by the app's own
    // post-login role redirect (T024) — not by a URL we ourselves navigated to mid-flow.
    await goToLogin(page);

    await submitKeycloakLogin(page, judge.email, judge.tempPassword);

    // Keycloak's login-actions/required-action flow forces the password update before the app
    // ever receives tokens (R-11) — no application code is involved in enforcing this.
    await expect(page.locator('#password-new')).toBeVisible();
    await expect(page).toHaveURL(new RegExp(`^${KEYCLOAK_ORIGIN}/`));

    // Structural no-bypass check, run on a second tab (shares the browser's Keycloak session
    // cookies) so it can't corrupt the first tab's in-progress redirect_uri: a direct navigation to
    // the judge landing route mid-flow must not render app/judge data. Observed real behavior: since
    // the required action is still pending, no full Keycloak SSO session exists yet, so `check-sso`
    // resolves anonymous and the guard drops the navigation on the public welcome screen — either
    // way the judge workspace itself is never reached.
    const bypassAttempt = await context.newPage();
    await bypassAttempt.goto('/judge/tables');
    await expect(bypassAttempt).toHaveURL(/localhost:4200\/$/);
    await expect(
      bypassAttempt.getByRole('heading', { name: 'Entra en tu concurso' }),
    ).toBeVisible();
    await expect(bypassAttempt.locator('app-judge-tables-list')).not.toBeAttached();
    await bypassAttempt.close();

    const newPassword = `Judge-${crypto.randomUUID()}`;
    await page.locator('#password-new').fill(newPassword);
    await page.locator('#password-confirm').fill(newPassword);
    await page.locator('#kc-passwd-update-form input[type="submit"]').click();

    await page.waitForURL('**/judge/tables');
    await expect(page).toHaveURL(/\/judge\/tables$/);
  });
});
