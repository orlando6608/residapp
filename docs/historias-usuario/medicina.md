# Historias de usuario — Medicina

Medicina recibe los escalados de Enfermería, valora, indica, hace seguimiento, deriva a Urgencias, cierra sin necesitar un segundo cierre de Enfermería, y gestiona el estado basal cuando tiene permiso.

## Cómo leer estas historias

Cada historia sigue el formato "Como Medicina, quiero..., para...", agrupa varios requisitos del PRD con sentido de negocio común, e incluye criterios de aceptación verificables en lenguaje llano. Para el detalle operativo paso a paso, ver `docs\flujos-clinicos\valoracion-conducta-medicina.md`, `docs\flujos-clinicos\derivacion-urgencias.md` y `docs\flujos-clinicos\gestion-basal-barthel.md`.

## Historias de usuario

### 1. Gestionar la bandeja de escalados

**Como** Medicina, **quiero** ver los escalados recibidos de Enfermería con su información reunida, **para** decidir sin tener que reconstruir el caso desde cero.

Criterios de aceptación:
- La bandeja muestra motivo, constantes, actuaciones, tiempo transcurrido y estado de cada escalado, sin ningún resumen diagnóstico automático.
- La observación original y la valoración de Enfermería son de solo lectura; nunca las modifico.
- La línea temporal completa solo se despliega bajo demanda y con la autorización correspondiente.

Códigos de origen: `MED-01`, `MED-02`, `MED-03`.

### 2. Realizar la valoración médica

**Como** Medicina, **quiero** completar mi valoración médica sobre un escalado o un evento propio, **para** documentar mis hallazgos y mis actuaciones.

Criterios de aceptación:
- La valoración incluye hallazgos y exploración, valoración propiamente dicha, constantes opcionales y actuaciones.
- Iniciar la valoración registra qué profesional y en qué momento lo hace, sin crear una propiedad permanente sobre el evento.
- Mi valoración no modifica la documentación previa de Enfermería.

Códigos de origen: `MED-04`.

### 3. Registrar indicaciones y seguir su cumplimiento

**Como** Medicina, **quiero** registrar una indicación para Enfermería y seguir si fue leída y realizada, **para** asegurarme de que mis instrucciones se ejecutan.

Criterios de aceptación:
- La indicación incluye texto, fecha prevista o criterio, e información adicional, sin selector automático de prioridad.
- Lectura y realización de la indicación son hitos distintos y ambos quedan visibles hasta su resolución.
- Una indicación no realizada exige registrar una incidencia; nunca desaparece silenciosamente.

Códigos de origen: `MED-05`, `MED-06`, `MED-08`.

### 4. Cerrar un evento clínico de forma definitiva

**Como** Medicina, **quiero** cerrar un evento sin que Enfermería tenga que cerrarlo también, **para** evitar duplicidad de trabajo cuando la decisión ya es definitiva.

Criterios de aceptación:
- Cerrar un evento desde Medicina no exige un segundo cierre por parte de Enfermería.
- El cierre es idempotente: repetirlo no genera dos cierres.
- Al cerrar como punto final del caso, decido y apruebo la comunicación familiar, con el mismo mecanismo de aprobación humana que en Enfermería.

Códigos de origen: `MED-05`, `MED-10`, `MED-12`.

### 5. Iniciar y continuar un seguimiento médico

**Como** Medicina, **quiero** mantener un evento en seguimiento cuando necesito un resultado antes de decidir, y gestionar la continuidad al cambiar de turno, **para** no cerrar un caso de forma prematura.

Criterios de aceptación:
- Si un resultado es necesario para el desenlace, el evento permanece en seguimiento; el sistema nunca decide por mí qué resultado es necesario.
- Un seguimiento vencido no caduca ni desaparece por sí solo.
- Al finalizar mi turno, decido explícitamente entre transferir el seguimiento al equipo entrante o conservarlo para mi propia próxima revisión.

