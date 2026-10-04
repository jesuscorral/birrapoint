# 0023 - Dual cloud target: Azure Container Apps or AWS ECS Fargate

**Status:** Accepted — extends ADR-0016/0021/0022 (Azure stays as is; AWS is added beside it)
**Date:** 2026-10-04

## Context

Production ran only on Azure Container Apps (ADR-0016, ADR-0021, ADR-0022). The user wants
BirraPoint deployable to either Azure or AWS, as independently as possible. A deployment targets
one cloud; a run never deploys both. The images (Docker Hub), the Keycloak image and Neon are
already cloud-agnostic, and ADR-0021 already routes secrets through a Dapr secret store.

## Decision

1. **Two targets, one per deployment.** Constitution v1.5.0 allows Azure Container Apps + Key
   Vault or AWS ECS Fargate + ALB + CloudFront + Secrets Manager.
2. **Independent roots**: `infra/azure/{terraform,teardown.ps1,Teardown.psm1,tests}` and
   `infra/aws/{...}` with the same shape. Each has its own HCP Terraform workspace (Local
   execution), committed `environments/<env>.tfvars`, `terraform test`, Pester tests and workflow:
   `deploy-azure.yml` (Azure OIDC, `wait-revisions.sh`) and `deploy-aws.yml` (AWS OIDC role,
   `aws ecs wait services-stable`). GitHub environments `azure-production` and `aws-production`
   hold environment-scoped secrets and variables, so names like `TF_WORKSPACE` stay the same per
   environment. Shared only: Docker Hub images, `infra/keycloak`, the Neon account.
3. **AWS compute**: ECS Fargate in a dedicated VPC, 2 public subnets, no NAT gateway. Tasks get a
   public IP only for egress (Docker Hub, Neon); security groups admit inbound only from the ALB.
   The API is internal (no target group), reached by the web nginx through ECS Service Connect
   (`api.birrapoint.local`), and stays at exactly 1 replica.
4. **AWS HTTPS**: two CloudFront distributions (web, Keycloak) on `*.cloudfront.net` with free
   certificates, WebSockets, caching disabled. The ALB accepts only CloudFront (managed
   origin-facing prefix list) and routes web vs Keycloak by a secret origin custom header.
5. **AWS secrets**: Secrets Manager (`recovery_window_in_days = 0` so redeploys do not collide),
   read through a Dapr sidecar in the API and Keycloak tasks (`secretstores.aws.secretmanager`; an init container
   writes the component YAML to a shared volume). The API and Keycloak each have an IAM task role with
   `GetSecretValue` on its own secrets only. Same mechanism as ADR-0021, so images are unchanged.
6. Logs in CloudWatch; Neon unchanged (one project per deployment, `aws-eu-central-1`); naming
   `birrapoint-<env>-<acronym>`.

Rationale and rejected options (App Runner, EKS, own domain + ACM, ECS native secrets injection):
research R-21.

## Consequences

- Two stacks to build, test and maintain; changes to shared behavior (Keycloak image, env vars,
  Dapr secret names) must be checked against both.
- Cost: AWS is roughly 85–100 USD/month (ALB ~18, Fargate incl. 2 daprd sidecars ~55, public IPv4
  ~15, CloudFront ~0); Azure ACA is cheaper.
- AWS URLs are `*.cloudfront.net` until a custom domain is added (a later ADR).
- Public task IPs without NAT trade network isolation for cost; the security groups are the only
  inbound control.
- The GitHub environment `production` is renamed `azure-production`: it must be recreated or
  renamed with its secrets and reviewers, and the Azure federated credential subject
  (`repo:<org>/<repo>:environment:production`) must change to `...:environment:azure-production`.
  The workflow `deploy.yml` becomes `deploy-azure.yml`.
- Paths move (`infra/terraform` → `infra/azure/terraform`, etc.); existing HCP workspaces and state
  are unaffected, but local clones need `terraform init` again.
- The AWS stack is delivered by T145 and T146; T146 completed it (below).

**Update 2026-10-04 (T145)** — implementation refinements, not a change of decision:

- **Web→API discovery**: a Cloud Map private DNS namespace (`api.birrapoint-<env>.local`, ECS service discovery A records) replaces ECS Service Connect. The web nginx resolves `API_UPSTREAM` per request through `resolver`, which ignores `/etc/hosts`, where Service Connect publishes its names.
- **HTTPS behind CloudFront**: CloudFront→ALB is HTTP and the ALB overwrites `X-Forwarded-Proto` with `http`. CloudFront therefore adds `Forwarded: proto=https` (the ALB leaves it untouched); Keycloak runs with `KC_PROXY_HEADERS=forwarded` and `KC_HOSTNAME` = the full https CloudFront URL. To verify on the first real apply. The API still sees forwarded proto `http`, so ASP.NET's `UseHsts` would skip the header: CloudFront adds `Strict-Transport-Security` through a response headers policy on both distributions (PR #62 review M1). Origin read timeout 60 s (CloudFront maximum without a quota increase).
- **One environment per AWS account and region**: `secretstores.aws.secretmanager` reads exact secret names (no prefix lookup), so names cannot be namespaced per environment.
- **daprd 1.16 listens on 127.0.0.1 only**; the init container and app reach it over the task's loopback.
- **API rollouts stop the old task first** (deployment minimum 0% / maximum 100%) because of the single SignalR and job consumer: each API deploy causes a short outage.

**Update (T146)** - operations tooling:

- `infra/aws/teardown.ps1` sweeps leftovers by exact name and the `application` + `environment` default tags, so another environment's or application's resources are never touched; the final check uses the tagging API.
- `.claude/hooks/guard.js` also blocks destructive `aws` CLI commands.
- Setup docs are split per cloud (`infra/azure/`, `infra/aws/`: setup guide + runbook); shared CI setup (Docker Hub, release bot) is `infra/ci-setup.md`.
- `deploy-aws.yml` uses the `aws-production` environment and an OIDC role (`AWS_ROLE_ARN`); health gate is `aws ecs wait services-stable`.
