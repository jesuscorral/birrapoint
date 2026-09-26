# Secrets (T142, ADR-0021): every application secret lives in Key Vault. The Container Apps read it
# at runtime through the Dapr secret store component below, authenticated with their own
# system-assigned managed identity (read-only "Key Vault Secrets User"); none of them receives a
# secret as a setting any more. Only the Docker Hub pull token stays a Container Apps registry
# secret: the platform needs it before any container - and so any sidecar - can start.

resource "azurerm_key_vault" "main" {
  name                = local.names.key_vault
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  tenant_id           = data.azurerm_client_config.current.tenant_id
  sku_name            = "standard"

  # Azure RBAC instead of access policies: the apps' identities get a read-only data-plane role.
  rbac_authorization_enabled = true

  # No purge protection, shortest retention: infra/teardown.ps1 purges the vault so a redeploy can
  # reuse its (globally unique) name. Every value is regenerated/re-written by Terraform anyway.
  purge_protection_enabled   = false
  soft_delete_retention_days = 7

  tags = var.tags
}

# The identity running Terraform writes the secrets.
resource "azurerm_role_assignment" "deployer_key_vault_secrets_officer" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = data.azurerm_client_config.current.object_id
}

# Azure RBAC assignments take a while to reach the Key Vault data plane; writing a secret right
# after the assignment is otherwise refused (403 ForbiddenByRbac) on a first apply.
resource "time_sleep" "key_vault_rbac_propagation" {
  create_duration = "90s"

  triggers = {
    role_assignment_id = azurerm_role_assignment.deployer_key_vault_secrets_officer.id
  }
}

locals {
  # Key Vault secret names allow only alphanumerics and '-'. The API's secrets use "--" for the
  # configuration section separator (ConnectionStrings--db -> ConnectionStrings:db, see
  # backend/src/BirraPoint.Api/Common/Secrets/DaprSecrets.cs); Keycloak's are mapped to its
  # environment variables by DAPR_SECRETS (apps.tf). The Keycloak admin-client secret is shared:
  # the API authenticates with the same value Keycloak's realm import sets.
  key_vault_secret_names = concat(
    [
      "ConnectionStrings--db",
      "ConnectionStrings--dbDirect",
      "Keycloak--AdminClientSecret",
      "keycloak-db-password",
      "keycloak-bootstrap-admin-password",
      "keycloak-deploy-client-secret",
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
    "keycloak-deploy-client-secret"     = random_password.deploy_client_secret.result
    "Smtp--Password"                    = var.smtp_password
  }
}

resource "azurerm_key_vault_secret" "app" {
  for_each = toset(local.key_vault_secret_names)

  name         = each.key
  value        = local.key_vault_secret_values[each.key]
  key_vault_id = azurerm_key_vault.main.id
  tags         = var.tags

  depends_on = [time_sleep.key_vault_rbac_propagation]
}

# Read-only access for every Container App's system-assigned identity (the web app has no secret
# today, but gets the same access so it can adopt the store without an infrastructure change).
resource "azurerm_role_assignment" "app_key_vault_secrets_user" {
  for_each = {
    api      = azurerm_container_app.api.identity[0].principal_id
    web      = azurerm_container_app.web.identity[0].principal_id
    keycloak = azurerm_container_app.keycloak.identity[0].principal_id
  }

  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = each.value
  principal_type       = "ServicePrincipal"
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
