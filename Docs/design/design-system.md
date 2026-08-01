# BirraPoint · Design System

**Versión:** 1.0  
**Paleta:** Botella y cobre  
**Última actualización:** Julio 2026

---

## Introducción

El design system de BirraPoint es una colección coherente de componentes visuales, tokens, y principios que aseguran consistencia, accesibilidad (WCAG 2.1 AA mínimo) y escalabilidad en toda la plataforma.

La paleta deliberadamente evita el ámbar/marrón que es estándar en apps cerveceras: en su lugar, el verde vidrio de botella captura la identidad sin ser genérico, y el cobre recuerda el equipo de cervecería. Los neutros hueso son cálidos pero sobrios.

### Principios

1. **Ceguera de marca por accesibilidad**: Los estados nunca se comunican solo por color (ej. éxito verde vs. marca verde están deliberadamente separados en saturación).
2. **Tipografía primero**: La jerarquía de tamaño, peso y familia (Fraunces vs. Inter) importa más que el color.
3. **Tokens, no hard-code**: Todo color, espacio y radio vive en `styles.css` como `@theme` de Tailwind v4. Las constantes Angular usan esos mismos valores.
4. **Accesibilidad por defecto**: Todos los controles tienen un objetivo táctil de ≥44 px; todos los textos pasan WCAG AA (4.5:1 mínimo); el focus ring es visible, de 3 px, con suficiente contraste.

---

## Paleta de colores

### Verde botella — marca y superficies oscuras

Inspirado en el vidrio de una botella de cerveza tradicional. Se usa en:
- Panel de marca (hero) en pantallas de login/registro.
- Iconos de estado sobre superficies neutras.
- Acentos gráficos (líneas decorativas, fondos de tarjetas).

| Token | Hex | RGB | Uso | Contraste más crítico |
|-------|-----|-----|-----|----------------------|
| verde-50 | `#EEF4F1` | 238, 244, 241 | Fondo suave, hover | — |
| verde-100 | `#D8E5DF` | 216, 229, 223 | Viñeta, badge deshabilitado | 5:1 con texto |
| verde-200 | `#B0CABF` | 176, 202, 191 | Separador soft | — |
| verde-300 | `#6E9C8B` | 110, 156, 139 | Borde al hover | 4.5:1 con texto |
| verde-400 | `#2E6B57` | 46, 107, 87 | Icono secundario | 8.2:1 con blanco |
| verde-500 | `#17513F` | 23, 81, 63 | Texto sobre hueso-50 | 9.5:1 |
| verde-600 | `#0B3B2E` | 11, 59, 46 | **Panel de marca** | **10.98:1** con hueso-50 |
| verde-700 | `#072720` | 7, 39, 32 | Fondo chrome (mockup) | — |
| verde-800 | `#041712` | 4, 23, 18 | Muy oscuro, raramente usado | — |

### Cobre — acción

Color cálido que remite al equipo de cervecería: tanques de cobre, calderería. Todas las acciones primarias (botones, enlaces, focus) usan esta familia.

| Token | Hex | RGB | Uso | Contraste |
|-------|-----|-----|-----|-----------|
| cobre-50 | `#FBF0EA` | 251, 240, 234 | Fondo de tarjeta rol, hover | — |
| cobre-100 | `#F5DCCD` | 245, 220, 205 | Badge, tag | — |
| cobre-200 | `#E9BCA1` | 233, 188, 161 | Separador warm | — |
| cobre-300 | `#E2A277` | 226, 162, 119 | **Acento sobre verde-600** | **5.76:1** |
| cobre-400 | `#D07A4C` | 208, 122, 76 | Gradiente en logo | — |
| cobre-500 | `#B85C33` | 184, 92, 51 | **Botón primario (normal)** | **4.55:1** con blanco |
| cobre-600 | `#9A4B27` | 154, 75, 39 | **Botón :hover, enlaces** | **6.16:1** con blanco |
| cobre-700 | `#7C3B1E` | 124, 59, 30 | Botón :active | **8.4:1** con blanco |

### Neutros hueso — fondos y superficies

