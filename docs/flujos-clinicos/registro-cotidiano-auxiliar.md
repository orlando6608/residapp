# Registro cotidiano y eventos del Auxiliar

El Auxiliar cierra cada turno, para cada residente que tiene asignado, eligiendo una de tres acciones excluyentes entre sí: confirmar que no hay cambios, marcar el día como no valorable, o registrar un cambio observado.

## Alcance y exclusiones

Este flujo cubre únicamente el cierre cotidiano del Auxiliar y la clasificación inicial de un cambio como ordinario o prioritario. No cubre:

- La valoración clínica del evento una vez llega a Enfermería (ver [valoracion-escalado-enfermeria.md](valoracion-escalado-enfermeria.md)).
- La edición del estado basal (ver [gestion-basal-barthel.md](gestion-basal-barthel.md)): el Auxiliar solo consulta un resumen del basal vigente, nunca lo crea, aporta, firma ni consulta en versiones históricas.

## Glosario mínimo

- **Sin cambios**: cierre del día confirmando que la situación habitual del residente se mantiene.
- **No valorable**: cierre del día indicando que no fue posible valorar la situación, con motivo obligatorio.
- **Registrar cambio**: cierre del día documentando uno o varios cambios observados en una o más áreas.
- **Evento ordinario / prioritario**: clasificación inicial de un cambio registrado, según si coincide con una de las situaciones prioritarias predefinidas.

## Flujo paso a paso

1. El Auxiliar abre la lista de residentes que tiene asignados (nunca ve residentes fuera de su asignación) y consulta, para cada uno, un resumen del basal vigente sin poder editarlo.
2. Selecciona un residente y entra a su registro cotidiano, donde debe elegir exactamente una de las tres acciones siguientes.
3. **Rama A — Sin cambios**: confirma y firma el cierre. El resultado no genera ninguna tarea para Enfermería.
4. **Rama B — No valorable**: debe indicar un motivo obligatorio antes de poder cerrar. Este estado nunca se interpreta como estabilidad clínica del residente.
5. **Rama C — Registrar cambio**:
   1. Selecciona al menos una de las diez áreas de registro cotidiano (ver tabla siguiente); no puede guardar sin marcar ninguna.
   2. Completa el contenido de cada área elegida, usando las opciones rápidas previstas o texto libre según el área.
   3. Registra la temperatura si procede; es un campo opcional.
   4. La fecha y hora del registro se generan automáticamente al guardar; no se exige indicar la hora exacta en que se observó el cambio.
6. Si eligió Registrar cambio, clasifica el cambio como **ordinario** o **prioritario**. Si es prioritario, debe seleccionar el motivo de una lista cerrada (ver tabla siguiente).
7. **Si el cambio es ordinario**: revisa un resumen y confirma. El registro firmado pasa a la bandeja ordinaria de Enfermería, conservando la autoría del Auxiliar.
8. **Si el cambio es prioritario**: antes de confirmar, debe documentar el aviso directo previsto (recordatorio de aplicar el protocolo del centro). Al confirmar, el registro pasa a la bandeja prioritaria de Enfermería. Confirmar dos veces por error no crea dos registros duplicados.
9. Este mismo subflujo (pasos 5 a 8) puede iniciarse también desde la pantalla de inicio del Auxiliar, seleccionando cualquier residente asignado, sin que ello amplíe su ámbito de acceso.

## Las diez áreas de registro cotidiano

| Área |
| --- |
| Alimentación / hidratación |
| Movilidad / funcionalidad |
| Ánimo / conducta |
| Dolor / malestar |
| Heces / diuresis |
| Sueño |
| Lesiones en piel |
| Participación / relación social |
| Incidencias / caídas |
| Estado de conciencia |

## Motivos prioritarios de catálogo cerrado

| Motivo |
| --- |
| Alteración de conciencia o del estado general |
| Caída, lesión o traumatismo |
| Fiebre o sospecha de infección |
| Dolor nuevo o intenso |
| Dificultad respiratoria |
| Déficit neurológico nuevo |

Esta clasificación organiza la bandeja de trabajo de Enfermería; no constituye un diagnóstico ni sustituye el protocolo urgente del centro.

## Reglas de negocio

- Las tres acciones de cierre cotidiano (Sin cambios, No valorable, Registrar cambio) son mutuamente excluyentes: no pueden combinarse en un mismo cierre.
- No valorable no puede guardarse sin motivo.
- Registrar cambio no puede guardarse sin al menos un área marcada.
- Un evento prioritario exige documentar el aviso directo antes de confirmar.
- El Auxiliar solo consulta residentes asignados y solo el basal vigente resumido; no tiene acceso a versiones históricas, borradores, aportaciones, firma ni corrección del basal.
- La autoría, el perfil activo y la fecha/hora del registro siempre provienen del servidor, nunca de datos introducidos libremente.

## Trazabilidad

Los códigos de requisito (`AUX-01`... del PRD) y los códigos de pantalla del wireframe funcional (AUX-01... del propio documento de wireframes) son dos numeraciones independientes que comparten prefijo por coincidencia; no representan el mismo elemento.

| Paso del flujo | Códigos de requisito (PRD) | Pantalla de referencia (wireframe) |
| --- | --- | --- |
| Lista de residentes asignados y basal resumido | `AUX-01`, `AUX-13` | AUX-01, AUX-03 |
| Registro cotidiano del residente | `AUX-02` | AUX-02 |
| Sin cambios | `AUX-03` | AUX-04 |
| No valorable | `AUX-04` | AUX-05 |
| Registrar cambio: áreas, opciones rápidas | `AUX-05`, `AUX-06` | AUX-06, AUX-07 |
| Temperatura opcional y fecha/hora automática | `AUX-07` | AUX-08, AUX-09 |
| Clasificación ordinaria/prioritaria | `AUX-08`, `AUX-09` | AUX-10 |
| Confirmación ordinaria a bandeja de Enfermería | `AUX-11` | AUX-11A |
| Aviso directo y confirmación de evento prioritario | `AUX-10`, `AUX-11` | AUX-11B, AUX-12 |
| Reutilización desde Inicio | `AUX-13` | AUX-13 |

Punto de validación de usabilidad pendiente (no bloquea el flujo): el campo "Desde cuándo" en estado de conciencia, y la carga de persona/canal/hora del aviso directo (`AUX-12` del PRD).

## Nota de procedencia

Este flujo consolida los requisitos `AUX-01` a `AUX-13` del PRD v0.5 y el wireframe funcional de Auxiliar v0.2, documentados durante la fase de diseño del producto previa a la migración hacia el monolito ASP.NET Core / SQL Server.
