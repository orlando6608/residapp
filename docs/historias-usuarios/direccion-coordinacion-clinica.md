# Historias de usuario — Dirección/Coordinación Clínica

Dirección/Coordinación Clínica supervisa el proceso asistencial de forma agregada. No interviene clínicamente desde este perfil: no valora, indica, ejecuta, escala, corrige ni cierra. El acceso a contenido clínico detallado exige permiso específico y queda siempre auditado.

## Cómo leer estas historias

Cada historia sigue el formato "Como Dirección, quiero..., para...", agrupa varios requisitos del PRD con sentido de negocio común, e incluye criterios de aceptación verificables en lenguaje llano. Para el detalle operativo paso a paso, ver `docs\flujos-clinicos\supervision-clinica-direccion.md`.

## Historias de usuario

### 1. Ver el panel agregado de actividad

**Como** Dirección, **quiero** ver un panel agregado de cierres pendientes, eventos abiertos o vencidos, seguimientos, continuidad e indicaciones con incidencia, **para** detectar dónde puede haber un problema de proceso sin invadir el trabajo clínico.

Criterios de aceptación:
- El panel agrega la información por mi ámbito de supervisión (centro, unidad).
- La supervisión operativa puede identificar residente y episodio cuando sea necesario, sin abrir automáticamente notas clínicas completas.
- No aparece en este panel ningún control de escritura asistencial.

Códigos de origen: `DIR-02`, `DIR-03`.

### 2. Supervisar pendientes sin ver contenido clínico

**Como** Dirección, **quiero** navegar por los pendientes de una unidad y ver su detalle operativo, **para** entender la carga de trabajo sin acceder a información clínica que no necesito.

Criterios de aceptación:
- El detalle operativo muestra hitos, responsables de equipo y estado, sin ningún contenido clínico.
- Desde este detalle no dispongo de ninguna acción asistencial (valorar, indicar, cerrar, etc.).

Códigos de origen: `DIR-02`, `DIR-03`.

### 3. Acceder puntualmente a contenido clínico bajo permiso y auditoría

**Como** Dirección, **quiero** poder consultar el detalle clínico de un caso cuando realmente lo necesito, **para** ejercer mi función de supervisión sin comprometer la confidencialidad del resto de casos.

Criterios de aceptación:
- El sistema exige que yo tenga un permiso clínico específico, declare una finalidad válida y esté dentro de mi ámbito antes de mostrarme el contenido.
- El acceso se registra en auditoría antes de entregarme el contenido, nunca después.
- Todo lo que consulto es de solo lectura; puedo ver el basal vigente si estoy autorizado, pero nunca la escala CFS, que no existe en el producto.
- Puedo desplegar la línea temporal completa del residente y su historial de eventos cerrados, siempre en solo lectura.

Códigos de origen: `DIR-04`, `DIR-11`.

### 4. Consultar indicadores agregados sin ranking individual

**Como** Dirección, **quiero** consultar indicadores de continuidad, seguimientos vencidos, indicaciones pendientes y evolución temporal, agregados por ámbito y periodo, **para** valorar la calidad del proceso sin evaluar a ningún profesional de forma individual.

Criterios de aceptación:
- Cada indicador se presenta con su denominador y periodo cuando corresponde.
- Ningún indicador ni informe genera predicción clínica, juicio automático o ranking nominativo de productividad individual.
- Los indicadores sirven para detectar proceso, no para atribuir desempeño a una persona concreta.

Códigos de origen: `DIR-08`, `DIR-09`.

### 5. Supervisar derivaciones y comunicación familiar en solo lectura

**Como** Dirección, **quiero** consultar el estado de las derivaciones a Urgencias y de la comunicación familiar, **para** supervisar el proceso sin intervenir en su contenido.

Criterios de aceptación:
- Puedo consultar un informe de derivación firmado si tengo permiso, pero nunca generarlo ni firmarlo yo.
- Puedo ver la frecuencia y el estado de las publicaciones familiares (pendientes, errores), pero no redacto, modifico ni apruebo ninguna.

Códigos de origen: `DIR-06`, `DIR-07`.

### 6. Consultar correcciones y rectificaciones en solo lectura

**Como** Dirección, **quiero** consultar el historial de correcciones y rectificaciones de los registros clínicos, **para** entender cómo se ha corregido un caso sin poder alterarlo yo misma.

Criterios de aceptación:
- Veo el original, la corrección o rectificación, el motivo, el autor y las fechas, siempre en modo de solo lectura.
- Nunca puedo iniciar yo una corrección ni una rectificación desde este perfil.

Códigos de origen: `DIR-04`, `COR-01`, `COR-02`.

### 7. Consultar informes de actividad agregados

**Como** Dirección, **quiero** generar informes de actividad agregados por ámbito y periodo, **para** compartir una visión de conjunto sin exponer historias clínicas individuales.

Criterios de aceptación:
- Los informes son agregados; no permiten exportación masiva de historias clínicas individuales.
- Ningún informe incluye rankings nominativos de productividad.

Códigos de origen: `DIR-10`.

### 8. Conocer mi ámbito de supervisión y gestionar accesos denegados

**Como** Dirección, **quiero** conocer qué centros, unidades y permisos tiene vigentes mi perfil activo, y recibir un mensaje claro cuando se me deniega un acceso, **para** entender los límites de mi propio rol sin necesidad de intentar manipular la aplicación.

Criterios de aceptación:
- Consultar mi propio ámbito no me permite ampliarlo.
- Un acceso denegado muestra un mensaje neutro y no revela si el contenido o el residente existen fuera de mi ámbito.
- Un error técnico se comunica de forma distinta a un acceso denegado; nunca se confunden.

Códigos de origen: `DIR-01`, `DIR-05`.

## Tabla resumen de trazabilidad

| Historia | Códigos cubiertos |
| --- | --- |
| 1. Panel agregado | `DIR-02`, `DIR-03` |
| 2. Supervisar pendientes sin clínica | `DIR-02`, `DIR-03` |
| 3. Acceso puntual con permiso y auditoría | `DIR-04`, `DIR-11` |
| 4. Indicadores agregados sin ranking | `DIR-08`, `DIR-09` |
| 5. Derivaciones y comunicación en solo lectura | `DIR-06`, `DIR-07` |
| 6. Correcciones en solo lectura | `DIR-04`, `COR-01`, `COR-02` |
| 7. Informes agregados | `DIR-10` |
| 8. Ámbito de supervisión y acceso denegado | `DIR-01`, `DIR-05` |

## Nota de procedencia

Estas historias agrupan los requisitos `DIR-01` a `DIR-11`, `COR-01` y `COR-02` del PRD v0.5, documentados durante la fase de diseño del producto previa a la migración hacia el monolito ASP.NET Core / SQL Server.
