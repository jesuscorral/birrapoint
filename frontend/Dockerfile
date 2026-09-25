# syntax=docker/dockerfile:1
# BirraPoint PWA — multi-stage image: Node build → Nginx Alpine (FR-043, constitution
# Containerization constraint). Build context is `frontend/`:
#   docker build frontend
# No environment-specific configuration is baked in: /config.json (Keycloak URL/realm/client,
# API base URL) is generated from environment variables when the container starts
# (nginx/40-runtime-config.sh), and the API upstream the /api + /hubs reverse proxy points at is
# substituted into the nginx config at the same moment.

FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
# public/config.json holds local-dev values; drop it from the output — the runtime one is
# generated at container start.
RUN npx ng build --configuration production \
    && rm dist/birrapoint/browser/config.json

# nginx-unprivileged: same upstream nginx build, runs as a non-root user and listens on 8080.
FROM nginxinc/nginx-unprivileged:1.29-alpine AS runtime
# Resolve the API upstream through the container's own DNS at request time (see the template).
ENV NGINX_ENTRYPOINT_LOCAL_RESOLVERS=1 \
    API_BASE_URL="" \
    KEYCLOAK_REALM=birrapoint \
    KEYCLOAK_CLIENT_ID=birrapoint-spa
COPY nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY --chmod=755 nginx/40-runtime-config.sh /docker-entrypoint.d/40-runtime-config.sh
COPY --from=build /app/dist/birrapoint/browser /usr/share/nginx/html
EXPOSE 8080
