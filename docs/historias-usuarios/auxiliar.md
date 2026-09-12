# Historias de usuario — Auxiliar

El Auxiliar es responsable del cierre cotidiano de cada residente que tiene asignado y del aviso directo ante situaciones prioritarias. No diagnostica, no valora clínicamente, no modifica el basal, no escala a Medicina y no aprueba publicaciones familiares.

## Cómo leer estas historias

Cada historia sigue el formato "Como Auxiliar, quiero..., para...", agrupa varios requisitos del PRD con sentido de negocio común, e incluye criterios de aceptación verificables en lenguaje llano. Para el detalle operativo paso a paso de estos flujos, ver `docs\flujos-clinicos\registro-cotidiano-auxiliar.md`.

## Historias de usuario

### 1. Ver mis residentes asignados y su estado basal resumido

**Como** Auxiliar, **quiero** ver únicamente los residentes que tengo asignados junto con un resumen de su basal vigente, **para** saber cómo cuidarlos sin acceder a información que no me corresponde.

Criterios de aceptación:
- La lista solo muestra residentes con asignación vigente para mi cuenta.
- El resumen del basal no incluye ninguna acción de edición, ni versiones históricas, ni detalle completo de Barthel.
- Intentar acceder a un residente no asignado se rechaza, sin revelar si el residente existe.

Códigos de origen: `AUX-01`, `AUX-13`.

### 2. Cerrar el turno confirmando que no hay cambios

**Como** Auxiliar, **quiero** confirmar que la situación habitual de un residente se mantiene, **para** cerrar el día sin generar trabajo innecesario a Enfermería.

Criterios de aceptación:
- Confirmar y firmar registra mi autoría y la fecha/hora automáticamente.
- El cierre no genera ninguna tarea en la bandeja de Enfermería.
- Puedo repetir esta acción cada turno sin que quede duplicada por doble pulsación.

Códigos de origen: `AUX-02`, `AUX-03`.

### 3. Marcar un día como no valorable

**Como** Auxiliar, **quiero** indicar que no he podido valorar a un residente en un turno, **para** dejar constancia sin fingir una observación que no hice.

Criterios de aceptación:
- No puedo guardar esta opción sin indicar un motivo.
- El sistema nunca interpreta "no valorable" como estabilidad clínica del residente.
- Queda registrado quién y cuándo marcó el día como no valorable.

Códigos de origen: `AUX-02`, `AUX-04`.

### 4. Registrar un cambio observado en una o más áreas

**Como** Auxiliar, **quiero** registrar los cambios que observo en el día a día de un residente, **para** que Enfermería reciba la información necesaria para valorarlos.

Criterios de aceptación:
- Debo seleccionar al menos una de las diez áreas de registro cotidiano antes de poder guardar.
- Cada área admite opciones rápidas predefinidas y, cuando corresponda, texto adicional.
- La temperatura es un campo opcional; puedo guardar el registro sin completarla.
- La fecha y hora del registro se generan automáticamente; no tengo que indicar la hora exacta en que observé el cambio.

Códigos de origen: `AUX-02`, `AUX-05`, `AUX-06`, `AUX-07`.

### 5. Clasificar y notificar un cambio como ordinario o prioritario

**Como** Auxiliar, **quiero** clasificar cada cambio registrado como ordinario o prioritario, y documentar el aviso directo cuando sea prioritario, **para** que Enfermería lo atienda con la urgencia adecuada.

Criterios de aceptación:
- Si marco un cambio como prioritario, debo elegir el motivo de una lista cerrada de situaciones predefinidas.
- Un cambio prioritario exige documentar el aviso directo antes de poder confirmarlo.
- Un cambio ordinario, tras confirmarse, llega a la bandeja ordinaria de Enfermería; uno prioritario, a la bandeja prioritaria.
- Mi autoría se conserva en el registro que recibe Enfermería.

Códigos de origen: `AUX-08`, `AUX-09`, `AUX-10`, `AUX-11`.

### 6. Reanudar el registro desde la pantalla de inicio

**Como** Auxiliar, **quiero** poder iniciar el registro de un cambio o evento directamente desde la pantalla de inicio, **para** no tener que navegar por pasos innecesarios cuando ya sé qué residente necesita atención.

Criterios de aceptación:
- Puedo elegir cualquier residente de los que tengo asignados y entrar directamente al registro de cambio.
- Este acceso no me permite ver ni actuar sobre residentes fuera de mi asignación, aunque conozca su identificador.
- El resultado es idéntico al de completar el mismo flujo desde la ficha del residente.

Códigos de origen: `AUX-13`.

## Tabla resumen de trazabilidad

| Historia | Códigos cubiertos |
| --- | --- |
| 1. Ver residentes asignados y basal resumido | `AUX-01`, `AUX-13` |
| 2. Cerrar sin cambios | `AUX-02`, `AUX-03` |
| 3. Marcar no valorable | `AUX-02`, `AUX-04` |
| 4. Registrar cambio por áreas | `AUX-02`, `AUX-05`, `AUX-06`, `AUX-07` |
| 5. Clasificar y notificar ordinario/prioritario | `AUX-08`, `AUX-09`, `AUX-10`, `AUX-11` |
| 6. Reanudar desde inicio | `AUX-13` |

Nota: `AUX-12` (validación de usabilidad del campo "Desde cuándo" y de la carga de persona/canal/hora del aviso) queda como punto de validación pendiente, no como historia cerrada.

## Nota de procedencia

Estas historias agrupan los requisitos `AUX-01` a `AUX-13` del PRD v0.5, documentados durante la fase de diseño del producto previa a la migración hacia el monolito ASP.NET Core / SQL Server.
