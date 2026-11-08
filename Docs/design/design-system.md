# BirraPoint · Design System

**Version:** 1.0  
**Palette:** Bottle and copper  
**Last updated:** July 2026 (components and implementation notes aligned with the code, November 2026)

---

## Introduction

The BirraPoint design system is a coherent set of visual components, tokens and principles that ensure consistency, accessibility (WCAG 2.1 AA minimum) and scalability across the platform.

The palette deliberately avoids the amber/brown that is standard in beer apps: bottle-glass green captures the identity without being generic, and copper recalls brewing equipment. The bone neutrals are warm but sober.

### Principles

1. **Never rely on colour alone**: states are never communicated by colour only (e.g. success green and brand green are deliberately separated in saturation).
2. **Typography first**: the hierarchy of size, weight and family (Fraunces vs. Inter) matters more than colour.
3. **Tokens, not hard-coded values**: every colour, spacing and radius lives in `styles.css` as a Tailwind v4 `@theme`. Angular constants use those same values.
4. **Accessible by default**: every control has a touch target of ≥44 px; all text passes WCAG AA (4.5:1 minimum); the focus ring is visible, 3 px, with sufficient contrast.

---

## Source of truth

`src/Birrapoint.Web/src/styles.css` is the source of truth: `--color-bp-*` tokens in `@theme` (Tailwind generates `bg-bp-*`, `text-bp-*`, `border-bp-*`, `ring-bp-*`), `--spacing-*` and `--radius-full` in `:root`, and the global `.sr-only` class. This document lists the values defined there. Contrast ratios are WCAG 2.1 relative-luminance ratios computed from those hex values.

---

## Colour palette

Only the steps listed exist; other steps (e.g. `verde-200`) are not defined.

### Bottle green (verde) — brand and secondary actions

| Token | Hex | RGB | Use | Contrast |
|---|---|---|---|---|
| verde-50 | `#EAF2EE` | 234, 242, 238 | Soft background, hover (secondary button hover) | — |
| verde-100 | `#DFECE5` | 223, 236, 229 | Subtle fill | — |
| verde-400 | `#6FAE91` | 111, 174, 145 | Hover border | — |
| verde-500 | `#3F8768` | 63, 135, 104 | Mid accent | 4.31:1 on white |
| verde-600 | `#2E6B57` | 46, 107, 87 | Secondary button text, brand green | 6.26:1 on white; 5.89:1 on hueso-50 |
| verde-800 | `#17352A` | 23, 53, 42 | Darkest green (dark surfaces) | 12.73:1 with text-on-dark |

### Copper (cobre) — action

| Token | Hex | RGB | Use | Contrast |
|---|---|---|---|---|
| cobre-50 | `#FBF3EC` | 251, 243, 236 | Card background, hover | — |
| cobre-100 | `#F5E0CD` | 245, 224, 205 | Badge, tag, input focus ring | — |
| cobre-300 | `#E2A277` | 226, 162, 119 | Accent on dark green | 6.12:1 on verde-800 |
| cobre-400 | `#D4915F` | 212, 145, 95 | Gradient | — |
| cobre-500 | `#D07A4C` | 208, 122, 76 | Brand copper; decorative / large elements only | 3.20:1 on white |
| cobre-600 | `#B85C33` | 184, 92, 51 | **Primary button**, links | 4.55:1 on white |
| cobre-700 | `#9A4B27` | 154, 75, 39 | Primary button hover / active | 6.16:1 on white |

### Bone neutrals (hueso) — backgrounds

| Token | Hex | RGB | Use |
|---|---|---|---|
| hueso-50 | `#FAF8F3` | 250, 248, 243 | **Page background** |
| hueso-100 | `#F2EDE4` | 242, 237, 228 | Sunken background (disabled inputs, ghost hover) |
| hueso-200 | `#E6DED0` | 230, 222, 208 | Badges, dashboard fills |

### Semantic

**Success (exito)**

| Token | Hex | Contrast |
|---|---|---|
| exito-50 | `#EEF6F0` | — |
| exito-600 | `#2E6B57` | 5.68:1 on exito-50; 5.89:1 on hueso-50 |

**Error / Danger**

| Token | Hex | Contrast |
|---|---|---|
| danger-50 | `#FDF1EF` | — |
| danger-600 | `#A3271F` | 6.63:1 on danger-50; 7.32:1 on white |

