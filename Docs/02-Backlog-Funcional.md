---
titulo: BirraPoint - Backlog Funcional MVP
estado: Refinado
tags: [birrapoint, producto, backlog, funcional, MVP]
---

# BirraPoint - Backlog Funcional (Epics, Features y User Stories)

Este documento detalla la funcionalidad de la aplicación organizada jerárquicamente para facilitar la planificación de Sprints y la lectura por parte de agentes de IA o equipos de desarrollo.

## Epic 1: Identidad y Seguridad
**Objetivo:** Garantizar un acceso seguro y unificado delegando la identidad a Keycloak.

* **Feature 1.1: Acceso a la Plataforma**
    * **US-1.1.1 [Login]:** Como usuario de BirraPoint, quiero iniciar sesión a través del portal de Keycloak para acceder a mi panel correspondiente (Organizador o Juez) según mi rol.
    * **US-1.1.2 [Forzado de Contraseña]:** Como Juez que accede por primera vez con credenciales generadas por el sistema, quiero ser forzado por Keycloak a cambiar mi contraseña antes de ver ningún dato del concurso para garantizar mi privacidad.

---

## Epic 2: Orquestación del Concurso
**Objetivo:** Proporcionar al organizador las herramientas para configurar el evento, participantes y distribución de trabajo.

* **Feature 2.1: Creación de Concursos**
    * **US-2.1.1 [Wizard de Creación]:** Como Organizador, quiero un asistente visual paso a paso para dar de alta los datos base del concurso, pudiendo guardarlo como borrador si no lo termino en el momento.
* **Feature 2.2: Gestión de Cervezas y Jueces**
    * **US-2.2.1 [Importación Excel]:** Como Organizador, quiero subir un archivo `.xlsx` con las cervezas inscritas para cargarlas masivamente. El sistema debe validar que los estilos introducidos existen en el catálogo maestro oficial precargado.
    * **US-2.2.2 [Registro de Jueces]:** Como Organizador, quiero introducir una lista de correos electrónicos para que el sistema genere sus perfiles pasivos y envíe las invitaciones automáticamente.
* **Feature 2.3: Configuración de Mesas**
    * **US-2.3.1 [Asignación]:** Como Organizador, quiero crear mesas de cata y asignar a cada una un grupo de jueces y un listado de cervezas específicas, asegurando que el flujo de trabajo quede repartido.

---

## Epic 3: Dinámica de Evaluación (Modo Juez)
**Objetivo:** Proveer una experiencia de cata digital impecable, tolerante a fallos de red y que garantice el anonimato.

* **Feature 3.1: Panel y Orquestación de la Mesa**
    * **US-3.1.1 [Cata a Ciegas]:** Como Juez, quiero ver mi listado de cervezas a catar identificadas exclusivamente por un código ciego y su estilo, sin ver jamás información del cervecero original.
    * **US-3.1.2 [Fijar Orden de Mesa]:** Como Juez de una mesa, quiero reordenar la secuencia de mis cervezas (*drag & drop*) y pulsar "Fijar Orden" para que todos mis compañeros evalúen en esa misma secuencia matemática de forma obligatoria.
* **Feature 3.2: Hoja de Evaluación y Offline**
    * **US-3.2.1 [Formulario Validado]:** Como Juez, quiero rellenar una hoja de evaluación dividida en categorías (Aroma, Apariencia, Sabor, etc.) que valide que no supero los puntos máximos de cada sección y autocalcule la nota total.
    * **US-3.2.2 [Motor Offline-First]:** Como Juez sin conexión a internet, quiero que mis hojas de cata se guarden localmente en el dispositivo. Cuando el dispositivo detecte red, el sistema debe enviar los datos al servidor de forma silenciosa.
    * **US-3.2.3 [Cierre de Mesa]:** Como Juez, quiero poder pulsar "Cerrar Mesa" cuando todas las evaluaciones estén completas, bloqueando permanentemente la edición de las notas para todos los integrantes.

---

## Epic 4: Monitorización y Cierre del Evento
**Objetivo:** Permitir al organizador seguir el evento en vivo y gestionar la entrega de resultados al finalizar.

* **Feature 4.1: Panel de Control en Vivo**
    * **US-4.1.1 [Dashboard SignalR]:** Como Organizador, quiero ver un panel que se actualice instantáneamente (sin recargar la página) cada vez que un juez complete una evaluación, mostrando el progreso de cada mesa.
    * **US-4.1.2 [Auditoría]:** Como Organizador, quiero poder hacer clic en una cerveza ya evaluada en el panel para leer en modo lectura las notas que han dejado los jueces.
* **Feature 4.2: Exportación y Comunicación**
    * **US-4.2.1 [Exportación ZIP]:** Como Organizador, quiero descargar un archivo `.zip` que contenga todas las hojas de cata generadas en formato PDF, estructuradas por participante.
    * **US-4.2.2 [Envío Automatizado]:** Como Organizador, al dar por finalizado el concurso, quiero que el sistema despache un correo electrónico automatizado a cada cervecero adjuntando sus hojas de evaluación en PDF.