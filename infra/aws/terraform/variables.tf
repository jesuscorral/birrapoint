variable "region" {
  description = "AWS region for the whole deployment (VPC, ECS, ALB, Secrets Manager). CloudFront is global."
  type        = string
  default     = "eu-central-1"
}

variable "environment" {
  description = "Deployment environment; every resource is named birrapoint-<environment>-<resource acronym>, lower-cased (e.g. birrapoint-prod-ecs). Letters and digits only."
  type        = string
  default     = "PROD"

  # At most 10 characters: keeps every derived name (ALB and target groups: 32) well within limits.
  validation {
    condition     = can(regex("^[A-Za-z][A-Za-z0-9]{1,9}$", var.environment))
    error_message = "environment must be 2-10 letters or digits, starting with a letter (e.g. PROD, DEV, STAGING)."
  }
}

variable "dapr_secret_store_name" {
  description = "Name of the Dapr secret store component (AWS Secrets Manager) the API and Keycloak read their secrets from."
  type        = string
  default     = "secretstore"
}

# --- Images (Docker Hub, constitution v1.5.0) ---------------------------------------------------

# Terraform owns the running image (T143, ADR-0022): changing a version here and applying rolls the
# ECS services to a new task definition. "latest" is the CI image docker.io/<ns>/birrapoint-<c>:latest;
# X.Y.Z is the release image docker.io/<ns>/birrapoint-<c>-release:X.Y.Z (release.yml, ADR-0019).

variable "image_namespace" {
  description = "Docker Hub user or organization that owns the birrapoint-* repositories."
  type        = string

  validation {
    condition     = upper(var.image_namespace) != "CHANGE-ME"
    error_message = "image_namespace is still the CHANGE-ME placeholder: set it in the environment's tfvars file."
  }

  validation {
    condition     = can(regex("^[a-z0-9][a-z0-9_.-]{1,254}$", var.image_namespace))
    error_message = "image_namespace must be a lowercase Docker Hub namespace."
  }
}

variable "release_version" {
  description = "Image version for all three components, always explicit: \"latest\" (CI images) or a release X.Y.Z. No default, so an apply never rolls to latest by accident."
  type        = string

  validation {
    condition     = var.release_version == "latest" || can(regex("^[0-9]+[.][0-9]+[.][0-9]+$", var.release_version))
    error_message = "release_version must be \"latest\" or X.Y.Z (e.g. 1.2.3)."
  }
}

variable "api_version" {
  description = "Overrides release_version for the API. Null uses release_version."
  type        = string
  default     = null

  validation {
    condition     = var.api_version == null || var.api_version == "latest" || can(regex("^[0-9]+[.][0-9]+[.][0-9]+$", var.api_version))
    error_message = "api_version must be \"latest\" or X.Y.Z."
  }
}

variable "web_version" {
  description = "Overrides release_version for the web app. Null uses release_version."
  type        = string
  default     = null

  validation {
    condition     = var.web_version == null || var.web_version == "latest" || can(regex("^[0-9]+[.][0-9]+[.][0-9]+$", var.web_version))
    error_message = "web_version must be \"latest\" or X.Y.Z."
  }
}

variable "keycloak_version" {
  description = "Overrides release_version for Keycloak. Null uses release_version."
  type        = string
  default     = null

  validation {
    condition     = var.keycloak_version == null || var.keycloak_version == "latest" || can(regex("^[0-9]+[.][0-9]+[.][0-9]+$", var.keycloak_version))
    error_message = "keycloak_version must be \"latest\" or X.Y.Z."
  }
}

variable "dockerhub_username" {
  description = "Docker Hub username for pulling private repositories (ECS repositoryCredentials). Leave empty for public repositories."
  type        = string
  default     = ""
}

variable "dockerhub_token" {
  description = "Docker Hub read-only access token matching dockerhub_username; stored in Secrets Manager and readable only by the ECS task execution role. Leave empty for public repositories."
  type        = string
  default     = ""
  sensitive   = true
}

# --- Neon (R-18) --------------------------------------------------------------------------------

variable "neon_region" {
  description = "Neon region for the project; pick the one closest to `region`."
  type        = string
  default     = "aws-eu-central-1"
}

variable "neon_org_id" {
  description = "Neon organization id to create the project in (an account identifier: never committed). Null or empty uses the API key's default organization; set TF_VAR_neon_org_id when the key spans several organizations."
  type        = string
  default     = null
}

variable "neon_history_retention_seconds" {
  description = "Point-in-time recovery window (FR-047). Neon's free plan caps it at 21600."
  type        = number
  default     = 21600
}

variable "dispatch_poll_interval" {
  description = "Safety-net poll of the DispatchWorker as a TimeSpan (hh:mm:ss), passed as Dispatch__SafetyNetPollInterval. The poll is a safety net; a longer interval lets Neon scale to zero (see README \"Neon compute budget\")."
  type        = string
  default     = "01:00:00"

  validation {
    condition     = can(regex("^([0-1][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9]$", var.dispatch_poll_interval)) && var.dispatch_poll_interval != "00:00:00"
    error_message = "dispatch_poll_interval must be a TimeSpan hh:mm:ss between 00:00:01 and 23:59:59 (e.g. 00:30:00)."
  }
}

# --- SMTP relay (invitations, results, Keycloak password reset) -------------------------------

variable "smtp_host" {
  description = "SMTP relay host used by both the API (MailKit) and Keycloak."
  type        = string
}

variable "smtp_port" {
  description = "SMTP relay port."
  type        = number
  default     = 587
}

variable "smtp_username" {
  description = "SMTP username. Empty disables SMTP authentication."
  type        = string
  default     = ""
}

variable "smtp_password" {
  description = "SMTP password or API key."
  type        = string
  default     = ""
  sensitive   = true
}

variable "smtp_use_starttls" {
  description = "Upgrade the SMTP connection with STARTTLS."
  type        = bool
  default     = true
}

variable "smtp_from_address" {
  description = "Sender address, verified with the SMTP provider (e.g. no-reply@example.com)."
  type        = string
}

# --- Sizing -------------------------------------------------------------------------------------

variable "web_min_replicas" {
  description = "Number of frontend tasks (ECS has no scale-to-zero here; 0 stops the PWA)."
  type        = number
  default     = 1
}

variable "tags" {
  description = "Tags applied to every AWS resource (provider default_tags); the environment tag is added from var.environment and cannot be overridden here."
  type        = map(string)
  default = {
    application = "birrapoint"
    managed-by  = "terraform"
  }
}
