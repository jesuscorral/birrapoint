output "web_url" {
  description = "Public URL of the BirraPoint PWA (CloudFront)."
  value       = local.web_url
}

output "keycloak_url" {
  description = "Public URL of Keycloak (login flows; admin console at /admin)."
  value       = local.keycloak_url
}

output "environment" {
  description = "Deployment environment (lower-cased) in every resource name, birrapoint-<environment>-<acronym>."
  value       = local.environment
}

output "region" {
  description = "AWS region of the deployment."
  value       = var.region
}

output "ecs_cluster_name" {
  description = "ECS cluster; used by the deploy workflow's `aws ecs wait services-stable`."
  value       = aws_ecs_cluster.main.name
}

output "ecs_service_names" {
  description = "ECS service name per component; used by the deploy workflow summary and health gate."
  value = {
    api      = aws_ecs_service.api.name
    web      = aws_ecs_service.web.name
    keycloak = aws_ecs_service.keycloak.name
  }
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
