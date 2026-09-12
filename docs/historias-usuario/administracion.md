# Historias de usuario — Administración

Administración gestiona la estructura subordinada del centro, las identidades, los familiares y sus autorizaciones, los usuarios profesionales, los turnos, la configuración de publicaciones y citas, y la auditoría administrativa. No accede a contenido clínico ni participa en actos asistenciales.

## Cómo leer estas historias

Cada historia sigue el formato "Como Administración, quiero..., para...", agrupa varios requisitos del PRD con sentido de negocio común, e incluye criterios de aceptación verificables en lenguaje llano.

## Historias de usuario

### 1. Gestionar la ficha administrativa y dar de alta a un residente

**Como** Administración, **quiero** dar de alta a un residente y gestionar su identidad y su historial de ubicación, **para** dejar constancia administrativa sin exponer información clínica.

Criterios de aceptación:
- El sexo documentado se toma literalmente de un catálogo cerrado (Hombre, Mujer, Otra categoría documentada, No consta), nunca por inferencia ni texto libre.
- El alta deja el basal en estado pendiente; no lo completo yo, lo completa Enfermería o Medicina cuando tengan permiso.
- Ninguna pantalla de gestión de residentes muestra basal, Barthel ni contenido clínico.

Códigos de origen: `ADM-02`, `RES-01`, `RES-02`.

### 2. Gestionar la estructura física y organizativa del centro

**Como** Administración, **quiero** gestionar la estructura subordinada de un centro ya provisionado (edificios, plantas, unidades, habitaciones, plazas) y el organigrama, **para** adaptar el sistema a la organización real del centro.

Criterios de aceptación:
- No puedo crear un centro nuevo: solo gestiono la estructura subordinada de centros ya provisionados por la plataforma.
- Un traslado de residente cierra el intervalo de ubicación anterior y abre uno nuevo en una única operación atómica; nunca sobrescribe el historial.
- Asignar un cargo, un turno o una posición organizativa no concede acceso clínico por sí mismo.

Códigos de origen: `ADM-11`, `ORG-*` (ver PRD v0.5, bloque de organización).

### 3. Gestionar familiares y sus autorizaciones

**Como** Administración, **quiero** gestionar los familiares vinculados a un residente y sus autorizaciones de acceso, **para** controlar quién puede entrar al Portal Familiar.

Criterios de aceptación:
- Crear un familiar no le concede automáticamente acceso a ningún residente; la autorización es un paso separado.
- Solo una autorización en estado Activa permite el acceso al Portal Familiar.
- Revocar o suspender una autorización tiene efecto inmediato en las nuevas solicitudes, aunque exista una sesión abierta, y queda auditado.
- Gestiono también el contacto urgente designado de cada residente.

Códigos de origen: `ADM-03`, `FAM-01`.

### 4. Gestionar usuarios profesionales, perfiles y turnos

**Como** Administración, **quiero** crear y gestionar cuentas profesionales con uno o varios perfiles, y planificar sus turnos, **para** que cada profesional tenga el acceso correcto en el momento correcto.

Criterios de aceptación:
- Puedo asignar varios perfiles a una misma cuenta, pero cada operación se realiza con un único perfil activo; los permisos de perfiles distintos nunca se combinan.
- Puedo activar y suspender cuentas de usuario.
- Al planificar turnos con fechas múltiples o recurrentes, el sistema muestra los conflictos de solapamiento antes de guardar, pero soy yo quien decide.
- Planificar un turno no concede acceso por sí solo; el acceso sigue dependiendo de la autorización de seguridad.

Códigos de origen: `ADM-04`, `ORG-02`, `ORG-04`.

### 5. Configurar la comunicación familiar y supervisar publicaciones

**Como** Administración, **quiero** configurar la frecuencia y el horario de las publicaciones familiares y consultar su estado, **para** que la comunicación llegue con la periodicidad que decida el centro, sin redactar yo el contenido.

Criterios de aceptación:
- Configuro la modalidad por residente (diaria, semanal o solo actualizaciones relevantes) y el horario de publicación.
- No redacto ni apruebo el contenido de ninguna publicación; eso corresponde a Enfermería o Medicina.
- Consulto el estado administrativo y técnico de las publicaciones (programada, publicada, con error) sin ver el texto clínico completo por defecto.

Códigos de origen: `ADM-05`, `ADM-06`.

### 6. Retirar excepcionalmente una publicación

**Como** Administración, **quiero** poder retirar una publicación ya publicada en casos excepcionales, **para** proteger la privacidad o la seguridad cuando sea estrictamente necesario.

