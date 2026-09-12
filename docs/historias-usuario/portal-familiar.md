# Historias de usuario — Portal Familiar

El Portal Familiar da acceso al familiar autorizado a las publicaciones aprobadas sobre "su" residente, y le permite gestionar citas con el equipo asistencial. No expone ningún contenido clínico interno.

## Cómo leer estas historias

Cada historia sigue el formato "Como familiar, quiero..., para...", agrupa varios requisitos del PRD con sentido de negocio común, e incluye criterios de aceptación verificables en lenguaje llano.

## Historias de usuario

### 1. Iniciar sesión y elegir el residente autorizado

**Como** familiar, **quiero** iniciar sesión de forma segura y elegir el residente para el que tengo autorización activa, **para** acceder solo a lo que me corresponde.

Criterios de aceptación:
- El acceso exige mi credencial y, antes de tratar datos reales, un segundo factor.
- Solo veo residentes para los que tengo una autorización en estado Activa; nunca se me revela si existen otros vínculos suspendidos, revocados o inexistentes.
- Conocer o manipular una URL o un identificador nunca me concede acceso adicional.

Códigos de origen: `FAM-01`, `AUTH-03`, `AUTH-04`, `AUTH-06`.

### 2. Consultar el inicio y el historial de publicaciones

**Como** familiar, **quiero** ver las últimas publicaciones sobre el residente y su historial visible, **para** mantenerme informado de forma comprensible.

Criterios de aceptación:
- Las publicaciones ordinarias permanecen visibles 30 días y las relevantes 6 meses.
- Si no hay publicaciones en el periodo, el sistema me lo indica sin sugerir que todo está bien o que no hay incidencias.
- El profesional que aparece en cada publicación es siempre "Equipo asistencial del centro", nunca un nombre individual.

Códigos de origen: `FAM-02`, `FAM-07`, `FAM-11`, `FAM-12`.

### 3. Comprender los distintos tipos de publicación

**Como** familiar, **quiero** entender si una publicación es un resumen periódico, una actualización relevante, una corrección o una retirada, **para** interpretar correctamente la información que recibo.

Criterios de aceptación:
- Un resumen periódico llega según la modalidad configurada por el centro (diaria, semanal o solo relevantes) y requiere siempre aprobación humana previa.
- La relevancia de una actualización la decide un profesional, nunca el sistema.
- Una publicación ya publicada es inmutable; un error se corrige con una publicación correctora vinculada, nunca sustituyendo el texto original en silencio.
- Una retirada excepcional muestra un aviso de contenido no disponible, sin que ello borre la evidencia interna del original.
- En ningún caso accedo a observaciones, valoraciones, constantes, basal, Barthel, indicaciones, seguimientos, derivaciones, historial ni línea temporal interna.

Códigos de origen: `FAM-03`, `FAM-04`, `FAM-05`, `FAM-06`, `FAM-08`, `FAM-09`, `FAM-10`.

### 4. Reservar una cita directamente

**Como** familiar, **quiero** reservar directamente un hueco disponible con el equipo asistencial, cuando el centro tiene activo este modo, **para** coordinar una cita sin depender de que alguien me la confirme después.

Criterios de aceptación:
- Elijo equipo (médico o de Enfermería) y modalidad (presencial, telefónica o videoconferencia), pero nunca un profesional concreto.
- Solo veo huecos realmente disponibles; al confirmar, la cita queda Confirmada de forma transaccional e inmediata.
- Si el hueco se ocupa justo antes de confirmar, no se crea la cita y se me ofrecen alternativas disponibles.
- Puedo reprogramar eligiendo otro hueco disponible, o cancelar con confirmación previa.
- En todas las pantallas se me recuerda que las citas no son un canal urgente.

Códigos de origen: `CIT-04`, `CIT-05`, `CIT-D01` a `CIT-D05`.

### 5. Solicitar una cita y gestionar la propuesta del centro

**Como** familiar, **quiero** enviar una solicitud de cita cuando el centro tiene activo ese modo, y responder a la propuesta que me haga, **para** coordinar una cita cuando no hay reserva directa disponible.

Criterios de aceptación:
- Mi solicitud incluye equipo, modalidad, motivo categorizado, un comentario breve opcional y mi disponibilidad aproximada.
- Tras enviarla, queda en estado Pendiente de gestión hasta que el centro me proponga una fecha y hora.
- Solo cuando acepto la propuesta se crea una cita Confirmada; también puedo pedir otro horario o cancelar mi solicitud.
- No se habilita ningún chat con el centro durante este proceso.

Códigos de origen: `CIT-04`, `CIT-05`, `CIT-S01` a `CIT-S04`.

### 6. Entender los estados excepcionales del portal

**Como** familiar, **quiero** entender con claridad cuándo mi sesión ha caducado, cuándo no tengo autorización, cuándo no hay publicaciones, o cuándo hay un fallo técnico, **para** no confundir un problema técnico con ausencia de información sobre el residente.

Criterios de aceptación:
- Cada uno de estos cuatro estados (sesión caducada, sin autorización, sin publicaciones, fallo técnico) se comunica de forma distinta y reconocible.
- Un fallo técnico nunca se presenta como si fuera ausencia de información clínica o de novedades.
- Si tengo varios residentes autorizados, cambiar entre ellos recarga completamente el ámbito, sin mezclar datos de uno con otro.

Códigos de origen: `FAM-01`, `AUTH-03`.

## Tabla resumen de trazabilidad

| Historia | Códigos cubiertos |
| --- | --- |
| 1. Login y selección de residente | `FAM-01`, `AUTH-03`, `AUTH-04`, `AUTH-06` |
| 2. Inicio e historial de publicaciones | `FAM-02`, `FAM-07`, `FAM-11`, `FAM-12` |
| 3. Tipos de publicación y sus límites | `FAM-03` a `FAM-06`, `FAM-08` a `FAM-10` |
| 4. Reserva directa de cita | `CIT-04`, `CIT-05`, `CIT-D01` a `CIT-D05` |
| 5. Solicitud previa de cita | `CIT-04`, `CIT-05`, `CIT-S01` a `CIT-S04` |
| 6. Estados excepcionales del portal | `FAM-01`, `AUTH-03` |

## Nota de procedencia

Estas historias agrupan los requisitos `FAM-01` a `FAM-12`, `CIT-04`, `CIT-05`, `CIT-D01` a `CIT-D05`, `CIT-S01` a `CIT-S04` y los requisitos de autenticación aplicables (`AUTH-03`, `AUTH-04`, `AUTH-06`) del PRD v0.5, documentados durante la fase de diseño del producto previa a la migración hacia el monolito ASP.NET Core / SQL Server.
