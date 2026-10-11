#!/bin/bash
# BirraPoint Keycloak entrypoint (T142, ADR-0021): when DAPR_SECRET_STORE is set (Azure Container
# Apps), loads the secrets listed in DAPR_SECRETS from the Dapr secret store (Key Vault) into the
# environment, then hands over to Keycloak. Without it (local `docker run`), it only starts
# Keycloak, so plain environment variables keep working.
set -euo pipefail

if [[ -n "${DAPR_SECRET_STORE:-}" ]]; then
    # Fails the container (and so the revision) when a secret cannot be read in time. Only the
    # loader's own output may reach stdout (it is eval'd): JVM warnings go to stderr.
    secret_exports="$(java -XX:+DisplayVMOutputToStderr -Xmx32m -cp /opt/birrapoint/dapr-secrets DaprSecretsEnv)"
    eval "$secret_exports"
    unset secret_exports
fi

# Fail fast (T130): without the API admin client secret the realm import would leave the
# birrapoint-api-admin client with no usable secret and judge provisioning would fail at runtime.
# Never prints the value.
if [[ -z "${API_ADMIN_CLIENT_SECRET:-}" || -z "${API_ADMIN_CLIENT_SECRET//[[:space:]]/}" ]]; then
    echo "FATAL: API_ADMIN_CLIENT_SECRET is not set or empty; refusing to start Keycloak." >&2
    exit 1
fi

exec "${KC_BIN:-/opt/keycloak/bin/kc.sh}" "$@"
