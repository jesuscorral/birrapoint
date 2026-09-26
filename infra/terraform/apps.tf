# The three Container Apps (FR-046). The images carry no configuration or secrets (FR-043):
# configuration arrives as environment variables, secrets are read from Key Vault through the Dapr
# secret store with each app's system-assigned managed identity (secrets.tf; T142, ADR-0021).

locals {
  # Dapr app ids; the secret store component is scoped to these.
  dapr_app_ids = {
    api      = local.names.api
    keycloak = local.names.keycloak
  }
}

# --- Backend API: internal ingress only, reached through the web app's nginx reverse proxy -----

resource "azurerm_container_app" "api" {
  # The image is rolled out by infra/deploy.ps1 / the deploy pipeline (`az containerapp update`);
  # Terraform only sets it at creation (FR-064, T134, ADR-0018).
  lifecycle {
    ignore_changes = [template[0].container[0].image]
  }

  # The first revision must find its secrets in the vault.
  depends_on = [azurerm_key_vault_secret.app]

  name                         = local.names.api
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = azurerm_resource_group.main.name
  revision_mode                = "Single"
  tags                         = var.tags

  identity {
    type = "SystemAssigned"
  }

  dynamic "registry" {
    for_each = local.private_registry
    content {
      server               = "docker.io"
      username             = registry.value
      password_secret_name = "dockerhub-token"
    }
  }

  dynamic "secret" {
    for_each = local.private_registry
    content {
      name  = "dockerhub-token"
      value = var.dockerhub_token
    }
  }

  # Secrets (ConnectionStrings:db/dbDirect, Keycloak:AdminClientSecret, Smtp:Password) are read
  # from the Dapr secret store at startup (Dapr__SecretStore below). No app port: the sidecar only
  # serves the app's own calls to it.
  dapr {
    app_id = local.dapr_app_ids.api
  }

  ingress {
    external_enabled = false
    target_port      = 8080
    transport        = "auto"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    # Fresh per apply (infra/deploy.ps1): a rollout leaves its own suffix in state, and Container
    # Apps rejects a template change that would reuse an existing revision suffix.
    revision_suffix = var.revision_suffix

    # Exactly one replica: SignalR has no backplane and the DispatchJob worker is designed for a
    # single consumer (R-06), so this app does not scale out, and never scales to zero (the
    # worker must keep draining the job queue).
    min_replicas = 1
    max_replicas = 1

    container {
      name   = "api"
      image  = local.images.api
      cpu    = 0.5
      memory = "1Gi"

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }
      env {
        name  = "Dapr__SecretStore"
        value = azurerm_container_app_environment_dapr_component.secret_store.name
      }
      env {
        name  = "Database__MigrateOnStartup"
        value = "true"
      }
      env {
        name  = "Keycloak__Authority"
        value = "${local.keycloak_url}/realms/birrapoint"
      }
      env {
        name  = "Keycloak__ApiAudience"
        value = "birrapoint-api"
      }
      env {
        name  = "Keycloak__AdminClientId"
        value = "birrapoint-api-admin"
      }
      env {
        name  = "Smtp__Host"
        value = var.smtp_host
      }
      env {
        name  = "Smtp__Port"
        value = tostring(var.smtp_port)
      }
      env {
        name  = "Smtp__Username"
        value = var.smtp_username
      }
      env {
        name  = "Smtp__UseStartTls"
        value = tostring(var.smtp_use_starttls)
      }
      env {
        name  = "Smtp__From"
        value = "BirraPoint <${var.smtp_from_address}>"
      }
      env {
        name  = "Frontend__BaseUrl"
        value = local.web_url
      }
    }
  }
}

# --- Keycloak: public ingress for login flows (R-19) ---------------------------------------------