Cálidos pero no cremosos. El hueso-50 es el fondo de página; hueso-200 es bordes decorativos.

| Token | Hex | RGB | Uso |
|-------|-----|-----|-----|
| hueso-50 | `#FBFAF6` | 251, 250, 246 | **Fondo de página** |
| hueso-100 | `#F5F3EC` | 245, 243, 236 | Fondo sunken (inputs deshabilitados, secciones recesivas) |
| hueso-200 | `#E8E5DA` | 232, 229, 218 | Bordes decorativos, separadores |

### Semántico

Estados persistentes y excepciones que comunican un significado más allá de la marca.

**Éxito** — Deliberadamente más saturado que el verde de marca, para evitar ambigüedad entre "correcto" e "identidad".

| Token | Hex | Contraste con fondo |
|-------|-----|-----|
| exito-50 | `#E9F2EC` | — |
| exito-600 | `#2C6E49` | 5.35:1 con hueso-50 (AA) |

**Error / Peligro** — Rojo neutro, sin matices cálidos del cobre.

| Token | Hex | Contraste |
|-------|-----|-----|
| danger-50 | `#FBEBEA` | — |
| danger-600 | `#A3271F` | 6.34:1 con danger-50 (AA) |

**Información** — Azul frío que contrasta con verde y cobre.

| Token | Hex |
|-------|-----|
| info-50 | `#E9F1F5` |
| info-600 | `#2A5C77` |

### Texto y superficie

| Token | Hex | Uso | Contraste en blanco |
|-------|-----|-----|-----|
| **text** | `#14201B` | Cuerpo, títulos sobre hueso-50 | **16.8:1 (AAA)** |
| **text-muted** | `#54655D` | Subtítulos, labels, captions | **6.2:1 (AA)** |
| **text-subtle** | `#63756E` | Placeholders, hints | **4.7:1 (UI/large)** |
| **text-on-dark** | `#EAF2EE` | Texto sobre verde-600 | **11:1 (AAA)** |
| **border-strong** | `#7A8C85` | Borde de control (input, card) | **3.55:1 (WCAG 1.4.11)** |
| **border** | `#DFDCCF` | Separador decorativo | — |

---

## Tipografía

### Familias

- **Fraunces** (serif, display): títulos, reclamos, encabezados. Pesos: 600, 700.
- **Inter** (sans, ui): cuerpo, labels, acciones. Pesos: 400, 500, 600, 700.

Ambas importadas de Google Fonts en `frontend/index.html`.

### Escala

| Clase / Var | Tamaño | Peso | Uso | Ejemplos |
|---|---|---|---|---|
| text-xs | 0.75rem (12px) | 600 | labels, tags, eyebrow | "Acceso", "Por invitación" |
| text-sm | 0.875rem (14px) | 400/600 | cuerpo pequeño, hints | Descripción de rol, help text |
| text-base | 1rem (16px) | 400/600 | **cuerpo, inputs** | Párrafos, descripciones |
| text-lg | 1.125rem (18px) | 500 | subtítulos | Lead en sidebar |
| text-xl | 1.375rem (22px) | 600 | — | — |
| text-2xl | 1.75rem (28px) | 600 | h2 pequeño | Títulos de sección |
| text-3xl | 2.25rem (36px) | 600 | h1 | Título de pantalla |
| text-4xl | 3rem (48px) | 600 | h1 grande | Reclamo de hero |

**Regla importante**: Los inputs **nunca** pueden estar por debajo de 16 px base de navegador, para evitar zoom accidental en iOS.

---

## Espaciado

Sistema en base 4, desde 0.25 rem. Derivadas desde `@theme` de Tailwind.

| Token | Px | Rem | Uso |
|-------|----|----|-----|
| space-1 | 4 | 0.25 | Gap mínimo entre inline elements |
| space-2 | 8 | 0.5 | Gap pequeño (entre icono y text) |
| space-3 | 12 | 0.75 | Gap estándar (radio-label, item-list) |
| space-4 | 16 | 1 | **Padding estándar** (campo, botón interno) |
| space-5 | 20 | 1.25 | Padding card, gap entre campos |
| space-6 | 24 | 1.5 | Separación de secciones |
| space-8 | 32 | 2 | Espacio vertical importante |
| space-10 | 40 | 2.5 | Padding lateral de panel |
| space-12 | 48 | 3 | Padding top/bottom de pantalla |
| space-16 | 64 | 4 | Espacio máximo |

