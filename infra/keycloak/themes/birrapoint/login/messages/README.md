# Mensajes del tema BirraPoint

Localizaciones para el tema de login/register de Keycloak.

## Idiomas disponibles

- `messages_es.properties` — Español
- `messages_en.properties` — English (complementario)

## Cómo agregar un nuevo idioma

1. Crear un archivo `messages_XX.properties` donde `XX` es el código de idioma ISO 639-1 (ej: `fr`, `de`, `it`).
2. Copiar las claves de `messages_es.properties` y traducir los valores.
3. Reiniciar Keycloak.

Ejemplo: `messages_de.properties` para alemán.

```properties
doLogIn=Anmelden
username=Benutzername
email=E-Mail-Adresse
password=Passwort
# ... resto de claves
```

## Claves disponibles

### Login

- `doLogIn` — Botón de envío
- `username` — Label de usuario
- `usernameOrEmail` — Label alternativo
- `email` — Label de email
- `password` — Label de contraseña
- `rememberMe` — Label de recordar
- `doForgotPassword` — Link de reset
- `noAccount` — Texto "¿No tienes cuenta?"
- `doRegister` — CTA de registro

### Registro

- `firstName` — Label
- `lastName` — Label
- `emailHint` — Hint sobre email
- `passwordHint` — Requisitos de contraseña
- `passwordConfirm` — Label confirmar
- `passwordStrengthWeak/Fair/Good/Strong` — Medidor de fuerza
- `acceptTerms` — Checkbox label
- `termsText` — Link términos
- `privacyText` — Link privacidad
- `alreadyHaveAccount` — Texto "¿Ya tienes cuenta?"

### Errores

- `errorPageExpiredTitle` — Título página error
- `errorPageExpiredMessage` — Mensaje error
- `invalidEmailMessage` — Email inválido
- `passwordMinLength` — Contraseña muy corta
- `passwordPatternError` — Requisitos no cumplidos
- `passwordNotMatch` — No coinciden
- `usernameExistsMessage` — Usuario existe
- `emailExistsMessage` — Email existe

### Acciones requeridas (Required Actions)

Keycloak activa estas cuando el usuario debe completar una acción:

- `requiredAction.UPDATE_PASSWORD` — Cambiar contraseña
- `requiredAction.VERIFY_EMAIL` — Verificar email
- `requiredAction.UPDATE_PROFILE` — Actualizar perfil
- `requiredAction.CONFIGURE_TOTP` — 2FA

## Activar español como idioma por defecto

En `birrapoint-realm.json`, agregar:

```json
{
  "realm": "birrapoint",
  "defaultLocale": "es",
  "internationalizationEnabled": true,
  "supportedLocales": ["es", "en"]
}
```

Entonces Keycloak usará español si:
1. El header `Accept-Language` del navegador es `es`.
2. El usuario no ha elegido idioma explícitamente.

## Notas

- Los idiomas soportados los controla Keycloak: si no hay `messages_XX.properties`, cae al default (`messages.properties` o `messages_en.properties`).
- Las claves de Keycloak estándar (ej. `requiredAction.UPDATE_PASSWORD`) tienen traducciones built-in en el theme padre `keycloak`.
- Las claves custom (ej. `passwordStrengthWeak`) las definimos nosotros y van en los files de mensajes.

## Sincronización con Frontend

El frontend (Angular) también tiene strings de UI. Para mantener coherencia:

1. Usar los mismos colores, tipografía, iconos.
2. Traducir terms de producto de forma consistente:
   - "organizer" → "organizador/a"
   - "competition" → "competición"
   - "judge" → "juez/a"
   - "beer entry" → "inscripción cervecera"
   - "blind code" → "código a ciegas"

Ver `frontend/src/i18n/` si lo tenemos (aún no implementado en MVP).
