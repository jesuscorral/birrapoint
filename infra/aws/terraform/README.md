# BirraPoint — cloud deployment on AWS (Terraform → ECS Fargate + CloudFront + Neon)

Constitution v1.5.1, research R-17/R-18/R-19/R-21, ADR-0016/ADR-0021/ADR-0022/ADR-0023, FR-043–FR-047.

This root is independent of `infra/azure/terraform`: a deployment targets one cloud, with its own
HCP Terraform workspace, `environments/<env>.tfvars` and tests. Only the Docker Hub images,
`infra/keycloak` and the Neon account are shared. The teardown script, Pester tests and
`deploy-aws.yml` arrive with T146; until then deploy and destroy by hand as described below.

## Topology

```text
                              Internet (https, *.cloudfront.net certificates)
               ┌───────────────────────┴────────────────────────┐
     ┌─────────▼──────────┐                           ┌─────────▼──────────┐
     │ CloudFront (web)   │                           │ CloudFront (kc)    │  caching disabled,
     │ birrapoint-prod-web│                           │ birrapoint-prod-kc │  WebSockets pass through
     └─────────┬──────────┘                           └─────────┬──────────┘
               │ http + X-Origin-Verify (secret) + Forwarded: proto=https
               └────────────────────────┬───────────────────────┘
                              ┌─────────▼──────────┐   security group: only the CloudFront
                              │ ALB  -prod-alb :80 │   origin-facing prefix list; default
                              │ default: fixed 403 │   action 403, forward only with the secret
                              └────┬──────────┬────┘   header (one value per distribution)
                       8080 (ALB)  │          │  8080 + 9000 health (ALB)
             ┌─────────────────────▼──┐   ┌───▼─────────────────────┐
             │ ECS service -prod-web  │   │ ECS service -prod-kc    │ Keycloak 26 + daprd
             │ nginx: PWA + /api,/hubs│   │ task role: own secrets  │──▶ Secrets Manager
             └───────────┬────────────┘   └───────────┬─────────────┘
          8080 (web SG)  │ api.birrapoint-prod.local  │ direct endpoint
             ┌───────────▼────────────┐               │
             │ ECS service -prod-api  │──▶ Secrets    │      ┌────────────────────────┐
             │ 1 task + daprd         │    Manager    └─────▶│ Neon project           │
             └───────────┬────────────┘                      │ birrapoint-prod-neon   │
                         └─ pooled endpoint (migrations: direct) ▶ db birrapoint /     │
   birrapoint-prod-ecs cluster, birrapoint-prod-vpc: 2 public subnets,│ keycloak       │
   no NAT gateway; tasks get a public IP for egress only      └────────────────────────┘
```

- **Naming** (ADR-0021): every resource is `birrapoint-<environment>-<acronym>`, the environment
  lower-cased (`environment`, default `PROD`): `-vpc`, `-alb`, `-ecs` cluster, `-api` / `-web` /
  `-kc` services, `-web` / `-kc` CloudFront comments, `-neon`. Cloud Map namespace
  `birrapoint-<env>.local`, log groups `/birrapoint/<env>/<service>` (30 days).
- **Images** come from Docker Hub, never built by the deployment (`image_namespace`,
  `release_version`, `api_version`/`web_version`/`keycloak_version`; ADR-0022): `latest` →
  `<ns>/birrapoint-<c>:latest`, `X.Y.Z` → `<ns>/birrapoint-<c>-release:X.Y.Z`. Terraform owns the
  running image: changing the version and applying registers a new task definition and ECS rolls
  the service. Private repositories: set `dockerhub_username` + `dockerhub_token` (read-only
  token); they go to a Secrets Manager secret read only by the task execution role
  (`repositoryCredentials`).
- **Network**: dedicated VPC `10.40.0.0/16`, 2 public subnets in 2 AZs, internet gateway, **no NAT**
  (about 32 USD/month saved per gateway). Tasks run with `assign_public_ip` only for egress
  (Docker Hub, Neon, SMTP, Secrets Manager). The security groups are the only inbound control:
  ALB ← CloudFront prefix list (tcp/80); web, Keycloak (8080, and 9000 for health) ← ALB;
  API (8080) ← web tasks only. The API has no load balancer and no public route.
