# Secrets (T142, ADR-0021): every application secret lives in Key Vault. The Container Apps read it
# at runtime through the Dapr secret store component below, authenticated with their own
# system-assigned managed identity, which can read only the secrets that app uses (read-only
# "Key Vault Secrets User" per secret); none of them receives a secret as a setting any more. Only the Docker Hub pull token stays a Container Apps registry
# secret: the platform needs it before any container - and so any sidecar - can start.

resource "azurerm_key_vault" "main" {
  name                = local.names.key_vault
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  tenant_id           = data.azurerm_client_config.current.tenant_id
  sku_name            = "standard"

  # Azure RBAC instead of access policies: the apps' identities get a read-only data-plane role.
  rbac_authorization_enabled = true

  # No purge protection, shortest retention: infra/azure/teardown.ps1 purges the vault so a redeploy can
  # reuse its (globally unique) name. Every value is regenerated/re-written by Terraform anyway.
  purge_protection_enabled   = false
  soft_delete_retention_days = 7

  tags = var.tags
}

# Who may write the secrets: the identity running Terraform plus key_vault_secrets_officer_principal_ids
# (an Entra group of every operator/CI identity that runs Terraform). Without the latter, an
# identity other than the last one to apply cannot even refresh the secrets (403), so plan, apply
# and destroy fail for it until it is granted Secrets Officer on the vault.
resource "azurerm_role_assignment" "key_vault_secrets_officer" {
  for_each = toset(concat([data.azurerm_client_config.current.object_id], var.key_vault_secrets_officer_principal_ids))

  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = each.value
}

# Azure RBAC assignments take a while to reach the Key Vault data plane; writing a secret right
# after the assignment is otherwise refused (403 ForbiddenByRbac) on a first apply.
resource "time_sleep" "key_vault_rbac_propagation" {
  create_duration = "90s"

  triggers = {
    role_assignment_ids = join(",", sort([for assignment in azurerm_role_assignment.key_vault_secrets_officer : assignment.id]))
  }
}

locals {
  # Key Vault secret names allow only alphanumerics and '-'. The API's secrets use "--" for the
  # configuration section separator (ConnectionStrings--db -> ConnectionStrings:db, see
  # backend/src/BirraPoint.Api/Common/Secrets/DaprSecrets.cs); Keycloak's are mapped to its
  # environment variables by DAPR_SECRETS (apps.tf, built from keycloak_secret_env below). The
  # Keycloak admin-client secret is shared: the API authenticates with the same value Keycloak's
  # realm import sets.
  key_vault_secret_names = concat(
    [
      "ConnectionStrings--db",
      "ConnectionStrings--dbDirect",
      "Keycloak--AdminClientSecret",
      "keycloak-db-password",
      "keycloak-bootstrap-admin-password",
    ],
    # Key Vault rejects empty values: stored only when a password is configured.
    local.has_smtp_password ? ["Smtp--Password"] : [],
  )

  key_vault_secret_values = {
    "ConnectionStrings--db"             = local.api_db_pooled
    "ConnectionStrings--dbDirect"       = local.api_db_direct
    "Keycloak--AdminClientSecret"       = random_password.api_admin_client_secret.result
    "keycloak-db-password"              = neon_role.keycloak.password
    "keycloak-bootstrap-admin-password" = random_password.keycloak_admin.result
    "Smtp--Password"                    = var.smtp_password
  }

  # Keycloak environment variable -> Key Vault secret (loaded by the image's entrypoint).
  keycloak_secret_env = merge(
    {
      KC_DB_PASSWORD              = "keycloak-db-password"
      KC_BOOTSTRAP_ADMIN_PASSWORD = "keycloak-bootstrap-admin-password"
      API_ADMIN_CLIENT_SECRET     = "Keycloak--AdminClientSecret"
    },
    local.has_smtp_password ? { SMTP_PASSWORD = "Smtp--Password" } : {},
  )

  # Least privilege: each app's identity reads exactly the secrets it uses - Dapr component scopes
  # only decide which apps may use the component, not which secrets they get, and an identity can
  # also call Key Vault directly. The API's list must match DaprSecrets.cs. The web app has an
  # identity but no secret, so no grant (ADR-0021).
  app_secret_names = {
    api = concat(
      ["ConnectionStrings--db", "ConnectionStrings--dbDirect", "Keycloak--AdminClientSecret"],
      local.has_smtp_password ? ["Smtp--Password"] : [],
    )
    keycloak = values(local.keycloak_secret_env)
  }

  app_principal_ids = {
    api      = azurerm_container_app.api.identity[0].principal_id
    keycloak = azurerm_container_app.keycloak.identity[0].principal_id
  }

  app_secret_grants = merge([
    for app, names in local.app_secret_names : {
      for name in names : "${app}/${name}" => { app = app, secret = name }
    }
  ]...)
}

resource "azurerm_key_vault_secret" "app" {
  for_each = toset(local.key_vault_secret_names)

  name         = each.key
  value        = local.key_vault_secret_values[each.key]
  key_vault_id = azurerm_key_vault.main.id
  tags         = var.tags

  depends_on = [time_sleep.key_vault_rbac_propagation]
}

# Read-only access, per secret, for the app that uses it.
resource "azurerm_role_assignment" "app_key_vault_secrets_user" {
  for_each = local.app_secret_grants

  scope                = azurerm_key_vault_secret.app[each.value.secret].resource_versionless_id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = local.app_principal_ids[each.value.app]
  principal_type       = "ServicePrincipal"
}

# An app's first revision starts before its identity has these roles (they need the identity,
# which exists only with the app). Waiting here makes `terraform apply` return only once they
# have propagated, so the revisions it started can read their secrets; the apps also retry on their own (DaprSecrets.cs, DaprSecretsEnv.java).
resource "time_sleep" "app_rbac_propagation" {
  create_duration = "120s"

  triggers = {
    role_assignment_ids = join(",", sort([for assignment in azurerm_role_assignment.app_key_vault_secrets_user : assignment.id]))
  }
}

# Dapr secret store over the vault. No client id in its metadata: the sidecar authenticates with
# the calling app's system-assigned identity. Scoped to the apps that read secrets.
resource "azurerm_container_app_environment_dapr_component" "secret_store" {
  name                         = var.dapr_secret_store_name
  container_app_environment_id = azurerm_container_app_environment.main.id
  component_type               = "secretstores.azure.keyvault"
  version                      = "v1"
  scopes                       = [local.dapr_app_ids.api, local.dapr_app_ids.keycloak]

  metadata {
    name  = "vaultName"
    value = azurerm_key_vault.main.name
  }
}
