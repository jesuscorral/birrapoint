# The three ECS Fargate services (FR-046, ADR-0023). The images carry no configuration or secrets
# (FR-043): configuration arrives as environment variables, secrets are read from Secrets Manager
# through a Dapr sidecar with the service's IAM task role (secrets.tf).

locals {
  # Pinned sidecar and init images. The init image comes from the public ECR mirror of Docker Hub
  # (no pull rate limit for the init step); daprd has no ECR mirror.
  daprd_image     = "docker.io/daprio/daprd:1.16.0"
  dapr_init_image = "public.ecr.aws/docker/library/busybox:1.37"
  dapr_http_port  = 3500
  dapr_grpc_port  = 50001

  # Fargate task sizes (valid CPU/memory combinations). The daprd sidecar (~100 MiB) shares the
  # task's budget with the app.
  task_sizes = {
    api      = { cpu = 512, memory = 1024 }
    keycloak = { cpu = 1024, memory = 2048 }
    web      = { cpu = 256, memory = 512 }
  }

  # Private Docker Hub repositories: ECS pulls with the credentials in Secrets Manager.
  docker_hub_credentials = local.has_dockerhub ? {
    repositoryCredentials = { credentialsParameter = aws_secretsmanager_secret.dockerhub[0].arn }
  } : {}

  log_config = {
    for service, group in aws_cloudwatch_log_group.app : service => {
      logDriver = "awslogs"
      options = {
        "awslogs-group"         = group.name
        "awslogs-region"        = var.region
        "awslogs-stream-prefix" = "ecs"
      }
    }
  }

  # Dapr secret store component, written by the init container into a task-level volume that
  # daprd reads. No credentials in the metadata: the AWS SDK's default chain picks up the task role.
  dapr_component_yaml = <<-YAML
    apiVersion: dapr.io/v1alpha1
    kind: Component
    metadata:
      name: ${var.dapr_secret_store_name}
    spec:
      type: secretstores.aws.secretmanager
      version: v1
      metadata:
        - name: region
          value: ${var.region}
  YAML

  # Init container + sidecar for the services that read secrets (API, Keycloak). Container names
  # are the same in both tasks; the app container depends on daprd (below).
  dapr_containers = {
    for service in ["api", "keycloak"] : service => [
      {
        name      = "dapr-init"
        image     = local.dapr_init_image
        essential = false
        command   = ["sh", "-c", "printf '%s' \"$DAPR_COMPONENT\" > /components/secretstore.yaml"]
        environment = [
          { name = "DAPR_COMPONENT", value = local.dapr_component_yaml },
        ]
        mountPoints      = [{ sourceVolume = "dapr-components", containerPath = "/components", readOnly = false }]
        logConfiguration = local.log_config[service]
      },
      merge({
        name      = "daprd"
        image     = local.daprd_image
        essential = true
        # Only the app in the same task talks to the sidecar (awsvpc shares the network
        # namespace), so it listens on loopback only. No --app-port: the sidecar serves the app's
        # own calls and never calls it back.
        command = [
          "./daprd",
          "--app-id", local.names[service],
          "--resources-path", "/components",
          "--dapr-http-port", tostring(local.dapr_http_port),
          "--dapr-grpc-port", tostring(local.dapr_grpc_port),
          "--dapr-listen-addresses", "127.0.0.1",
          "--log-level", "warn",
          "--enable-metrics=false",
        ]
        dependsOn        = [{ containerName = "dapr-init", condition = "SUCCESS" }]
        mountPoints      = [{ sourceVolume = "dapr-components", containerPath = "/components", readOnly = true }]
        logConfiguration = local.log_config[service]
      }, local.docker_hub_credentials),
    ]
  }

  api_environment = {
    ASPNETCORE_ENVIRONMENT          = "Production"
    Dapr__SecretStore               = var.dapr_secret_store_name
    DAPR_HTTP_PORT                  = tostring(local.dapr_http_port)
    DAPR_GRPC_PORT                  = tostring(local.dapr_grpc_port)
    Database__MigrateOnStartup      = "true"
    Keycloak__Authority             = "${local.keycloak_url}/realms/birrapoint"
    Keycloak__ApiAudience           = "birrapoint-api"
    Keycloak__AdminClientId         = "birrapoint-api-admin"
    Dispatch__SafetyNetPollInterval = var.dispatch_poll_interval
    Smtp__Host                      = var.smtp_host
    Smtp__Port                      = tostring(var.smtp_port)
    Smtp__Username                  = var.smtp_username
    Smtp__UseStartTls               = tostring(var.smtp_use_starttls)
    Smtp__From                      = "BirraPoint <${var.smtp_from_address}>"
    Frontend__BaseUrl               = local.web_url
  }

  keycloak_environment = {
    KC_DB_URL      = local.keycloak_jdbc_url
    KC_DB_USERNAME = neon_role.keycloak.name
    KC_HOSTNAME    = local.keycloak_url
    # TLS terminates at CloudFront; the ALB then speaks plain HTTP and overwrites
    # X-Forwarded-Proto, so Keycloak trusts the standard Forwarded header CloudFront adds.
    KC_HTTP_ENABLED             = "true"
    KC_PROXY_HEADERS            = "forwarded"
    KC_BOOTSTRAP_ADMIN_USERNAME = "admin"
    # ${VAR:default} placeholders in the imported realm (infra/keycloak/birrapoint-realm.json).
    SPA_URL       = local.web_url
    SMTP_HOST     = var.smtp_host
    SMTP_PORT     = tostring(var.smtp_port)
    SMTP_FROM     = var.smtp_from_address
    SMTP_AUTH     = tostring(var.smtp_username != "")
    SMTP_STARTTLS = tostring(var.smtp_use_starttls)
    SMTP_USER     = var.smtp_username
    # Secrets: environment variable = secret name, read through Dapr at container start.
    DAPR_SECRET_STORE = var.dapr_secret_store_name
    DAPR_HTTP_PORT    = tostring(local.dapr_http_port)
    DAPR_SECRETS      = join(",", [for env, secret in local.keycloak_secret_env : "${env}=${secret}"])
    # Below the ALB health check grace period, so a store that stays unreadable fails the task
    # cleanly instead of the service killing Keycloak mid-boot.
    DAPR_SECRETS_TIMEOUT_SECONDS = "180"
  }

  web_environment = {
    API_UPSTREAM = local.api_internal_url
    KEYCLOAK_URL = local.keycloak_url
    # NGINX_LOCAL_RESOLVERS is derived by the image (NGINX_ENTRYPOINT_LOCAL_RESOLVERS=1) from
    # /etc/resolv.conf, which on Fargate is the VPC resolver: it resolves the Cloud Map name.
  }
}