- **HTTPS**: CloudFront terminates TLS on the free `*.cloudfront.net` certificate and reaches the
  ALB over HTTP. The ALB overwrites `X-Forwarded-Proto` with `http`, so each distribution adds the
  standard header `Forwarded: proto=https`; Keycloak runs with `KC_PROXY_HEADERS=forwarded` and
  `KC_HOSTNAME=<keycloak_url>`.
- **The API runs exactly one task** (`desired_count = 1`, deployment 0% min / 100% max, so the old
  task stops before the new one starts: a short API outage per deploy): SignalR has no backplane
  and the `DispatchJob` worker is a single consumer (R-06).
- **Service discovery**: Cloud Map private DNS (`api.birrapoint-<env>.local`), not ECS Service
  Connect: nginx resolves `API_UPSTREAM` per request with its `resolver` directive, which ignores
  the `/etc/hosts` entries Service Connect writes. The image derives `NGINX_LOCAL_RESOLVERS` from
  `/etc/resolv.conf` (`NGINX_ENTRYPOINT_LOCAL_RESOLVERS=1`), which on Fargate is the VPC resolver.
- **Secrets** (Neon credentials, Keycloak bootstrap admin password, API admin-client secret, SMTP
  password) live in **Secrets Manager** (and in Terraform's state in HCP; the generated ones come
  from `random_password`), `recovery_window_in_days = 0`. No task receives a secret as a setting
  (ADR-0021):
  - the **API** and **Keycloak** tasks each run a `daprd` sidecar (`daprio/daprd`, pinned) with the
    component `secretstores.aws.secretmanager` (name `secretstore`, region only: credentials come
    from the task role through the AWS default chain). An init container (`dapr-init`) writes the
    component YAML into a task-level volume that daprd reads; daprd listens on loopback only;
  - each service has an IAM **task role** with `secretsmanager:GetSecretValue` on its own secrets'
    ARNs only (`app_secret_names` in `secrets.tf`; no wildcards). The **web** reads no secret and
    has no task role;
  - the API loads `ConnectionStrings--db`, `ConnectionStrings--dbDirect`,
    `Keycloak--AdminClientSecret` and (optional) `Smtp--Password` at startup; Keycloak's entrypoint
    loads the secrets listed in `DAPR_SECRETS` into its environment (same names and code as Azure).
- **Neon** is identical to the Azure root (`neon.tf`): one project per deployment, databases
  `birrapoint` and `keycloak`, 6 h point-in-time recovery on the free plan.

## Prerequisites

1. An **AWS account** and credentials able to create VPC, ECS, ELB, CloudFront, IAM, Secrets Manager,
   CloudWatch Logs and Cloud Map resources (administrator rights for the first deploy):
   `aws configure sso` then `aws sso login --profile <name>` and `$env:AWS_PROFILE = "<name>"`
   (or the usual `AWS_*` variables).
2. **HCP Terraform**: a workspace of its own for this root, execution mode **Local**; set
   `TF_CLOUD_ORGANIZATION` and `TF_WORKSPACE`, and run `terraform login`.
3. **Neon**: `NEON_API_KEY` in the environment; `TF_VAR_neon_org_id` if the key spans several
   organizations.
4. **SMTP relay**: values in `environments/prod.tfvars`, `smtp_password` in `terraform.tfvars`
   (copy `terraform.tfvars.example`, gitignored) or `TF_VAR_smtp_password`.
5. Set a real `image_namespace` in `environments/prod.tfvars` (`CHANGE-ME` is rejected).

## Deploy

PowerShell (quote the arguments):

```powershell
terraform -chdir=infra/aws/terraform init
terraform -chdir=infra/aws/terraform apply "-var-file=environments/prod.tfvars" "-var=release_version=X.Y.Z"
```

Bash:

```bash
terraform -chdir=infra/aws/terraform init
terraform -chdir=infra/aws/terraform apply -var-file=environments/prod.tfvars -var release_version=X.Y.Z
```

