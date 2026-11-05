import { TEST_APP_CONFIG } from '../config/app-config.testing';
import { buildKeycloakConfig, keycloakInitOptions } from './keycloak.providers';

// provideAppKeycloak() itself is exercised via a real browser (dev-server smoke check), not
// here: calling it directly constructs a real `Keycloak` instance outside any Angular injection
// context, which trips a Jest/jsdom ESM↔CJS interop quirk in keycloak-js unrelated to this file's
// own logic. These two assertions cover everything that IS this file's logic — the config values.
describe('keycloak config', () => {
  it('uses the runtime AppConfig Keycloak settings', () => {
    expect(buildKeycloakConfig(TEST_APP_CONFIG)).toEqual(TEST_APP_CONFIG.keycloak);
  });

  it('checks for an existing SSO session without forcing a login redirect, with PKCE S256', () => {
    // Public landing page decision — see Docs/adrs/0012-public-welcome-landing-with-check-sso.md
    // and spec.md FR-001.
    expect(keycloakInitOptions.onLoad).toBe('check-sso');
    expect(keycloakInitOptions.pkceMethod).toBe('S256');
  });
});
