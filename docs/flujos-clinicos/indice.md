# Índice de flujos clínicos

Esta carpeta documenta, paso a paso, los procesos asistenciales de ResidApp: cómo un profesional observa, valora, decide y cierra una situación de un residente, y cómo se gestiona el estado basal que sirve de referencia a todo lo anterior.

Es complementaria a otras dos carpetas de `docs\`:
- `docs\historias-usuarios\` da la perspectiva de negocio (qué quiere lograr cada perfil y cómo se verifica), agrupando los mismos requisitos en historias de usuario.
- `docs\producto\` da la visión general del producto, sus objetivos y su alcance, sin entrar en el detalle operativo de cada pantalla.

## Mapa del circuito clínico completo

El circuito funcional completo del producto es:

**Alta administrativa → basal profesional → registro cotidiano o evento → revisión de Enfermería → seguimiento o escalado → valoración médica e indicaciones → derivación si procede → cierre → texto familiar → aprobación → publicación → consulta familiar.**

Los ficheros de esta carpeta cubren los tramos asistenciales de ese circuito (desde el basal profesional hasta el cierre y la decisión de comunicar); el alta administrativa, la aprobación/publicación del texto familiar y la consulta familiar se documentan como historias de usuario en `docs\historias-usuarios\administracion.md` y `docs\historias-usuarios\portal-familiar.md`.

## Tabla de ficheros

| Fichero | Perfil(es) | Códigos PRD cubiertos | Qué NO cubre |
| --- | --- | --- | --- |
| `registro-cotidiano-auxiliar.md` | Auxiliar | `AUX-01` a `AUX-13` | La valoración clínica del evento (ver `valoracion-escalado-enfermeria.md`); la edición del basal, que el Auxiliar solo consulta resumido |
| `valoracion-escalado-enfermeria.md` | Enfermería | `ENF-01` a `ENF-16` | La gestión del basal (ver `gestion-basal-barthel.md`); el detalle del informe de derivación (ver `derivacion-urgencias.md`); la redacción/aprobación completa de la publicación familiar (ver `docs\historias-usuarios\portal-familiar.md`) |
| `valoracion-conducta-medicina.md` | Medicina | `MED-01` a `MED-18` | La gestión del basal (ver `gestion-basal-barthel.md`); el detalle del informe de derivación (ver `derivacion-urgencias.md`) |
| `derivacion-urgencias.md` | Enfermería y Medicina (módulo común) | `DER-01` a `DER-06` | Cómo se llega hasta el protocolo urgente (ver los dos ficheros anteriores) |
| `gestion-basal-barthel.md` | Enfermería y Medicina (módulo común); Auxiliar y Dirección como lectores limitados | `BAS-01` a `BAS-19` | El alta administrativa que deja el basal pendiente (ver `docs\historias-usuarios\administracion.md`); el registro cotidiano del Auxiliar (ver `registro-cotidiano-auxiliar.md`) |
| `supervision-clinica-direccion.md` | Dirección/Coordinación Clínica | `DIR-01` a `DIR-16` | La parte administrativa de Dirección —ámbito de supervisión, acceso denegado— (ver `docs\historias-usuarios\direccion-coordinacion-clinica.md`) |

## Glosario transversal

| Término | Definición corta |
| --- | --- |
| Bandeja (ordinaria / prioritaria) | Lista de trabajo pendiente de un perfil, agrupada por urgencia declarada, no por diagnóstico |
| Evento clínico | Cambio o hecho relevante registrado por Auxiliar, Enfermería o Medicina sobre un residente |
| Valoración | Aportación profesional (hallazgos, actuaciones, constantes) sobre un evento; nunca modifica la observación original |
| Seguimiento | Trabajo pendiente con fecha o criterio y equipo responsable; permanece abierto aunque venza |
| Escalado | Envío de un evento de Enfermería a Medicina, con toda la información reunida, sin resumen diagnóstico automático |
| Indicación | Tarea que Medicina asigna a Enfermería; su lectura y su realización son hitos distintos |
| Cierre | Fin de la gestión de un evento por parte de quien lo cierra; puede exigir decidir si hay comunicación familiar |
| Derivación a Urgencias | Informe firmado y en PDF que documenta el envío de un residente a un servicio de urgencias externo |
| Basal | Estado habitual de referencia del residente, en 9 áreas más la escala Barthel; existe en versión vigente y versiones históricas |
| Barthel | Escala funcional común de 10 ítems, con puntuación y total automático, vinculada a una versión del basal |
| Borrador / versión vigente / versión histórica | Ciclo de vida del basal: un borrador se completa y firma, se convierte en la versión vigente, y la anterior pasa a histórica |

## Perfiles y su alcance clínico

| Perfil | Qué puede hacer en estos flujos | Qué tiene prohibido explícitamente |
| --- | --- | --- |
| Auxiliar | Cerrar el registro cotidiano; registrar y clasificar cambios; consultar el basal vigente resumido | Valorar clínicamente, modificar el basal, escalar a Medicina, aprobar publicaciones |
| Enfermería | Valorar eventos, decidir su desenlace, escalar, derivar, gestionar el basal, redactar y aprobar comunicación familiar | Editar observaciones ajenas, firmar un borrador de basal que no creó |
| Medicina | Valorar eventos escalados o propios, indicar a Enfermería, hacer seguimiento, derivar, cerrar, gestionar el basal | Obtener permiso de alta administrativa solo por poder valorar el basal, editar un basal firmado |
| Dirección/Coordinación Clínica | Supervisar de forma agregada; acceder a detalle clínico solo con permiso, finalidad y auditoría previa, siempre en solo lectura | Valorar, indicar, ejecutar, escalar, corregir o cerrar desde este perfil |

## Nota de procedencia

El contenido de esta carpeta adapta el PRD del prototipo legado (`docs\legado-cloudflare\docs\product\2026-09-06-PRD-plataforma-contacto-familias-v0.5-consolidado.md`) y los wireframes funcionales vigentes de cada perfil, elaborados durante la fase de diseño previa a la migración hacia el monolito ASP.NET Core / SQL Server (ver `docs\decisiones-arquitectura\instrucciones-migracion-net10.md`).