`release_version` is always explicit (`latest` or `X.Y.Z`). The **first apply takes 15-20 minutes**
(each CloudFront distribution takes 5-10 minutes to deploy); Keycloak's first start also imports
the realm and creates its schema on Neon (health check grace period 10 minutes). Check progress with
`aws ecs describe-services --cluster birrapoint-prod-ecs --services birrapoint-prod-api birrapoint-prod-web birrapoint-prod-kc`
or `aws ecs wait services-stable ...` (the T146 workflow uses the same gate).

Outputs: `web_url`, `keycloak_url` (CloudFront domains), `environment`, `region`,
`ecs_cluster_name`, `ecs_service_names`, `neon_project_id`, and the sensitive
`keycloak_admin_password` (`terraform output -raw keycloak_admin_password`; user `admin`).

Tests, with no credentials: `terraform -chdir=infra/aws/terraform init -backend=false`, then
`terraform -chdir=infra/aws/terraform test` (mocked providers).

## Teardown

`infra/aws/teardown.ps1` and the deploy workflow arrive with T146. Until then:
`terraform -chdir=infra/aws/terraform destroy "-var-file=environments/prod.tfvars" "-var=release_version=latest"`
(destroys the Neon project and its data too; secrets are deleted without a recovery window).

## Known limitations and risks

- **Default CloudFront domains**: URLs are `https://dxxxx.cloudfront.net` (ADR-0023); a custom
  domain needs its own ADR. The realm's redirect URIs are derived from `SPA_URL` at import, so the
  realm is imported with the right domain on the first start.
- **The ALB is reachable only through CloudFront** (prefix list + secret header). A rule on the
  CloudFront managed prefix list counts one entry per prefix against the security group's
  "rules per security group" quota (default 60). If the first apply fails with
  `RulesPerSecurityGroupLimitExceeded`, request a quota increase (Service Quotas, VPC).
- **`Forwarded` header**: Keycloak builds its URLs from `KC_HOSTNAME` and the scheme from
  `Forwarded: proto=https`; verify login redirects on the first real apply. The web nginx forwards
  the ALB's `X-Forwarded-Proto: http` to the API, so the API sees `http` for the original scheme
  and ASP.NET's `UseHsts` never emits `Strict-Transport-Security`. HSTS is therefore added by
  CloudFront: a `birrapoint-<env>-security` response headers policy (max-age one year, no
  subdomains, override) on both distributions. The public URLs the API needs come from
  `Frontend__BaseUrl` and `Keycloak__Authority`.
- **60 s limit at the edge**: both CloudFront origins use `origin_read_timeout = 60`, the maximum
  without a quota increase (default 30 s). nginx allows 120 s on `/api/`, so a request that takes
  longer than 60 s (a very large `.xlsx` import) gets a 504 from CloudFront; request an increase
  of the "Response timeout per origin" quota (Service Quotas) if that happens.
- **daprd on Fargate (first apply check)**: the sidecar runs with `--enable-metrics=false` and
  loopback-only listeners. Flags to disable the placement/scheduler connection in standalone mode
  could not be confirmed for Dapr 1.16, so none are set: on the first real apply, check the
  `daprd` log group for placement or scheduler connection retries and add the flags if present.
- **One environment per AWS account and region**: the Dapr component reads the exact secret names
  the apps ask for (`ConnectionStrings--db`, `keycloak-db-password`, ...) and has no name prefix
  option, so the application secrets are not environment-qualified.
- **API deployments stop the old task first** (single consumer): expect a brief API outage on
  each rollout. `deployment_circuit_breaker` rolls back a task that fails to start.
- **No container health checks**: the Keycloak image has no curl and the API has none on Azure
  either; the ALB checks web (`/`) and Keycloak (`/health/ready` on port 9000). A crashing API is
  restarted by ECS only when the process exits.
- Public task IPs without NAT trade network isolation for cost; the security groups are the only
  inbound control. Container Insights is off to save cost.
- **Cost** (ADR-0023): about 85-100 USD/month: ALB ~18, Fargate incl. two `daprd` sidecars ~55,
  public IPv4 addresses ~15, CloudFront ~0 at this traffic. Azure Container Apps is cheaper.