**Information (info)**

| Token | Hex | Contrast |
|---|---|---|
| info-50 | `#EEF4F7` | — |
| info-600 | `#2C5F7A` | 6.25:1 on info-50 |

**Warning (aviso)**

| Token | Hex | Contrast |
|---|---|---|
| aviso-50 | `#FDF3E5` | — |
| aviso-500 | `#D9902E` | 2.40:1 on aviso-50 (decorative / large only) |
| aviso-600 | `#B8741C` | 3.44:1 on aviso-50 |

### Text and surface

| Token | Hex | Use | Contrast on white |
|---|---|---|---|
| **surface** | `#FFFFFF` | Card / input background | — |
| **text** | `#1F2320` | Body, headings | 15.91:1 |
| **text-muted** | `#5B655F` | Subtitles, labels, captions | 6.05:1 |
| **text-subtle** | `#8A8F8A` | Placeholders, hints (below 4.5:1: not for essential text) | 3.29:1 |
| **text-on-dark** | `#FBFAF6` | Text on dark green | 12.73:1 on verde-800 |
| **border-strong** | `#C7BFAE` | Control border (input, card); below the 3:1 of WCAG 1.4.11 | 1.83:1 |
| **border** | `#E2DDD3` | Decorative divider | 1.35:1 |

---

## Typography

### Families

- **Fraunces** (serif, display): titles, taglines, headings. Weights: 600, 700.
- **Inter** (sans, ui): body, labels, actions. Weights: 400, 500, 600, 700.

Components set the family locally (e.g. `font-family: 'Fraunces', serif;`). No font files or Google Fonts link are loaded globally yet (see Future changes and debt).

### Scale

No custom scale: components use the Tailwind v4 defaults (`text-xs` 12 px ... `text-4xl` 36 px) with weights 400-700 chosen per component.

**Important rule**: inputs must **never** be below the 16 px browser base, to avoid accidental zoom on iOS.

---

## Spacing

Base-4 system, starting at 0.25 rem. Defined as `--spacing-*` custom properties in `styles.css` (the table names them `space-N` for brevity).

| Token | Px | Rem | Use |
|-------|----|----|-----|
| space-1 | 4 | 0.25 | Minimum gap between inline elements |
| space-2 | 8 | 0.5 | Small gap (between icon and text) |
| space-3 | 12 | 0.75 | Standard gap (radio-label, list item) |
| space-4 | 16 | 1 | **Standard padding** (field, button inner) |
| space-5 | 20 | 1.25 | Card padding, gap between fields |
| space-6 | 24 | 1.5 | Section separation |
| space-8 | 32 | 2 | Significant vertical space |
| space-10 | 40 | 2.5 | Panel side padding |
| space-12 | 48 | 3 | Screen top/bottom padding |
| space-16 | 64 | 4 | Maximum space |

---

## Radius and shadow

No custom tokens except `--radius-full` (9999px). Components use Tailwind defaults: `rounded-md` for buttons, inputs and most controls, and `shadow-sm` / `shadow-md` / `shadow-lg` for elevation.

---

## Components

Components are Angular standalone components under `src/Birrapoint.Web/src/app/shared/components/` (exported from `index.ts`). They consume the tokens through Tailwind utilities (`bg-bp-cobre-600`, `text-bp-text`, ...) and scoped component styles. Variants are inputs, not global CSS classes.

| Component | Selector | Main inputs |
|-----------|----------|-------------|
| Button | `bp-button` | `label`, `variant` (`primary`, `secondary`, `ghost`), `size` (`sm`, `md`, `lg`), `type`, `loading`, `disabled`, `block`, `ariaLabel`; output `clicked` |
| Text input | `bp-input` | `label`, `type`, `placeholder`, `required`, `hint`, `hasError`, `errorMessage`, `min`, `max` |
| Textarea | `bp-textarea` | see component |
| Checkbox | `bp-checkbox` | `checked`, `disabled` |
| Alert | `bp-alert` | `type` (`error`, `info`, `success`), `title`, `role` (`alert`, `status`) |
| File dropzone, step actions, top bar | `bp-file-dropzone`, `bp-step-actions`, `bp-topbar` | see components |

### Button

