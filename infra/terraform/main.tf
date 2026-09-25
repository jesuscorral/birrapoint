locals {
  api_name      = "${var.name_prefix}-api"
  web_name      = "${var.name_prefix}-web"
  keycloak_name = "${var.name_prefix}-kc"

  # Container App FQDNs are deterministic from the environment's default domain, so each app can
  # be told the others' URLs without a dependency cycle between the app resources.
  default_domain = azurerm_container_app_environment.main.default_domain
  web_url        = "https://${local.web_name}.${local.default_domain}"
  keycloak_url   = "https://${local.keycloak_name}.${local.default_domain}"
  # The API has internal ingress only: reachable from inside the environment, never publicly.
  api_internal_url = "https://${local.api_name}.internal.${local.default_domain}"

  images = {
    api      = "docker.io/${var.image_namespace}/birrapoint-api:${var.image_tag}"
    web      = "docker.io/${var.image_namespace}/birrapoint-web:${var.image_tag}"
    keycloak = "docker.io/${var.image_namespace}/birrapoint-keycloak:${var.image_tag}"
  }

  private_registry     = var.dockerhub_username != "" ? [var.dockerhub_username] : []
  smtp_password_secret = var.smtp_password != "" ? [1] : []
}

resource "azurerm_resource_group" "main" {
  name     = "rg-${var.name_prefix}"
  location = var.location
  tags     = var.tags
}

# Console logs and platform metrics of every Container App (FR-048; OpenTelemetry export is T098).
resource "azurerm_log_analytics_workspace" "main" {
  name                = "log-${var.name_prefix}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
  tags                = var.tags
}

# One secured environment boundary hosting frontend, backend and Keycloak (FR-046).
resource "azurerm_container_app_environment" "main" {
  name                       = "cae-${var.name_prefix}"
  location                   = azurerm_resource_group.main.location
  resource_group_name        = azurerm_resource_group.main.name
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id
  tags                       = var.tags
}

# Generated secrets: never in the repo or a tfvars file, only in (remote, encrypted) state and
# Container Apps secrets.
resource "random_password" "keycloak_admin" {
  length  = 32
  special = false
}

resource "random_password" "api_admin_client_secret" {
  length  = 48
  special = false
}
