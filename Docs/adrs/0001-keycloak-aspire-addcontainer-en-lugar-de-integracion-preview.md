# 0001 - Keycloak en Aspire mediante AddContainer en lugar del paquete de integración (preview)

**Status:** Accepted
**Date:** 2026-07-07

## Context

La tarea T005 exige orquestar Keycloak 25+ en el AppHost de .NET Aspire con importación
automática del realm `birrapoint` (FR-044, R-16). Aspire ofrece una integración de primera clase
(`Aspire.Hosting.Keycloak`, con `AddKeycloak(...)` + `WithRealmImport(...)` y health check
integrado), pero en el tren de versiones actual (13.4.x) ese paquete **solo existe en preview**
(`13.4.6-preview.1`), mientras que el resto de paquetes de hosting usados (PostgreSQL, MailPit)
son estables. El Principio V de la constitución exige dependencias justificadas y penaliza
riesgo innecesario; mezclar un paquete preview con el SDK estable puede además arrastrar
conflictos de versiones.

## Decision

Orquestar Keycloak con la API genérica estable `builder.AddContainer("keycloak",
"quay.io/keycloak/keycloak", "26.2")`, configurando manualmente: `start-dev --import-realm`,
credenciales bootstrap por variables de entorno (placeholders de desarrollo local),
`WithBindMount` de `infra/keycloak/` a `/opt/keycloak/data/import` y un endpoint HTTP proxied en
el puerto 8081. No se adopta `Aspire.Hosting.Keycloak` mientras no publique una versión estable
del tren en uso.

## Consequences

- **Positivo**: cero dependencias preview; el AppHost solo usa paquetes estables; el
  comportamiento del contenedor es idéntico al de producción (misma imagen y flags que ACA).
- **Negativo**: sin health check integrado, no es posible `WaitFor(keycloak)` sobre un Keycloak
  *listo* — existe una carrera latente en arranques fríos cuando la API dependa de Keycloak
  (T011+). Mitigación prevista: añadir un health check HTTP manual (`WithHttpHealthCheck` sobre
  `/realms/birrapoint`) o migrar a `AddKeycloak` cuando salga estable.
- **Revisión**: reevaluar en cada actualización del tren de Aspire; si `Aspire.Hosting.Keycloak`
  se estabiliza, migrar y marcar este ADR como Superseded. (Señalado también por la revisión
  automática del PR #2.)
