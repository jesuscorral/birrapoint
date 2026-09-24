variable "subscription_id" {
  description = "Azure subscription to deploy into (infra/deploy.ps1 passes the `az account show` one)."
  type        = string
}

variable "location" {
  description = "Azure region for the resource group and Container Apps environment."
  type        = string
  default     = "westeurope"
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

variable "image_namespace" {
  description = "Docker Hub user or organization owning the birrapoint-api/-web/-keycloak repositories."
  type        = string
}

variable "image_tag" {
  description = "Tag of all three images to deploy (infra/deploy.ps1 uses the git commit SHA)."
  type        = string
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
  default     = null
}

variable "neon_history_retention_seconds" {
  description = "Point-in-time recovery window (FR-047). Neon's free plan caps it at 86400 (1 day)."
  type        = number
  default     = 86400
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
