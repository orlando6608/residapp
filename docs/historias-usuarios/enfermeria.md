# Historias de usuario — Enfermería

Enfermería gestiona bandejas de trabajo clínico, valora eventos, decide su desenlace, escala a Medicina, deriva a Urgencias, gestiona el estado basal cuando tiene permiso, y decide la comunicación familiar al cerrar un evento.

## Cómo leer estas historias

Cada historia sigue el formato "Como Enfermería, quiero..., para...", agrupa varios requisitos del PRD con sentido de negocio común, e incluye criterios de aceptación verificables en lenguaje llano. Para el detalle operativo paso a paso, ver `docs\flujos-clinicos\valoracion-escalado-enfermeria.md`, `docs\flujos-clinicos\derivacion-urgencias.md` y `docs\flujos-clinicos\gestion-basal-barthel.md`.

## Historias de usuario

### 1. Gestionar mis bandejas de trabajo

**Como** Enfermería, **quiero** ver mis bandejas de prioritarios, ordinarios, seguimientos, indicaciones y comunicaciones pendientes, compartidas por unidad, **para** organizar mi trabajo sin perder ningún caso.

Criterios de aceptación:
- Las bandejas están organizadas por unidad y son compartidas por todo el equipo de Enfermería de esa unidad.
- Un evento cerrado sale de las bandejas y pasa a Historial.
- Un evento abierto y vencido sigue visible; nunca desaparece por sí solo.

Códigos de origen: `ENF-01`, `ENF-02`.

### 2. Valorar un evento clínico

**Como** Enfermería, **quiero** empezar y completar la valoración de un evento recibido, **para** documentar mi aportación profesional sin alterar la observación original.

Criterios de aceptación:
- Al empezar a valorar, se registra qué profesional y en qué momento lo hace, sin que eso me otorgue propiedad permanente sobre el evento.
- Si otro profesional edita el mismo evento a la vez, se me exige recargar antes de continuar.
- La observación original nunca se modifica por mi valoración; se conserva junto a mis hallazgos, actuaciones y constantes.
- Las constantes son opcionales y admiten una constante adicional con nombre, valor y unidad.

Códigos de origen: `ENF-03`, `ENF-04`, `ENF-05`.

### 3. Cerrar un evento y decidir la comunicación familiar

**Como** Enfermería, **quiero** cerrar un evento y decidir si genero una comunicación a la familia, **para** finalizar mi gestión con la información adecuada llegando a quien corresponda.

Criterios de aceptación:
- Cerrar es una de cuatro salidas mutuamente excluyentes de la decisión asistencial.
- El cierre es idempotente: repetirlo no genera dos cierres.
- Al cerrar, debo decidir explícitamente entre no comunicar o preparar una comunicación familiar.
- Si preparo una comunicación, esta requiere aprobación humana y se muestra siempre como "Equipo asistencial del centro".

Códigos de origen: `ENF-06`, `ENF-12`.

### 4. Iniciar y transferir un seguimiento

**Como** Enfermería, **quiero** iniciar un seguimiento con fecha o criterio y equipo responsable, y transferirlo explícitamente al cambio de turno, **para** que ningún caso pendiente se pierda entre turnos.

Criterios de aceptación:
- No puedo crear un seguimiento sin indicar una fecha prevista o un criterio.
- Cada actuación sobre el seguimiento conserva la autoría individual de quien la realizó.
- La transferencia entre turnos es una acción explícita; el evento sigue visible aunque la recepción no se confirme.
- Un seguimiento vencido permanece abierto y visible, nunca se cierra automáticamente.

Códigos de origen: `ENF-06`, `ENF-07`, `ENF-08`.

### 5. Escalar un evento a Medicina

**Como** Enfermería, **quiero** escalar un evento a Medicina con toda la información reunida, **para** que Medicina decida sin tener que repetir la valoración desde cero.

Criterios de aceptación:
- El escalado transmite observación, basal vigente, valoración, constantes, actuaciones y el motivo del escalado.
- El sistema no genera ningún resumen diagnóstico automático al escalar.
- Escalar no cierra el evento por mi parte; el desenlace queda en manos de Medicina.

Códigos de origen: `ENF-06`, `ENF-09`.

### 6. Activar protocolo urgente y derivar a Urgencias

**Como** Enfermería, **quiero** activar el protocolo urgente y, si procede, derivar al residente a Urgencias, **para** documentar la atención urgente sin retrasarla.

Criterios de aceptación:
- Documentar el protocolo urgente no retrasa la atención al residente.
- Si deriva, el informe se genera con vista previa obligatoria, permite editar el texto sin alterar los registros de origen, y se firma antes de generar el PDF.
- El informe nunca incluye la escala CFS ni un bloque externo de contactos.
- Toda derivación genera una actualización relevante para la familia y exige documentar el intento de llamada al contacto designado.

