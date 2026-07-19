import { test, expect, Page } from '@playwright/test';
import { createJudgeUser, deleteUser, ProvisionedJudge } from './support/keycloak-admin';

// quickstart.md scenario 1 / spec.md US1 (FR-001–FR-003). Asserts the TARGET nested landing URLs
// (/organizer/dashboard, /judge/tables) that T024 introduces — today's app only has flat
// /organizer and /judge placeholder routes, so this spec is expected to fail until T024 lands.

const KEYCLOAK_ORIGIN = 'http://localhost:8081';
const ORGANIZER_USERNAME = 'organizer';
const ORGANIZER_PASSWORD = 'organizer';

async function submitKeycloakLogin(page: Page, username: string, password: string): Promise<void> {
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.locator('#kc-login').click();
}

test.describe('US1 — secure access with role-based entry', () => {
  let judge: ProvisionedJudge;

  test.beforeAll(async () => {
    judge = await createJudgeUser(`judge-${Date.now()}-${crypto.randomUUID()}@birrapoint.local`);
  });

  test.afterAll(async () => {
    await deleteUser(judge.id);
  });

  test('unauthenticated visit redirects to Keycloak hosted login with PKCE', async ({ page }) => {
    await page.goto('/');

    await page.waitForURL(
      new RegExp(`^${KEYCLOAK_ORIGIN}/realms/birrapoint/protocol/openid-connect/auth`),
    );

    const redirectUrl = new URL(page.url());
    expect(redirectUrl.searchParams.get('client_id')).toBe('birrapoint-spa');
    expect(redirectUrl.searchParams.get('code_challenge_method')).toBe('S256');
    expect(redirectUrl.searchParams.get('code_challenge')).toBeTruthy();
  });

  test('organizer login lands on /organizer/dashboard', async ({ page }) => {
    await page.goto('/');
    await page.waitForURL(new RegExp(`^${KEYCLOAK_ORIGIN}/`));

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
    await page.goto('/');
    await page.waitForURL(new RegExp(`^${KEYCLOAK_ORIGIN}/`));

    await submitKeycloakLogin(page, judge.email, judge.tempPassword);

    // Keycloak's login-actions/required-action flow forces the password update before the app
    // ever receives tokens (R-11) — no application code is involved in enforcing this.
    await expect(page.locator('#password-new')).toBeVisible();
    await expect(page).toHaveURL(new RegExp(`^${KEYCLOAK_ORIGIN}/`));

    // Structural no-bypass check, run on a second tab (shares the browser's Keycloak session
    // cookies) so it can't corrupt the first tab's in-progress redirect_uri: a direct navigation to
    // the judge landing route mid-flow must not render app/judge data. Observed real behavior: since
    // the required action is still pending, no full Keycloak SSO session exists yet, so
    // keycloak-js's `login-required` check on the fresh navigation restarts authentication from
    // scratch (a new login form) rather than resuming the required-action screen directly — either
    // way the app itself is never reached.
    const bypassAttempt = await context.newPage();
    await bypassAttempt.goto('/judge/tables');
    await bypassAttempt.waitForURL(new RegExp(`^${KEYCLOAK_ORIGIN}/`));
    await expect(bypassAttempt.locator('app-root')).not.toBeAttached();
    await bypassAttempt.close();

    const newPassword = `Judge-${crypto.randomUUID()}`;
    await page.locator('#password-new').fill(newPassword);
    await page.locator('#password-confirm').fill(newPassword);
    await page.locator('#kc-passwd-update-form button[type="submit"]').click();

    await page.waitForURL('**/judge/tables');
    await expect(page).toHaveURL(/\/judge\/tables$/);
  });
});
