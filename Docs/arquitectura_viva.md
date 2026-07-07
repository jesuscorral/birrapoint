# Arquitectura Viva — BirraPoint

> Documento vivo: refleja el **estado actual real** del sistema. Se actualiza obligatoriamente al
> cierre de cada tarea del backlog (ver CLAUDE.md §Implementation workflow, paso 6). Las
> decisiones con trade-offs se registran en `Docs/adrs/`; el diseño aprobado vive en
> `specs/001-birrapoint-mvp/`.

**Última actualización:** 2026-07-07 · tras T001–T006 (Fase 1 Setup, PR #2)

## Estado global

Fase 1 (Setup) completada salvo T007. Existe el esqueleto completo de ambos stacks y la
orquestación local; **no hay todavía lógica de negocio, dominio, autenticación ni endpoints de
API** (empiezan en Fase 2, T008–T020).

## Topología local (.NET Aspire — `dotnet run --project backend/src/BirraPoint.AppHost`)

| Recurso | Implementación | Endpoint local | Notas |
|---|---|---|---|
| `postgres` / db `db` | contenedor `postgres:16`, volumen persistente | puerto dinámico | connection string inyectada a la API como `ConnectionStrings__db` |
| `keycloak` | contenedor `quay.io/keycloak/keycloak:26.2` vía `AddContainer` (ADR-0001) | http://localhost:8081 | realm `birrapoint` auto-importado desde `infra/keycloak/` |
| `mailpit` | CommunityToolkit MailPit | SMTP y UI en puertos dinámicos | sumidero de correo local |
| `api` | proyecto `BirraPoint.Api` | puerto dinámico | recibe env: `Keycloak__Authority`, `Keycloak__AdminClientId/Secret` (placeholder dev), `Smtp__Host/Port` |
| `frontend` | `npm start` (ng serve) vía `AddNpmApp` | http://localhost:4200 | espera a `api` |

## Backend (`backend/`)

- **Proyectos**: `BirraPoint.Api` (monolito modular, esqueleto `Domain/ Common/ Features/
  Realtime/` vacío), `BirraPoint.AppHost`, `BirraPoint.ServiceDefaults` (OpenTelemetry, health
  checks, resilience), tests `BirraPoint.Api.UnitTests` y `BirraPoint.Api.IntegrationTests`.
- **Endpoints HTTP actuales**: solo infraestructura — `/health` y `/alive` (expuestos únicamente
  en Development por `MapDefaultEndpoints`). Ningún endpoint de negocio todavía; el primero será
  `GET /api/v1/styles` (T017).
- **Paquetes clave**: MediatR 12.5.0 (pineado — no subir a 13+), FluentValidation 12.1.1,
  Npgsql.EntityFrameworkCore.PostgreSQL 10.0.2, ClosedXML 0.105.0, QuestPDF 2026.7.0
  (requiere `Settings.License = Community` al arrancar — pendiente para el slice Dispatch),
  MailKit 4.17.0.

## Frontend (`frontend/`)

- Angular 20 standalone + Signals, PWA (`@angular/pwa`), Tailwind CSS v4 (plugin PostCSS,
  `.postcssrc.json`), zone.js (decisión zoneless pendiente — ver revisión PR #2).
- Estructura FSD: `src/app/core/` `features/` `shared/` (vacías, con `.gitkeep`).
- Dependencias fijadas a la línea compatible con Angular 20: `@angular/cdk@20`,
  `keycloak-angular@20`; además `keycloak-js@26`, `dexie@4`, `@microsoft/signalr@10`.
- Componente raíz: shell mínimo accesible (h1 + `router-outlet`), sin rutas definidas aún.

## Testing y gates

| Suite | Comando | Estado |
|---|---|---|
| Backend unit + integration | `dotnet test backend/BirraPoint.sln` | smoke tests (el harness real de integración con Testcontainers llega en T018) |
| Frontend unit | `cd frontend && npx jest` | jest-preset-angular; Karma eliminado |
| E2E + a11y | `cd frontend && npx playwright test -c e2e` | smoke + gate axe WCAG 2.1 A/AA (solo chromium; webkit pendiente antes de las suites offline) |
| Lint/format | `ng lint`, `prettier`, `dotnet format --verify-no-changes` | limpios |

## Flujos de datos

Ninguno implementado todavía. Contratos objetivo en `specs/001-birrapoint-mvp/contracts/`
(REST `/api/v1`, hub SignalR `CompetitionHub`, fichero de import `.xlsx`).

## Deuda registrada / pendientes inmediatos

- T007: volcar comandos reales a CLAUDE.md y cerrar Fase 1.
- Decidir zoneless change detection antes de la Fase 3 de frontend (revisión PR #2).
- Endurecer smoke de integración (arrancar `PostgreSqlContainer`) — se cubrirá en T018.
- `WaitFor` sobre Keycloak listo cuando se cablee auth (T011, ADR-0001).
- `Aspire.Hosting.NodeJs` en tren de versiones antiguo (9.5.2); alinear cuando exista 13.x.
