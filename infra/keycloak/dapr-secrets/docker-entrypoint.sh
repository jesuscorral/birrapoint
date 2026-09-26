#!/bin/bash
# BirraPoint Keycloak entrypoint (T142, ADR-0021): when DAPR_SECRET_STORE is set (Azure Container
# Apps), loads the secrets listed in DAPR_SECRETS from the Dapr secret store (Key Vault) into the
# environment, then hands over to Keycloak. Without it (local `docker run`), it only starts
# Keycloak, so plain environment variables keep working.
set -euo pipefail

if [[ -n "${DAPR_SECRET_STORE:-}" ]]; then
    # Fails the container (and so the revision) when a secret cannot be read in time.
    secret_exports="$(java -Xmx32m -cp /opt/birrapoint/dapr-secrets DaprSecretsEnv)"
    eval "$secret_exports"
    unset secret_exports
fi

exec /opt/keycloak/bin/kc.sh "$@"
