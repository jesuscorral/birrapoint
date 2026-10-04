terraform {
  required_version = ">= 1.9"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
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

  # State lives in HCP Terraform (constitution v1.5.0, ADR-0023) in this root's own workspace -
  # never committed. The workspace must use the Local execution mode: HCP only stores the state,
  # plans and applies run where the AWS credentials are (`aws configure sso` on a laptop, an OIDC
  # role in deploy-aws.yml). Organization and workspace come from TF_CLOUD_ORGANIZATION and
  # TF_WORKSPACE, so nothing account-specific is committed; the API token comes from
  # `terraform login` or TF_TOKEN_app_terraform_io.
  cloud {}
}

provider "aws" {
  region = var.region

  default_tags {
    tags = local.default_tags
  }
}

# Authenticates with the NEON_API_KEY environment variable (never a Terraform variable, so it
# cannot end up in a tfvars file).
provider "neon" {}
