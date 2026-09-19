# Traspaso a Claude Code — T127

> Fichero temporal, **sin rastrear**. Bórralo con `del HANDOFF-T127.md` cuando termines.
> **No uses `git clean -xfd` para borrarlo**: eso arrasa también `frontend/node_modules`,
> que es exactamente lo que dejó el proyecto sin arrancar en septiembre.

---

## 0 · Arrancar Claude Code

PowerShell (no hace falta administrador):

```powershell
irm https://claude.ai/install.ps1 | iex
```

Comprobar y abrir sesión en el repo:

```powershell
claude --version
cd C:\MyWS\birrapoint
claude
```

Recomendado: tener [Git for Windows](https://git-scm.com/downloads/win) instalado — habilita la
herramienta Bash; sin él Claude Code usa PowerShell.

Primer mensaje sugerido en la sesión:

> Lee `HANDOFF-T127.md` en la raíz del repo y ejecuta la tarea T127 que describe.
> Sigue el workflow de `CLAUDE.md` (rama por tarea, tollgate antes de escribir código, TDD).

---

## 1 · Estado verificado de `main` (19 sep 2026)

- `T124`, `T125` y `T125b` ya están mergeadas en `main`.
- Suite completa en verde: **644 tests, 57 suites**. El árbol compila (`tsc` app y spec limpios).
- Hay un commit posterior a ese trabajo (17 sep) que tocó `table-board`, `mesa-card`,
  `beer-token`, `unassigned-column`, `organizer-dashboard` y `app.routes.ts`. **Léelos antes de
  modificarlos**, no asumas el contenido.

### Requisitos de entorno

- **Node 24+** es obligatorio: `jest.config.ts` depende del type stripping nativo de Node.
  Con Node 22 `ng serve` arranca, pero `npx jest` falla con
  `Preset jest-preset-angular not found` — es versión de Node, no instalación rota.
- Si `frontend/node_modules` no existe: `cd frontend && npm ci`.

---

## 2 · El problema a resolver

El organizador abre `/organizer/competitions/:id/tables` y ve la pantalla pegada a los bordes,
sin topbar ni cabecera, y sin ninguna referencia de en qué punto del proceso está.

**Causa raíz, ya diagnosticada:** ninguna ruta suelta del organizador tiene shell.
`TableManagementComponent` es literalmente `<app-table-board [competitionId]="…" />` y nada más.
Lo mismo en `judge-management`, `competition-monitor`, `results-dispatch` y
`organizer-dashboard`: **ninguno usa `bp-topbar`**. El asistente es el único sitio del proyecto
con shell, y por eso es el único con márgenes.

Los pasos no aparecen porque esa URL no es el asistente. El panel enruta por estado
(`organizer-dashboard.component.ts`, método `destination()`): Draft → asistente,
Active → `/tables`, resto → `/monitor`.

### Lo que pidió el usuario, literal

1. Que se vean los 6 pasos y se pueda navegar entre ellos **en cualquier estado**.
2. Que todos los campos se sigan mostrando aunque no se puedan editar.
3. Márgenes. La app es desktop-first, pero no pegada a los bordes.

---

## 3 · DECISIÓN PENDIENTE — resolver en el primer turno

El usuario dijo *"si la competición ya está activa no podré editar la información"*.
Eso **contradice el spec aprobado**. `specs/001-birrapoint-mvp/data-model.md`, tabla de puertas
por estado:

| Estado | Puerta |
|---|---|
| Draft | Setup del organizador; invisible para jueces |
| **Active** | Jueces ven asignaciones; orden fijable; hojas bloqueadas; **el setup sigue siendo editable** |
| **InEvaluation** | Hojas se desbloquean; **importaciones y ediciones del asistente rechazadas (409)** |
| Finalized | Todo de solo lectura |

El solo-lectura empieza en **InEvaluation**, no en Active. Además: la captura que motivó todo
esto es de una competición **Active** asignando cervezas a mesas — bloquear Active rompería
justo el flujo que el usuario estaba usando, y la UI prohibiría algo que la API acepta.

**Recomendación:** `readOnly = state === 'InEvaluation' || state === 'Finalized'`.
Confírmalo con el usuario antes de implementar. Si insiste en bloquear Active, hazlo, pero
déjale claro por escrito que se aparta del spec y que habría que enmendar `data-model.md`.

---

## 4 · Plan T127

### 4.1 Shell de organizador
Nuevo `frontend/src/app/shared/components/bp-page-shell/` (+ spec): topbar, contenedor a `88rem`
y gutters que crecen a 1280px/1800px. Aplicarlo a las cinco pantallas sueltas: panel, jueces,
mesas, monitor, envío. Exportarlo en `shared/components/index.ts`.

Ojo al precedente: `--spacing-16` se usaba sin estar definido en `styles.css`, y **un `var()` sin
resolver invalida la declaración `padding` entera**, así que el asistente llevaba meses sin
padding. Ya está corregido; no reintroduzcas tokens inexistentes.

### 4.2 Asistente navegable en cualquier estado
`competition-wizard.component.ts`: cargar en cualquier estado, los 6 pasos siempre visibles y
alcanzables. Añadir deep-link `?step=N` para que se pueda enlazar el paso 6 y para que un F5 no
devuelva al paso 1 (hoy lo hace).

### 4.3 Modo consulta
`readOnly` derivado del estado y pasado como input a cada paso. Campos deshabilitados; ocultar
las acciones mutadoras (Guardar borrador, Subir archivo, Consolidar, Add table, arrastrar).
Los datos se siguen viendo enteros.

### 4.4 Enrutado del panel
`destination()`: Draft y Active → asistente. Monitor y Envío siguen siendo rutas propias; no son
pasos de creación.

---

## 5 · Invariantes que NO se pueden romper

Nueve specs E2E los buscan literalmente. Verificar tras cualquier cambio en
`features/table-management/`:

- Nombres accesibles `Beer {blindCode} — view details` y `Judge {displayName} — view details`
  (guion largo, exacto). Información nueva va a `aria-describedby`, **nunca** al nombre accesible.
- Clase CSS `beer-token--bos-flagged`, en el mismo elemento que lleva `role="button"`.
- Ids de drop list `judges-{tableId}` / `beers-{tableId}` / `judges-unassigned` / `beers-unassigned`.
- `dd[data-stat="judges"]` y `dd[data-stat="beers"]`.
- Las cadenas **en inglés** `New table name` (label) y `Add table` (botón). El resto del tablero
  está en español, pero nueve specs las buscan por label. No traducir.
- `<h1>Table management</h1>` en la ruta suelta `/tables`.
- En el asistente: `Siguiente` (avance), `Atrás`, `Finalizar` (paso 6), `Subir archivo`,
  `Consolidar`, `Guardar borrador`.

Specs dependientes: `us5-tables`, `us6-order`, `us7-offline`, `us8-close`, `us9-dashboard`,
`us10-dispatch`, `us11-discrepancy`, `us12-removal`, `us13-dashboard`, `us4-judges`,
`us14-judge-import`, `e2e/a11y/routes.a11y`.

### Dos ejes de categoría, nunca fundir
- **Categoría de concurso** — `BeerEntry.CompetitionCategoryId`, la agrupación propia del
  organizador del paso 3. Null para entradas creadas fuera del flujo de importación.
- **Categoría BJCP** — `BjcpStyle.CategoryNumber`/`CategoryName` (p. ej. "21"/"IPA"), la taxonomía
  publicada. Null solo si el código de estilo no tiene fila en el catálogo.

`Domain/CompetitionCategory.cs` lo dice explícitamente. Viajan como `competitionCategoryName` +
`bjcpCategoryNumber`/`bjcpCategoryName`.

---

## 6 · Puertas de verificación

```powershell
cd C:\MyWS\birrapoint\frontend
npx jest                 # 644 tests en verde antes de empezar
npx ng lint
npm run format:check
npx tsc -p tsconfig.app.json --noEmit
npm run build:budget     # 500 KB gzip; main está en ~230 KB

# Backend (ahora sí disponible desde terminal)
cd C:\MyWS\birrapoint
dotnet build backend\BirraPoint.sln
dotnet test backend\tests\BirraPoint.Api.UnitTests
dotnet test backend\tests\BirraPoint.Api.IntegrationTests   # necesita Docker

# E2E — necesita el stack Aspire vivo en otra terminal
dotnet run --project backend\src\BirraPoint.AppHost
cd frontend && npm run e2e
```

---

## 7 · Deuda conocida, encontrada pero no causada por este trabajo

`us2-wizard.spec.ts`, `us3-import.spec.ts`, `us3-import-scale.spec.ts` y
`e2e/a11y/routes.a11y.spec.ts` manejan el asistente con etiquetas **en inglés** anteriores a la
traducción (`Next`, `Save Draft`, `Upload`, `Consolidate`, `getByLabel('Name')`, encabezado
`Import beer entries`) y una ruta `/import` que ya no existe. Llevan rotos desde que el asistente
se tradujo y se plegó en seis pasos.

`us4-judges` y `us14-judge-import` sí usan las etiquetas reales y están al día.

**Ahora que hay terminal, esto por fin se puede arreglar de verdad**: levanta el stack Aspire,
ejecuta los cuatro specs y corrígelos contra la UI real en vez de a ciegas. Es una tarea propia
(T128 o la numeración que toque), no la mezcles con T127.

---

## 8 · Workflow del repo — `CLAUDE.md` manda

Por cada tarea, sin saltarse pasos:

1. Rama `feature/<task-id>` desde `main`.
2. **Tollgate**: exponer ficheros exactos, enfoque y estrategia de test. **Parar** hasta que el
   usuario responda "Approved, proceed".
3. TDD: tests primero, verificados en rojo, luego implementación.
4. Commit semántico, push, PR contra `main` con `gh`.
5. Agente `senior-code-reviewer` sobre el diff del PR, hallazgos como comentario informativo.
6. Documentación en el mismo cambio: ADR en `Docs/adrs/` si hubo decisión técnica significativa,
   y `Docs/arquitectura_viva.md` refrescado. Toda la documentación nueva **en inglés**.

Entrada en `specs/001-birrapoint-mvp/tasks.md` al cerrar, con el mismo nivel de detalle que
T122–T125b.
