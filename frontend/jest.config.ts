import type { Config } from 'jest';

const config: Config = {
  preset: 'jest-preset-angular',
  setupFilesAfterEnv: ['<rootDir>/setup-jest.ts'],
  testMatch: ['<rootDir>/src/**/*.spec.ts'],
  collectCoverageFrom: ['src/app/**/*.ts', '!src/app/**/*.spec.ts'],
  // jest-preset-angular's default only transforms .mjs under node_modules; keycloak-js ships
  // ESM (`"type": "module"`) from a plain .js file with no CJS entry point, so it needs the same
  // treatment (T019) — this replaces rather than merges with the preset's pattern, so the
  // .mjs$/locale exemption it needs is repeated here.
  transformIgnorePatterns: [
    'node_modules/(?!(.*\\.mjs$|@angular/common/locales/.*\\.js$|keycloak-js/.*))',
  ],
};

export default config;
