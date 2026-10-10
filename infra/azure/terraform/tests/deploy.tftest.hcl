# Plan-level tests of the deployment (T143): image references, resource names and the absence of
# the removed birrapoint-deploy secret. Every provider is mocked, so no credentials are needed:
#   terraform -chdir=infra/azure/terraform init -backend=false && terraform -chdir=infra/azure/terraform test

mock_provider "azurerm" {
  # Provider-side validation rejects the random strings mocks generate for UUID attributes.
  mock_data "azurerm_client_config" {
    defaults = {
      tenant_id       = "00000000-0000-0000-0000-000000000001"
      object_id       = "00000000-0000-0000-0000-000000000002"
      client_id       = "00000000-0000-0000-0000-000000000003"
      subscription_id = "00000000-0000-0000-0000-000000000004"
    }
  }

  mock_resource "azurerm_container_app" {
    defaults = {
      identity = [{
        type         = "SystemAssigned"
        principal_id = "00000000-0000-0000-0000-000000000005"
        tenant_id    = "00000000-0000-0000-0000-000000000001"
      }]
    }
  }
}
mock_provider "neon" {}
mock_provider "random" {}
mock_provider "time" {}

variables {
  image_namespace   = "acme"
  smtp_host         = "smtp.example.com"
  smtp_from_address = "no-reply@example.com"
  smtp_password     = "not-a-real-password"
  environment       = "PROD"
  release_version   = "latest"
}

run "latest_images_when_requested" {
  command = plan

  assert {
    condition     = azurerm_container_app.api.template[0].container[0].image == "docker.io/acme/birrapoint-api:latest"
    error_message = "api must default to docker.io/<ns>/birrapoint-api:latest"
  }
  assert {
    condition     = azurerm_container_app.web.template[0].container[0].image == "docker.io/acme/birrapoint-web:latest"
    error_message = "web must default to docker.io/<ns>/birrapoint-web:latest"
  }
  assert {
    condition     = azurerm_container_app.keycloak.template[0].container[0].image == "docker.io/acme/birrapoint-keycloak:latest"
    error_message = "keycloak must default to docker.io/<ns>/birrapoint-keycloak:latest"
  }
}

run "released_version_uses_release_repositories" {
  command = plan

  variables {
    release_version = "1.2.3"
  }

  assert {
    condition     = azurerm_container_app.api.template[0].container[0].image == "docker.io/acme/birrapoint-api-release:1.2.3"
    error_message = "api must use the -release repository for X.Y.Z"
  }
  assert {
    condition     = azurerm_container_app.web.template[0].container[0].image == "docker.io/acme/birrapoint-web-release:1.2.3"
    error_message = "web must use the -release repository for X.Y.Z"
  }
  assert {
    condition     = azurerm_container_app.keycloak.template[0].container[0].image == "docker.io/acme/birrapoint-keycloak-release:1.2.3"
    error_message = "keycloak must use the -release repository for X.Y.Z"
  }
}

run "per_component_override" {
  command = plan

  variables {
    release_version  = "1.2.3"
    api_version      = "1.3.0"
    keycloak_version = "latest"
  }

  assert {
    condition     = azurerm_container_app.api.template[0].container[0].image == "docker.io/acme/birrapoint-api-release:1.3.0"
    error_message = "api_version must override release_version for the API only"
  }
  assert {
    condition     = azurerm_container_app.web.template[0].container[0].image == "docker.io/acme/birrapoint-web-release:1.2.3"
    error_message = "web must keep release_version"
  }
  assert {
    condition     = azurerm_container_app.keycloak.template[0].container[0].image == "docker.io/acme/birrapoint-keycloak:latest"
    error_message = "keycloak_version = latest must select the CI image"
  }
}

run "invalid_release_version_is_rejected" {
  command = plan

  variables {
    release_version = "v1.2"
  }

  expect_failures = [var.release_version]
}

