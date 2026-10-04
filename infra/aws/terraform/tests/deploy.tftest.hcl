# Plan/apply-level tests of the AWS deployment (T145, ADR-0023). Every provider is mocked, so no
# credentials are needed and nothing is created:
#   terraform -chdir=infra/aws/terraform init -backend=false && terraform -chdir=infra/aws/terraform test
# Tests that read the task definitions (they embed the CloudFront domains) or compare one resource's attribute with another's id use `command = apply` (against the
# mocks): ids are unknown during a plain plan.

mock_provider "aws" {
  mock_data "aws_availability_zones" {
    defaults = {
      names = ["eu-central-1a", "eu-central-1b", "eu-central-1c"]
    }
  }

  # Provider-side validation rejects the random strings mocks generate for ARN attributes that
  # other resources consume. (Secret ARNs are only embedded in JSON policies, so they stay
  # distinct per secret.)
  mock_resource "aws_lb" {
    defaults = {
      arn = "arn:aws:elasticloadbalancing:eu-central-1:111111111111:loadbalancer/app/birrapoint-prod-alb/0123456789abcdef"
    }
  }
  mock_resource "aws_lb_listener" {
    defaults = {
      arn = "arn:aws:elasticloadbalancing:eu-central-1:111111111111:listener/app/birrapoint-prod-alb/0123456789abcdef/0123456789abcdef"
    }
  }
  mock_resource "aws_lb_target_group" {
    defaults = {
      arn = "arn:aws:elasticloadbalancing:eu-central-1:111111111111:targetgroup/birrapoint-prod-tg/0123456789abcdef"
    }
  }
  mock_resource "aws_iam_role" {
    defaults = {
      arn = "arn:aws:iam::111111111111:role/birrapoint-prod-role"
    }
  }
  mock_resource "aws_service_discovery_service" {
    defaults = {
      arn = "arn:aws:servicediscovery:eu-central-1:111111111111:service/srv-0123456789abcdef"
    }
  }
}
mock_provider "neon" {}
mock_provider "random" {}

variables {
  image_namespace   = "acme"
  smtp_host         = "smtp.example.com"
  smtp_from_address = "no-reply@example.com"
  smtp_password     = "not-a-real-password"
  environment       = "PROD"
  release_version   = "latest"
}

# --- Images and version variables (same logic as the Azure root) -------------------------------

run "latest_images_when_requested" {
  command = apply

  assert {
    condition     = local.images.api == "docker.io/acme/birrapoint-api:latest" && local.images.web == "docker.io/acme/birrapoint-web:latest" && local.images.keycloak == "docker.io/acme/birrapoint-keycloak:latest"
    error_message = "images must default to docker.io/<ns>/birrapoint-<c>:latest"
  }
  assert {
    condition     = [for c in jsondecode(aws_ecs_task_definition.api.container_definitions) : c.image if c.name == "api"][0] == "docker.io/acme/birrapoint-api:latest"
    error_message = "the api task must run the api image"
  }
}