- **Primary**: copper; normal → hover/active = cobre-600 → cobre-700, white text, font-semibold.
- **Secondary**: white background, `bp-border-strong` border, `bp-verde-600` text; hover background `bp-verde-50`, border `bp-verde-400`.
- **Touch target**: min height 44 px.
- **Focus**: 3 px ring with offset 2 (`focus-visible:ring-3 focus-visible:ring-offset-2`).
- **Disabled**: reduced opacity, no pointer events. `loading` shows a spinner and sets `aria-busy`; the spinner slows under `prefers-reduced-motion`.

```html
<bp-button label="Sign in" variant="primary" (clicked)="signIn()" />
<bp-button label="Cancel" variant="secondary" />
```

Pass `ariaLabel` when several buttons share the same visible label (one per list row).

### Input

- Min height 44 px, border `bp-border-strong`, hover border `bp-verde-400`, focus border `bp-cobre-500` with a copper ring.
- Error (`hasError`): danger border and `bp-danger-50` background, plus `errorMessage` text (not colour alone).
- Disabled: `bp-hueso-100` background, `cursor-not-allowed`.

```html
<bp-input label="Email" type="email" placeholder="you@club.com" [required]="true" />
```

### Alert

Icon (18 px) + content. Variants: `error` (danger-50 / danger-600), `info` (info-50 / info-600), `success` (exito-50 / exito-600). `role="alert"` by default, `status` for non-urgent messages.

```html
<bp-alert type="error" title="Could not sign in">Check your credentials.</bp-alert>
```

### Checkbox

`bp-checkbox` wraps the native checkbox; the label is associated through `for`/`id`.

---

## Accessibility

### WCAG 2.1 AA — minimum

- **Text contrast**: all text/background pairs ≥ 4.5:1.
- **Components**: border and fill ≥ 3:1 (WCAG 1.4.11, non-text contrast); `border-strong` currently falls short (see debt).
- **Touch target**: ≥ 44 × 44 px in every direction (WCAG 2.5.5).
- **Visible focus**: 3 px ring, contrast ≥ 3:1 with the background.

### Checklist for new components

- [ ] Every input has a `<label>` with `for/id` or an `aria-label`.
- [ ] States (error, disabled) are not communicated **only** by colour: include an icon, text or attribute change.
- [ ] The focus ring is visible, with sufficient contrast.
- [ ] An icon without text uses `aria-label` or `role="img"` + `<title>`.
- [ ] Alerts use `role="alert"` and titles are `<strong>`.
- [ ] Nothing flashes or changes colour outside the user's control (animations honour `prefers-reduced-motion`).

### Validation

Accessibility is checked with Playwright + axe (`npm run e2e` from `src/Birrapoint.Web/`, specs in `e2e/a11y`). The suite is not yet part of CI.

---

## Using the tokens in Angular

Tokens live in `src/Birrapoint.Web/src/styles.css` (`@theme`); Tailwind generates the matching utilities. Prefer the shared `bp-*` components over raw utilities; use utilities only for layout or one-off overrides.

```html
<!-- Preferred: shared component -->
<bp-button label="Action" />

<!-- Override or layout: generated utilities -->
<div class="bg-bp-verde-50 border-bp-border-strong text-bp-text">Content</div>
```

---

## Future changes and debt

1. **Contrast gaps**: `text-subtle` (3.29:1), `border-strong` (1.83:1, below the 3:1 non-text floor) and `cobre-500` (3.20:1) are not safe for essential text or control boundaries; `bp-input` relies on them today.
2. **Fonts**: Fraunces and Inter are referenced per component but not loaded globally (no `<link>`/`@font-face`); decide on self-hosting.
3. **Type scale, radius and shadow** are Tailwind defaults, not project tokens.
4. **Password strength meter** is designed but not implemented.
5. **Logo**: should be an SVG with a monochrome option.
6. **Dark mode**: out of scope for the MVP; if it arrives, invert verde ↔ hueso and keep copper.
7. **Animations**: spring timings pending; everything currently uses `duration-150 ease-out`.
8. **Icon set**: undocumented; plan a 50-icon set with 2 px stroke on a 24×24 box.

---

## Changelog

### v1.0 (July 2026)

- Bottle and copper palette defined.
- Tokens in Tailwind v4 `@theme`.
- Base components: button, input, alert, checkbox.
- Accessibility verified (WCAG AA).
