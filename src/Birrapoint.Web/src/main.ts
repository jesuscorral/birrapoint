import { bootstrapApplication } from '@angular/platform-browser';
import { buildAppConfig } from './app/app.config';
import { App } from './app/app';
import { loadAppConfig } from './app/core/config/app-config.loader';

function renderFatalError(error: unknown): void {
  console.error('BirraPoint failed to start.', error);
  document.body.innerHTML =
    '<p style="font-family: sans-serif; padding: 2rem; text-align: center;">' +
    'BirraPoint could not start. Please try again later.</p>';
}

loadAppConfig()
  .then((config) => bootstrapApplication(App, buildAppConfig(config)))
  .catch(renderFatalError);
