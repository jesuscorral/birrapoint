<#import "template.ftl" as layout>
<@layout.registrationLayout displayInfo=true displayMessage=!messagesPerField.existsError('username'); section>
  <#if section = "header">
    <span class="eyebrow">${msg("problemsAccessing")}</span>
    <h1 class="title">${msg("emailForgotTitle")}</h1>
    <p class="subtitle">
      <#if realm.duplicateEmailsAllowed>
        ${msg("emailInstructionUsername")}
      <#else>
        ${msg("emailInstruction")}
      </#if>
    </p>
  <#elseif section = "form">
    <form id="kc-reset-password-form" class="login-form" action="${url.loginAction}" method="post">
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
            value="${(auth.attemptedUsername!'')}"
            class="input"
            autofocus
            autocomplete="username"
            placeholder="tu@club.com"
            aria-invalid="<#if messagesPerField.existsError('username')>true</#if>"
          />
        </div>
        <#if messagesPerField.existsError('username')>
          <span class="field__hint" style="color: var(--color-bp-danger-600)" aria-live="polite">
            ${kcSanitize(messagesPerField.get('username'))?no_esc}
          </span>
        </#if>
      </div>

      <input
        class="btn btn-primary btn-block"
        type="submit"
        value="${msg('doSubmit')}"
      />
    </form>

    <div class="form-foot">
      <a href="${url.loginUrl}" class="link">${msg("backToLogin")}</a>
    </div>

  <#elseif section = "info">
    <!-- noop — instrucciones ya mostradas en el subtitle del header -->
  </#if>
</@layout.registrationLayout>
