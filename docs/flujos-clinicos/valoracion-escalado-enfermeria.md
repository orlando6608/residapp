# Valoración de eventos clínicos y escalado de Enfermería

Enfermería recibe los eventos registrados por Auxiliar (y puede registrar los suyos propios), los valora, y decide entre cuatro desenlaces posibles: cerrar, hacer seguimiento, escalar a Medicina o activar el protocolo urgente.

## Alcance y exclusiones

Este flujo cubre la gestión de bandejas, la valoración y la decisión asistencial de Enfermería sobre un evento clínico. No cubre:

- La gestión del estado basal y la escala Barthel (ver [gestion-basal-barthel.md](gestion-basal-barthel.md)).
- El detalle del informe de derivación a Urgencias, que es un módulo común con Medicina (ver [derivacion-urgencias.md](derivacion-urgencias.md)).
- El proceso completo de redacción, aprobación y publicación de la comunicación familiar: aquí solo se documenta la decisión inicial de comunicar o no (ver `docs\historias-usuarios\portal-familiar.md` para el resto).

## Glosario mínimo

- **Evento**: cambio o hecho relevante sobre un residente, originado por Auxiliar, Enfermería o Medicina.
- **Valoración**: aportación profesional de Enfermería sobre un evento (hallazgos, actuaciones, constantes); nunca modifica la observación original.
- **Decisión asistencial**: elección explícita de uno de los cuatro desenlaces posibles de un evento.
- **Escalado**: envío de toda la información reunida sobre un evento a Medicina, sin resumen diagnóstico automático.
- **Indicación**: tarea que Medicina asigna a Enfermería sobre un evento; lectura y realización son hitos distintos.

## Flujo paso a paso

1. Enfermería abre su bandeja ordinaria o su bandeja prioritaria, compartidas por unidad, y selecciona un evento pendiente.
2. Abre el detalle del evento (observación original, basal vigente resumido, últimas actuaciones) y pulsa "empezar valoración". Esto registra qué profesional y en qué momento inicia la valoración, sin crear una propiedad permanente sobre el evento; si otro profesional lo edita a la vez, se exige recargar antes de continuar.
3. Completa su valoración: hallazgos, actuaciones, comunicaciones, resultado, y constantes opcionales (temperatura, presión arterial, frecuencia cardíaca, frecuencia respiratoria, saturación de oxígeno, aire ambiente u oxigenoterapia, flujo de oxígeno, glucemia, u otra constante con nombre/valor/unidad).
4. Decide el desenlace del evento, eligiendo una de estas cuatro salidas mutuamente excluyentes:
   - **a. Cerrar el evento**: revisa un resumen de la valoración y las actuaciones, y cierra de forma idempotente. Debe decidir si genera o no una comunicación familiar (ver paso 6). El evento pasa a Historial.
   - **b. Iniciar seguimiento**: indica una fecha prevista o un criterio, y el equipo responsable. El evento entra en una bandeja compartida de seguimientos, donde se registran actuaciones, se reprograma justificadamente o se resuelve; permanece abierto aunque venza. Al cambio de turno, puede transferirse explícitamente al equipo entrante, y el evento sigue visible aunque la recepción no se confirme.
   - **c. Escalar a Medicina**: se envía la observación original, el basal vigente, la valoración, las constantes y las actuaciones registradas, junto con el motivo del escalado. El sistema no genera ningún resumen diagnóstico ni decide automáticamente. El evento pasa a la bandeja de Medicina.
   - **d. Activar protocolo urgente**: se documenta la activación del protocolo, las actuaciones y la evolución, sin que esta documentación retrase la atención. Si procede, desde aquí se abre el módulo común de derivación a Urgencias (ver [derivacion-urgencias.md](derivacion-urgencias.md)).
5. En paralelo a la gestión de eventos, Enfermería recibe las indicaciones que Medicina le asigna: primero confirma su lectura, y más tarde registra si fue realizada o no realizada (con incidencia en ese caso). Lectura y realización son hitos distintos y ambos quedan visibles hasta su resolución.
6. Si al cerrar el evento decide que hay comunicación familiar, elige entre no comunicar o preparar una comunicación. Si prepara una comunicación, redacta un texto comprensible, que debe aprobarse humanamente antes de publicarse, y que se muestra siempre como "Equipo asistencial del centro" (el detalle completo de este proceso se documenta junto al Portal Familiar, no aquí). Una derivación a Urgencias genera siempre una actualización relevante y exige documentar el intento de llamada al contacto familiar designado, sin retrasar la atención.
7. Enfermería también puede registrar un evento que ella misma observa directamente, sin que eso altere el rol del Auxiliar; ese evento sigue el mismo ciclo descrito desde el paso 2.

## Estados del evento

| Estado | Significado | Quién puede provocar la transición |
| --- | --- | --- |
| Pendiente | Recibido en bandeja, sin valoración iniciada | Auxiliar (al registrar), Enfermería (evento propio) |
| En valoración | Un profesional ha empezado a valorarlo | Enfermería |
| En seguimiento | Tiene fecha o criterio y equipo responsable pendiente | Enfermería |
| Escalado a Medicina | Enviado con toda la información reunida | Enfermería |
| Protocolo urgente activo | En curso de protocolo urgente, posible derivación | Enfermería |
| Cerrado por Enfermería | Gestión finalizada, pasa a Historial | Enfermería |

## Reglas de negocio

- Las cuatro salidas de la decisión asistencial (cerrar, seguimiento, escalar, protocolo urgente) son mutuamente excluyentes en un mismo ciclo de valoración.
- Escalar a Medicina no cierra el evento: Medicina decide su propio desenlace (ver [valoracion-conducta-medicina.md](valoracion-conducta-medicina.md)).
- El escalado nunca incluye un resumen automático generado por el sistema; solo transmite la información reunida.
- Un seguimiento vencido permanece abierto y visible; el sistema nunca lo cierra ni lo oculta automáticamente.
- La comunicación familiar exige siempre una aprobación humana explícita antes de publicarse.
- La observación original de un evento no es editable una vez registrada.

## Trazabilidad

| Paso del flujo | Pantalla de referencia (wireframe Enfermería v0.3) |
| --- | --- |
| Bandejas de trabajo | ENF-01, ENF-02, ENF-03 |
| Detalle del evento y "empezar valoración" | ENF-04 |
| Completar valoración | ENF-05 |
| Decisión asistencial (4 salidas) | ENF-06 |
| Cierre | ENF-07A |
| Seguimiento y transferencia de turno | ENF-07B, ENF-08, ENF-09 |
| Escalado a Medicina | ENF-10 |
| Protocolo urgente | ENF-11 |
| Indicaciones de Medicina (lectura/realización) | ENF-13 |
| Decisión y redacción de comunicación familiar | ENF-14, ENF-15 |
| Evento observado directamente por Enfermería | ENF-16 |

## Nota de procedencia

Este flujo consolida los requisitos `ENF-01` a `ENF-16` del PRD v0.5 y el wireframe funcional de Enfermería v0.3, documentados durante la fase de diseño del producto previa a la migración hacia el monolito ASP.NET Core / SQL Server.
