#!/bin/sh
# Generates /config.json for the PWA from environment variables at container start (FR-043: the
# image carries no environment-specific configuration). Runs from the nginx image's
# /docker-entrypoint.d/ after the template envsubst step; a non-zero exit aborts startup.
#
#   KEYCLOAK_URL        required, public Keycloak base URL (e.g. https://birrapoint-kc.<domain>)
#   KEYCLOAK_REALM      default: birrapoint
#   KEYCLOAK_CLIENT_ID  default: birrapoint-spa
#   API_BASE_URL        default: "" (same origin — nginx proxies /api and /hubs)
#   API_UPSTREAM        required by the nginx template; checked here so a missing value fails fast
set -eu

fail() {
    echo "40-runtime-config.sh: $1" >&2
    exit 1
}

[ -n "${KEYCLOAK_URL:-}" ] || fail "KEYCLOAK_URL is required"
[ -n "${API_UPSTREAM:-}" ] || fail "API_UPSTREAM is required"

# Values are URLs/identifiers; reject characters that would need JSON escaping rather than
# escaping them, so a malformed value fails loudly instead of producing surprising config.
for name in KEYCLOAK_URL KEYCLOAK_REALM KEYCLOAK_CLIENT_ID API_BASE_URL; do
    eval "value=\${$name:-}"
    case "$value" in
        *\"* | *\\* ) fail "$name contains a quote or backslash" ;;
    esac
done

mkdir -p /tmp/birrapoint
cat > /tmp/birrapoint/config.json <<EOF
{
  "keycloak": {
    "url": "${KEYCLOAK_URL%/}",
    "realm": "${KEYCLOAK_REALM:-birrapoint}",
    "clientId": "${KEYCLOAK_CLIENT_ID:-birrapoint-spa}"
  },
  "apiBaseUrl": "${API_BASE_URL:-}"
}
EOF
echo "40-runtime-config.sh: wrote /tmp/birrapoint/config.json"
