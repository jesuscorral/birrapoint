# Guía rápida · Usar el Design System

## Archivos

| Archivo | Propósito |
|---------|-----------|
| `Docs/design/design-system.md` | Documentación completa: tokens, componentes, accesibilidad |
| `Docs/design/auth-mockup.html` | Mockup interactivo (7 pantallas, tokens visibles) |
| `frontend/src/styles.css` | Tokens Tailwind v4 `@theme` + componentes en `@layer` |

## Cómo empezar

### 1. Ver el mockup

Abre `Docs/design/auth-mockup.html` en el navegador. Las 7 pestañas muestran el flujo login/register, incluida una sección de tokens con colores hex y ratios de contraste.

### 2. Tokens en componentes Angular

En `frontend/src/styles.css` hay un bloque `@theme` con todos los tokens (colores, espaciado, radio, fuentes). Tailwind genera clases automáticamente.

```html
<!-- Usar utilidades generadas -->
<button class="bg-bp-cobre-500 text-white px-5 py-3 rounded-md">
  Acción
</button>

<!-- Mejor: usar componentes en @layer -->
<button class="btn-primary">Acción</button>
```

### 3. Construir componentes Angular

```typescript
import { Component, Input } from '@angular/core';

@Component({
  selector: 'bp-button',
  standalone: true,
  template: `
    <button [class.btn-primary]="variant === 'primary'"
            [class.btn-secondary]="variant === 'secondary'">
      {{ label }}
    </button>
  `,
})
export class BpButtonComponent {
  @Input() label = '';
  @Input() variant: 'primary' | 'secondary' = 'primary';
}
```

Úsalo en templates:

```html
<bp-button label="Entrar" variant="primary"></bp-button>
```

## Colores más usados (shortcuts)

- **Acción**: `bg-bp-cobre-500`, `text-bp-cobre-600`, `hover:bg-bp-cobre-600`
- **Marca**: `bg-bp-verde-600`, `text-bp-text-on-dark`
- **Error**: `bg-bp-danger-50`, `text-bp-danger-600`, `border-bp-danger-600`
- **Texto**: `text-bp-text`, `text-bp-text-muted`, `text-bp-text-subtle`
- **Bordes**: `border-bp-border-strong` (controles), `border-bp-border` (decorativos)

## Verificar accesibilidad

Todos los componentes pasan WCAG 2.1 AA:

- ✅ Texto ≥ 4.5:1 contraste
- ✅ Componentes ≥ 3:1 contraste (borde/fondo)
- ✅ Objetivo táctil ≥ 44 px
- ✅ Focus ring visible (3 px)
- ✅ Sin color como único medio

Si usas un componente fuera del design system, verifica con [axe-core](https://github.com/dequelabs/axe-core).

## Preguntas frecuentes

**P: ¿Puedo cambiar los colores?**  
R: Edita los valores `--color-bp-*` en `frontend/src/styles.css` bajo `@theme`. Todas las referencias se actualizan automáticamente.

**P: ¿Cómo añado más componentes?**  
R: Agrega la clase en `@layer components` en `styles.css` y documenta en `design-system.md`. Ejemplo: `.btn-tertiary`, `.card`, `.badge`.

**P: ¿Qué hago con estados como "loading" o "error"?**  
R: Usa `[class.is-loading]="loading"` y selectores CSS (ej. `.btn-primary.is-loading { ... }`). O mejor: propiedades HTML nativas como `aria-busy="true"` y `aria-invalid="true"`.

**P: ¿Se pueden usar otros colores?**  
R: Solo en excepciones (dataviz, mapas, etc.). Para todo lo demás, pide primero: puede haber una razón de accesibilidad o consistencia.

## Próximos pasos

1. **Componentes wizard**: input pequeño, stepper, selector de rango de fechas.
2. **Tabla de datos**: encabezados, orden, paginación, filas seleccionables.
3. **Planilla de evaluación**: grid de inputs, medidor de puntuación, alertas inline.
4. **Dashboard**: tarjetas KPI, gráficos, timeline.

Cada uno necesita:
- Mockup en `Docs/design/` (ej. `table-mockup.html`)
- Documentación en `design-system.md`
- Clases en `@layer components`
