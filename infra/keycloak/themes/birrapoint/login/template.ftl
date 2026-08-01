<#-- displayMessage matches the parameter every built-in Keycloak page (reset password, update
     password, verify email, etc.) passes to this macro — without it, any page this theme doesn't
     override crashes with "has no parameter with name displayMessage" as soon as it's rendered
     against this custom layout. -->
<#macro registrationLayout displayInfo=false displayMessage=true displayWatermark=true showAnotherWayIfPresent=true>
  <!DOCTYPE html>
  <html lang="es">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>
      <#if pageTitle??>
        ${pageTitle}
      <#else>
        BirraPoint · Acceso seguro
      </#if>
    </title>
    <link rel="preconnect" href="https://fonts.googleapis.com" />
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
    <link
      href="https://fonts.googleapis.com/css2?family=Fraunces:opsz,wght@9..144,500;9..144,600;9..144,700&family=Inter:wght@400;500;600;700&display=swap"
      rel="stylesheet"
    />
    <link href="${url.resourcesPath}/css/style.css" rel="stylesheet">
  </head>

  <body>
    <div class="auth">
      <!-- Panel de marca -->
      <aside class="brand-panel">
        <svg class="brand-panel__bubbles" aria-hidden="true">
          <circle cx="14%" cy="72%" r="46" fill="rgba(255,255,255,.06)" />
          <circle cx="30%" cy="86%" r="22" fill="rgba(255,255,255,.05)" />
          <circle cx="78%" cy="24%" r="60" fill="rgba(255,255,255,.05)" />
          <circle cx="62%" cy="60%" r="14" fill="rgba(255,255,255,.07)" />
          <circle cx="88%" cy="70%" r="28" fill="rgba(255,255,255,.04)" />
        </svg>

        <div class="logo">
          <span class="logo__mark" aria-hidden="true">
            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="#FBFAF6" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M7 8h9v11a2 2 0 0 1-2 2H9a2 2 0 0 1-2-2z" />
              <path d="M16 10h2a2 2 0 0 1 2 2v3a2 2 0 0 1-2 2h-2" />
              <path d="M7 8a2.5 2.5 0 0 1 .6-3.6A2.5 2.5 0 0 1 11.5 5a2.5 2.5 0 0 1 4.4 1.2A2 2 0 0 1 16 8" />
            </svg>
          </span>
          BirraPoint
        </div>

        <div>
          <h2 class="brand-panel__claim">Cada cata, en su punto.</h2>
          <p class="brand-panel__lead">
            Tu sesión es gestionada por el proveedor de identidad de BirraPoint.
          </p>
        </div>

        <div class="brand-panel__foot">
          <span>Acceso seguro · OIDC</span>
        </div>
      </aside>

      <!-- Panel de formulario -->
      <main class="form-panel">
        <div class="form-panel__inner">
          <#if displayMessage && message?? && message.summary??>
            <div class="alert alert-error" role="alert">
              <svg class="alert__icon" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" aria-hidden="true">
                <circle cx="12" cy="12" r="9" />
                <path d="M12 7v6M12 16.5v.01" />
              </svg>
              <span>
                <strong class="alert-title">${message.summary}</strong>
                <#if message.detail??>${message.detail}</#if>
              </span>
            </div>
          </#if>

          <#-- Each template owns its own eyebrow/title/subtitle in its "header" section instead of
               this macro guessing page type — Keycloak never actually exposes a `pageType`
               variable, so the old #if pageType == "register" check silently always fell through
               to the login copy (every page, including error.ftl, showed "Acceso / Inicia sesión")
               and never reflected what was really being rendered. -->
          <#nested "header">

          <#nested "form">
        </div>
      </main>
    </div>

    <#nested "info">
  </body>

  </html>
</#macro>
