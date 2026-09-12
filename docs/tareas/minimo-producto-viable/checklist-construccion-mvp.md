# Checklist de construcción — Mínimo Producto Viable

Estado al 2026-09-12.

## Qué es y qué no es este documento

Este documento es un **checklist de tareas de construcción** pendientes para tener funcionando el mínimo producto viable: los seis perfiles y el circuito funcional completo ya descritos en [`docs/producto/alcance.md`](../../producto/alcance.md). No redefine el alcance (esa es la referencia vigente, sin recortes) ni sustituye el estado general por vertical, que vive en [`docs/producto/roadmap.md`](../../producto/roadmap.md). Este fichero traduce ese alcance y ese orden de construcción en tareas concretas, citando en cada bloque las historias de usuario, los flujos clínicos y los wireframes funcionales de los que salen, en vez de repetir su contenido.

Se actualiza a medida que avanza cada vertical: cuando uno empiece a construirse en serio, es esperable que su detalle técnico se traslade a un fichero propio en `docs/tareas/alta-prioridad/`, como ya ocurrió con Residente/Basal en [`pendientes-migracion-inicial.md`](../alta-prioridad/pendientes-migracion-inicial.md).

## Orden de construcción y estado

El orden se hereda de `roadmap.md` (heredado a su vez del prototipo legado, única parte de aquella hoja de ruta que sigue vigente):

| Vertical | Perfil(es) | Estado |
| --- | --- | --- |
| Residente / Basal | Transversal — lo usan los seis perfiles | En curso |
| Auxiliar | Auxiliar | No iniciado |
| Enfermería | Enfermería | No iniciado |
| Medicina | Medicina | No iniciado |
| Familia / Portal Familiar | Familiar | No iniciado |
| Administración | Administración | No iniciado |
| Dirección / Coordinación Clínica | Dirección/Coordinación Clínica | No iniciado |

### Residente / Basal

Cubre el módulo *Residentes* de `alcance.md` (identidad, historial de ubicación, basal vigente/versionado, Barthel común, cognición). Es transversal: el resto de verticales lo consultan o lo completan, no lo duplican.

Las tareas de construcción pendientes ya están detalladas y no se repiten aquí — ver [`docs/tareas/alta-prioridad/pendientes-migracion-inicial.md`](../alta-prioridad/pendientes-migracion-inicial.md): ejecutar el DDL contra una instancia real de SQL Server, sacar `ResidApp.Web` de la plantilla en blanco, y añadir tests propios del vertical.

### Auxiliar

Cubre el módulo *Registro cotidiano* de `alcance.md`.

- Historias de usuario: [`docs/historias-usuarios/auxiliar.md`](../../historias-usuarios/auxiliar.md) (`AUX-01` a `AUX-13`).
- Flujo clínico: [`docs/flujos-clinicos/registro-cotidiano-auxiliar.md`](../../flujos-clinicos/registro-cotidiano-auxiliar.md).
- Wireframe funcional: [`docs/bocetos-pantallas/wireframes-funcionales/auxiliar.md`](../../bocetos-pantallas/wireframes-funcionales/auxiliar.md).

Tareas de construcción pendientes:
- Dominio: modelar el registro cotidiano (sin cambios / no valorable / cambio observado) y su clasificación ordinario/prioritario con aviso directo.
- Aplicación e infraestructura: casos de uso y repositorio Dapper/SQL Server; nueva migración numerada bajo `database/scripts/` (la `0001` es inmutable, ver [`docs/decisiones-arquitectura/integridad-sql-basal-legado.md`](../../decisiones-arquitectura/integridad-sql-basal-legado.md)).
- `ResidApp.Web`: pantallas de cierre de turno, residentes asignados y reanudación del registro.
- Autorización: política Claims-Based propia del perfil Auxiliar, sin permitir valoración clínica ni edición de basal.
- Tests: validación de dominio, motor de autorización y casos de uso.

### Enfermería

Cubre los módulos *Eventos* y, junto con Medicina, *Medicina* (basal/Barthel) y *Derivación* de `alcance.md`.

- Historias de usuario: [`docs/historias-usuarios/enfermeria.md`](../../historias-usuarios/enfermeria.md) (`ENF-01` a `ENF-16`).
- Flujos clínicos: [`docs/flujos-clinicos/valoracion-escalado-enfermeria.md`](../../flujos-clinicos/valoracion-escalado-enfermeria.md), [`gestion-basal-barthel.md`](../../flujos-clinicos/gestion-basal-barthel.md) (`BAS-01` a `BAS-19`, común con Medicina), [`derivacion-urgencias.md`](../../flujos-clinicos/derivacion-urgencias.md) (`DER-01` a `DER-06`, común con Medicina).
- Wireframe funcional: [`docs/bocetos-pantallas/wireframes-funcionales/enfermeria.md`](../../bocetos-pantallas/wireframes-funcionales/enfermeria.md).

