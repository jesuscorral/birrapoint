import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZoneChangeDetection,
  isDevMode,
} from '@angular/core';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';
import { provideServiceWorker } from '@angular/service-worker';
import { provideAuthBearerInterceptor } from './core/auth/auth-interceptor.providers';
import { provideAppKeycloak } from './core/auth/keycloak.providers';
import type { AppConfig } from './core/config/app-config.model';

// Factory rather than a module-level constant: the app's providers depend on the runtime AppConfig
// (FR-043), which is only known after main.ts awaits loadAppConfig() — there is no build-time
// environment object to close over anymore.
export function buildAppConfig(config: AppConfig): ApplicationConfig {
  return {
    providers: [
      provideBrowserGlobalErrorListeners(),
      provideZoneChangeDetection({ eventCoalescing: true }),
      provideRouter(routes),
      provideServiceWorker('ngsw-worker.js', {
        enabled: !isDevMode(),
        registrationStrategy: 'registerWhenStable:30000',
      }),
      provideAppKeycloak(config),
      provideAuthBearerInterceptor(config),
    ],
  };
}
