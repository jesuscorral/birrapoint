# BirraPoint — cloud deployment on AWS (Terraform → ECS Fargate + CloudFront + Neon)

Constitution v1.5.1, research R-17/R-18/R-19/R-21, ADR-0016/ADR-0021/ADR-0022/ADR-0023, FR-043–FR-047.

This root is independent of `infra/azure/terraform`: a deployment targets one cloud, with its own
HCP Terraform workspace, `environments/<env>.tfvars` and tests. Only the Docker Hub images,
`infra/keycloak` and the Neon account are shared. Deploys run from a workstation (below) or through
`deploy-aws.yml`; `infra/aws/teardown.ps1` removes everything.

First deploy from scratch? Follow the end-to-end checklist in [`infra/aws/deployment-runbook.md`](../deployment-runbook.md)
(accounts, keys, order). The one-time GitHub setup (OIDC role, `aws-production` environment, secrets)
is in [`infra/aws/github-actions-setup.md`](../github-actions-setup.md); what is shared with Azure
(Docker Hub, release bot) is in [`infra/ci-setup.md`](../../ci-setup.md).

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
             └───────────┬────────────┘                      │ prod-aws-neon          │
                         └─ pooled endpoint (migrations: direct) ▶ db birrapoint /     │
   birrapoint-prod-ecs cluster, birrapoint-prod-vpc: 2 public subnets,│ keycloak       │
   no NAT gateway; tasks get a public IP for egress only      └────────────────────────┘
