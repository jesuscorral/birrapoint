<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('password','password-confirm'); section>
  <#if section = "header">
    <span class="eyebrow">${msg("doForgotPassword")}</span>
    <h1 class="title">${msg("updatePasswordTitle")}</h1>
  <#elseif section = "form">
    <form id="kc-passwd-update-form" class="login-form" action="${url.loginAction}" method="post">
      <div class="field">
        <label for="password-new" class="field__label">${msg("passwordNew")}</label>
        <div class="field__control">
          <input
            type="password"
            id="password-new"
            name="password-new"
            class="input input--with-action"
            autofocus
            autocomplete="new-password"
            aria-invalid="<#if messagesPerField.existsError('password','password-confirm')>true</#if>"
            required
          />
          <button
            class="field__action"
            type="button"
            aria-label="${msg('showPassword')}"
            onclick="togglePassword('password-new')"
          >
            Show
          </button>
        </div>
        <#if messagesPerField.existsError('password')>
          <span class="field__hint" style="color: var(--color-bp-danger-600)" aria-live="polite">
            ${kcSanitize(messagesPerField.get('password'))?no_esc}
          </span>
        <#else>
          <span class="field__hint">${msg("passwordHint")}</span>
        </#if>
      </div>

      <div class="field">
        <label for="password-confirm" class="field__label">${msg("passwordConfirm")}</label>
        <div class="field__control">
          <input
            type="password"
            id="password-confirm"
            name="password-confirm"
            class="input input--with-action"
            autocomplete="new-password"
            aria-invalid="<#if messagesPerField.existsError('password-confirm')>true</#if>"
            required
          />
          <button
            class="field__action"
            type="button"
            aria-label="${msg('showPassword')}"
            onclick="togglePassword('password-confirm')"
          >
            Show
          </button>
        </div>
        <#if messagesPerField.existsError('password-confirm')>
          <span class="field__hint" style="color: var(--color-bp-danger-600)" aria-live="polite">
            ${kcSanitize(messagesPerField.get('password-confirm'))?no_esc}
          </span>
        </#if>
      </div>

      <div class="form-group">
        <label class="check">
          <input type="checkbox" id="logout-sessions" name="logout-sessions" value="on" checked/>
          <span>${msg("logoutOtherSessions")}</span>
        </label>
      </div>

      <#if isAppInitiatedAction??>
        <input class="btn btn-primary btn-block" type="submit" value="${msg('doSubmit')}"/>
        <button
          class="btn btn-block"
          style="margin-top: var(--spacing-3); background: transparent; border-color: var(--color-bp-border-strong); color: var(--color-bp-text)"
          type="submit"
          name="cancel-aia"
          value="true"
        >
          ${msg("doCancel")}
        </button>
      <#else>
        <input class="btn btn-primary btn-block" type="submit" value="${msg('doSubmit')}"/>
      </#if>
    </form>
  </#if>
</@layout.registrationLayout>

<script>
function togglePassword(inputId) {
  const input = document.getElementById(inputId);
  const btn = event.target;
  if (input.type === 'password') {
    input.type = 'text';
    btn.textContent = 'Ocultar';
  } else {
    input.type = 'password';
    btn.textContent = 'Mostrar';
  }
}
</script>