run "invalid_component_version_is_rejected" {
  command = plan

  variables {
    api_version = "1.2"
  }

  expect_failures = [var.api_version]
}

run "resource_names_follow_the_convention" {
  command = plan

  assert {
    condition     = azurerm_resource_group.main.name == "birrapoint-prod-rg"
    error_message = "resource group must be birrapoint-prod-rg"
  }
  assert {
    condition     = azurerm_key_vault.main.name == "birrapoint-prod-kv"
    error_message = "key vault must be birrapoint-prod-kv"
  }
  assert {
    condition     = azurerm_container_app.api.name == "birrapoint-prod-api" && azurerm_container_app.web.name == "birrapoint-prod-web" && azurerm_container_app.keycloak.name == "birrapoint-prod-kc"
    error_message = "container apps must be birrapoint-prod-api/-web/-kc"
  }
  assert {
    condition     = azurerm_container_app_environment.main.name == "birrapoint-prod-cae" && azurerm_log_analytics_workspace.main.name == "birrapoint-prod-log"
    error_message = "environment and workspace must be birrapoint-prod-cae/-log"
  }
  assert {
    condition     = neon_project.main.name == "birrapoint-prod-neon"
    error_message = "neon project must be birrapoint-prod-neon"
  }
}

run "no_deploy_client_secret_in_key_vault" {
  command = plan

  assert {
    condition     = !contains(keys(azurerm_key_vault_secret.app), "keycloak-deploy-client-secret")
    error_message = "the birrapoint-deploy client is removed: its secret must not be stored"
  }
  assert {
    condition     = !contains(values(local.keycloak_secret_env), "keycloak-deploy-client-secret")
    error_message = "Keycloak must not be told to load a deploy client secret"
  }
}

run "release_version_is_required" {
  command = plan

  variables {
    release_version = null
  }

  expect_failures = [var.release_version]
}

run "placeholder_image_namespace_is_rejected" {
  command = plan

  variables {
    image_namespace = "CHANGE-ME"
  }

  expect_failures = [var.image_namespace]
}

run "empty_neon_org_id_means_the_api_key_default" {
  command = plan

  variables {
    neon_org_id = ""
  }

  assert {
    condition     = local.neon_org_id == null && neon_project.main.org_id == null
    error_message = "TF_VAR_neon_org_id empty must become null (the API key's default organization)"
  }
}

run "neon_org_id_is_used_when_set" {
  command = plan

  variables {
    neon_org_id = "org-test-1"
  }

  assert {
    condition     = local.neon_org_id == "org-test-1" && neon_project.main.org_id == "org-test-1"
    error_message = "a set neon_org_id must be passed to the project"
  }
}

run "dispatch_poll_interval_defaults_to_one_hour" {
  command = plan

  assert {
    condition     = [for e in azurerm_container_app.api.template[0].container[0].env : e.value if e.name == "Dispatch__SafetyNetPollInterval"] == ["01:00:00"]
    error_message = "the API must receive Dispatch__SafetyNetPollInterval=01:00:00 by default (Neon compute budget)"
  }
}

run "dispatch_poll_interval_is_configurable" {
  command = plan

  variables {
    dispatch_poll_interval = "00:05:00"
  }

  assert {
    condition     = [for e in azurerm_container_app.api.template[0].container[0].env : e.value if e.name == "Dispatch__SafetyNetPollInterval"] == ["00:05:00"]
    error_message = "dispatch_poll_interval must reach the API container"
  }
}

run "invalid_dispatch_poll_interval_is_rejected" {
  command = plan

  variables {
    dispatch_poll_interval = "30s"
  }

  expect_failures = [var.dispatch_poll_interval]
}

run "zero_dispatch_poll_interval_is_rejected" {
  command = plan

  variables {
    dispatch_poll_interval = "00:00:00"
  }

  expect_failures = [var.dispatch_poll_interval]
}
