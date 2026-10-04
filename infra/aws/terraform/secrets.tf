# Secrets (ADR-0023, same mechanism as ADR-0021): every application secret lives in AWS Secrets
# Manager. The API and Keycloak read them at runtime through a Dapr sidecar
# (secretstores.aws.secretmanager, ecs.tf) authenticated with their own IAM task role, which can
# read only the secrets that service uses; no task receives a secret as a setting. The web tasks
# read none and have no task role. Only the optional Docker Hub pull credentials are read by ECS
# itself (task execution role): the platform needs them before any container - and so any
# sidecar - can start.
#
# Secret names are the exact names the apps ask Dapr for (the component has no name prefix
# option), so they are not environment-qualified: ONE ENVIRONMENT PER AWS ACCOUNT AND REGION.
# recovery_window_in_days = 0 deletes secrets immediately on destroy, so a redeploy can reuse the
# names (a pending-deletion secret would block them); every value is rewritten by Terraform.

locals {
  # Same names as the Azure Key Vault ("--" stands for the ':' configuration separator, see
  # backend/src/BirraPoint.Api/Common/Secrets/DaprSecrets.cs); Keycloak's are mapped to its
  # environment variables by DAPR_SECRETS (ecs.tf, built from keycloak_secret_env below). The
  # Keycloak admin-client secret is shared: the API authenticates with the same value Keycloak's
  # realm import sets.
  secret_names = concat(
    [
      "ConnectionStrings--db",
      "ConnectionStrings--dbDirect",
      "Keycloak--AdminClientSecret",
      "keycloak-db-password",
      "keycloak-bootstrap-admin-password",
    ],
    # Stored only when a password is configured (a relay without authentication has none).
    local.has_smtp_password ? ["Smtp--Password"] : [],
  )

  secret_values = {
    "ConnectionStrings--db"             = local.api_db_pooled
    "ConnectionStrings--dbDirect"       = local.api_db_direct
    "Keycloak--AdminClientSecret"       = random_password.api_admin_client_secret.result
    "keycloak-db-password"              = neon_role.keycloak.password
    "keycloak-bootstrap-admin-password" = random_password.keycloak_admin.result
    "Smtp--Password"                    = var.smtp_password
  }

  # Keycloak environment variable -> secret name (loaded by the image's entrypoint).
  keycloak_secret_env = merge(
    {
      KC_DB_PASSWORD              = "keycloak-db-password"
      KC_BOOTSTRAP_ADMIN_PASSWORD = "keycloak-bootstrap-admin-password"
      API_ADMIN_CLIENT_SECRET     = "Keycloak--AdminClientSecret"
    },
    local.has_smtp_password ? { SMTP_PASSWORD = "Smtp--Password" } : {},
  )

  # Least privilege: each task role reads exactly the secrets its service uses. The API's list
  # must match DaprSecrets.cs.
  app_secret_names = {
    api = concat(
      ["ConnectionStrings--db", "ConnectionStrings--dbDirect", "Keycloak--AdminClientSecret"],
      local.has_smtp_password ? ["Smtp--Password"] : [],
    )
    keycloak = values(local.keycloak_secret_env)
  }
}

resource "aws_secretsmanager_secret" "app" {
  for_each = toset(local.secret_names)

  name                    = each.key
  recovery_window_in_days = 0
}

resource "aws_secretsmanager_secret_version" "app" {
  for_each = aws_secretsmanager_secret.app

  secret_id     = each.value.id
  secret_string = local.secret_values[each.key]
}

# Docker Hub pull credentials (private repositories only), in the format ECS expects.
resource "aws_secretsmanager_secret" "dockerhub" {
  count = local.has_dockerhub ? 1 : 0

  name                    = "${local.name_prefix}-dockerhub"
  recovery_window_in_days = 0
}

resource "aws_secretsmanager_secret_version" "dockerhub" {
  count = local.has_dockerhub ? 1 : 0

  secret_id = aws_secretsmanager_secret.dockerhub[0].id
  secret_string = jsonencode({
    username = var.dockerhub_username
    password = var.dockerhub_token
  })
}

# --- IAM ----------------------------------------------------------------------------------------

locals {
  ecs_tasks_assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Action    = "sts:AssumeRole"
      Principal = { Service = "ecs-tasks.amazonaws.com" }
    }]
  })
}

# Task execution role: what ECS itself needs to start the tasks - write the log streams and, for a
# private registry, read the Docker Hub credentials. No other secret.
resource "aws_iam_role" "execution" {
  name               = "${local.name_prefix}-ecs-exec"
  assume_role_policy = local.ecs_tasks_assume_role_policy
}

resource "aws_iam_role_policy" "execution" {
  name = "logs-and-registry"
  role = aws_iam_role.execution.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = concat(
      [{
        Effect   = "Allow"
        Action   = ["logs:CreateLogStream", "logs:PutLogEvents"]
        Resource = [for group in aws_cloudwatch_log_group.app : "${group.arn}:*"]
      }],
      local.has_dockerhub ? [{
        Effect   = "Allow"
        Action   = ["secretsmanager:GetSecretValue"]
        Resource = [aws_secretsmanager_secret.dockerhub[0].arn]
      }] : [],
    )
  })
}

# Task roles (API and Keycloak only; the web has none): GetSecretValue on the service's own
# secrets, by ARN. Dapr's Secrets Manager store reads a secret with GetSecretValue only.
resource "aws_iam_role" "task" {
  for_each = local.app_secret_names

  name               = "${local.name_prefix}-${each.key == "keycloak" ? "kc" : each.key}-task"
  assume_role_policy = local.ecs_tasks_assume_role_policy
}

resource "aws_iam_role_policy" "task_secrets" {
  for_each = local.app_secret_names

  name = "read-own-secrets"
  role = aws_iam_role.task[each.key].id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["secretsmanager:GetSecretValue"]
      Resource = [for name in each.value : aws_secretsmanager_secret.app[name].arn]
    }]
  })
}
