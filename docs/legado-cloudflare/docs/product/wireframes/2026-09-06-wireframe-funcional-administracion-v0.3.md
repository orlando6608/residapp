# Wireframe funcional - Administración

**Versión:** 0.3  
**Fecha:** 2026-09-06  
**Estado:** Aprobado para la línea base funcional v1.1  
**Fuente canónica:** Markdown  
**Sustituye:** Wireframe funcional Administración v0.2  
**Referencias:** PRD v0.5 y matriz de permisos v0.2.1.

## 1. Alcance de la revisión

Se conservan los códigos `ADM-01` a `ADM-30`. Se corrigen la separación clínico-administrativa, la provisión de centros, el historial temporal de ubicación, las cuentas multirol y los dos modos excluyentes de citas.

## 2. Límites del perfil

- Administración opera solo en centros autorizados y sobre estructura subordinada ya provisionada por la plataforma.
- Gestionar estructura, usuarios o perfiles no concede acceso clínico.
- No accede al basal, Barthel, notas, constantes, valoraciones, seguimientos, indicaciones ni texto clínico de publicaciones por defecto.
- Toda operación sensible se autoriza y audita en servidor.

## 3. Pantallas

### ADM-01. Inicio de Administración

- **Muestra:** altas/ubicaciones pendientes, autorizaciones, usuarios, conflictos de turnos, publicaciones en estado administrativo y citas.
- **Regla:** sin eventos ni contenido clínico.

### ADM-02. Lista de residentes

- **Muestra:** identidad, estado administrativo, unidad y ubicación actual.
- **Acciones:** buscar, filtrar y abrir ficha.

### ADM-03. Ficha administrativa del residente

- **Muestra:** identidad, contacto administrativo, estado e historial de ubicación.
- **Acciones:** editar datos autorizados, trasladar, gestionar familiares.
- **Regla:** no muestra basal ni historia clínica.

### ADM-04. Alta administrativa de residente

- **Campos:** nombre, fecha de nacimiento, sexo documentado `male`, `female`, `other` o `unknown`, fecha de alta y ubicación inicial coherente con la estructura configurada.
- **Etiquetas visibles:** Hombre, Mujer, Otra categoría documentada y No consta.
- **Resultado:** identidad creada y basal pendiente.
- **Reglas:** el sexo se copia de documentación administrativa, nunca se infiere y no admite texto libre; idempotencia; Enfermería solo puede usar este flujo con permiso excepcional; Medicina no.

### ADM-05. Estructura física del centro

- **Muestra:** centro provisionado, unidades obligatorias y edificio/planta opcionales.
- **Acciones:** crear, renombrar, ordenar o inactivar niveles subordinados.
- **Reglas:** Administración no crea centros; cambios con ámbito y auditoría.

### ADM-06. Gestión de habitación y plaza/cama

- **Muestra:** habitaciones y plazas/camas cuando el centro las utilice.
- **Acciones:** configurar disponibilidad estructural.
- **Regla:** no modifica retroactivamente ubicaciones históricas.

### ADM-07. Organigrama del centro

- **Muestra:** cargos y relaciones organizativas.
- **Reglas:** cargo, turno, perfil y permiso son conceptos separados.

### ADM-08. Familiares vinculados

- **Muestra:** familiares, relación, autorizaciones y contacto urgente designado.
- **Acciones:** vincular, abrir autorización y designar contacto.

### ADM-09. Crear familiar

- **Campos:** identidad y datos de cuenta/contacto necesarios.
- **Regla:** crear familiar no activa acceso a ningún residente.

### ADM-10. Autorización familiar

- **Campos:** familiar, residente, estado, vigencia y alcance.
- **Regla:** solo estado Activa permite acceso; cambios auditados.

### ADM-11. Revocar o suspender autorización

- **Campos:** motivo y fecha efectiva.
- **Regla:** efecto inmediato en nuevas solicitudes y accesos; conserva historial.

### ADM-12. Usuarios profesionales

- **Muestra:** cuenta, estado, perfiles y ámbitos.
- **Acciones:** crear, activar, suspender y abrir detalle.

### ADM-13. Alta y gestión de usuario profesional

- **Campos:** cuenta, uno o varios perfiles, ámbitos y permisos configurables.
- **Reglas:** cada operación usa un único perfil activo; permisos de perfiles distintos no se combinan.

### ADM-14. Planificación semanal de personal

- **Muestra:** turnos por unidad y fecha.
- **Acciones:** crear, editar o retirar planificación.
- **Regla:** planificar no concede acceso por sí solo.

### ADM-15. Asignación de turnos en varias fechas

- **Acciones:** seleccionar fechas y asignar turno/equipo.
- **Regla:** valida ámbito y muestra conflictos antes de guardar.

### ADM-16. Turno recurrente y excepciones

- **Campos:** patrón, intervalo y excepciones.
- **Regla:** las excepciones no reescriben instancias históricas.

### ADM-17. Detección de conflictos de planificación

- **Muestra:** solapamientos y contexto.
- **Acciones:** volver a editar o confirmar decisión justificada.
- **Regla:** el sistema informa; Administración decide.