# --- Cluster, logs, service discovery ---------------------------------------------------------

resource "aws_ecs_cluster" "main" {
  name = local.names.cluster

  setting {
    name  = "containerInsights"
    value = "disabled"
  }
}

resource "aws_cloudwatch_log_group" "app" {
  for_each = toset(["api", "web", "keycloak"])

  name              = "/birrapoint/${local.environment}/${each.key}"
  retention_in_days = 30
}

# Cloud Map: the web nginx resolves api.<namespace> per request through the VPC resolver
# (`resolver` directive). ECS Service Connect is not used: it publishes names in /etc/hosts, which
# the nginx resolver ignores.
resource "aws_service_discovery_private_dns_namespace" "main" {
  name = local.names.namespace
  vpc  = aws_vpc.main.id
}

resource "aws_service_discovery_service" "api" {
  name = "api"

  dns_config {
    namespace_id   = aws_service_discovery_private_dns_namespace.main.id
    routing_policy = "MULTIVALUE"

    dns_records {
      type = "A"
      ttl  = 10
    }
  }
}

# --- Backend API: no load balancer, reached by the web tasks through Cloud Map ---------------------

resource "aws_ecs_task_definition" "api" {
  family                   = local.names.api
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = local.task_sizes.api.cpu
  memory                   = local.task_sizes.api.memory
  execution_role_arn       = aws_iam_role.execution.arn
  task_role_arn            = aws_iam_role.task["api"].arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }

  volume {
    name = "dapr-components"
  }

  container_definitions = jsonencode(concat(
    local.dapr_containers.api,
    [merge({
      name      = "api"
      image     = local.images.api
      essential = true
      portMappings = [
        { containerPort = 8080, protocol = "tcp" },
      ]
      environment      = [for name, value in local.api_environment : { name = name, value = value }]
      dependsOn        = [{ containerName = "daprd", condition = "START" }]
      logConfiguration = local.log_config.api
    }, local.docker_hub_credentials)],
  ))
}

