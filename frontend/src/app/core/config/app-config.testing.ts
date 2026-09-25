import type { AppConfig } from './app-config.model';

// Shared fixture for specs that exercise anything downstream of APP_CONFIG (ApiClient and
// everything built on it, the bearer interceptor, the Keycloak provider, the hub connection
// factory). Mirrors frontend/public/config.json's local-dev values so assertions built against it
// stay meaningful — never imported from application code, only from *.spec.ts.
export const TEST_APP_CONFIG: AppConfig = {
  keycloak: {
    url: 'http://localhost:8081',
    realm: 'birrapoint',
    clientId: 'birrapoint-spa',
  },
  apiBaseUrl: 'http://localhost:5121',
};
