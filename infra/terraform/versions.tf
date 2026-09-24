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
  }

  # Remote state in Azure Storage (constitution v1.3.1 — never committed). Partial configuration:
  # infra/deploy.ps1 creates the storage account idempotently and passes resource_group_name,
  # storage_account_name, container_name and key via -backend-config at `terraform init`.
  backend "azurerm" {}
}

provider "azurerm" {
  features {}
  subscription_id = var.subscription_id
}

# Authenticates with the NEON_API_KEY environment variable (never a Terraform variable, so it
# cannot end up in a tfvars file).
provider "neon" {}
