variable "subscription_id" {
  description = "Azure subscription to deploy into (infra/deploy.ps1 passes the `az account show` one)."
  type        = string
}

variable "location" {
  description = "Azure region for the resource group and Container Apps environment."
  type        = string
  default     = "northeurope"
}

variable "name_prefix" {
  description = "Prefix for every Azure resource and Container App name (lowercase letters, digits, hyphens)."
  type        = string
  default     = "birrapoint"

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{1,18}[a-z0-9]$", var.name_prefix))
    error_message = "name_prefix must be 3-20 chars: lowercase letters, digits and hyphens, starting with a letter."
  }
}

# --- Images (Docker Hub, constitution v1.3.1) ---------------------------------------------------

# Full image references, e.g. docker.io/<ns>/birrapoint-api-release:0.3.0 (resolved by
# infra/deploy.ps1). Used only when a Container App is first created: afterwards the image is
# owned by the deployment rollout (`az containerapp update`, via infra/deploy.ps1 or the deploy
# pipeline) and Terraform ignores it (see `lifecycle` in apps.tf), so infrastructure applies never
# roll an app back to an older image (FR-064, T134).

variable "api_image" {
  description = "Initial image of the API Container App (ignored after creation)."
  type        = string
}

variable "web_image" {
  description = "Initial image of the web Container App (ignored after creation)."
  type        = string
}

variable "keycloak_image" {
  description = "Initial image of the Keycloak Container App (ignored after creation)."
  type        = string
}

variable "revision_suffix" {
  description = "Revision suffix for every Container App, unique per apply (infra/deploy.ps1 passes infra-<UTC timestamp>). Each apply therefore creates a new revision of all three apps (a restart); never run Terraform directly with a reused value."
  type        = string

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]*[a-z0-9]$", var.revision_suffix)) && !strcontains(var.revision_suffix, "--")
    error_message = "revision_suffix must be lowercase alphanumerics and single hyphens, starting with a letter."
  }

  # Container Apps limits a revision name (<app>--<suffix>) to 64 characters; -api/-web are the
  # longest app names.
  validation {
    condition     = length("${var.name_prefix}-api--${var.revision_suffix}") <= 64
    error_message = "name_prefix + revision_suffix give a revision name longer than 64 characters."
  }
}

variable "dockerhub_username" {
  description = "Docker Hub username for pulling private repositories. Leave empty for public repositories."
  type        = string
  default     = ""
}

variable "dockerhub_token" {
  description = "Docker Hub read-only access token matching dockerhub_username. Leave empty for public repositories."
  type        = string
  default     = ""
  sensitive   = true
}

# --- Neon (R-18) --------------------------------------------------------------------------------

variable "neon_region" {
  description = "Neon region for the project; pick the one closest to `location`."
  type        = string
  default     = "aws-eu-central-1"
}

variable "neon_org_id" {
  description = "Neon organization id to create the project in. Null uses the API key's default."
  type        = string
  default     = "org-plain-cloud-73738163"
}

variable "neon_history_retention_seconds" {
  description = "Point-in-time recovery window (FR-047). Neon's free plan caps it at 21600."
  type        = number
  default     = 21600
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
  description = "Minimum frontend replicas. 0 allows scale-to-zero at the cost of a cold start."
  type        = number
  default     = 1
}

variable "tags" {
  description = "Tags applied to every Azure resource."
  type        = map(string)
  default = {
    application = "birrapoint"
    managed-by  = "terraform"
  }
}