### ADM-18. Configuración de comunicación familiar

- **Muestra:** modalidad ordinaria por residente: diaria, semanal o solo relevantes.
- **Acciones:** cambiar frecuencia.
- **Regla:** no redacta ni aprueba contenido.

### ADM-19. Horarios de publicación

- **Campos:** día/hora según frecuencia y centro.
- **Regla:** el cambio afecta publicaciones futuras y no inventa contenido.

### ADM-20. Estado administrativo de publicaciones

- **Muestra:** residente, tipo, estado, fecha programada/publicada y errores técnicos.
- **Regla:** texto completo oculto por defecto; no equivale a acceso clínico.

### ADM-21. Configuración del servicio de citas

- **Opciones excluyentes:** desactivado, **Reserva directa** o **Solicitud previa**.
- **Campos:** equipos (Médico/Enfermería), modalidades habilitadas, reglas generales.
- **Regla:** solo una modalidad vigente por centro; cambiarla no reescribe citas existentes.

### ADM-22. Configuración de disponibilidad para citas

- **Campos:** franjas recurrentes, duración, antelación mínima, horizonte máximo y bloqueos por equipo/modalidad.
- **Regla:** no expone profesionales concretos al Portal Familiar.

### ADM-23. Agenda semanal de citas

- **Muestra:** huecos, solicitudes, propuestas y citas confirmadas.
- **Acciones:** filtrar, bloquear huecos y abrir gestión.

### ADM-24. Reserva directa desde Portal Familiar

- **Muestra:** reserva confirmada, equipo, modalidad, fecha/hora y actor familiar.
- **Acciones administrativas:** reprogramar o cancelar con motivo.
- **Reglas:** el hueco se confirma de forma transaccional e idempotente; no existe propuesta previa.

### ADM-25. Gestión administrativa de cita

- **Modo solicitud:** revisar petición, proponer fecha/hora y asignar profesional internamente.
- **Modo directo:** gestionar cita ya confirmada.
- **Regla:** sin chat clínico; el familiar nunca elige profesional concreto.

### ADM-26. Estados y cambios de cita

- **Solicitud:** pendiente de gestión, propuesta, cambio solicitado, cancelada; solo aceptación produce cita confirmada.
- **Cita:** confirmada, reprogramada, cancelada o finalizada.
- **Regla:** cada transición conserva actor y fecha/hora.

### ADM-27. Retirada excepcional de publicación

- **Acción condicionada:** retirar por privacidad, seguridad, audiencia/residente incorrectos o causa equivalente.
- **Reglas:** permiso específico, motivo obligatorio y auditoría; original conservado internamente.

### ADM-28. Auditoría administrativa

- **Muestra:** actor, perfil activo, ámbito, acción, recurso y fecha/hora de usuarios, perfiles, asignaciones, autorizaciones, horarios, citas, retiradas y seguridad.
- **Regla:** append-only para usuarios ordinarios; sin texto clínico.

### ADM-29. Configuración del centro

- **Muestra:** parámetros del centro ya provisionado.
- **Acciones:** estructura subordinada y opciones administrativas autorizadas.
- **Reglas:** no provisiona centros ni modifica categorías/umbrales clínicos.

### ADM-30. Mi cuenta y seguridad

- **Acciones:** gestionar credenciales, segundo factor y sesión; seleccionar perfil en cuenta multirol.
- **Regla:** el cambio de perfil es explícito y genera nuevo contexto de autorización.

## 4. Componente transversal: traslado de residente

- La ubicación actual procede del único intervalo vigente.
- Trasladar cierra el intervalo anterior y abre el nuevo en una operación atómica.
- Centro y unidad son obligatorios; los niveles opcionales deben pertenecer a la rama seleccionada.
- No se permite editar o borrar un intervalo histórico desde la interfaz ordinaria.

## 5. Criterios de aceptación

1. Ninguna pantalla administrativa expone basal, Barthel o contenido clínico.
2. ADM-05/29 no permiten crear un centro, solo gestionar su estructura subordinada.
3. Un traslado conserva el intervalo previo y deja uno solo vigente.
4. ADM-21 impide activar a la vez Reserva directa y Solicitud previa.
5. ADM-24 refleja reserva directa confirmada; ADM-25 soporta solicitud previa sin mezclar flujos.
6. Asignar perfil, turno o estructura nunca concede acceso clínico implícito.

## 6. Cambios frente a v0.1

| Elemento | Cambio v0.2 |
| --- | --- |
| Centro | Provisionado por plataforma; Administración gestiona subordinados |
| Ubicación | Historial temporal, no sobrescritura |
| Separación de funciones | Alta administrativa separada de basal y permisos clínicos |
| Usuarios | Cuenta multirol con perfil activo explícito |
| Citas | Modos directo/solicitud mutuamente excluyentes y continuidad de estados |

## 7. Cambios de v0.2 a v0.3

- ADM-04 incorpora el catálogo cerrado de sexo documentado, incluido `unknown` como No consta.
- No se modifica el acceso administrativo al basal ni se amplían permisos.

