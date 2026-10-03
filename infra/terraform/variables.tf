variable "subscription_id" {
  description = "Azure subscription to deploy into. Null (default) uses ARM_SUBSCRIPTION_ID, or the az CLI's current subscription."
  type        = string
  default     = null
}

variable "location" {
  description = "Azure region for the resource group and Container Apps environment."
  type        = string
  default     = "northeurope"
}

variable "environment" {
  description = "Deployment environment; every resource is named birrapoint-<environment>-<resource acronym>, lower-cased (e.g. birrapoint-prod-rg). Letters and digits only."
  type        = string
  default     = "PROD"

  # At most 10 characters: the Key Vault name (birrapoint-<environment>-kv) is limited to 24.
  validation {
    condition     = can(regex("^[A-Za-z][A-Za-z0-9]{1,9}$", var.environment))
    error_message = "environment must be 2-10 letters or digits, starting with a letter (e.g. PROD, DEV, STAGING)."
  }
}

variable "key_vault_name" {
  description = "Overrides the Key Vault name (default birrapoint-<environment>-kv). Key Vault names are globally unique across Azure: set this only when the default is taken by another subscription."
  type        = string
  default     = null

  validation {
    condition     = var.key_vault_name == null || can(regex("^[a-zA-Z][a-zA-Z0-9-]{1,22}[a-zA-Z0-9]$", var.key_vault_name))
    error_message = "key_vault_name must be 3-24 letters, digits or hyphens, starting with a letter and not ending with a hyphen."
  }
}

variable "key_vault_secrets_officer_principal_ids" {
  description = "Object ids (ideally one Entra group) of every identity besides the current one that runs Terraform for this environment; they get Key Vault Secrets Officer on the vault so they can refresh and write its secrets."
  type        = list(string)
  default     = []
}

variable "dapr_secret_store_name" {
  description = "Name of the Dapr secret store component (Key Vault) the apps read their secrets from."
  type        = string
  default     = "secretstore"
}

# --- Images (Docker Hub, constitution v1.3.1) ---------------------------------------------------

# Terraform owns the running image (T143, ADR-0022): changing a version here and applying rolls the
# Container App to a new revision. "latest" is the CI image docker.io/<ns>/birrapoint-<c>:latest;
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
  description = "Neon organization id to create the project in (an account identifier: never committed). Null or empty uses the API key's default organization; set TF_VAR_neon_org_id when the key spans several organizations."
  type        = string
  default     = null
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
