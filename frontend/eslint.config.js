// @ts-check
const eslint = require('@eslint/js');
const { defineConfig } = require('eslint/config');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');

module.exports = defineConfig([
  {
    ignores: ['dist/', 'coverage/', '.angular/', 'test-results/', 'playwright-report/', 'out-tsc/'],
  },
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      tseslint.configs.recommended,
      tseslint.configs.stylistic,
      angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        {
          type: 'attribute',
          prefix: 'app',
          style: 'camelCase',
        },
      ],
      '@angular-eslint/component-selector': [
        'error',
        {
          type: 'element',
          prefix: ['app', 'bp'],
          style: 'kebab-case',
        },
      ],
    },
  },
  {
    // ControlValueAccessor's onChange/onTouched default to no-op arrow functions until
    // registerOnChange/registerOnTouched wire them up — standard CVA boilerplate, not a real
    // empty-function smell. One override here instead of an eslint-disable-next-line on every
    // stub across every CVA-implementing component.
    files: [
      'src/app/shared/components/bp-checkbox/bp-checkbox.component.ts',
      'src/app/shared/components/bp-input/bp-input.component.ts',
      'src/app/shared/components/bp-textarea/bp-textarea.component.ts',
    ],
    rules: {
      '@typescript-eslint/no-empty-function': ['error', { allow: ['arrowFunctions'] }],
    },
  },
  {
    files: ['**/*.html'],
    extends: [angular.configs.templateRecommended, angular.configs.templateAccessibility],
    rules: {},
  },
]);
