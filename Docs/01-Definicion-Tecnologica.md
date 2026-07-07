---
titulo: BirraPoint - Definición Tecnológica (Stack y Arquitectura)
estado: MVP
stack: Angular, .NET 10, SignalR, Keycloak, PostgreSQL
arquitectura: Vertical Slices, Feature-Sliced Design
tags: [birrapoint, arquitectura, .NET, angular, keycloak]
---

# BirraPoint - Definición Tecnológica (Stack y Arquitectura)

**Fase:** MVP (Producto Mínimo Viable)
**Descripción:** Plataforma PWA para la gestión integral de competiciones de cerveza y catas a ciegas, con soporte offline y monitorización en tiempo real.

## 1. Frontend (Aplicación Cliente)
* **Framework:** **Angular 17+**. Uso de *Standalone Components* (sin `NgModules` para mayor ligereza) y **Signals** para una reactividad síncrona, moderna y de alto rendimiento.
* **Arquitectura:** Feature-Sliced Design. División por módulos lógicos de negocio (Features) en lugar de por tipos de archivo.
* **PWA y Modo Offline:** Implementación nativa con `@angular/pwa` para Service Workers (`ngsw-worker.js`).
* **Almacenamiento Local:** **Dexie.js** (IndexedDB) inyectado como un servicio de Angular para almacenar temporalmente las hojas de cata cuando no hay conexión.
* **Estilos y UI:** **Tailwind CSS** para un diseño *mobile-first* ágil. Librerías complementarias como `@angular/cdk/drag-drop` para la ordenación de las mesas de cata.

## 2. Backend (API y Lógica de Negocio)
* **Framework:** **.NET 10 LTS (ASP.NET Core Minimal APIs)**. APIs ligeras, rápidas y sin la verbosidad de los controladores clásicos.
* **Arquitectura:** **Vertical Slice Architecture** gestionada con **MediatR**. Cada funcionalidad (ej. `CrearMesa`, `EnviarCata`) es un bloque aislado con sus propios DTOs, validaciones y lógica.
* **Validación:** **FluentValidation** integrado en el *pipeline* de MediatR para garantizar la integridad de los datos antes de tocar la base de datos.
* **Tiempo Real:** **SignalR**. Hub dedicado (`CompetitionHub`) para emitir eventos de progreso desde las mesas de los jueces hacia el dashboard del organizador.

## 3. Identidad y Seguridad (IdP)
* **Plataforma:** **Keycloak**.
* **Protocolo:** OpenID Connect (OIDC) / OAuth 2.0 (Flujo *Authorization Code with PKCE*).
* **Gestión:** Keycloak se encarga de las pantallas de login, forzado de cambio de contraseñas temporales y generación de JWT. El backend en .NET se limita a verificar la firma y autorizar mediante Claims (Roles: `ORGANIZER`, `JUDGE`).

## 4. Persistencia de Datos
* **Base de Datos:** **PostgreSQL**.
* **ORM:** **Entity Framework Core (EF Core)** con el proveedor `Npgsql`. Se utilizarán migraciones *Code-First* para mantener sincronizado el esquema relacional.

## 5. Infraestructura y Despliegue
* **Contenerización:** Imágenes Docker multi-stage para todos los componentes (backend: imagen SDK de .NET para build/publish → runtime ASP.NET; frontend: build con Node.js → Nginx Alpine sirviendo los estáticos compilados). Las imágenes nunca contienen secretos ni configuración de entorno.
* **Orquestación Local:** **.NET Aspire** — proyecto `AppHost` centralizado que levanta PostgreSQL, Keycloak, el backend y el frontend con un solo comando, y proyecto `ServiceDefaults` que inyecta OpenTelemetry, health checks y resiliencia estándar.
* **Infraestructura en la Nube (IaC):** **Bicep** integrado con Azure Developer CLI: `azd up` construye las imágenes, las publica en el registro y despliega en un único comando.
* **Hosting (Azure Container Apps):**
    * *Registro:* Azure Container Registry (ACR).
    * *Frontend:* Container App (imagen Angular/Nginx) con ingress público.
    * *Backend:* Container App (imagen .NET) con ingress.
    * *Identidad:* Keycloak como Container App en el mismo entorno ACA.
    * *Base de Datos:* PostgreSQL como contenedor en el entorno ACA, con almacenamiento persistente, backup/export programado y procedimiento de restauración documentado.