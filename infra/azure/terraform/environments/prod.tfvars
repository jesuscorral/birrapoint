# Non-secret inputs of the PROD environment, committed so a local apply and deploy-azure.yml use exactly
# the same values (ADR-0022). deploy-azure.yml loads environments/<BIRRAPOINT_ENVIRONMENT lower-cased>.tfvars:
#   terraform -chdir=infra/azure/terraform apply -var-file=environments/prod.tfvars -var release_version=X.Y.Z
# Secrets never go here: smtp_password, dockerhub_username and dockerhub_token live in the gitignored
# terraform.tfvars (see terraform.tfvars.example) or in TF_VAR_* environment variables.

environment = "PROD"
location    = "northeurope"

# Docker Hub user or organization that owns the birrapoint-* repositories.
# CHANGE-ME is rejected by a validation: set the real value before the first deploy.
image_namespace = "jesuscorral"

# Placeholders: set real values before the first deploy.
smtp_host         = "smtp.example.com"
smtp_port         = 587
smtp_username     = "apikey"
smtp_use_starttls = true
smtp_from_address = "no-reply@example.com"

web_min_replicas = 1

neon_region                    = "aws-eu-central-1"
neon_history_retention_seconds = 21600
# DispatchWorker safety-net poll (hh:mm:ss). Default 01:00:00 lets Neon scale to zero between wakes;
# see "Neon compute budget" in the README before shortening it.
# dispatch_poll_interval = "01:00:00"
# Neon organization: an account identifier, not committed. Only needed when the API key spans
# several organizations: export TF_VAR_neon_org_id=org-... locally, or set the NEON_ORG_ID GitHub variable.

key_vault_secrets_officer_principal_ids = []
# key_vault_name = "birrapoint-prod-kv-x7"   # only if birrapoint-prod-kv is taken (globally unique)