resource "azurerm_container_app" "keycloak" {
  # The image is rolled out by infra/deploy.ps1 / the deploy pipeline (`az containerapp update`);
  # Terraform only sets it at creation (FR-064, T134, ADR-0018).
  lifecycle {
    ignore_changes = [template[0].container[0].image]
  }

  # The first revision must find its secrets in the vault.
  depends_on = [azurerm_key_vault_secret.app]

  name                         = local.names.keycloak
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = azurerm_resource_group.main.name
  revision_mode                = "Single"
  tags                         = var.tags

  identity {
    type = "SystemAssigned"
  }

  dynamic "registry" {
    for_each = local.private_registry
    content {
      server               = "docker.io"
      username             = registry.value
      password_secret_name = "dockerhub-token"
    }
  }

  dynamic "secret" {
    for_each = local.private_registry
    content {
      name  = "dockerhub-token"
      value = var.dockerhub_token
    }
  }

  # The image's entrypoint (infra/keycloak/dapr-secrets) loads the secrets listed in DAPR_SECRETS
  # from the Dapr secret store into Keycloak's environment before starting it.
  dapr {
    app_id = local.dapr_app_ids.keycloak
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    # Fresh per apply (infra/deploy.ps1): a rollout leaves its own suffix in state, and Container
    # Apps rejects a template change that would reuse an existing revision suffix.
    revision_suffix = var.revision_suffix

    min_replicas = 1
    max_replicas = 1

    container {
      name   = "keycloak"
      image  = local.images.keycloak
      cpu    = 1
      memory = "2Gi"

      # First start imports the realm and creates Keycloak's schema on Neon — allow it time.
      startup_probe {
        transport               = "HTTP"
        port                    = 9000
        path                    = "/health/started"
        interval_seconds        = 10
        failure_count_threshold = 30
      }

      readiness_probe {
        transport = "HTTP"
        port      = 9000
        path      = "/health/ready"
      }

      liveness_probe {
        transport = "HTTP"
        port      = 9000
        path      = "/health/live"
      }

      env {
        name  = "KC_DB_URL"
        value = local.keycloak_jdbc_url
      }
      env {
        name  = "KC_DB_USERNAME"
        value = neon_role.keycloak.name
      }
      env {
        name  = "KC_HOSTNAME"
        value = local.keycloak_url
      }
      # TLS terminates at the ACA ingress, which forwards plain HTTP with X-Forwarded-* headers.
      env {
        name  = "KC_HTTP_ENABLED"
        value = "true"
      }
      env {
        name  = "KC_PROXY_HEADERS"
        value = "xforwarded"
      }
      env {
        name  = "KC_BOOTSTRAP_ADMIN_USERNAME"
        value = "admin"
      }
      # ${VAR:default} placeholders in the imported realm (infra/keycloak/birrapoint-realm.json).
      env {
        name  = "SPA_URL"
        value = local.web_url
      }
      env {
        name  = "SMTP_HOST"
        value = var.smtp_host
      }
      env {
        name  = "SMTP_PORT"
        value = tostring(var.smtp_port)
      }
      env {
        name  = "SMTP_FROM"
        value = var.smtp_from_address
      }
      env {
        name  = "SMTP_AUTH"
        value = tostring(var.smtp_username != "")
      }
      env {
        name  = "SMTP_STARTTLS"
        value = tostring(var.smtp_use_starttls)
      }
      env {
        name  = "SMTP_USER"
        value = var.smtp_username
      }
      # Secrets: environment variable = Key Vault secret, read through Dapr at container start.
      env {
        name  = "DAPR_SECRET_STORE"
        value = azurerm_container_app_environment_dapr_component.secret_store.name
      }
      env {
        name = "DAPR_SECRETS"
        value = join(",", concat(
          [
            "KC_DB_PASSWORD=keycloak-db-password",
            "KC_BOOTSTRAP_ADMIN_PASSWORD=keycloak-bootstrap-admin-password",
            "API_ADMIN_CLIENT_SECRET=Keycloak--AdminClientSecret",
            "DEPLOY_CLIENT_SECRET=keycloak-deploy-client-secret",
          ],
          local.has_smtp_password ? ["SMTP_PASSWORD=Smtp--Password"] : [],
        ))
      }
    }
  }
}

# --- Frontend: public ingress; nginx serves the PWA and proxies /api + /hubs to the API ---------

resource "azurerm_container_app" "web" {
  # The image is rolled out by infra/deploy.ps1 / the deploy pipeline (`az containerapp update`);
  # Terraform only sets it at creation (FR-064, T134, ADR-0018).
  lifecycle {
    ignore_changes = [template[0].container[0].image]
  }

  name                         = local.names.web
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = azurerm_resource_group.main.name
  revision_mode                = "Single"
  tags                         = var.tags

  identity {
    type = "SystemAssigned"
  }

  dynamic "registry" {
    for_each = local.private_registry
    content {
      server               = "docker.io"
      username             = registry.value
      password_secret_name = "dockerhub-token"
    }
  }

  dynamic "secret" {
    for_each = local.private_registry
    content {
      name  = "dockerhub-token"
      value = var.dockerhub_token
    }
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    # Fresh per apply (infra/deploy.ps1): a rollout leaves its own suffix in state, and Container
    # Apps rejects a template change that would reuse an existing revision suffix.
    revision_suffix = var.revision_suffix

    min_replicas = var.web_min_replicas
    max_replicas = 3

    container {
      name   = "web"
      image  = local.images.web
      cpu    = 0.25
      memory = "0.5Gi"

      env {
        name  = "API_UPSTREAM"
        value = local.api_internal_url
      }
      env {
        name  = "KEYCLOAK_URL"
        value = local.keycloak_url
      }
    }
  }
}