Tareas de construcción pendientes:
- Dominio: bandejas, valoración de eventos, seguimiento, escalado a Medicina, cierre con decisión de comunicación familiar.
- Dominio compartido con Medicina (construir una sola vez, no duplicar): gestión de basal/Barthel más allá del alta inicial (reevaluaciones) y derivación a Urgencias.
- Aplicación e infraestructura: casos de uso y repositorios Dapper/SQL Server; nueva migración para las tablas de eventos/bandejas/derivación.
- `ResidApp.Web`: pantallas de bandejas, valoración, escalado, indicaciones recibidas y derivación.
- Autorización: política propia del perfil Enfermería, incluida la firma no transferible del basal.
- Tests: dominio, motor de autorización y casos de uso, incluida la concurrencia optimista del borrador de basal (mismo patrón que `BASELINE_DRAFT_REVISION_CONFLICT`).

### Medicina

Cubre el módulo *Medicina* de `alcance.md` y comparte *Derivación* y la gestión de basal con Enfermería.

- Historias de usuario: [`docs/historias-usuarios/medicina.md`](../../historias-usuarios/medicina.md) (`MED-01` a `MED-18`).
- Flujos clínicos: [`docs/flujos-clinicos/valoracion-conducta-medicina.md`](../../flujos-clinicos/valoracion-conducta-medicina.md), más `gestion-basal-barthel.md` y `derivacion-urgencias.md` (compartidos con Enfermería, ver arriba).
- Wireframe funcional: [`docs/bocetos-pantallas/wireframes-funcionales/medicina.md`](../../bocetos-pantallas/wireframes-funcionales/medicina.md).

Tareas de construcción pendientes:
- Dominio: bandeja de escalados, valoración médica, indicaciones a Enfermería con seguimiento de cumplimiento, eventos propios, cierre definitivo.
- Aplicación e infraestructura: casos de uso y repositorios Dapper/SQL Server sobre las mismas tablas de eventos/derivación que Enfermería (no crear un esquema paralelo).
- `ResidApp.Web`: pantallas de escalados, valoración, indicaciones y seguimiento médico.
- Autorización: política propia del perfil Medicina, sin automatizar decisiones ni atribuir actos a otros.
- Tests: dominio, motor de autorización y casos de uso, incluido el historial con corrección dentro de ventana permitida (`HIS-01` a `HIS-03`, `COR-01`, `COR-02`).

### Familia / Portal Familiar

Cubre el módulo *Portal Familiar* de `alcance.md`.

- Historias de usuario: [`docs/historias-usuarios/portal-familiar.md`](../../historias-usuarios/portal-familiar.md) (`FAM-*`, `AUTH-03`, `AUTH-04`, `AUTH-06`, `CIT-*`).
- No tiene flujo clínico propio: el circuito familiar (aprobación, publicación, consulta) se documenta como historia de usuario, no como flujo asistencial (ver [`docs/flujos-clinicos/indice.md`](../../flujos-clinicos/indice.md)).
- Wireframe funcional: [`docs/bocetos-pantallas/wireframes-funcionales/portal-familiar.md`](../../bocetos-pantallas/wireframes-funcionales/portal-familiar.md).

Tareas de construcción pendientes:
- Dominio: sesión familiar, selección de residente autorizado, consulta de publicaciones, citas (reserva directa y solicitud previa según modalidad del centro).
- Aplicación e infraestructura: casos de uso y repositorios Dapper/SQL Server para publicaciones y citas, ligados a las autorizaciones familiares que gestiona Administración.
- `ResidApp.Web`: pantallas del portal (inicio, historial de publicaciones, citas, estados excepcionales).
- Autorización: acceso condicionado a autorización activa por residente; sin acceso a contenido clínico interno.
- Tests: dominio, motor de autorización (incluida revocación con efecto inmediato) y casos de uso.

### Administración

Cubre los módulos *Organización*, *Identidades*, *Publicaciones* (configuración, no redacción) y *Administración* de `alcance.md`.

- Historias de usuario: [`docs/historias-usuarios/administracion.md`](../../historias-usuarios/administracion.md) (`ADM-01` a `ADM-11`, `ORG-*`, `FAM-01`/`FAM-09`, `CIT-01`/`CIT-D*`/`CIT-S*`/`CIT-C*`, `AUD-01` a `AUD-03`).
- No tiene flujo clínico propio (es un perfil no asistencial, ver `docs/flujos-clinicos/indice.md`).
- Wireframe funcional: [`docs/bocetos-pantallas/wireframes-funcionales/administracion.md`](../../bocetos-pantallas/wireframes-funcionales/administracion.md).

