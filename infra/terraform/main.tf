locals {
  # Every deployed resource is named birrapoint-<environment>-<resource acronym> (T142, ADR-0021);
  # the environment is lower-cased because Container Apps names only allow lowercase.
  environment = lower(var.environment)
  name_prefix = "birrapoint-${local.environment}"
  names = {
    resource_group            = "${local.name_prefix}-rg"
    log_analytics_workspace   = "${local.name_prefix}-log"
    container_app_environment = "${local.name_prefix}-cae"
    key_vault                 = coalesce(var.key_vault_name, "${local.name_prefix}-kv")
    api                       = "${local.name_prefix}-api"
    web                       = "${local.name_prefix}-web"
    keycloak                  = "${local.name_prefix}-kc"
    neon_project              = "${local.name_prefix}-neon"
  }

  # Container App FQDNs are deterministic from the environment's default domain, so each app can
  # be told the others' URLs without a dependency cycle between the app resources.
  default_domain = azurerm_container_app_environment.main.default_domain
  web_url        = "https://${local.names.web}.${local.default_domain}"
  keycloak_url   = "https://${local.names.keycloak}.${local.default_domain}"
  # The API has internal ingress only: reachable from inside the environment, never publicly.
  api_internal_url = "https://${local.names.api}.internal.${local.default_domain}"

  # Image per component: "latest" -> birrapoint-<c>:latest, X.Y.Z -> birrapoint-<c>-release:X.Y.Z.
  image_versions = {
    api      = coalesce(var.api_version, var.release_version)
    web      = coalesce(var.web_version, var.release_version)
    keycloak = coalesce(var.keycloak_version, var.release_version)
  }
  images = {
    for component, version in local.image_versions :
    component => version == "latest" ? "docker.io/${var.image_namespace}/birrapoint-${component}:latest" : "docker.io/${var.image_namespace}/birrapoint-${component}-release:${version}"
  }

  private_registry = var.dockerhub_username != "" ? [var.dockerhub_username] : []
  # Whether an SMTP password exists is not itself a secret (only its value is), and resource
  # for_each cannot take sensitive values.
  has_smtp_password = nonsensitive(var.smtp_password != "")
}

data "azurerm_client_config" "current" {}

resource "azurerm_resource_group" "main" {
  name     = local.names.resource_group
  location = var.location
  tags     = var.tags
}

# Console logs and platform metrics of every Container App (FR-048; OpenTelemetry export is T098).
resource "azurerm_log_analytics_workspace" "main" {
  name                = local.names.log_analytics_workspace
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
  tags                = var.tags
}

# One secured environment boundary hosting frontend, backend and Keycloak (FR-046).
resource "azurerm_container_app_environment" "main" {
  name                       = local.names.container_app_environment
  location                   = azurerm_resource_group.main.location
  resource_group_name        = azurerm_resource_group.main.name
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id
  tags                       = var.tags
}

# Generated secrets: never in the repo or a tfvars file, only in (remote, encrypted) state and in
# Key Vault (secrets.tf).
resource "random_password" "keycloak_admin" {
  length  = 32
  special = false
}

resource "random_password" "api_admin_client_secret" {
  length  = 48
  special = false
}
