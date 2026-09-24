output "web_url" {
  description = "Public URL of the BirraPoint PWA."
  value       = local.web_url
}

output "keycloak_url" {
  description = "Public URL of Keycloak (login flows; admin console at /admin)."
  value       = local.keycloak_url
}

output "api_internal_url" {
  description = "Internal-only URL of the API, reachable from inside the Container Apps environment."
  value       = local.api_internal_url
}

output "resource_group_name" {
  description = "Resource group holding every Azure resource of this deployment."
  value       = azurerm_resource_group.main.name
}

output "neon_project_id" {
  description = "Neon project id (point-in-time restore is done against this project — see README)."
  value       = neon_project.main.id
}

output "keycloak_admin_password" {
  description = "Keycloak bootstrap admin password (user `admin`). Read with `terraform output -raw keycloak_admin_password`."
  value       = random_password.keycloak_admin.result
  sensitive   = true
}
