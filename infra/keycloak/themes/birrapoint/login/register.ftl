<#import "template.ftl" as layout>
<@layout.registrationLayout displayInfo=social.displaySocialProviders; section>
  <#if section = "header">
    <span class="eyebrow">${msg("registerEyebrow")}</span>
    <h1 class="title">${msg("registerTitle")}</h1>
    <p class="subtitle">${msg("registerDescription")}</p>
  <#elseif section = "form">
    <form id="kc-register-form" class="register-form" action="${url.registrationAction}" method="post">
      <div class="form-group">
        <div class="field">
          <label for="firstName" class="field__label">
            ${msg("firstName")}
            <span class="req" aria-hidden="true">*</span>
          </label>
          <div class="field__control">
            <input
              type="text"
              id="firstName"
              name="firstName"
              class="input"
              value="${(register.formData.firstName!'')}"
              placeholder="${msg('firstName')}"
              required
            />
          </div>
        </div>
      </div>

      <div class="form-group">
        <div class="field">
          <label for="lastName" class="field__label">
            ${msg("lastName")}
            <span class="req" aria-hidden="true">*</span>
          </label>
          <div class="field__control">
            <input
              type="text"
              id="lastName"
              name="lastName"
              class="input"
              value="${(register.formData.lastName!'')}"
              placeholder="${msg('lastName')}"
              required
            />
          </div>
        </div>
      </div>

      <div class="form-group">
        <div class="field">
          <label for="email" class="field__label">
            ${msg("email")}
            <span class="req" aria-hidden="true">*</span>
          </label>
          <div class="field__control">
            <input
              type="email"
              id="email"
              name="email"
              class="input"
              value="${(register.formData.email!'')}"
              placeholder="tu@club.com"
              autocomplete="email"
              required
            />
          </div>
          <span class="field__hint">
            ${msg("emailHint")}
          </span>
        </div>
      </div>

      <div class="form-group">
        <div class="field">
          <label for="password" class="field__label">
            ${msg("password")}
            <span class="req" aria-hidden="true">*</span>
          </label>
          <div class="field__control">
            <input
              type="password"
              id="password"
              name="password"
              class="input input--with-action"
              placeholder=""
              autocomplete="new-password"
              aria-describedby="password-meter password-hint"
              required
            />
            <button
              class="field__action"
              type="button"
              aria-label="${msg('password')}"
              onclick="togglePassword('password')"
            >
              Show
            </button>
          </div>
          <div class="meter" id="password-meter" data-level="1">
            <div class="meter__track" aria-hidden="true">
              <span class="meter__seg"></span>
              <span class="meter__seg"></span>
              <span class="meter__seg"></span>
              <span class="meter__seg"></span>
            </div>
            <span class="meter__label" role="status">${msg("passwordStrengthWeak")}</span>
          </div>
          <span class="field__hint" id="password-hint">
            ${msg("passwordHint")}
          </span>
        </div>
      </div>

      <div class="form-group">
        <div class="field">
          <label for="password-confirm" class="field__label">
            ${msg("passwordConfirm")}
            <span class="req" aria-hidden="true">*</span>
          </label>
          <div class="field__control">
            <input
              type="password"
              id="password-confirm"
              name="password-confirm"
              class="input input--with-action"
              placeholder=""
              autocomplete="new-password"
              required
            />
            <button
              class="field__action"
              type="button"
              aria-label="${msg('passwordConfirm')}"
              onclick="togglePassword('password-confirm')"
            >
              Show
            </button>
          </div>
        </div>
      </div>

      <div class="form-group">
        <label class="check">
          <input type="checkbox" name="acceptTerms" required/>
          <span>
            ${msg("acceptTerms")} <a href="#" class="link">${msg("termsText")}</a> ${msg("and")}
            <a href="#" class="link">${msg("privacyText")}</a>.
            <span class="req" aria-hidden="true">*</span>
          </span>
        </label>
      </div>

      <input
        class="btn btn-primary btn-block"
        type="submit"
        value="${msg('registerSubmit')}"
      />
    </form>

    <div class="form-foot">
      ${msg("alreadyHaveAccount")}
      <a href="${url.loginUrl}" class="link">
        ${msg("doLogIn")}
      </a>
    </div>

  <#elseif section = "info">
    <!-- noop — social providers, etc. -->
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

// Validación de fuerza de contraseña (simple)
document.addEventListener('DOMContentLoaded', function() {
  const passwordInput = document.getElementById('password');
  const meterLabel = document.querySelector('.meter__label');
  const meter = document.getElementById('password-meter');

  if (passwordInput) {
    passwordInput.addEventListener('input', function() {
      const strength = estimatePasswordStrength(this.value);
      meter.setAttribute('data-level', strength);
      const labels = ['débil', 'regular', 'media', 'fuerte'];
      meterLabel.textContent = 'Seguridad: ' + labels[strength - 1];
    });
  }
});

function estimatePasswordStrength(password) {
  let score = 0;
  if (password.length >= 10) score++;
  if (/[a-z]/.test(password)) score++;
  if (/[A-Z]/.test(password)) score++;
  if (/[0-9]/.test(password)) score++;
  if (/[^a-zA-Z0-9]/.test(password)) score++;
  return Math.min(Math.ceil(score / 1.25), 4);
}
</script>