Códigos de origen: `ENF-06`, `DER-01` a `DER-06` (ver `docs\flujos-clinicos\derivacion-urgencias.md`).

### 7. Gestionar indicaciones recibidas de Medicina

**Como** Enfermería, **quiero** confirmar la lectura de una indicación médica y registrar después si la realicé, **para** que quede claro el estado real de cada tarea encomendada.

Criterios de aceptación:
- Confirmar la lectura de una indicación no la marca automáticamente como realizada.
- Puedo registrar una indicación como realizada o como no realizada con incidencia.
- Ambos hitos (lectura y realización) permanecen visibles hasta su resolución; no caducan silenciosamente.

Códigos de origen: `ENF-10`.

### 8. Registrar un evento que yo mismo observo

**Como** Enfermería, **quiero** registrar directamente un evento que yo observo, **para** documentarlo sin necesidad de que pase primero por el Auxiliar.

Criterios de aceptación:
- Registrar un evento propio no altera ni sustituye el rol del Auxiliar en otros casos.
- El evento sigue el mismo ciclo de valoración y decisión que uno recibido de Auxiliar.

Códigos de origen: `ENF-11`.

### 9. Iniciar el estado basal o una reevaluación

**Como** Enfermería, **quiero** crear un borrador de basal o iniciar una reevaluación cuando tengo permiso, **para** mantener actualizada la referencia habitual del residente.

Criterios de aceptación:
- Solo puedo crear o reevaluar el basal si tengo el permiso correspondiente y el residente está dentro de mi ámbito.
- Solo puede existir un borrador activo por residente a la vez.
- Otro profesional autorizado puede aportar al borrador sin convertirse en su autor.
- El permiso para gestionar el basal es distinto del permiso para crear la identidad administrativa de un residente.

Códigos de origen: `ENF-13` (ver `docs\flujos-clinicos\gestion-basal-barthel.md` para el detalle completo, requisitos `BAS-*`).

### 10. Completar las nueve áreas y firmar la versión vigente del basal

**Como** Enfermería, **quiero** completar las nueve áreas del basal y la escala Barthel, y firmar la versión resultante, **para** dejar constancia formal del estado habitual del residente.

Criterios de aceptación:
- Las nueve áreas deben estar respondidas para poder firmar.
- La escala Barthel calcula el total automáticamente a partir de los diez ítems.
- Solo puedo firmar un borrador que yo mismo haya creado, con mi mismo perfil profesional.
- Al firmar, la versión anterior pasa a histórica y la nueva a vigente, en una sola operación atómica.
- Un basal firmado es inmutable; un error se corrige con una nueva versión de rectificación, nunca editando la firmada.

Códigos de origen: `ENF-13` (ver `docs\flujos-clinicos\gestion-basal-barthel.md`, requisitos `BAS-*`).

### 11. Consultar el historial de eventos y del basal

**Como** Enfermería, **quiero** consultar el historial de eventos cerrados y las versiones anteriores del basal, **para** entender la evolución del residente cuando lo necesito.

Criterios de aceptación:
- Puedo consultar eventos cerrados y su línea temporal, respetando los permisos de mi ámbito.
- Puedo consultar versiones históricas del basal, no solo la vigente.
- Ningún elemento del historial permite editar los registros originales.

Códigos de origen: `HIS-01`, `HIS-02`, `HIS-03`.

## Tabla resumen de trazabilidad

| Historia | Códigos cubiertos |
| --- | --- |
| 1. Bandejas de trabajo | `ENF-01`, `ENF-02` |
| 2. Valorar evento | `ENF-03`, `ENF-04`, `ENF-05` |
| 3. Cerrar y decidir comunicación | `ENF-06`, `ENF-12` |
| 4. Seguimiento y transferencia | `ENF-06`, `ENF-07`, `ENF-08` |
| 5. Escalar a Medicina | `ENF-06`, `ENF-09` |
| 6. Protocolo urgente y derivación | `ENF-06`, `DER-01` a `DER-06` |
| 7. Indicaciones de Medicina | `ENF-10` |
| 8. Evento propio | `ENF-11` |
| 9. Iniciar basal/reevaluación | `ENF-13` |
| 10. Completar y firmar basal | `ENF-13` |
| 11. Consultar historial | `HIS-01`, `HIS-02`, `HIS-03` |

## Nota de procedencia

Estas historias agrupan los requisitos `ENF-01` a `ENF-13`, `DER-01` a `DER-06` e `HIS-01` a `HIS-03` del PRD v0.5, documentados durante la fase de diseño del producto previa a la migración hacia el monolito ASP.NET Core / SQL Server.
