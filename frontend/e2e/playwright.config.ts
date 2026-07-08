import { defineConfig, devices } from '@playwright/test';

/**
 * E2E suites for BirraPoint (quickstart.md scenarios). The a11y/ folder holds the
 * axe-core WCAG 2.1 AA checks that gate every judge-facing route (SC-009).
 * Run from frontend/: `npm run e2e` (alias for `playwright test -c e2e`).
 */
export default defineConfig({
  testDir: '.',
  fullyParallel: true,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 2 : 0,
  reporter: 'list',
  use: {
    baseURL: 'http://localhost:4200',
    trace: 'on-first-retry',
  },
  webServer: {
    command: 'npm start',
    cwd: '..',
    url: 'http://localhost:4200',
    reuseExistingServer: !process.env['CI'],
    timeout: 180_000,
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