resource "aws_ecs_service" "api" {
  # The first task must find its secrets.
  depends_on = [aws_secretsmanager_secret_version.app, aws_iam_role_policy.task_secrets, aws_iam_role_policy.execution]

  name            = local.names.api
  cluster         = aws_ecs_cluster.main.id
  task_definition = aws_ecs_task_definition.api.arn
  launch_type     = "FARGATE"

  # Exactly one task, and never two at once (0% minimum healthy / 100% maximum): SignalR has no
  # backplane and the DispatchJob worker is designed for a single consumer (R-06), so this
  # service does not scale out and a deployment stops the old task before starting the new one.
  desired_count                      = 1
  deployment_minimum_healthy_percent = 0
  deployment_maximum_percent         = 100

  deployment_circuit_breaker {
    enable   = true
    rollback = true
  }

  network_configuration {
    subnets          = aws_subnet.public[*].id
    security_groups  = [aws_security_group.api.id]
    assign_public_ip = true # egress only: there is no NAT gateway
  }

  service_registries {
    registry_arn = aws_service_discovery_service.api.arn
  }

  propagate_tags = "SERVICE"
}

# --- Keycloak: behind the ALB for login flows (R-19) ---------------------------------------------

resource "aws_ecs_task_definition" "keycloak" {
  family                   = local.names.keycloak
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = local.task_sizes.keycloak.cpu
  memory                   = local.task_sizes.keycloak.memory
  execution_role_arn       = aws_iam_role.execution.arn
  task_role_arn            = aws_iam_role.task["keycloak"].arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }

  volume {
    name = "dapr-components"
  }

  # The image's entrypoint (infra/keycloak/dapr-secrets) loads the secrets listed in DAPR_SECRETS
  # from the Dapr secret store into Keycloak's environment before starting it. No container
  # health check: the image has no curl; the ALB checks /health/live on the management port.
  container_definitions = jsonencode(concat(
    local.dapr_containers.keycloak,
    [merge({
      name      = "keycloak"
      image     = local.images.keycloak
      essential = true
      portMappings = [
        { containerPort = 8080, protocol = "tcp" },
        { containerPort = 9000, protocol = "tcp" },
      ]
      environment      = [for name, value in local.keycloak_environment : { name = name, value = value }]
      dependsOn        = [{ containerName = "daprd", condition = "START" }]
      logConfiguration = local.log_config.keycloak
    }, local.docker_hub_credentials)],
  ))
}

resource "aws_ecs_service" "keycloak" {
  # The first task must find its secrets, and the listener rule must exist before the service
  # attaches to the target group.
  depends_on = [aws_secretsmanager_secret_version.app, aws_iam_role_policy.task_secrets, aws_iam_role_policy.execution, aws_lb_listener_rule.keycloak]

  name            = local.names.keycloak
  cluster         = aws_ecs_cluster.main.id
  task_definition = aws_ecs_task_definition.keycloak.arn
  launch_type     = "FARGATE"
  desired_count   = 1

  # First start imports the realm and creates Keycloak's schema on Neon: allow it time before
  # the ALB health check can fail the task.
  health_check_grace_period_seconds = 600

  deployment_circuit_breaker {
    enable   = true
    rollback = true
  }

  network_configuration {
    subnets          = aws_subnet.public[*].id
    security_groups  = [aws_security_group.keycloak.id]
    assign_public_ip = true # egress only: there is no NAT gateway
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.keycloak.arn
    container_name   = "keycloak"
    container_port   = 8080
  }

  propagate_tags = "SERVICE"
}

# --- Frontend: nginx serves the PWA and proxies /api + /hubs to the API ----------------------------

resource "aws_ecs_task_definition" "web" {
  family                   = local.names.web
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = local.task_sizes.web.cpu
  memory                   = local.task_sizes.web.memory
  execution_role_arn       = aws_iam_role.execution.arn
  # No task role: the web reads no secrets.

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }

  container_definitions = jsonencode([
    merge({
      name      = "web"
      image     = local.images.web
      essential = true
      portMappings = [
        { containerPort = 8080, protocol = "tcp" },
      ]
      environment      = [for name, value in local.web_environment : { name = name, value = value }]
      logConfiguration = local.log_config.web
    }, local.docker_hub_credentials),
  ])
}

resource "aws_ecs_service" "web" {
  depends_on = [aws_iam_role_policy.execution, aws_lb_listener_rule.web]

  name            = local.names.web
  cluster         = aws_ecs_cluster.main.id
  task_definition = aws_ecs_task_definition.web.arn
  launch_type     = "FARGATE"
  desired_count   = var.web_min_replicas

  deployment_circuit_breaker {
    enable   = true
    rollback = true
  }

  network_configuration {
    subnets          = aws_subnet.public[*].id
    security_groups  = [aws_security_group.web.id]
    assign_public_ip = true # egress only: there is no NAT gateway
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.web.arn
    container_name   = "web"
    container_port   = 8080
  }

  propagate_tags = "SERVICE"
}