Criterios de aceptación:
- Solo puedo hacerlo con un permiso específico habilitado para esta acción.
- La retirada exige un motivo obligatorio (privacidad, seguridad, audiencia o residente incorrectos, o causa equivalente) y queda auditada.
- El contenido original se conserva internamente; la retirada no lo elimina.

Códigos de origen: `ADM-08`, `FAM-09`.

### 7. Configurar y gestionar citas en modo reserva directa

**Como** Administración, **quiero** configurar el servicio de citas en modo reserva directa y gestionar las citas resultantes, **para** que las familias puedan reservar un hueco disponible sin intervención previa mía.

Criterios de aceptación:
- Configuro equipos, modalidades habilitadas, franjas recurrentes, duración, antelación mínima, horizonte máximo y bloqueos.
- Una reserva directa exige exclusión transaccional del hueco: dos familiares no pueden reservar el mismo hueco.
- Puedo reprogramar o cancelar una cita ya confirmada, dejando trazabilidad de actor y fecha/hora.

Códigos de origen: `ADM-07`, `CIT-01`, `CIT-D01` a `CIT-D05`, `CIT-C01` a `CIT-C03`.

### 8. Configurar y gestionar citas en modo solicitud previa

**Como** Administración, **quiero** configurar el servicio de citas en modo solicitud previa y gestionar las solicitudes recibidas, **para** proponer fecha, hora y profesional interno antes de confirmar una cita.

Criterios de aceptación:
- Los dos modos de citas (reserva directa y solicitud previa) son excluyentes: solo uno puede estar vigente a la vez en un centro.
- Cambiar de modo afecta a las nuevas operaciones, pero no reescribe las solicitudes o citas ya existentes.
- Solo la aceptación de mi propuesta por parte del familiar produce una cita confirmada.

Códigos de origen: `ADM-07`, `CIT-01`, `CIT-S01` a `CIT-S04`, `CIT-C01` a `CIT-C03`.

### 9. Consultar la auditoría administrativa

**Como** Administración, **quiero** consultar el registro de auditoría de usuarios, perfiles, asignaciones, autorizaciones, horarios, citas, retiradas y seguridad, **para** revisar quién hizo qué y cuándo.

Criterios de aceptación:
- Los registros de auditoría son append-only: no puedo modificarlos ni borrarlos.
- La auditoría nunca expone texto clínico ni se usa como ranking de trabajadores.
- Cada registro conserva cuenta, perfil activo, ámbito, acción, recurso y fecha/hora.

Códigos de origen: `ADM-09`, `AUD-01` a `AUD-03`.

### 10. Configurar el centro y gestionar mi cuenta multirol

**Como** Administración, **quiero** ver el panel inicial de asuntos administrativos y gestionar mi propia cuenta con sus perfiles, **para** trabajar de forma eficiente sin mezclar información clínica.

Criterios de aceptación:
- La pantalla inicial no muestra eventos, constantes, seguimientos ni indicaciones clínicas.
- Si mi cuenta tiene varios perfiles, debo seleccionar explícitamente cuál está activo antes de operar.
- No accedo al basal ni, por defecto, al historial clínico, ni modifico registros profesionales.

Códigos de origen: `ADM-01`, `ADM-10`.

## Tabla resumen de trazabilidad

| Historia | Códigos cubiertos |
| --- | --- |
| 1. Ficha administrativa y alta | `ADM-02`, `RES-01`, `RES-02` |
| 2. Estructura física y organizativa | `ADM-11`, `ORG-*` |
| 3. Familiares y autorizaciones | `ADM-03`, `FAM-01` |
| 4. Usuarios, perfiles y turnos | `ADM-04`, `ORG-02`, `ORG-04` |
| 5. Comunicación y publicaciones | `ADM-05`, `ADM-06` |
| 6. Retirada excepcional | `ADM-08`, `FAM-09` |
| 7. Citas — reserva directa | `ADM-07`, `CIT-01`, `CIT-D01` a `CIT-D05`, `CIT-C01` a `CIT-C03` |
| 8. Citas — solicitud previa | `ADM-07`, `CIT-01`, `CIT-S01` a `CIT-S04`, `CIT-C01` a `CIT-C03` |
| 9. Auditoría administrativa | `ADM-09`, `AUD-01` a `AUD-03` |
| 10. Panel inicial y cuenta multirol | `ADM-01`, `ADM-10` |

## Nota de procedencia

Estas historias agrupan los requisitos `ADM-01` a `ADM-11`, `ORG-*`, `RES-01`/`RES-02`, `FAM-01`/`FAM-09`, `CIT-*` y `AUD-01` a `AUD-03` del PRD v0.5, documentados durante la fase de diseño del producto previa a la migración hacia el monolito ASP.NET Core / SQL Server.
