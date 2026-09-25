# syntax=docker/dockerfile:1
# BirraPoint Keycloak — optimized production image (FR-043/FR-046, R-19).
# Build context is `infra/keycloak/`:
#   docker build infra/keycloak
# Bakes in the login theme and the realm definition only. The realm file carries no secrets:
# the SPA URL, the API admin client secret and SMTP settings are ${VAR:default} placeholders resolved
# at import time from container environment variables. Database, hostname and bootstrap admin
# credentials are likewise runtime environment variables (KC_DB_URL, KC_HOSTNAME, ...).

ARG KEYCLOAK_VERSION=26.2

# Derive the production realm from the local-dev one so a production realm never ships a known
# credential:
#  - strip the seeded `organizer`/`organizer` account (E2E-only); the service-account user stays;
#  - drop the admin client secret's local-dev fallback, so the import takes it from
#    API_ADMIN_CLIENT_SECRET or not at all.
FROM alpine:3.22 AS realm
RUN apk add --no-cache jq
COPY birrapoint-realm.json /realm/birrapoint-realm.json
RUN jq '.users |= map(select(.username != "organizer")) \
        | .clients |= map(if .clientId == "birrapoint-api-admin" \
                          then .secret = "${API_ADMIN_CLIENT_SECRET}" else . end)' \
        /realm/birrapoint-realm.json > /realm/birrapoint-realm.prod.json \
    && ! grep -q -e '"value": "organizer"' -e 'dev-only-secret' /realm/birrapoint-realm.prod.json

FROM quay.io/keycloak/keycloak:${KEYCLOAK_VERSION} AS builder
ENV KC_DB=postgres \
    KC_HEALTH_ENABLED=true \
    KC_METRICS_ENABLED=true
COPY themes/birrapoint /opt/keycloak/themes/birrapoint
RUN /opt/keycloak/bin/kc.sh build

FROM quay.io/keycloak/keycloak:${KEYCLOAK_VERSION}
COPY --from=builder /opt/keycloak/ /opt/keycloak/
COPY --from=realm /realm/birrapoint-realm.prod.json /opt/keycloak/data/import/birrapoint-realm.json
EXPOSE 8080 9000
ENTRYPOINT ["/opt/keycloak/bin/kc.sh"]
# --import-realm uses IGNORE_EXISTING: the realm is seeded on first start only.
CMD ["start", "--optimized", "--import-realm"]