run "released_version_uses_release_repositories" {
  command = apply

  variables {
    release_version = "1.2.3"
  }

  assert {
    condition     = local.images.api == "docker.io/acme/birrapoint-api-release:1.2.3" && local.images.web == "docker.io/acme/birrapoint-web-release:1.2.3" && local.images.keycloak == "docker.io/acme/birrapoint-keycloak-release:1.2.3"
    error_message = "X.Y.Z must use the -release repositories"
  }
  assert {
    condition     = [for c in jsondecode(aws_ecs_task_definition.web.container_definitions) : c.image if c.name == "web"][0] == "docker.io/acme/birrapoint-web-release:1.2.3"
    error_message = "the web task must run the release image"
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
    condition     = local.images.api == "docker.io/acme/birrapoint-api-release:1.3.0" && local.images.web == "docker.io/acme/birrapoint-web-release:1.2.3" && local.images.keycloak == "docker.io/acme/birrapoint-keycloak:latest"
    error_message = "component versions must override release_version for that component only"
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
    web_version = "1.2"
  }

  expect_failures = [var.web_version]
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

run "invalid_environment_is_rejected" {
  command = plan

  variables {
    environment = "p"
  }

  expect_failures = [var.environment]
}

# --- Naming (ADR-0021) -------------------------------------------------------------------------

run "resource_names_follow_the_convention" {
  command = plan

  assert {
    condition     = aws_ecs_cluster.main.name == "birrapoint-prod-ecs"
    error_message = "cluster must be birrapoint-prod-ecs"
  }
  assert {
    condition     = aws_ecs_service.api.name == "birrapoint-prod-api" && aws_ecs_service.web.name == "birrapoint-prod-web" && aws_ecs_service.keycloak.name == "birrapoint-prod-kc"
    error_message = "services must be birrapoint-prod-api/-web/-kc"
  }
  assert {
    condition     = aws_lb.main.name == "birrapoint-prod-alb" && aws_vpc.main.tags["Name"] == "birrapoint-prod-vpc"
    error_message = "ALB and VPC must be birrapoint-prod-alb/-vpc"
  }
  assert {
    condition     = aws_cloudfront_distribution.web.comment == "birrapoint-prod-web" && aws_cloudfront_distribution.keycloak.comment == "birrapoint-prod-kc"
    error_message = "distributions must be commented birrapoint-prod-web/-kc"
  }
  assert {
    condition     = neon_project.main.name == "birrapoint-prod-neon"
    error_message = "neon project must be birrapoint-prod-neon"
  }
  assert {
    condition     = aws_service_discovery_private_dns_namespace.main.name == "birrapoint-prod.local"
    error_message = "service discovery namespace must be birrapoint-prod.local"
  }
}

# --- Network: nothing but CloudFront reaches the ALB, nothing but the ALB the apps -------------

run "alb_admits_only_the_cloudfront_prefix_list" {
  command = apply

  assert {
    condition     = aws_vpc_security_group_ingress_rule.alb_from_cloudfront.prefix_list_id == data.aws_ec2_managed_prefix_list.cloudfront.id
    error_message = "the ALB must admit only the CloudFront origin-facing prefix list"
  }
  assert {
    condition     = aws_vpc_security_group_ingress_rule.alb_from_cloudfront.from_port == 80 && aws_vpc_security_group_ingress_rule.alb_from_cloudfront.to_port == 80 && aws_vpc_security_group_ingress_rule.alb_from_cloudfront.ip_protocol == "tcp"
    error_message = "the ALB ingress must be tcp/80"
  }
  assert {
    condition     = aws_vpc_security_group_ingress_rule.alb_from_cloudfront.cidr_ipv4 == null
    error_message = "the ALB must not admit any CIDR"
  }
  assert {
    condition     = data.aws_ec2_managed_prefix_list.cloudfront.name == "com.amazonaws.global.cloudfront.origin-facing"
    error_message = "must look up the CloudFront origin-facing managed prefix list"
  }
}

run "api_admits_only_the_web_security_group" {
  command = apply

  assert {
    condition     = aws_vpc_security_group_ingress_rule.api_from_web.referenced_security_group_id == aws_security_group.web.id && aws_vpc_security_group_ingress_rule.api_from_web.from_port == 8080
    error_message = "the API must admit tcp/8080 only from the web security group"
  }
  assert {
    condition     = aws_vpc_security_group_ingress_rule.web_from_alb.referenced_security_group_id == aws_security_group.alb.id && aws_vpc_security_group_ingress_rule.keycloak_from_alb.referenced_security_group_id == aws_security_group.alb.id
    error_message = "web and Keycloak must admit only the ALB security group"
  }
  assert {
    condition     = aws_vpc_security_group_ingress_rule.keycloak_health_from_alb.referenced_security_group_id == aws_security_group.alb.id && aws_vpc_security_group_ingress_rule.keycloak_health_from_alb.from_port == 9000
    error_message = "Keycloak's management port 9000 (health checks) must admit only the ALB"
  }
}

run "two_public_subnets_in_two_zones" {
  command = plan

  assert {
    condition     = length(aws_subnet.public) == 2 && aws_subnet.public[0].availability_zone != aws_subnet.public[1].availability_zone
    error_message = "two public subnets in two availability zones"
  }
  assert {
    condition     = alltrue([for s in aws_subnet.public : s.map_public_ip_on_launch == false])
    error_message = "subnets must not hand out public IPs implicitly (tasks ask for one explicitly)"
  }
}

run "listener_refuses_requests_without_the_origin_header" {
  command = plan

  assert {
    condition     = aws_lb_listener.http.default_action[0].type == "fixed-response" && aws_lb_listener.http.default_action[0].fixed_response[0].status_code == "403"
    error_message = "the listener's default action must be a fixed 403"
  }
  assert {
    condition     = aws_lb_listener.http.port == 80
    error_message = "the ALB listens on HTTP/80 (TLS ends at CloudFront)"
  }
  assert {
    condition     = one(one(aws_lb_listener_rule.web.condition).http_header).http_header_name == "X-Origin-Verify" && one(one(aws_lb_listener_rule.keycloak.condition).http_header).http_header_name == "X-Origin-Verify"
    error_message = "both forwarding rules must require the secret origin header"
  }
}

run "cloudfront_sends_origin_secret_and_forwarded_proto" {
  command = plan

  assert {
    condition     = [for x in one(aws_cloudfront_distribution.keycloak.origin).custom_header : x.value if x.name == "Forwarded"] == ["proto=https"]
    error_message = "the Keycloak distribution must send Forwarded: proto=https (the ALB overwrites X-Forwarded-Proto with http)"
  }
  assert {
    condition     = [for x in one(aws_cloudfront_distribution.web.origin).custom_header : x.value if x.name == "Forwarded"] == ["proto=https"]
    error_message = "the web distribution must send Forwarded: proto=https"
  }
  assert {
    condition     = contains([for x in one(aws_cloudfront_distribution.web.origin).custom_header : x.name], "X-Origin-Verify") && contains([for x in one(aws_cloudfront_distribution.keycloak.origin).custom_header : x.name], "X-Origin-Verify")
    error_message = "both distributions must send the secret origin header"
  }
  assert {
    condition     = one(one(aws_cloudfront_distribution.web.origin).custom_origin_config).origin_protocol_policy == "http-only"
    error_message = "CloudFront reaches the ALB over HTTP (the ALB only admits CloudFront)"
  }
}

# --- ECS services --------------------------------------------------------------------------------

run "api_is_internal_and_single_instance" {
  command = plan

  assert {
    condition     = length(aws_ecs_service.api.load_balancer) == 0
    error_message = "the API must have no load balancer attachment"
  }
  assert {
    condition     = aws_ecs_service.api.desired_count == 1 && aws_ecs_service.api.deployment_minimum_healthy_percent == 0 && aws_ecs_service.api.deployment_maximum_percent == 100
    error_message = "the API runs exactly one task and never two at once"
  }
  assert {
    condition     = length(aws_ecs_service.api.service_registries) == 1
    error_message = "the API registers in Cloud Map for the web nginx"
  }
  assert {
    condition     = length(aws_ecs_service.web.load_balancer) == 1 && length(aws_ecs_service.keycloak.load_balancer) == 1
    error_message = "web and Keycloak attach to their target groups"
  }
}

run "web_replicas_follow_the_variable" {
  command = plan

  variables {
    web_min_replicas = 2
  }

  assert {
    condition     = aws_ecs_service.web.desired_count == 2
    error_message = "web desired_count must be web_min_replicas"
  }
}

run "dapr_sidecar_only_where_secrets_are_read" {
  command = apply

  assert {
    condition     = contains([for c in jsondecode(aws_ecs_task_definition.api.container_definitions) : c.name], "daprd") && contains([for c in jsondecode(aws_ecs_task_definition.keycloak.container_definitions) : c.name], "daprd")
    error_message = "API and Keycloak tasks need the daprd sidecar"
  }
  assert {
    condition     = !contains([for c in jsondecode(aws_ecs_task_definition.web.container_definitions) : c.name], "daprd")
    error_message = "the web task reads no secrets and must not have a sidecar"
  }
  assert {
    condition     = contains([for c in jsondecode(aws_ecs_task_definition.api.container_definitions) : c.name], "dapr-init")
    error_message = "the API task needs the init container that writes the Dapr component"
  }
}

run "keycloak_proxy_settings" {
  command = apply

  assert {
    condition     = [for e in [for c in jsondecode(aws_ecs_task_definition.keycloak.container_definitions) : c if c.name == "keycloak"][0].environment : e.value if e.name == "KC_PROXY_HEADERS"] == ["forwarded"]
    error_message = "Keycloak must trust the Forwarded header (the ALB overwrites X-Forwarded-Proto)"
  }
  assert {
    condition     = [for e in [for c in jsondecode(aws_ecs_task_definition.keycloak.container_definitions) : c if c.name == "keycloak"][0].environment : e.value if e.name == "KC_HOSTNAME"] == [local.keycloak_url]
    error_message = "KC_HOSTNAME must be the public Keycloak URL"
  }
  assert {
    condition     = local.keycloak_url == "https://${aws_cloudfront_distribution.keycloak.domain_name}" && local.web_url == "https://${aws_cloudfront_distribution.web.domain_name}"
    error_message = "public URLs are the CloudFront domains over https"
  }
}

# --- Secrets and IAM -----------------------------------------------------------------------------

run "secrets_are_purged_immediately_on_destroy" {
  command = plan

  assert {
    condition     = length(aws_secretsmanager_secret.app) >= 5 && alltrue([for s in aws_secretsmanager_secret.app : s.recovery_window_in_days == 0])
    error_message = "every application secret must have recovery_window_in_days = 0"
  }
  assert {
    condition     = contains(keys(aws_secretsmanager_secret.app), "ConnectionStrings--db") && contains(keys(aws_secretsmanager_secret.app), "Keycloak--AdminClientSecret") && contains(keys(aws_secretsmanager_secret.app), "keycloak-bootstrap-admin-password")
    error_message = "the secrets the apps ask Dapr for must exist under their exact names"
  }
}

run "optional_smtp_password_secret" {
  command = plan

  variables {
    smtp_password = ""
  }

  assert {
    condition     = !contains(keys(aws_secretsmanager_secret.app), "Smtp--Password")
    error_message = "no SMTP password, no secret"
  }
}

run "task_roles_read_only_their_own_secrets" {
  command = apply

  # Literal sets: backend DaprSecrets.cs (API) and infra/azure/terraform/secrets.tf (Keycloak).
  assert {
    condition     = toset(local.app_secret_names.api) == toset(["ConnectionStrings--db", "ConnectionStrings--dbDirect", "Keycloak--AdminClientSecret", "Smtp--Password"])
    error_message = "the API reads exactly the secrets DaprSecrets.cs asks for"
  }
  assert {
    condition     = toset(local.app_secret_names.keycloak) == toset(["keycloak-db-password", "keycloak-bootstrap-admin-password", "Keycloak--AdminClientSecret", "Smtp--Password"])
    error_message = "Keycloak reads exactly the secrets its DAPR_SECRETS mapping lists"
  }
  assert {
    condition     = toset(jsondecode(aws_iam_role_policy.task_secrets["api"].policy).Statement[0].Resource) == toset([for n in ["ConnectionStrings--db", "ConnectionStrings--dbDirect", "Keycloak--AdminClientSecret", "Smtp--Password"] : aws_secretsmanager_secret.app[n].arn])
    error_message = "the API role must reference exactly the API's secrets"
  }
  assert {
    condition     = toset(jsondecode(aws_iam_role_policy.task_secrets["keycloak"].policy).Statement[0].Resource) == toset([for n in ["keycloak-db-password", "keycloak-bootstrap-admin-password", "Keycloak--AdminClientSecret", "Smtp--Password"] : aws_secretsmanager_secret.app[n].arn])
    error_message = "the Keycloak role must reference exactly Keycloak's secrets"
  }
  assert {
    condition     = jsondecode(aws_iam_role_policy.task_secrets["api"].policy).Statement[0].Action == ["secretsmanager:GetSecretValue"]
    error_message = "task roles may only GetSecretValue"
  }
  assert {
    condition     = !contains(local.app_secret_names.api, "keycloak-db-password") && !contains(local.app_secret_names.keycloak, "ConnectionStrings--db")
    error_message = "the API and Keycloak must not read each other's secrets"
  }
  assert {
    condition     = !contains(keys(aws_iam_role_policy.task_secrets), "web") && !contains(keys(aws_iam_role.task), "web") && aws_ecs_task_definition.web.task_role_arn == null
    error_message = "the web task has no role and so no secret access"
  }
  assert {
    condition     = alltrue([for k, p in aws_iam_role_policy.task_secrets : alltrue([for r in jsondecode(p.policy).Statement[0].Resource : r != "*"])])
    error_message = "no wildcard resources in the secret policies"
  }
  assert {
    condition     = [for e in [for c in jsondecode(aws_ecs_task_definition.keycloak.container_definitions) : c if c.name == "keycloak"][0].environment : e.value if e.name == "DAPR_SECRETS"][0] == "API_ADMIN_CLIENT_SECRET=Keycloak--AdminClientSecret,KC_BOOTSTRAP_ADMIN_PASSWORD=keycloak-bootstrap-admin-password,KC_DB_PASSWORD=keycloak-db-password,SMTP_PASSWORD=Smtp--Password"
    error_message = "Keycloak's DAPR_SECRETS must map each variable to its secret name"
  }
}

run "task_roles_without_smtp_password" {
  command = apply

  variables {
    smtp_password = ""
  }

  assert {
    condition     = toset(local.app_secret_names.api) == toset(["ConnectionStrings--db", "ConnectionStrings--dbDirect", "Keycloak--AdminClientSecret"])
    error_message = "without an SMTP password the API reads three secrets"
  }
  assert {
    condition     = toset(local.app_secret_names.keycloak) == toset(["keycloak-db-password", "keycloak-bootstrap-admin-password", "Keycloak--AdminClientSecret"])
    error_message = "without an SMTP password Keycloak reads three secrets"
  }
  assert {
    condition     = length(jsondecode(aws_iam_role_policy.task_secrets["api"].policy).Statement[0].Resource) == 3 && length(jsondecode(aws_iam_role_policy.task_secrets["keycloak"].policy).Statement[0].Resource) == 3
    error_message = "the policies must not reference a missing Smtp--Password secret"
  }
  assert {
    condition     = [for e in [for c in jsondecode(aws_ecs_task_definition.keycloak.container_definitions) : c if c.name == "keycloak"][0].environment : e.value if e.name == "DAPR_SECRETS"][0] == "API_ADMIN_CLIENT_SECRET=Keycloak--AdminClientSecret,KC_BOOTSTRAP_ADMIN_PASSWORD=keycloak-bootstrap-admin-password,KC_DB_PASSWORD=keycloak-db-password"
    error_message = "DAPR_SECRETS must omit SMTP_PASSWORD when there is no password"
  }
}

run "dockerhub_credentials_only_when_configured" {
  command = plan

  assert {
    condition     = length(aws_secretsmanager_secret.dockerhub) == 0
    error_message = "no Docker Hub secret for public repositories"
  }
}

run "dockerhub_credentials_when_configured" {
  command = plan

  variables {
    dockerhub_username = "hubuser"
    dockerhub_token    = "dckr_pat_fake"
  }

  assert {
    condition     = length(aws_secretsmanager_secret.dockerhub) == 1 && aws_secretsmanager_secret.dockerhub[0].recovery_window_in_days == 0
    error_message = "a private registry needs its credentials secret"
  }
}

# --- Origin secret wiring (ALB rule <-> CloudFront header) -----------------------------------------

run "origin_secrets_match_between_alb_and_cloudfront" {
  command = apply

  assert {
    condition     = one(one(aws_lb_listener_rule.web.condition).http_header).values == toset([random_password.origin_verify_web.result])
    error_message = "the web rule must require the web origin secret"
  }
  assert {
    condition     = one(one(aws_lb_listener_rule.keycloak.condition).http_header).values == toset([random_password.origin_verify_keycloak.result])
    error_message = "the Keycloak rule must require the Keycloak origin secret"
  }
  assert {
    condition     = [for x in one(aws_cloudfront_distribution.web.origin).custom_header : x.value if x.name == "X-Origin-Verify"] == [random_password.origin_verify_web.result]
    error_message = "the web distribution must send the web origin secret"
  }
  assert {
    condition     = [for x in one(aws_cloudfront_distribution.keycloak.origin).custom_header : x.value if x.name == "X-Origin-Verify"] == [random_password.origin_verify_keycloak.result]
    error_message = "the Keycloak distribution must send the Keycloak origin secret"
  }
  assert {
    condition     = random_password.origin_verify_web.result != random_password.origin_verify_keycloak.result
    error_message = "web and Keycloak must use different origin secrets"
  }
}

# --- HSTS at the edge (the ALB makes the API see http, so ASP.NET UseHsts stays silent) -------------

run "cloudfront_adds_hsts" {
  command = apply

  assert {
    condition     = aws_cloudfront_response_headers_policy.security.name == "birrapoint-prod-security"
    error_message = "the response headers policy must be birrapoint-prod-security"
  }
  assert {
    condition     = one(aws_cloudfront_response_headers_policy.security.security_headers_config).strict_transport_security[0].access_control_max_age_sec >= 31536000 && one(aws_cloudfront_response_headers_policy.security.security_headers_config).strict_transport_security[0].override == true
    error_message = "HSTS max-age must be at least one year and override the origin"
  }
  assert {
    condition     = one(aws_cloudfront_response_headers_policy.security.security_headers_config).strict_transport_security[0].include_subdomains == false
    error_message = "HSTS must not include subdomains (cloudfront.net is shared)"
  }
  assert {
    condition     = aws_cloudfront_distribution.web.default_cache_behavior[0].response_headers_policy_id == aws_cloudfront_response_headers_policy.security.id && aws_cloudfront_distribution.keycloak.default_cache_behavior[0].response_headers_policy_id == aws_cloudfront_response_headers_policy.security.id
    error_message = "both distributions must attach the security headers policy"
  }
}

run "cloudfront_origin_timeouts" {
  command = plan

  assert {
    condition     = one(one(aws_cloudfront_distribution.web.origin).custom_origin_config).origin_read_timeout == 60 && one(one(aws_cloudfront_distribution.keycloak.origin).custom_origin_config).origin_read_timeout == 60
    error_message = "origin_read_timeout must be 60 s (the maximum without a quota increase)"
  }
  assert {
    condition     = one(one(aws_cloudfront_distribution.web.origin).custom_origin_config).origin_keepalive_timeout == 5 && one(one(aws_cloudfront_distribution.keycloak.origin).custom_origin_config).origin_keepalive_timeout == 5
    error_message = "origin_keepalive_timeout must be explicit (5 s)"
  }
}

# --- No secret in plain environment, daprd hardening ----------------------------------------------

run "no_secret_value_in_container_environment" {
  command = apply

  assert {
    condition     = length(setintersection(toset(flatten([for td in [aws_ecs_task_definition.api, aws_ecs_task_definition.keycloak, aws_ecs_task_definition.web] : [for c in jsondecode(td.container_definitions) : [for e in try(c.environment, []) : e.value]]])), toset(nonsensitive(values(local.secret_values))))) == 0
    error_message = "no secret value may appear in a container environment"
  }
  assert {
    condition     = length(setintersection(toset(flatten([for td in [aws_ecs_task_definition.api, aws_ecs_task_definition.keycloak, aws_ecs_task_definition.web] : [for c in jsondecode(td.container_definitions) : [for e in try(c.environment, []) : e.value]]])), toset([nonsensitive(random_password.origin_verify_web.result), nonsensitive(random_password.origin_verify_keycloak.result), nonsensitive(random_password.api_admin_client_secret.result), nonsensitive(random_password.keycloak_admin.result), nonsensitive(neon_role.keycloak.password)]))) == 0
    error_message = "origin secrets and passwords must not reach any container environment"
  }
}

run "daprd_is_hardened" {
  command = apply

  assert {
    condition     = alltrue([for td in [aws_ecs_task_definition.api, aws_ecs_task_definition.keycloak] : [for c in jsondecode(td.container_definitions) : c.command[index(c.command, "--dapr-listen-addresses") + 1] == "127.0.0.1" if c.name == "daprd"][0]])
    error_message = "daprd must listen on 127.0.0.1 only"
  }
  assert {
    condition     = alltrue([for td in [aws_ecs_task_definition.api, aws_ecs_task_definition.keycloak] : [for c in jsondecode(td.container_definitions) : contains(c.command, "--enable-metrics=false") if c.name == "daprd"][0]])
    error_message = "daprd metrics must be disabled"
  }
  assert {
    condition     = contains([for c in jsondecode(aws_ecs_task_definition.keycloak.container_definitions) : c.name], "dapr-init")
    error_message = "the Keycloak task needs the init container that writes the Dapr component"
  }
  assert {
    condition     = [for e in [for c in jsondecode(aws_ecs_task_definition.api.container_definitions) : c if c.name == "api"][0].environment : e.value if e.name == "Dapr__SecretStore"] == [var.dapr_secret_store_name]
    error_message = "the API must name the Dapr secret store component"
  }
}

# --- Neon (same as Azure) ------------------------------------------------------------------------

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