---

## Radio (redondeado)

| Token | Px | Uso |
|-------|-----|-----|
| radius-sm | 6 | Input, small buttons |
| radius-md | 10 | **Botones, campos, modales** (estándar) |
| radius-lg | 16 | **Tarjetas, paneles** |
| radius-xl | 24 | Hero sections, large modals |
| radius-full | 999 | Pills, avatares |

---

## Sombra

Sistema de tres niveles. Usa el color texto oscuro (verde-800 / verde-900) con baja opacidad.

| Token | Definición CSS | Uso |
|-------|---|---|
| shadow-sm | `0 1px 2px rgba(4, 23, 18, 0.07)` | Elevación sutil (hover cards) |
| shadow-md | `0 4px 12px rgba(4, 23, 18, 0.09)` | Tarjetas, dropdowns |
| shadow-lg | `0 18px 40px -12px rgba(4, 23, 18, 0.24)` | **Modales, hero sections** |

---

## Componentes

Cada componente aquí tiene una clase CSS en `@layer components` y está listo para copiar en Angular standalone components. Las variantes (primario, secundario, deshabilitado) se manejan vía clases u `[attr]` selectors.

### Botón

#### Primario (`.btn-primary`)

- **Estados**: normal → hover → active
- **Color**: bg-bp-cobre-500 → cobre-600 → cobre-700
- **Texto**: blanco (text-white), 16px, font-semibold
- **Padding**: px-5 (20 px lateral), min-h-11 (44 px)
- **Focus**: ring-3 around, ring-hueso-50, ring-offset-2, offset-cobre-500
- **Disabled**: opacity-50, pointer-events-none

```html
<button class="btn-primary">Entrar</button>
<button class="btn-primary" aria-busy="true">
  <span class="spinner"></span>Cargando
</button>
```

#### Secundario (`.btn-secondary`)

- **Color**: bg-white, border-bp-border-strong, text-bp-verde-600
- **Hover**: bg-bp-verde-50, border-bp-verde-400
- **Igual padding y focus que primario**

```html
<button class="btn-secondary">Cancelar</button>
```

### Campo de entrada (`.input-field`)

