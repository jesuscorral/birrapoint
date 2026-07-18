import { environment } from '../../../environments/environment';
import { keycloakConfig, keycloakInitOptions } from './keycloak.providers';

// provideAppKeycloak() itself is exercised via a real browser (dev-server smoke check), not
// here: calling it directly constructs a real `Keycloak` instance outside any Angular injection
// context, which trips a Jest/jsdom ESM↔CJS interop quirk in keycloak-js unrelated to this file's
// own logic. These two assertions cover everything that IS this file's logic — the config values.
describe('keycloak config', () => {
  it('uses the environment Keycloak settings', () => {
    expect(keycloakConfig).toEqual(environment.keycloak);
  });

  it('requires login before any content renders, with PKCE S256', () => {
    expect(keycloakInitOptions.onLoad).toBe('login-required');
    expect(keycloakInitOptions.pkceMethod).toBe('S256');
  });
});
