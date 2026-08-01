# BirraPoint Keycloak Theme

**Botella y cobre** — Tema de login/register para Keycloak.

## Estructura

```
birrapoint/
└── login/
    ├── template.ftl          # Layout maestro (panel marca + form)
    ├── login.ftl             # Formulario de login
    ├── register.ftl          # Formulario de registro
    ├── error.ftl             # Página de error
    ├── theme.properties      # Configuración del tema
    └── resources/
        └── css/
            └── style.css     # Estilos (Botella y cobre)
```

## Características

- **Layout partido**: Panel de marca (verde-600, Fraunces) + Panel de formulario (blanco).
- **Paleta Botella y cobre**: Verde vidrio + cobre cervecería + neutros hueso.
- **Accesibilidad**: WCAG 2.1 AA (contraste, focus ring, aria-labels).
- **Responsivo**: Desktop (grid 1.05fr 1fr) → Móvil (1fr, panel marca oculto).
- **FreeMarker templates**: Integrados con Keycloak variables (`url.*`, `message.*`, etc.).

## Instalación

1. **Copiar el tema** a `$KEYCLOAK_HOME/themes/` (o mount durante docker-compose).
2. **Actualizar el realm** en `birrapoint-realm.json`:
   ```json
   {
     "loginTheme": "birrapoint",
     "accountTheme": "birrapoint",
     "registrationAllowed": true
   }
   ```
3. **Reiniciar Keycloak**.
4. **Verificar** en `http://localhost:8081/auth/realms/birrapoint/account/` que el tema se carga.

## Plantillas FreeMarker

### login.ftl

Formulario de login con:
- Email/username field
- Password field + toggle show/hide
- Remember me checkbox
- Forgot password link
- Link a registro

Variables Keycloak usadas:
- `url.loginAction` — POST action
- `url.loginResetCredentialsUrl` — password reset link
- `url.registrationUrl` — signup link
- `message.*` — i18n strings
- `login.username`, `login.rememberMe` — form state

### register.ftl

Formulario de registro con:
- First/Last name
- Email
- Password + password confirm + strength meter
- Accept terms checkbox

Variables:
- `url.registrationAction` — POST action
- `url.loginUrl` — back to login link
- `register.formData.*` — prefilled data
- `_csrf.parameterName`, `_csrf.token` — CSRF protection

### error.ftl

Página de error con:
- Icon (error circle)
- Message summary + detail
- Back to login button

### template.ftl

Macro `registrationLayout` que extienden los templates anteriores. Maneja:
- HTML structure (doctype, meta, fonts)
- Brand panel (bubbles, logo, tagline)
- Form panel container
- Message rendering (alerts)

## Customización

### Colores

Editar CSS variables en `resources/css/style.css`:

```css
:root {
  --color-bp-verde-600: #0b3b2e;  /* Panel de marca */
  --color-bp-cobre-500: #b85c33;  /* Botones primarios */
}
```

### Mensajes (i18n)

Keycloak usa archivos `messages_XX.properties` en `messages/` para traducir. Si necesitas keys custom:

1. Crear `themes/birrapoint/login/messages/messages_es.properties`
2. Agregar `key=valor`
3. Referenciar con `${msg("key")}`

Ejemplo:
```properties
doForgotPassword=¿Olvidaste la contraseña?
acceptTerms=Acepto términos
```

### JavaScript

Los templates incluyen inline JS para:
- Toggle password visibility
- Password strength meter
- CSRF token handling

Todo es vanilla (sin jQuery).

## Testing

### Local

```bash
# Docker compose auto-monta el tema
docker-compose up

# Login: http://localhost:8081/auth/realms/birrapoint/account/
# Debug: Keycloak logs muestran parsing errors de FreeMarker
```

### Browsers

- Chrome/Firefox: Login + registro, toggle password, validación form
- Mobile: Layout responsivo, inputs no zooman
- Accessibility: axe-core debe pasar WCAG AA

## Troubleshooting

**Problema**: El tema no se carga (sigue viendo keycloak default).

**Solución**: 
1. Verificar que la carpeta esté en `$KEYCLOAK_HOME/themes/birrapoint/login/`
2. Verificar `theme.properties` existe
3. Restart Keycloak
4. Clear browser cache

**Problema**: FreeMarker error en los logs.

**Solución**: Revisar sintaxis FreeMarker en los `.ftl` files (no HTML):
- `${var}` para variables
- `<#if condition>` para condicionales
- `<#list items as item>` para loops
- Sin `endif`, `endfor` — usa `</#if>`, `</#list>`

## Relacionado

- `birrapoint-realm.json` — Configuración del realm (registrationAllowed, themes, etc.)
- `frontend/src/styles.css` — Tailwind v4 con mismos tokens (sincronizar cambios)
