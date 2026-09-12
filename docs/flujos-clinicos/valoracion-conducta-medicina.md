# Valoración médica, indicaciones y cierre clínico de Medicina

Medicina recibe los eventos escalados por Enfermería (y puede iniciar eventos propios), realiza la valoración médica, y decide su conducta: indicar a Enfermería, cerrar, hacer seguimiento o activar el protocolo urgente.

## Alcance y exclusiones

Este flujo cubre la gestión de escalados, la valoración médica y la decisión de conducta de Medicina sobre un evento clínico. No cubre:

- La gestión del estado basal y la escala Barthel (ver [gestion-basal-barthel.md](gestion-basal-barthel.md)).
- El detalle del informe de derivación a Urgencias, que es un módulo común con Enfermería (ver [derivacion-urgencias.md](derivacion-urgencias.md)).

## Glosario mínimo

- **Escalado recibido**: evento que Enfermería envía a Medicina con toda su información reunida.
- **Conducta médica**: decisión explícita entre las cuatro salidas posibles de un evento en manos de Medicina.
- **Indicación**: tarea que Medicina asigna a Enfermería; su lectura y su realización son hitos distintos.
- **Continuidad entre médicos**: decisión de transferir un seguimiento al equipo entrante o conservarlo para la próxima revisión propia.

## Flujo paso a paso

1. Medicina abre su bandeja de escalados y revisa residente, unidad, motivo, constantes, actuaciones y antigüedad de cada uno; la bandeja no ofrece ningún resumen diagnóstico automático.
2. Abre el detalle del escalado: observación original y valoración de Enfermería (ambas de solo lectura), constantes, actuaciones y basal vigente; la línea temporal completa solo se despliega bajo demanda y con autorización.
3. Pulsa iniciar valoración médica, lo que registra qué profesional y en qué momento la inicia (con aviso de concurrencia si otro profesional edita a la vez, sin que ello cree una propiedad permanente sobre el evento).
4. Completa su valoración: hallazgos y exploración, valoración propiamente dicha, constantes opcionales, y actuaciones. La documentación previa de Enfermería no se modifica.
5. Decide la conducta médica, eligiendo una de estas cuatro salidas mutuamente excluyentes:
   - **a. Registrar indicaciones a Enfermería**: texto de la indicación, fecha prevista o criterio, e información adicional, sin selector automático de prioridad. Enfermería confirma la lectura y, después, registra si la indicación fue realizada o no realizada (con incidencia en ese caso); ambos hitos permanecen visibles hasta su resolución, sin caducar silenciosamente.
   - **b. Cerrar el evento**: confirma el cierre de forma idempotente. El evento pasa a Historial y **no exige un segundo cierre por parte de Enfermería**. Debe decidir si genera o no una comunicación familiar, con el mismo mecanismo de aprobación humana que en Enfermería.
   - **c. Iniciar seguimiento médico**: indica una fecha prevista o un criterio, el equipo responsable y el objetivo del seguimiento; si un resultado es necesario para decidir el desenlace, el evento permanece en seguimiento (el sistema nunca decide qué resultado es necesario). Al finalizar el turno, Medicina decide explícitamente entre transferir el seguimiento al equipo entrante o conservarlo para su propia próxima revisión; un seguimiento vencido nunca caduca ni desaparece por sí solo.
   - **d. Activar protocolo urgente**: documenta el protocolo y la evolución sin retrasar la atención. Si procede, desde aquí se abre el módulo común de derivación a Urgencias (ver [derivacion-urgencias.md](derivacion-urgencias.md)).
6. Como alternativa a recibir un escalado, Medicina puede registrar un evento propio directamente, sin que este se reenvíe artificialmente a Enfermería; sigue el mismo ciclo descrito desde el paso 4.

## Estados del evento (desde la perspectiva de Medicina)

| Estado | Significado | Quién puede provocar la transición |
| --- | --- | --- |
| Pendiente médica | Escalado recibido o evento propio sin valorar | Enfermería (escalado), Medicina (evento propio) |
| En valoración | Un profesional médico ha empezado a valorarlo | Medicina |
| Con indicación pendiente | Indicación registrada, aún no leída o no realizada | Medicina |
| En seguimiento médico | Tiene fecha o criterio y equipo responsable pendiente | Medicina |
| Protocolo urgente activo | En curso de protocolo urgente, posible derivación | Medicina |
| Cerrado por Medicina | Gestión finalizada, pasa a Historial, sin cierre adicional de Enfermería | Medicina |

## Reglas de negocio

- Las cuatro salidas de la conducta médica (indicaciones, cierre, seguimiento, protocolo urgente) son mutuamente excluyentes en un mismo ciclo de valoración.
- Si Medicina cierra el evento, Enfermería no realiza un segundo cierre sobre el mismo evento.
- La continuidad entre médicos al cambio de turno exige una decisión explícita: transferir o conservar; nunca queda implícita.
- Un seguimiento vencido, o una indicación no leída o con incidencia, permanecen visibles hasta su resolución.
- Un evento propio de Medicina sigue exactamente el mismo ciclo de decisión que uno recibido por escalado.
- La observación original y la valoración previa de Enfermería nunca se modifican desde Medicina.

## Trazabilidad

| Paso del flujo | Pantalla de referencia (wireframe Medicina v0.3) |
| --- | --- |
| Bandeja de escalados | MED-01, MED-02 |
| Detalle del escalado, fuentes de solo lectura | MED-03 |
| Inicio de valoración médica | MED-04 |
| Valoración médica | MED-05 |
| Conducta médica (4 salidas) | MED-06 |
| Indicaciones a Enfermería y su seguimiento | MED-07, MED-08, MED-09 |
| Seguimiento médico y continuidad entre médicos | MED-10, MED-11, MED-12 |
| Protocolo urgente | MED-13 |
| Cierre sin segundo cierre de Enfermería | MED-15 |
| Decisión y redacción de comunicación familiar | MED-16, MED-17 |
| Evento iniciado directamente por Medicina | MED-18 |

## Nota de procedencia

Este flujo consolida los requisitos `MED-01` a `MED-18` del PRD v0.5 y el wireframe funcional de Medicina v0.3, documentados durante la fase de diseño del producto previa a la migración hacia el monolito ASP.NET Core / SQL Server.
