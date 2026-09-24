# The three Container Apps (FR-046). Configuration and secrets arrive only as environment
# variables / Container Apps secrets; the images carry none (FR-043).

# --- Backend API: internal ingress only, reached through the web app's nginx reverse proxy -----

resource "azurerm_container_app" "api" {
  name                         = local.api_name
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = azurerm_resource_group.main.name
  revision_mode                = "Single"
  tags                         = var.tags

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

  secret {
    name  = "db-pooled"
    value = local.api_db_pooled
  }

  secret {
    name  = "db-direct"
    value = local.api_db_direct
  }

  secret {
    name  = "keycloak-admin-client-secret"
    value = random_password.api_admin_client_secret.result
  }

  # Container Apps rejects empty secret values, so the SMTP password secret (and the env var
  # referencing it) only exist when a password is configured.
  dynamic "secret" {
    for_each = local.smtp_password_secret
    content {
      name  = "smtp-password"
      value = var.smtp_password
    }
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
        name        = "ConnectionStrings__db"
        secret_name = "db-pooled"
      }
      env {
        name        = "ConnectionStrings__dbDirect"
        secret_name = "db-direct"
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
        name        = "Keycloak__AdminClientSecret"
        secret_name = "keycloak-admin-client-secret"
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
      dynamic "env" {
        for_each = local.smtp_password_secret
        content {
          name        = "Smtp__Password"
          secret_name = "smtp-password"
        }
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
  name                         = local.keycloak_name
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = azurerm_resource_group.main.name
  revision_mode                = "Single"
  tags                         = var.tags

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

  secret {
    name  = "db-password"
    value = neon_role.keycloak.password
  }

  secret {
    name  = "bootstrap-admin-password"
    value = random_password.keycloak_admin.result
  }

  secret {
    name  = "api-admin-client-secret"
    value = random_password.api_admin_client_secret.result
  }

  # Container Apps rejects empty secret values, so the SMTP password secret (and the env var
  # referencing it) only exist when a password is configured.
  dynamic "secret" {
    for_each = local.smtp_password_secret
    content {
      name  = "smtp-password"
      value = var.smtp_password
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
        name        = "KC_DB_PASSWORD"
        secret_name = "db-password"
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
      env {
        name        = "KC_BOOTSTRAP_ADMIN_PASSWORD"
        secret_name = "bootstrap-admin-password"
      }
      # ${env.*} placeholders in the imported realm (infra/keycloak/birrapoint-realm.json).
      env {
        name  = "SPA_URL"
        value = local.web_url
      }
      env {
        name        = "API_ADMIN_CLIENT_SECRET"
        secret_name = "api-admin-client-secret"
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
      dynamic "env" {
        for_each = local.smtp_password_secret
        content {
          name        = "SMTP_PASSWORD"
          secret_name = "smtp-password"
        }
      }
    }
  }
}

# --- Frontend: public ingress; nginx serves the PWA and proxies /api + /hubs to the API ---------

resource "azurerm_container_app" "web" {
  name                         = local.web_name
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = azurerm_resource_group.main.name
  revision_mode                = "Single"
  tags                         = var.tags

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
