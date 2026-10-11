# Keycloak production administration (T130)

Shared by the Azure and AWS deployments. The production image (`infra/keycloak/Dockerfile`) is
hardened; this page lists what that means for operators.

## What the image enforces

- **No admin console UI.** The `admin` feature is disabled at build time (`KC_FEATURES_DISABLED=admin`):
  `<keycloak_url>/admin/` and `/admin/master/console/` answer 404. The **Admin REST API stays on**
  (`admin-api` feature): the API's service account `birrapoint-api-admin` provisions judges through it,
  and operators use it for the steps below.
- **Realm (`realm-prod.jq`, applied to `birrapoint-realm.json`)**: brute-force detection with temporary
  lockout (5 failures, 1 min doubling to 15 min, never permanent); password policy
  `length(12) and notUsername and notEmail`; `verifyEmail=true` (self-registration grants ORGANIZER);
  no seeded `organizer` account; the API admin client secret comes only from `API_ADMIN_CLIENT_SECRET`.
  The password policy deliberately has no character-class rules: the temporary judge password the API
  generates (`KeycloakAdminClient.GenerateTemporaryPassword`, 24 random base64 characters) does not
  always contain digits or symbols. Adding class rules requires changing that generator first.
- **`verifyEmail` needs working SMTP.** Both Terraform roots require `smtp_host` and
  `smtp_from_address`, but cannot prove the relay works: with a wrong relay nobody can complete
  registration. Check SMTP (a judge invitation, or a self-registration) after the first deploy.
- **Fail-fast**: the container refuses to start when `API_ADMIN_CLIENT_SECRET` is unset or blank
  (after the Dapr secrets are loaded), instead of running with a client nobody can authenticate as.
- **Existing environments**: the realm is imported on the first start only (`IGNORE_EXISTING`). The
  realm hardening reaches a new environment only. For a running one, change the realm settings
  through the Admin REST API (below), or wipe and re-create it.

## Replace the temporary bootstrap admin

`KC_BOOTSTRAP_ADMIN_USERNAME/PASSWORD` create a *temporary* `admin` in the `master` realm on first
start. Replace it with a permanent administrator, then delete it. With no console, use the Admin REST
API through the public Keycloak URL (PowerShell 5.1 or 7). The bootstrap password is the Terraform
output `keycloak_admin_password`.

```powershell
$kc = '<keycloak_url>'                                    # Terraform output keycloak_url
$boot = terraform -chdir=infra/<azure|aws>/terraform output -raw keycloak_admin_password

function Get-KcToken($user, $pass) {
  (Invoke-RestMethod -Method Post -Uri "$kc/realms/master/protocol/openid-connect/token" -Body @{
    grant_type = 'password'; client_id = 'admin-cli'; username = $user; password = $pass }).access_token
}
function Kc($method, $path, $body) {
  $p = @{ Method = $method; Uri = "$kc/admin/realms/$path"; Headers = @{ Authorization = "Bearer $script:tok" } }
  if ($body) { $p.Body = ($body | ConvertTo-Json -Depth 5); $p.ContentType = 'application/json' }
  Invoke-RestMethod @p
}

# 1. Create the permanent admin (pick a strong password; keep it in your password manager).
$tok = Get-KcToken 'admin' $boot
$new = Read-Host 'New admin password'                      # visible; use a private terminal
Kc Post 'master/users' @{ username = 'birrapoint-admin'; enabled = $true; email = '<you@example.com>'
  emailVerified = $true; credentials = @(@{ type = 'password'; value = $new; temporary = $false }) }
$id = (Kc Get 'master/users?username=birrapoint-admin&exact=true')[0].id
Kc Post "master/users/$id/role-mappings/realm" @(Kc Get 'master/roles/admin')

# 2. Authenticate as the new admin, then delete the bootstrap one.
$tok = Get-KcToken 'birrapoint-admin' $new
$bootId = (Kc Get 'master/users?username=admin&exact=true')[0].id
Kc Delete "master/users/$bootId"
Kc Get 'master/users' | Select-Object username
```

The same operations work from inside the container with `kcadm.sh` (`config credentials`, `create
users`, `set-password`, `add-roles --rolename admin`, `delete users/<id>`) where a shell is available
(`az containerapp exec` on Azure; ECS Exec is not enabled on AWS).

After this, `keycloak_admin_password` in Terraform state and Key Vault / Secrets Manager no longer
opens anything: the bootstrap variables are only read on an empty database. Keep a note of the
permanent admin credentials outside the repository.

## Day-2 changes without the console

Use the same `Kc` helper: for example `Kc Get 'birrapoint'` reads the realm and
`Kc Put 'birrapoint' @{ ... }` updates it (send the full representation from the `Get`, edited).
Rotating the API admin client secret: `Kc Get 'birrapoint/clients?clientId=birrapoint-api-admin'`, then
`Kc Put "birrapoint/clients/$cid" ...` with the new `secret`, matching the Key Vault / Secrets
Manager value.
