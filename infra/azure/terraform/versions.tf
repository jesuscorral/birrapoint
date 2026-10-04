terraform {
  required_version = ">= 1.9"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    neon = {
      source  = "kislerdm/neon"
      version = "~> 0.18"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
    time = {
      source  = "hashicorp/time"
      version = "~> 0.13"
    }
  }

  # State lives in HCP Terraform (constitution v1.4.0, T143) - never committed. The workspace must
  # use the Local execution mode: HCP only stores the state, plans and applies run where the Azure
  # credentials are (`az login` on a laptop, OIDC in deploy-azure.yml). Organization and workspace come
  # from TF_CLOUD_ORGANIZATION and TF_WORKSPACE, so nothing account-specific is committed; the API
  # token comes from `terraform login` or TF_TOKEN_app_terraform_io.
  cloud {}
}

provider "azurerm" {
  features {
    # Purge instead of the 14-day soft-delete, so a teardown (infra/azure/teardown.ps1) followed by a
    # fresh deploy never collides with a soft-deleted workspace of the same name.
    log_analytics_workspace {
      permanently_delete_on_destroy = true
    }
    # Same for Key Vault, whose name is globally unique (T142): purged on destroy so a redeploy
    # can recreate it. infra/azure/teardown.ps1 also purges it after a direct resource-group delete.
    key_vault {
      purge_soft_delete_on_destroy    = true
      recover_soft_deleted_key_vaults = false
    }
  }
  # Null falls back to the ARM_SUBSCRIPTION_ID environment variable, then to the az CLI default.
  subscription_id = var.subscription_id
}

# Authenticates with the NEON_API_KEY environment variable (never a Terraform variable, so it
# cannot end up in a tfvars file).
provider "neon" {}