Tareas de construcción pendientes:
- Dominio: estructura subordinada del centro, identidades y familiares/autorizaciones, usuarios profesionales multirol y turnos, configuración de publicaciones, citas, auditoría administrativa.
- Aplicación e infraestructura: casos de uso y repositorios Dapper/SQL Server; nueva migración para organización/identidades/citas/auditoría.
- `ResidApp.Web`: pantallas de gestión de estructura, familiares, usuarios/turnos, configuración de publicaciones/citas y auditoría.
- Autorización: política propia del perfil Administración, sin acceso por defecto a contenido clínico ni participación en actos asistenciales.
- Tests: dominio, motor de autorización y casos de uso, incluida la revocación inmediata de autorizaciones familiares.

### Dirección / Coordinación Clínica

Cubre el módulo *Dirección Clínica* de `alcance.md`.

- Historias de usuario: [`docs/historias-usuarios/direccion-coordinacion-clinica.md`](../../historias-usuarios/direccion-coordinacion-clinica.md) (`DIR-01` a `DIR-11`, `COR-01`, `COR-02`).
- Flujo clínico: [`docs/flujos-clinicos/supervision-clinica-direccion.md`](../../flujos-clinicos/supervision-clinica-direccion.md) (`DIR-01` a `DIR-16`).
- Wireframe funcional: [`docs/bocetos-pantallas/wireframes-funcionales/direccion-coordinacion-clinica.md`](../../bocetos-pantallas/wireframes-funcionales/direccion-coordinacion-clinica.md).

Tareas de construcción pendientes:
- Dominio: panel agregado, supervisión de pendientes sin contenido clínico, indicadores/informes agregados sin ranking individual.
- Aplicación e infraestructura: casos de uso e indicadores agregados sobre las tablas ya construidas por el resto de verticales (no genera datos propios).
- `ResidApp.Web`: pantallas de panel, indicadores, informes y acceso puntual a detalle clínico.
- Autorización: obligación de auditoría atómica (`ClinicalDetailAuditObligation`, ver `instrucciones-migracion-net10.md`) en toda lectura de detalle clínico — este perfil nunca valora, indica, ejecuta ni cierra.
- Tests: dominio, motor de autorización (incluida la obligación de auditoría) y casos de uso.

## Transversal — no atado a un solo vertical

- **Motor de autorización por perfil:** el patrón deny-by-default de Claims-Based Authorization ya construido para Residente/Basal (`ResidentBaselinePolicy`) debe generalizarse al resto de operaciones, no reimplementarse desde cero por vertical.
- **Cuentas multirol y perfil activo:** módulos *Acceso* e *Identidades* de `alcance.md` — cada operación depende de un único perfil activo por cuenta; es prerequisito de todos los verticales, no una historia de usuario de ninguno en concreto.
- **Auditoría (`audit_events`):** la obligación de auditar ciertas acciones sensibles (inventariada en [`integridad-sql-basal-legado.md`](../../decisiones-arquitectura/integridad-sql-basal-legado.md)) debe extenderse a cada vertical según se construya, no reinventarse.
- **Historial y corrección (`HIS-01` a `HIS-03`, `COR-01`, `COR-02`):** común a Enfermería y Medicina, con Dirección Clínica en solo lectura — construir una sola vez.
- **Gestión de basal/Barthel (`BAS-*`) y derivación a Urgencias (`DER-*`):** módulos comunes a Enfermería y Medicina — construir una sola vez y no duplicar esquema ni pantallas.

## Explícitamente fuera de este checklist

Estas decisiones no son tareas de construcción del MVP y no se duplican aquí; ya están recogidas en `README.md` ("Decisiones pendientes") y en `roadmap.md` ("Decisiones abiertas heredadas del legado"):

- Proveedor de autenticación y segundo factor productivos.
- SLA, RPO, RTO y copias de seguridad.
- EIPD, contratos y seguridad para tratar datos reales.
- Configuración real de citas, equipos y tiempos de cada centro piloto.
- Criterios de actualización familiar relevante.
- Política de corrección de notas clínicas y su conservación.

No bloquean construir el MVP con datos ficticios, pero sí condicionan cualquier piloto real o despliegue productivo.

## Nota de mantenimiento

Este documento se revisa cada vez que arranca la construcción de un nuevo vertical. Cuando eso ocurra, su detalle técnico (como en Residente/Basal) debe pasar a un fichero propio en `docs/tareas/alta-prioridad/`, dejando aquí solo el enlace, para que este checklist no se convierta en el lugar donde vive el detalle de implementación de cada vertical.
