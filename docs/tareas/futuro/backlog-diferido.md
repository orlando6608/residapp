# Backlog diferido — fuera del MVP

Estado al 2026-09-12.

## Qué es y qué no es este documento

Este documento reúne lo que ya está **explícitamente pospuesto o pendiente de decidir**, tal como lo fijan hoy [`docs/producto/alcance.md`](../../producto/alcance.md) (sección "Fuera de alcance") y [`docs/producto/roadmap.md`](../../producto/roadmap.md) (sección "Decisiones abiertas heredadas del legado"). No añade prioridad relativa entre estos puntos ni decide cuál abordar antes: es un punto de partida para cuando el Product Owner (CJ) decida traer alguno de vuelta al alcance, o para cuando el centro piloto obligue a resolver alguna de las decisiones pendientes.

No confundir con:
- [`docs/tareas/minimo-producto-viable/checklist-construccion-mvp.md`](../minimo-producto-viable/checklist-construccion-mvp.md) — lo que sí hay que construir.
- `docs/tareas/media-prioridad/` — de momento vacía; a diferencia de esta carpeta, no hay hoy ningún criterio de prioridad intermedia documentado por CJ entre el MVP y este backlog diferido, así que no se rellena por iniciativa propia.

## Funcionalidades explícitamente pospuestas

Del "Fuera de alcance" de `alcance.md`, sin cambios ni interpretación adicional:

- Sustituir la historia clínica oficial o el software integral del centro.
- Diagnóstico, prescripción, solicitud de pruebas, triaje o recomendaciones automáticas.
- Mensajería libre entre familia y profesionales.
- Contenido sanitario por email, SMS, WhatsApp o enlaces públicos.
- Notificaciones push en el piloto.
- App móvil nativa; la primera versión es web adaptable/PWA.
- Modo clínico offline con sincronización posterior.
- Administración de medicación, facturación, nóminas, fichaje o gestión laboral avanzada.
- Videollamada propia; solo puede ofrecerse como modalidad si el centro dispone del medio externo adecuado.
- Rankings nominativos de productividad o evaluaciones automáticas de mala praxis.
- Exportación masiva de historias clínicas individuales.
- Integraciones con historias clínicas externas en la primera versión.
- Flujos específicos de Centro de Día y SAAD antes de validar el modelo inicial en residencias.
- CFS y Pfeiffer dentro del estado basal.

## Decisiones y preparación para producción/piloto real

De "Decisiones abiertas heredadas del legado" en `roadmap.md` (mismo contenido que "Decisiones pendientes" en [`README.md`](../../../README.md)):

| Decisión | Estado |
| --- | --- |
| Autenticación y segundo factor productivos | Completamente abierta; no hay proveedor equivalente decidido en la arquitectura .NET. |
| SLA, RPO, RTO y copias de seguridad | Pendiente de definir antes de producción. |
| EIPD, contratos y seguridad | Pendiente de formalizar antes de tratar datos reales. |
| Configuración real de citas, equipos y tiempos | Pendiente de definir junto con cada centro piloto. |
| Criterios de actualización familiar relevante | Pendiente de acordar entre dirección clínica y centro. |
| Política de corrección de notas clínicas y su conservación | Pendiente de acordar entre centro y responsable de protección de datos. |

Ninguna de estas decisiones bloquea construir el MVP con datos ficticios, pero todas condicionan cualquier piloto real o despliegue productivo.

## Nota de procedencia

Este backlog no origina alcance nuevo: todo su contenido ya estaba escrito por CJ (o heredado sin cambios de su PRD legado) en `producto/alcance.md` y `producto/roadmap.md`. Si alguno de estos puntos se retoma, la decisión de traerlo de vuelta al alcance corresponde al Product Owner, no a este documento.