```

- **Naming** (ADR-0021): every resource is `birrapoint-<environment>-<acronym>`, the environment
  lower-cased (`environment`, default `PROD`): `-vpc`, `-alb`, `-ecs` cluster, `-api` / `-web` /
  `-kc` services, `-web` / `-kc` CloudFront comments, `-aws-neon` (the Neon account is shared with
  the Azure root, whose project is `-neon`: the AWS name differs so the two never collide). Cloud Map namespace
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
  (the `DispatchJob` queue is lease-based and multi-worker safe, ADR-0024).
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
or `aws ecs wait services-stable ...` (`deploy-aws.yml` runs `.github/scripts/ecs-health.sh`: the
same waiter, retried up to three times because it gives up after 10 minutes, followed by a check that
each service's PRIMARY deployment is `COMPLETED` on the task definition in the
`ecs_task_definition_arns` output; a deployment the circuit breaker rolled back leaves the services
"stable" on the old version, and the gate fails on it).

`deploy-aws.yml` runs the same `init` + `apply` with the release versions through an OIDC role
(no stored AWS key), in the `aws-production` GitHub environment, then waits for the ECS services
and writes a summary with the URLs and versions.

Outputs: `web_url`, `keycloak_url` (CloudFront domains), `environment`, `region`,
`ecs_cluster_name`, `ecs_service_names`, `neon_project_id`, and the sensitive
`keycloak_admin_password` (`terraform output -raw keycloak_admin_password`; user `admin`).

Tests, with no credentials: `terraform -chdir=infra/aws/terraform init -backend=false`, then
`terraform -chdir=infra/aws/terraform test` (mocked providers), and the Pester tests of the teardown
logic: `Invoke-Pester infra/aws/tests` (Pester 5+). Both run in `infra.yml` when `infra/**` changes.

**Tags.** Every resource carries the provider `default_tags`: `var.tags` (`application=birrapoint`,
`managed-by=terraform`) plus `environment=<lower-cased environment>`, which cannot be overridden
through `var.tags`. The teardown sweep selects resources by tag, so applying this change to an
existing deployment adds the `environment` tag in place (no replacement).

## Teardown

```powershell
$env:NEON_API_KEY = "<key>"   # plus TF_CLOUD_ORGANIZATION, TF_WORKSPACE (the AWS workspace), terraform login, aws sso login
./infra/aws/teardown.ps1 -WhatIf   # preview, changes nothing
./infra/aws/teardown.ps1           # wipes everything; type birrapoint-<env> to confirm (-Force skips it)
```

Always a full wipe, Neon data included, independent from `infra/azure/teardown.ps1`. The script
(Windows PowerShell 5.1 and PowerShell 7; the decisions are in `Teardown.psm1`, tested in
`infra/aws/tests`):

1. runs `terraform init` and **guards** the HCP workspace: it must hold the requested environment
   (or be empty); an unreadable state aborts. It also reads `terraform output -raw neon_project_id`
   from the state;
2. shows what exists, asks for the typed confirmation, runs `terraform destroy` (no `-target`; the
   image variables get placeholders, the resolved region is always passed);
3. **sweeps** what the destroy left behind (it also continues when the destroy fails): Secrets
   Manager secrets (deleted without a recovery window), log groups `/birrapoint/<env>/`, ECS
   services and cluster, the ALB with listeners and target groups, the Cloud Map services and
   namespace, IAM roles, CloudFront distributions (disabled, then deleted: about 15 minutes) and the
   response headers policy, the VPC with its subnets, route tables, internet gateway and security
   groups, and the Neon project. Everything else is selected by **exact name and the
   tags `application=birrapoint`, `environment=<env>`**: a secret such as `ConnectionStrings--db`
   without those tags (another environment or application) is never touched. A VPC that contains
   VPC endpoints or NAT gateways is left in place and reported. **Neon is never selected by name
   alone** (the Neon account is shared with the Azure deployment): only the project id read from
   the state is deleted, after the Neon API confirms that its name is exactly
   `birrapoint-<env>-aws-neon`; another name (for example Azure's `birrapoint-<env>-neon`) is refused.
   With an empty state the script only reports name matches; to delete one, pass
   `-NeonProjectId <id>` (its name must still match);
4. removes the local `.terraform` folder and verifies through the resource groups tagging API
   (`aws resourcegroupstaggingapi get-resources`, in the region and in `us-east-1` for the global
   CloudFront), probes the three IAM roles with `aws iam get-role` (it is not certain that the
   tagging API indexes IAM roles) and checks the Neon project by id, so that nothing of the
   deployment is left; otherwise it lists it and
   exits non-zero. The tagging index can lag a few minutes behind a deletion, so re-run the script
   (it is idempotent). Deregistered ECS task definitions stay visible forever and are ignored.

Region: `-Region`, then `AWS_REGION` / `AWS_DEFAULT_REGION`, then `region` in the var file (a
mismatch with the var file's region aborts, unless `-Region` is given); the resolved region is always
passed to `terraform destroy`. Not touched: Docker Hub
images, GitHub secrets, the IAM OIDC provider and deploy role, the HCP workspace itself.
Manual alternative: `terraform -chdir=infra/aws/terraform destroy "-var-file=environments/prod.tfvars" "-var=release_version=latest"`.

## Neon compute budget

Neon Free gives each project 100 CU-hours of compute per month (0.25 CU for about 400 h). When it
is exhausted the compute is **suspended until the next billing period or an upgrade** (data is
kept, but the API and Keycloak cannot reach the database). Free also fixes scale-to-zero at 5
minutes of inactivity (no active queries; idle connections alone should not keep it awake, per
Neon's docs - confirm in the console, Monitoring). Launch ($0.106/CU-hour) can disable scale-to-zero
and has no monthly cap. The project pins a 0.25 CU floor (`primary_compute` in `neon.tf`).

Wake sources, and what this deployment does about each:

| Source | Effect | Mitigation |
|---|---|---|
| DispatchWorker safety-net poll | one wake per interval; every 30 s would keep compute on 24/7 (about 182 CU-h/month at 0.25 CU) | `dispatch_poll_interval`, default `01:00:00` |
| Keycloak internal cleanup task (about every 15 min, expired offline sessions) | about 4 wakes/h of at least 5 min each: roughly 20 min/h, about 61 CU-h/month | none: no documented server option; budget for it |
| Keycloak health check | `/health/ready` validates the pooled DB connections on every call (measured, Keycloak 26.2: `/health/live` and `/health/started` do not touch the DB) | the ALB target group checks `/health/live`; the lost signal is "database unreachable" in the check (Keycloak still fails requests itself) |
| Real traffic (organizers, judges, offline sync) | active time during use | none; this is the point of the database |

Estimate with the defaults and an idle system: about 61 CU-h/month from Keycloak plus up to
about 15 CU-h from the hourly poll, so roughly 60-80 CU-h of the 100 free. A busy event week
uses more.

**Recommendation.** Free plan works for low traffic and idle periods with the 1 h poll. A paid
plan (Launch) is a **prerequisite for a live competition period** or any guaranteed availability:
a suspended compute means no logins and no evaluations until the next period. Check the Neon
console, Monitoring, CU-hours, before an event.

Change the poll with the `dispatch_poll_interval` variable (TimeSpan `hh:mm:ss`, `00:00:01` to
`23:59:59`, passed to the API as `Dispatch__SafetyNetPollInterval`), e.g. in
`environments/prod.tfvars`: `dispatch_poll_interval = "00:05:00"` for faster recovery of missed
dispatch wake-ups on a paid plan, then apply as usual.

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
- **API deployments stop the old task first** (SignalR has no backplane): expect a brief API outage on
  each rollout. `deployment_circuit_breaker` rolls back a task that fails to start.
- **No container health checks**: the Keycloak image has no curl and the API has none on Azure
  either; the ALB checks web (`/`) and Keycloak (`/health/live` on port 9000; `/health/ready` validates DB connections and would keep Neon awake). A crashing API is
  restarted by ECS only when the process exits.
- Public task IPs without NAT trade network isolation for cost; the security groups are the only
  inbound control. Container Insights is off to save cost.
- **Cost** (ADR-0023): about 85-100 USD/month: ALB ~18, Fargate incl. two `daprd` sidecars ~55,
  public IPv4 addresses ~15, CloudFront ~0 at this traffic. Azure Container Apps is cheaper.