Códigos de origen: `MED-05`, `MED-07`, `MED-08`, `MED-09`.

### 6. Activar protocolo urgente y derivar a Urgencias

**Como** Medicina, **quiero** activar el protocolo urgente y, si procede, derivar al residente a Urgencias, **para** documentar la atención urgente sin retrasarla.

Criterios de aceptación:
- Documentar el protocolo urgente no retrasa la atención al residente.
- El módulo de derivación es idéntico al que usa Enfermería: vista previa obligatoria, edición del texto sin alterar los registros de origen, firma y PDF vinculado al evento.
- El informe nunca incluye la escala CFS ni un bloque externo de contactos.

Códigos de origen: `MED-05`, `DER-01` a `DER-06` (ver `docs\flujos-clinicos\derivacion-urgencias.md`).

### 7. Iniciar un evento clínico propio

**Como** Medicina, **quiero** registrar un evento que yo mismo observo directamente, **para** documentarlo sin que se reenvíe artificialmente como si viniera de un escalado.

Criterios de aceptación:
- Un evento propio de Medicina sigue el mismo ciclo de valoración y decisión que uno recibido por escalado.
- El sistema no simula un reenvío desde Enfermería para este tipo de evento.

Códigos de origen: `MED-11`.

### 8. Gestionar el estado basal

**Como** Medicina, **quiero** crear o reevaluar el basal de un residente cuando tengo permiso, **para** mantener actualizada su referencia habitual sin obtener por ello otros permisos.

Criterios de aceptación:
- Solo puedo gestionar el basal si tengo el permiso correspondiente y el residente está dentro de mi ámbito.
- Poder valorar o reevaluar el basal nunca me concede el permiso de alta administrativa de residentes.
- Aplico las mismas reglas de las nueve áreas y de la escala Barthel que Enfermería (ver `docs\flujos-clinicos\gestion-basal-barthel.md`).

Códigos de origen: `MED-13` (ver `docs\flujos-clinicos\gestion-basal-barthel.md`, requisitos `BAS-*`).

### 9. Consultar y corregir historial dentro de la ventana permitida

**Como** Medicina, **quiero** consultar el historial clínico y corregir mis propias notas dentro de la ventana de tiempo habilitada, **para** rectificar errores sin comprometer la trazabilidad.

Criterios de aceptación:
- La ventana de corrección de seis horas solo se aplica a cursos o notas clínicas expresamente habilitadas para ello, nunca al basal.
- Fuera de esa ventana, cualquier corrección se añade como una rectificación trazable, conservando el original, el motivo, el autor y la fecha.
- Cada evento del historial conserva la referencia al basal y a la ubicación aplicables cuando ocurrió.

Códigos de origen: `HIS-01` a `HIS-03`, `COR-01`, `COR-02`.

## Tabla resumen de trazabilidad

| Historia | Códigos cubiertos |
| --- | --- |
| 1. Bandeja de escalados | `MED-01`, `MED-02`, `MED-03` |
| 2. Valoración médica | `MED-04` |
| 3. Indicaciones y seguimiento | `MED-05`, `MED-06`, `MED-08` |
| 4. Cierre sin segundo cierre de Enfermería | `MED-05`, `MED-10`, `MED-12` |
| 5. Seguimiento médico y continuidad | `MED-05`, `MED-07`, `MED-08`, `MED-09` |
| 6. Protocolo urgente y derivación | `MED-05`, `DER-01` a `DER-06` |
| 7. Evento propio | `MED-11` |
| 8. Gestión del basal | `MED-13` |
| 9. Historial y corrección | `HIS-01` a `HIS-03`, `COR-01`, `COR-02` |

## Nota de procedencia

Estas historias agrupan los requisitos `MED-01` a `MED-13`, `DER-01` a `DER-06`, `HIS-01` a `HIS-03` y `COR-01`/`COR-02` del PRD v0.5, documentados durante la fase de diseño del producto previa a la migración hacia el monolito ASP.NET Core / SQL Server.
