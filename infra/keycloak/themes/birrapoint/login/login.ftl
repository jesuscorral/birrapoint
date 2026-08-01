<#import "template.ftl" as layout>
<@layout.registrationLayout displayInfo=social.displaySocialProviders; section>
  <#if section = "header">
    <span class="eyebrow">${msg("loginEyebrow")}</span>
    <h1 class="title">${msg("loginTitle")}</h1>
    <p class="subtitle">${msg("loginDescription")}</p>
  <#elseif section = "form">
    <form id="kc-form-login" class="login-form" action="${url.loginAction}" method="post">
      <#if !usernameEditingDisabled??>
        <div class="field">
          <label for="username" class="field__label">
            <#if !realm.loginWithEmailAllowed>
              ${msg("username")}
            <#elseif !realm.registrationEmailAsUsername>
              ${msg("usernameOrEmail")}
            <#else>
              ${msg("email")}
            </#if>
          </label>
          <div class="field__control">
            <input
              type="text"
              id="username"
              name="username"
              value="${(login.username!'')}"
              class="input"
              autofocus
              autocomplete="username"
              placeholder="tu@club.com"
              required
            />
          </div>
        </div>
      <#else>
        <input type="hidden" id="username" name="username" value="${login.username}"/>
      </#if>

      <div class="field">
        <label for="password" class="field__label">${msg("password")}</label>
        <div class="field__control">
          <input
            type="password"
            id="password"
            name="password"
            class="input input--with-action"
            autocomplete="current-password"
            required
          />
          <button
            class="field__action"
            type="button"
            aria-label="${msg('password')}"
            onclick="togglePassword()"
          >
            ${msg("password")?cap_first}
          </button>
        </div>
      </div>

      <div class="form-actions">
        <label class="check">
          <input
            type="checkbox"
            id="rememberMe"
            name="rememberMe"
            <#if login.rememberMe??>checked</#if>
          />
          <span>${msg("rememberMe")}</span>
        </label>

        <#if realm.resetPasswordAllowed>
          <a href="${url.loginResetCredentialsUrl}" class="link link--sm">
            ${msg("doForgotPassword")}
          </a>
        </#if>
      </div>

      <input
        class="btn btn-primary btn-block"
        type="submit"
        value="${msg('doLogIn')}"
      />
    </form>

    <#if realm.password && realm.registrationAllowed && !registrationDisabled??>
      <div class="form-foot">
        ${msg("noAccount")}
        <a href="${url.registrationUrl}" class="link">
          ${msg("doRegister")}
        </a>
      </div>
    </#if>

  <#elseif section = "info">
    <#if realm.password && social.providers??>
      <div id="kc-social-providers" class="social-providers">
        <hr class="divider" />
        <#list social.providers as p>
          <a id="social-${p.alias}" class="social-provider" href="${p.loginUrl}">
            <i class="${p.displayName}"></i>${p.displayName}
          </a>
        </#list>
      </div>
    </#if>

  <#elseif section = "socialProfile">
    <!-- noop — optional para SAML/social mapping -->
  </#if>
</@layout.registrationLayout>

<script>
function togglePassword() {
  const input = document.getElementById('password');
  const btn = event.target;
  if (input.type === 'password') {
    input.type = 'text';
    btn.textContent = 'Ocultar';
  } else {
    input.type = 'password';
    btn.textContent = 'Mostrar';
  }
}

// Disabilitar autocomplete de navegador en contraseña temporales si es necesario
document.addEventListener('DOMContentLoaded', function() {
  const form = document.getElementById('kc-form-login');
  if (form) {
    form.addEventListener('submit', function() {
      // Enviar token CSRF si está presente
    });
  }
});
</script>
