<#import "template.ftl" as layout>
<@layout.registrationLayout; section>
  <#if section = "header">
    <!-- noop — header renderizado por template.ftl -->
  <#elseif section = "form">
    <div class="error-container">
      <div class="status-icon error-icon" aria-hidden="true">
        <svg width="30" height="30" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
          <circle cx="12" cy="12" r="9" />
          <path d="M12 7v6M12 16.5v.01" />
        </svg>
      </div>

      <#-- This template renders for several distinct Keycloak error conditions (expired action,
           CSRF/invalid state, and uncaught server exceptions among them) — previously the title
           always read "Session Expired" regardless of which one occurred, which was misleading
           for e.g. the "internal server error" case. Show a neutral title and let message.summary
           (Keycloak's actual per-case text) carry the specific explanation. -->
      <h1 class="title">${msg("errorGenericTitle")}</h1>
      <p class="subtitle">
        <#if message?? && message.summary??>
          ${message.summary}
        <#else>
          ${msg("errorPageExpiredMessage")}
        </#if>
      </p>

      <#if client?? && client.baseUrl??>
        <a href="${client.baseUrl}" class="btn btn-primary">
          ${msg("backToWelcome")}
        </a>
      <#else>
        <a href="${url.loginUrl}" class="btn btn-primary">
          ${msg("backToLogin")}
        </a>
      </#if>
    </div>
  </#if>
</@layout.registrationLayout>

<style>
.error-container {
  text-align: center;
  padding: 2rem 0;
}

.status-icon {
  width: 64px;
  height: 64px;
  display: inline-grid;
  place-items: center;
  border-radius: 50%;
  background: var(--color-bp-danger-50);
  color: var(--color-bp-danger-600);
  margin-bottom: 1.5rem;
}

.status-icon.error-icon {
  color: var(--color-bp-danger-600);
}

.error-container .title {
  font-size: 2.25rem;
  margin: 1rem 0;
}

.error-container .subtitle {
  color: var(--color-bp-text-muted);
  margin-bottom: 2rem;
}

.error-container .btn {
  margin-top: 1rem;
}
</style>
