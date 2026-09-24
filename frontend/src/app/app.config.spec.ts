import { makeEnvironmentProviders } from '@angular/core';
import { TestBed } from '@angular/core/testing';

// provideAppKeycloak() (unmocked) constructs a real Keycloak instance outside any Angular
// injection context as soon as it's called, which trips a Jest/jsdom ESM<->CJS interop quirk in
// keycloak-js unrelated to this file's own logic - see keycloak.providers.spec.ts. Stub it here so
// buildAppConfig() can actually run end to end in Jest; the real Keycloak wiring is covered by
// keycloak.providers.spec.ts and a real-browser smoke check.
jest.mock('./core/auth/keycloak.providers', () => ({
  provideAppKeycloak: jest.fn(() => makeEnvironmentProviders([])),
}));

import { buildAppConfig } from './app.config';
import { APP_CONFIG } from './core/config/app-config.model';
import { TEST_APP_CONFIG } from './core/config/app-config.testing';

// Composition-root test: buildAppConfig(config) must itself provide APP_CONFIG, not just rely on
// individual specs injecting a fixture. Every downstream consumer (ApiClient, the Keycloak
// provider, the bearer interceptor, the SignalR hub connection factory) injects APP_CONFIG
// directly, so if the real bootstrap in main.ts omitted it, the app would throw
// NullInjectorError on first API call.
describe('buildAppConfig', () => {
  it('includes a provider for APP_CONFIG', () => {
    const { providers } = buildAppConfig(TEST_APP_CONFIG);

    TestBed.configureTestingModule({ providers });

    expect(TestBed.inject(APP_CONFIG)).toEqual(TEST_APP_CONFIG);
  });
});
