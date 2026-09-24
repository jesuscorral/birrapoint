import { InjectionToken } from '@angular/core';

// Runtime configuration (FR-043): the production image is environment-agnostic — an nginx
// entrypoint writes this shape to /config.json from env vars at container start, so the same
// built bundle runs in every environment. Locally, ng serve/Aspire serve frontend/public/config.json
// unchanged. Never build-time-inline these values (that's what src/environments/environment.ts
// used to do, and why it was removed) — always go through APP_CONFIG, populated once in main.ts
// before bootstrap.
export interface AppConfig {
  keycloak: {
    url: string;
    realm: string;
    clientId: string;
  };
  // Same-origin deployments (production, reverse-proxied by nginx) use '' so every ApiClient/hub
  // URL is built as a relative path — see auth-interceptor.providers.ts and
  // competition-hub.service.ts for why every consumer must treat '' as a valid, working value
  // rather than a missing one.
  apiBaseUrl: string;
}

export const APP_CONFIG = new InjectionToken<AppConfig>('APP_CONFIG');
