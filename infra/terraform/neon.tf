# Production PostgreSQL on Neon (R-18/FR-047): one project, its default branch holding two
# databases — `birrapoint` (the API) and `keycloak` (R-19) — each owned by its own role.

resource "neon_project" "main" {
  name                      = var.name_prefix
  region_id                 = var.neon_region
  pg_version                = 16
  org_id                    = var.neon_org_id
  history_retention_seconds = var.neon_history_retention_seconds

  branch {
    name          = "main"
    database_name = "birrapoint"
    role_name     = "birrapoint"
  }
}

resource "neon_role" "keycloak" {
  project_id = neon_project.main.id
  branch_id  = neon_project.main.default_branch_id
  name       = "keycloak"
}

resource "neon_database" "keycloak" {
  project_id = neon_project.main.id
  branch_id  = neon_project.main.default_branch_id
  name       = "keycloak"
  owner_name = neon_role.keycloak.name
}

locals {
  neon_direct_host = neon_project.main.database_host
  neon_pooled_host = neon_project.main.database_host_pooler

  # Npgsql format. Runtime traffic goes through Neon's PgBouncer (transaction pooling); EF
  # migrations use the direct endpoint because their advisory lock is session-scoped.
  api_db_pooled = join(";", [
    "Host=${local.neon_pooled_host}",
    "Database=${neon_project.main.database_name}",
    "Username=${neon_project.main.database_user}",
    "Password=${neon_project.main.database_password}",
    "SSL Mode=VerifyFull",
  ])
  api_db_direct = join(";", [
    "Host=${local.neon_direct_host}",
    "Database=${neon_project.main.database_name}",
    "Username=${neon_project.main.database_user}",
    "Password=${neon_project.main.database_password}",
    "SSL Mode=VerifyFull",
  ])

  # Keycloak (Hibernate + server-side prepared statements) needs the direct endpoint.
  keycloak_jdbc_url = "jdbc:postgresql://${local.neon_direct_host}/${neon_database.keycloak.name}?sslmode=require"
}