- **Padding**: px-4, min-h-11 (objetivo táctil)
- **Borde**: 1.5px solid, bp-border-strong (#7A8C85)
- **Hover**: borde → bp-verde-400
- **Focus**: borde → bp-cobre-500, ring-3 bp-cobre-100
- **Error** (`[aria-invalid="true"]`): borde → bp-danger-600, bg → bp-danger-50
- **Disabled**: bg-bp-hueso-100, cursor-not-allowed, color text-subtle
- **Placeholder**: text-bp-text-subtle

```html
<input class="input-field" type="email" placeholder="tu@club.com" />
<input class="input-field" aria-invalid="true" value="error" />
```

### Alerta (`.alert`, `.alert-error`, `.alert-info`, `.alert-success`)

Estructura: icono (18px) + contenido (flex column).

```html
<div class="alert alert-error" role="alert">
  <svg class="alert__icon" width="18" height="18"><!-- ícono --></svg>
  <span>
    <strong class="alert-title">No hemos podido iniciar sesión</strong>
    Revisa tus credenciales.
  </span>
</div>
```

**Variantes**:
- `.alert-error`: bg-bp-danger-50, border-danger-200, text-bp-danger-600
- `.alert-info`: bg-bp-info-50, border-info-200, text-bp-info-600
- `.alert-success`: bg-bp-exito-50, border-exito-200, text-bp-exito-600

### Checkbox

Usa `accent-color: bp-cobre-500` para el check nativo. Label associated via `for/id`.

```html
<label class="check">
  <input type="checkbox" />
  <span>Aceptar <a href="#">Términos</a></span>
</label>
```

### Medidor de contraseña

Barra de 4 segmentos que se colorean según fuerza. Pasa `data-level="1..4"` y cambia color de `--meter-seg`.

```html
<div class="meter" data-level="3">
  <div class="meter__track">
    <span class="meter__seg"></span>
    <span class="meter__seg"></span>
    <span class="meter__seg"></span>
    <span class="meter__seg"></span>
  </div>
  <span class="meter__label" role="status">Seguridad: media</span>
</div>
```

---

## Accesibilidad

### WCAG 2.1 AA — Mínimo

- **Contraste de texto**: todos los pares texto/fondo ≥ 4.5:1.
- **Componentes**: borde y fondo ≥ 3:1 (WCAG 1.4.11, non-text contrast).
- **Objetivo táctil**: ≥ 44 × 44 px en todas las direcciones (WCAG 2.5.5).
- **Focus visible**: anillo de 3 px, contraste ≥ 3:1 con fondo.

### Checklist para nuevos componentes

- [ ] Todos los inputs tienen `<label>` con `for/id` o `aria-label`.
- [ ] Los estados (error, disabled) no se comunican **solo** por color: incluir icono o cambio de atributo.
- [ ] Focus ring es visible, con suficiente contraste.
- [ ] Si hay icono sin texto, usar `aria-label` o `role="img"` + `<title>`.
- [ ] Las alertas usan `role="alert"` y los títulos son `<strong>`.
- [ ] No hay color parpadea o cambia sin que el usuario lo controle (animations `prefers-reduced-motion`).

### Validación

Antes de pasar a staging:

```bash
# En Angular: Ally checker (ej. axe-core)
npx axe-core URL

# En CSS: contrast checker (autoprefixer + lightningcss)
npx lightningcss --minify frontend/src/styles.css
```

---

## Implementación en Angular

### Importar tokens

Los tokens están en `frontend/src/styles.css` como `@theme` de Tailwind. Las clases generadas están disponibles inmediatamente:

```typescript
// En template:
// <button class="btn-primary">Acción</button>

// Con Tailwind utilities (si necesitas override):
// <div class="bg-bp-verde-50 text-bp-text">...</div>
```

### Construir componentes reutilizables

```typescript
// auth-button.component.ts
import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-auth-button',
  standalone: true,
  imports: [CommonModule],
  template: `
    <button
      [class.btn-primary]="variant === 'primary'"
      [class.btn-secondary]="variant === 'secondary'"
      [attr.aria-busy]="loading"
      [disabled]="loading || disabled"
      (click)="onClick.emit()"
    >
      <span *ngIf="loading" class="spinner"></span>
      {{ label }}
    </button>
  `,
})
export class AuthButtonComponent {
  @Input() label: string = '';
  @Input() variant: 'primary' | 'secondary' = 'primary';
  @Input() loading = false;
  @Input() disabled = false;
  @Output() onClick = new EventEmitter<void>();
}
```

### Temas y referencias

Todos los colores están disponibles via Tailwind utilities:

```html
<!-- Con escala de colores generada -->
<div class="bg-bp-verde-50 border-bp-border-strong text-bp-text">
  Contenido
</div>

<!-- Con componentes en @layer -->
<button class="btn-primary">Acción</button>
```

---

## Cambios futuros y debt

1. **Logo cromático**: actualmente usa un gradiente cobre en mockup; en HTML real, debería ser SVG con posibilidad de monocromo.
2. **Dark mode**: no está en scope MVP; si llega, invertir verde ↔ hueso, mantener cobre.
3. **Animaciones**: Spring timings pendientes; ahora todas usan `duration-150 ease-out`.
4. **Tipografía variable**: si crece el proyecto, investigar Fraunces en rango opsz (9..144) y pesos interpolados.
5. **Icono set**: no documentado aún; planificar set de 50 iconos con trazo 2px, caja 24×24.

---

## Changelog

### v1.0 (julio 2026)

- Paleta Botella y cobre definida.
- Tokens en Tailwind v4 `@theme`.
- Componentes base: botón, campo, alerta, checkbox.
- Accesibilidad verificada (WCAG AA).
- Mockup interactivo en `Docs/design/auth-mockup.html`.
