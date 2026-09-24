# BirraPoint — cloud deployment (Terraform → Azure Container Apps + Neon)

Constitution v1.3.1, research R-17/R-18/R-19, ADR-0016/ADR-0017, FR-043–FR-047, SC-011.

## Topology

```text
                 Internet
          ┌─────────┴──────────┐
   https  │                    │ https
  ┌───────▼────────┐   ┌───────▼────────┐
  │ birrapoint-web │   │ birrapoint-kc  │  Keycloak 26 (realm imported on first start)
  │ nginx: PWA +   │   │ external       │
  │ /api,/hubs     │   │ ingress        │──────┐
  │ reverse proxy  │   └────────────────┘      │ direct endpoint
  └───────┬────────┘                           │
          │ https (internal ingress only)      │
  ┌───────▼────────┐                    ┌──────▼──────────────────┐
  │ birrapoint-api │── pooled endpoint ─▶ Neon project            │
  │ 1 replica      │   (migrations on   │  db `birrapoint` (API)  │
  └────────────────┘    direct endpoint)│  db `keycloak`          │
   Azure Container Apps environment     └─────────────────────────┘
   (+ Log Analytics)                     outside Azure (R-18)
```

- **Images** come from Docker Hub (`<namespace>/birrapoint-api|web|keycloak:<tag>`); none
  contains secrets or environment-specific configuration (FR-043). The web image renders
  `/config.json` and its nginx upstream from env vars at start; the Keycloak realm resolves its
  `${env.*}` placeholders at import; the API reads everything from env vars.
- **The API is not publicly reachable.** Browsers only talk to the web app (same origin, no
  CORS) and to Keycloak.
- **The API runs exactly one replica**: SignalR has no backplane and the `DispatchJob` worker is a
  single consumer (R-06). Scaling out requires a backplane first.
- **Secrets** (Neon credentials, Keycloak bootstrap admin password, API admin-client secret,
  SMTP password, Docker Hub token) live only in Terraform's remote state and Container Apps
  secrets. The two generated ones come from `random_password`.

## Prerequisites (one-time)

| What | How |
|---|---|
| Azure subscription + CLI | `az login` (Contributor on the subscription) |
| Terraform ≥ 1.9 | <https://developer.hashicorp.com/terraform/install> |
| Docker + Docker Hub account | `docker login`; the three repositories are created on first push |
| Neon account + API key | Neon console → Account settings → API keys; `$env:NEON_API_KEY = "..."` |
| SMTP relay | Any provider with SMTP credentials and a verified sender address |
| Variables file | `cp infra/terraform/terraform.tfvars.example infra/terraform/terraform.tfvars`, fill in |

Private Docker Hub repositories additionally need `dockerhub_username` + a **read-only** access
token in `dockerhub_token`; with public repositories leave both empty.

## Deploy (single command)

```powershell
$env:NEON_API_KEY = "<key>"
./infra/deploy.ps1 -ImageNamespace <dockerhub-user-or-org>          # interactive apply
./infra/deploy.ps1 -ImageNamespace <dockerhub-user-or-org> -AutoApprove
```

The script is idempotent: it creates the state storage (`rg-birrapoint-tfstate`) if missing,
builds and pushes the three images tagged with the current commit SHA (clean tree required, or
`-ImageTag`/`-AllowDirty`), then runs `terraform init` + `terraform apply`. `-SkipBuild`
redeploys images already on Docker Hub. It prints `web_url` and `keycloak_url` at the end.

First start takes a few minutes: Keycloak creates its schema on Neon and imports the realm, and
the API applies EF Core migrations (including the BJCP catalog seed) before serving.

The Keycloak admin console is at `<keycloak_url>/admin`, user `admin`, password from
`terraform -chdir=infra/terraform output -raw keycloak_admin_password`.

There is **no seeded organizer account** in production (the image strips the local-dev
`organizer`/`organizer` user); organizers self-register from the login page.

## Restore — Neon point-in-time recovery (FR-047)

There is no self-managed backup job. Neon keeps a write-ahead-log history for
`neon_history_retention_seconds` (default 1 day, the free-plan maximum; raise it on a paid plan)
and can restore any moment inside that window:

1. Neon console → project `birrapoint` (id: `terraform output neon_project_id`) → **Restore**.
2. Pick branch `main` and the timestamp to restore to, then confirm. Neon keeps a backup
   branch of the pre-restore state, so the restore itself is reversible.
3. Both databases (`birrapoint` and `keycloak`) live on that branch and are restored together,
   which keeps application data and Keycloak user ids consistent.
4. Restart the API and Keycloak revisions so they drop pooled connections:
   `az containerapp revision restart` for `birrapoint-api` and `birrapoint-kc`, or redeploy.

For a non-destructive check first, create a branch from a past timestamp (Neon console →
Branches → New branch → "Past data") and inspect it with `psql` before restoring `main`.

## Tear down

```powershell
terraform -chdir=infra/terraform destroy -var="subscription_id=<id>" -var="image_namespace=<ns>" -var="image_tag=<tag>" -var-file=terraform.tfvars
```

This deletes the Azure resources **and the Neon project with all its data**. The state storage
account (`rg-birrapoint-tfstate`) is left in place.

## Known limitations

- Default `*.azurecontainerapps.io` host names; no custom domain yet.
- OpenTelemetry export to Azure and health probes for the API arrive with T098; until then the
  API uses ACA's default TCP probe and logs go to Log Analytics via console output.
- Keycloak's JDBC connection uses `sslmode=require` (encrypted, server certificate not
  verified); the API's Npgsql connections use `VerifyFull`.
