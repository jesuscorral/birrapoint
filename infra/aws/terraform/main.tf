locals {
  # Every deployed resource is named birrapoint-<environment>-<resource acronym> (ADR-0021); the
  # environment is lower-cased to match the Azure root's names.
  environment = lower(var.environment)
  name_prefix = "birrapoint-${local.environment}"

  # Provider default_tags (versions.tf): var.tags plus the environment, which always derives from
  # var.environment. infra/aws/teardown.ps1 selects a deployment's resources by exact name AND
  # application=birrapoint + environment=<environment>, so the sweep never touches another
  # application's (or another environment's) resources. It is still ONE BirraPoint environment per
  # AWS account and region: the generic secret names and the IAM role names would collide.
  default_tags = merge(var.tags, { environment = local.environment })
  names = {
    vpc      = "${local.name_prefix}-vpc"
    igw      = "${local.name_prefix}-igw"
    alb      = "${local.name_prefix}-alb"
    cluster  = "${local.name_prefix}-ecs"
    api      = "${local.name_prefix}-api"
    web      = "${local.name_prefix}-web"
    keycloak = "${local.name_prefix}-kc"
    # Cloud-specific: the Neon account is shared with the Azure root, whose project is
    # birrapoint-<environment>-neon. teardown.ps1 deletes only the id from the state, after checking
    # this name.
    neon_project = "${local.name_prefix}-aws-neon"
    # Cloud Map private DNS namespace: the web nginx reaches the API at api.<namespace>.
    namespace = "${local.name_prefix}.local"
  }

  # Public URLs are the CloudFront default domains (ADR-0023: no custom domain yet). They depend
  # only on the distributions, which depend only on the ALB, so the task definitions can embed
  # them without a dependency cycle.
  web_url      = "https://${aws_cloudfront_distribution.web.domain_name}"
  keycloak_url = "https://${aws_cloudfront_distribution.keycloak.domain_name}"
  # The API has no load balancer: only the web tasks' security group can reach it, by Cloud Map name.
  api_internal_url = "http://api.${local.names.namespace}:8080"

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

  # TF_VAR_neon_org_id="" (an unset GitHub variable) means "the API key's default organization".
  neon_org_id = var.neon_org_id == "" ? null : var.neon_org_id

  # Whether a Docker Hub login or an SMTP password exists is not itself a secret (only the value
  # is), and resource count/for_each cannot take sensitive values.
  has_dockerhub     = var.dockerhub_username != ""
  has_smtp_password = nonsensitive(var.smtp_password != "")
}

# Generated secrets: never in the repo or a tfvars file, only in (remote, encrypted) state and in
# Secrets Manager (secrets.tf).
resource "random_password" "keycloak_admin" {
  length  = 32
  special = false
}

resource "random_password" "api_admin_client_secret" {
  length  = 48
  special = false
}

# The ALB admits only CloudFront (security group), and forwards a request only when it carries one
# of these values in X-Origin-Verify (alb.tf): another CloudFront distribution, which shares the
# same origin-facing IP ranges, cannot reach the apps. One secret per distribution (cloudfront.tf).
resource "random_password" "origin_verify_web" {
  length  = 48
  special = false
}

resource "random_password" "origin_verify_keycloak" {
  length  = 48
  special = false
}
