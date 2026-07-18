// Local-dev config only (fixed Aspire ports, CLAUDE.md §Commands). No dev/prod split yet — real
// per-environment values (and any build-time file replacement) arrive with Phase 16 (Bicep/nginx).
export const environment = {
  keycloak: {
    url: 'http://localhost:8081',
    realm: 'birrapoint',
    clientId: 'birrapoint-spa',
  },
  apiBaseUrl: 'http://localhost:5121',
};
