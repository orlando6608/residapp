# Pendientes del vertical Administración

Estado al 2026-10-01 (tras el bloque 5, el perfil de plataforma, la auditoría y los turnos y equipos). Historias de referencia: [`docs/historias-usuarios/administracion.md`](../../historias-usuarios/administracion.md).
No tiene flujo clínico propio; wireframe [`administracion.md`](../../bocetos-pantallas/wireframes-funcionales/administracion.md).

## Hecho

- **Alta de residente (historia 1, ADM-04)** — antes de este vertical, en Residente/Basal (`ResidentsController/Create`). El basal queda
  pendiente.
- **Bloque 1 (residentes, ficha administrativa y corrección de identidad; ADM-01 a ADM-03, RES-01, RES-03, RES-04; historia 1)** — 2026-10-01.
  - **Qué hace:**
    - `/Administracion` (inicio, ADM-01 mínimo): tarjetas «Residentes», con el número de residentes del ámbito, y «Alta de residente».
      La tarjeta del inicio general para Administración lleva aquí.
    - `/Administracion/Residentes` (ADM-02): nombre, edad calculada (RES-03), sexo documentado, unidad y fecha de alta, con búsqueda por
      nombre sin mayúsculas ni acentos y filtro por unidad.
    - `/Administracion/Residente` (ADM-03): identidad, unidad actual, fecha de alta, historial de ubicación (RES-04) e historial de
      correcciones de identidad.
    - `/Administracion/CorregirIdentidad`: nombre, fecha de nacimiento y sexo documentado, con motivo obligatorio.
    - Ninguna pantalla muestra basal, Barthel ni contenido clínico.
  - **Decisiones del usuario (2026-10-01):**
    - primer bloque = residentes (historia 1);
    - **sin traslado ni baja:** `docs/flujos-clinicos/gestion-basal-barthel.md` declara `D1-P04` diferida y denegada por defecto, y el PRD
      y la matriz la dejan a producto y centro. Se ha preguntado a CJ en `docs/pendientes-cj/traslado-y-baja-residente.html`;
    - la corrección de identidad lleva motivo obligatorio e histórico de solo inserción, además de la auditoría.
  - **Suposiciones aprobadas con el plan:**
    - la lista y la ficha siguen la regla de ámbito de Enfermería (unidades concedidas y, si los restringe, sus residentes), para un ámbito
      activo de Administración;
    - cuenta de desarrollo `dev-integrado-administracion` en el seed del escenario integrado; en Azure la corrección se prueba sobre un
      residente nuevo, nunca sobre Residente Integrado Uno.
  - **Implementación:** script `0021_correccion_identidad_residente`.
    - Tabla de solo inserción `residentes_identidad_correcciones`, con los valores anteriores y nuevos, el motivo y un número por residente
      (`UX_ric_numero`, el token contra el doble envío).
    - `TR_res_identity_guard`: un cambio de nombre, fecha o sexo en `residentes` solo se acepta si coincide con la última corrección. El
      nombre se compara en binario, así que corregir solo mayúsculas o acentos cuenta como cambio.
    - Aplicación: `AdministracionApplicationService`, `IAdministracionResidentDirectory`/`SqlAdministracionResidentDirectory` y
      `IResidentIdentityRepository`/`SqlResidentIdentityRepository`. La corrección pasa por `RequestAuthorizationContextResolver`
      con el target nuevo `AuthorizationTarget.IdentityUpdate` (acción `ResidentIdentityUpdate`, que la política reserva a
      Administración). Validación en `Domain/Residents/ResidentIdentityCorrection.cs`. Auditoría `RESIDENT_IDENTITY_CORRECT`, sin
      los valores.
  - **Tests:** `AdministracionResidentesTests` (integración), `ResidentIdentityCorrectionTests` (unitarios) y
    `AdministrativeResidentListTests` (funcionales).

- **Bloque 2 (familiares, autorizaciones y contacto urgente; ADM-08 a ADM-11 del wireframe, FAM-01; historia 3)** — 2026-10-01.
  - **Qué hace:**
    - la ficha administrativa tiene la sección «Familiares» (nombre, relación, teléfono, correo y estado de la autorización, con
      la etiqueta del contacto urgente) y la sección «Contacto urgente» con su historial;
    - `/Administracion/AnadirFamiliar` y `/Administracion/EditarFamiliar` (vista `Familiar`): nombre, relación (texto libre),
      teléfono (6 a 15 dígitos) y correo opcional. Añadir no abre la autorización (FAM-01);
    - `/Administracion/AutorizacionFamiliar`: estado de hoy, cambios posibles e historial;
    - `/Administracion/ContactoUrgente`: elegir entre los familiares del residente o «Sin contacto urgente».
  - **Decisiones del usuario (2026-10-01):**
    - alcance: familiares, contacto urgente y autorizaciones con estados e historial, que no dan acceso a nada hasta que exista el
      Portal Familiar;
    - el contacto urgente es uno de los familiares vinculados, uno vigente, con historial;
    - la derivación no se toca en este bloque (ver Pendiente);
    - estados del PRD: Pendiente → Activa → Suspendida / Revocada / Caducada, con «válida hasta» opcional.
  - **Suposiciones aprobadas con el plan:**
    - todo se gestiona desde la ficha; el familiar se crea y se vincula al residente en el mismo paso (vincular un familiar ya
      existente a otro residente queda fuera, aunque las tablas lo permiten);
    - la relación es texto libre (no hay catálogo de parentescos decidido); los datos se editan con auditoría, sin versiones;
    - la autorización se abre en Pendiente; Activar vale desde Pendiente, Suspendida o Caducada; Suspender (solo una Activa
      vigente) y Revocar piden motivo y surten efecto al momento; Revocada es final; Caducada no se guarda: es una Activa con
      «válida hasta» pasada (día local, ese día incluido);
    - el contacto urgente no exige autorización activa;
    - se autoriza como la corrección de identidad (`AuthorizationTarget.IdentityUpdate`: Administración y residente de su ámbito).
  - **Implementación:** script `0022_familiares_autorizaciones`.
    - `familiares` (solo cambian nombre, teléfono y correo), `residentes_familiares` (vínculo; solo cambia la relación; sin
      borrado), `familiares_autorizaciones_cambios` (solo inserción, número por vínculo con `UX_fac_numero`;
      `TR_fac_transition` valida cada cambio contra el anterior) y `residentes_contacto_urgente` (solo inserción, número por
      residente; `FK_rcu_vinculo` exige un vínculo del mismo residente).
    - Dominio en `Domain/Families/` (`FamilyMember`, `FamilyAuthorizationRules`); repositorio `SqlResidentFamilyRepository`
      (transacción, conflicto por número esperado, auditoría `FAMILY_MEMBER_CREATE`, `FAMILY_MEMBER_UPDATE`,
      `FAMILY_AUTHORIZATION_CHANGE` y `EMERGENCY_CONTACT_DESIGNATE`, sin datos); lectura en `SqlAdministracionResidentDirectory`.
    - El identificador del familiar es el `OperacionId` del formulario, así que reenviar el alta no lo duplica.
  - **Tests:** `AdministracionFamiliaresTests` (integración), `FamilyRulesTests` (unitarios) y `FamilyAuthorizationDisplayTests`
    (funcionales).

- **Contacto urgente en la derivación y en la ficha de Enfermería y Medicina (sin script)** — 2026-10-01.
  - **Qué hace:** tras firmar la derivación, la pantalla del protocolo muestra el contacto urgente vigente (nombre, relación y
    teléfono pulsable con `tel:`) y precarga «A quién se llama» con «Nombre (relación)», editable; se sigue guardando como texto
    en `intentos_llamada_familia`. La ficha del residente de Enfermería y de Medicina tiene la tarjeta «Contacto urgente» de
    solo lectura (`_ContactoUrgente`). Sin contacto designado, lo dice. El correo no se muestra.
  - **Decisiones del usuario (2026-10-01):** mostrar y precargar, sin guardar el vínculo (sin script); verlo también en la ficha.
  - **Implementación:** `EmergencyContactQuery` (la designación con el número más alto; null si la quitó) la usan
    `SqlChangeInboxDirectory.FindReferralAsync` (`ReferralDetail.EmergencyContact`) y
    `SqlEnfermeriaResidentDirectory.FindEmergencyContactAsync`. El caso de uso `FindEmergencyContact` reutiliza
    `FindScopeResident` (solo Enfermería y Medicina, mismo criterio que abrir la ficha). El informe de derivación no lo incluye
    (DER-04).
  - **Tests:** `ContactoUrgenteLecturaTests` (integración: perfiles, ámbito, vigente, quitado y derivación),
    `ReferralReportBuilderTests` (el informe no lo incluye aunque el detalle lo tenga) y `EmergencyContactDisplayTests`.

- **Bloque 3 (usuarios profesionales, perfiles, unidades y residentes de Auxiliar; ADM-12 y ADM-13, ADM-04; historia 4 sin
  turnos ni permisos)** — 2026-10-01.
  - **Qué hace:**
    - `/Administracion/Usuarios` (ADM-12): las cuentas con algún perfil (vigente o revocado) en el centro, con nombre (o
      identificador), identificador de acceso, estado y perfiles vigentes con sus unidades. Tarjeta «Usuarios» en el inicio.
    - `/Administracion/NuevoUsuario`: identificador de acceso, nombre, perfil y unidades (casillas de las del ámbito de quien
      gestiona). La cuenta nace activa.
    - `/Administracion/Usuario` (ADM-13): datos, «Cambiar nombre» (`NombreUsuario`), «Suspender»/«Reactivar» (`EstadoUsuario`),
      perfiles vigentes y revocados, y «Conceder perfil» (`ConcederPerfil`).
    - `/Administracion/PerfilUsuario`: unidades vigentes (conceder y revocar, `UnidadPerfil`), residentes asignados de un
      perfil Auxiliar (asignar y retirar, `ResidentePerfil`), lo revocado y «Revocar perfil» (`RevocarPerfil`).
  - **Decisiones del usuario (2026-10-01):**
    - dos bloques: este, y los permisos configurables aparte (siguen por SQL; la matriz los pone en «COND política del centro»);
    - nombre visible nuevo en `cuentas`, obligatorio en el alta y editable con auditoría;
    - suspender o reactivar solo si todos los perfiles vigentes de la cuenta son de este centro (ADR 0005: retirar un centro
      no suspende la cuenta global); si no, se revocan los de este centro;
    - la propia cuenta de quien gestiona es de solo lectura.
  - **Suposiciones aprobadas con el plan:**
    - se ven todas las cuentas con algún perfil en el centro; las unidades que se conceden o revocan deben ser del ámbito de quien
      gestiona, y los residentes que se asignan, también (regla de `SqlAdministracionResidentDirectory`);
    - el alta lleva un perfil y al menos una unidad, así que toda cuenta nace ligada al centro. Un identificador que ya existe
      (en cualquier centro, sin distinguir mayúsculas) es un conflicto: vincular una cuenta de otro centro queda fuera;
    - perfiles concedibles: Auxiliar, Enfermería, Medicina, Administración y Dirección Clínica; Familiar no;
    - revocar no pide motivo (las fuentes no lo definen); la última unidad de un perfil no se revoca; lo revocado no se
      reactiva; retirar el último residente de un Auxiliar lo deja sin nadie (ADR 0004);
    - la autorización es de centro, como la de los rangos de referencia: el servicio exige el ámbito activo de Administración
      y el repositorio lo repite dentro de la transacción, que además bloquea la cuenta gestionada (`UPDLOCK`).
  - **Implementación:** script `0023_gestion_cuentas_profesionales` (`cuentas.nombre_visible`, `TR_acc_update_guard`: el sujeto
    externo y la fecha de creación no cambian; `TR_acc_no_delete`). Las concesiones siguen en las tablas de `0002`.
    - Dominio en `Domain/Accounts/ProfessionalAccount.cs`; puertos en `Ports/IProfessionalAccountDirectory.cs`; lectura en
      `SqlProfessionalAccountDirectory` y escritura en `SqlProfessionalAccountRepository`. Auditoría sin datos: `ACCOUNT_CREATE`,
      `ACCOUNT_RENAME`, `ACCOUNT_SUSPEND`, `ACCOUNT_ACTIVATE`, `PROFILE_SCOPE_GRANT`, `PROFILE_SCOPE_REVOKE`,
      `PROFILE_UNIT_GRANT`, `PROFILE_UNIT_REVOKE`, `PROFILE_RESIDENT_GRANT` y `PROFILE_RESIDENT_REVOKE`.
    - El `OperacionId` del formulario es el id de la cuenta (alta) o del ámbito (conceder perfil): reenviar no duplica.
  - **Tests:** `AdministracionUsuariosTests` (integración, con la evidencia de autorización real), `ProfessionalAccountTests`
    (unitarios) y `ProfessionalAccountScreensTests` (funcionales, por HTTP).

- **Bloque 4 (permisos configurables, historia 4; e Inicio por permiso)** — 2026-10-01.
  - **Qué hace:** la pantalla de cada perfil (`PerfilUsuario`) tiene la sección «Permisos» con el catálogo de ese perfil,
    cada uno «no concedido» o vigente (desde cuándo y quién), y «Conceder» o «Revocar» (`PermisoPerfil`). Los revocados
    pasan al historial «Revocado». El Inicio enseña «Alta de residente» a Enfermería y «Rangos de referencia» a Medicina y
    Dirección solo si el ámbito activo tiene el permiso.
  - **Decisiones del usuario (2026-10-01):**
    - la «política del centro» de la matriz la aplica su Administración: concede y revoca cualquier permiso del catálogo
      de cada perfil, con auditoría y sin motivo;
    - `CLINICAL_DETAIL_READ` se concede desde la pantalla: solo habilita lo que ya existe (histórico de basal con finalidad
      «supervisión clínica», auditado);
    - el Inicio respeta los permisos.
  - **Suposiciones aprobadas con el plan:**
    - catálogo (`Domain/Accounts/ProfilePermissions.cs`):
      - Enfermería: alta de residente, basal inicial y reevaluación;
      - Medicina: basal inicial, reevaluación y rangos;
      - Dirección Clínica: lectura clínica detallada y rangos;
      - Auxiliar, Administración y Familiar: ninguno;
      - `BASELINE_DRAFT_CONTRIBUTE` no se ofrece, porque no se usa;
    - se gestionan en la pantalla del perfil, no en el alta;
    - rigen las mismas reglas que el bloque 3 (propia cuenta, perfil vigente, revocar sin borrar).
  - **Implementación:**
    - script `0024_permisos_por_perfil` (`TR_pp_profile_catalog`, THROW 50420: un permiso solo en los perfiles que lo usan;
      `REFERENCE_RANGES_MANAGE` sigue en el trigger de 0008);
    - `SqlProfessionalAccountRepository.GrantPermissionAsync`/`RevokePermissionAsync`, con auditoría
      `PROFILE_PERMISSION_GRANT`/`_REVOKE`;
    - `IProfileScopeDirectoryProvider.ListPermissionsAsync` y el caso de uso `ListActiveScopePermissions` para el Inicio.
  - **Tests:** en `AdministracionUsuariosTests` (evidencia real y trigger), `ProfessionalAccountTests` y
    `ProfessionalAccountScreensTests` (Inicio con y sin el permiso).

- **Bloque 5 (estructura del centro: unidades; ADM-05; historia 2, primer bloque)** — 2026-10-01.
  - **Qué hace:** `/Administracion/Estructura` (tarjeta «Estructura del centro» del inicio) lista las unidades concedidas al ámbito
    activo de Administración, activas e inactivas, con su nombre, código, estado y residentes ubicados. `NuevaUnidad` (código y
    nombre), `NombreUnidad` (renombrar) y `EstadoUnidad` (inactivar o reactivar).
  - **Decisiones del usuario (2026-10-01):** primer bloque de la historia 2 = solo unidades (crear, renombrar, inactivar); edificios,
    plantas, habitaciones, plazas y organigrama quedan fuera. Se prefirió a turnos y equipos porque estos necesitan primero la estructura.
  - **Suposiciones aprobadas con el plan:**
    - Administración gestiona unidades de centros ya provisionados; no crea centros;
    - se ven y gestionan solo las unidades concedidas al ámbito de Administración (deny-by-default, como Usuarios);
    - una unidad nueva se concede al ámbito de quien la crea; desde Usuarios se concede después a cada perfil;
    - el código (2 a 64 caracteres: letras sin acentos, dígitos, «-» y «_»; se guarda en mayúsculas) no cambia; el nombre sí;
    - código y nombre no se repiten dentro del centro (sin distinguir mayúsculas);
    - una unidad con residentes ubicados (ubicación vigente) no se inactiva; una inactiva no se ofrece en el alta de residentes ni en
      Usuarios, que ya filtraban por unidades activas; no se borra.
  - **Implementación:** script `0025_estructura_unidades` (`TR_units_guard`, THROW 50430: centro, código, edificio, planta y fecha de
    creación no cambian; `TR_units_no_delete`, THROW 50431). Sin tablas nuevas: `dbo.unidades` ya existía.
    - Dominio `Domain/Structure/CenterUnit.cs`; puertos `Ports/ICenterStructure.cs`; lectura en `SqlCenterStructureDirectory` y
      escritura en `SqlCenterStructureRepository`, que reutiliza `EnsureAdministratorAsync` y `AuditAsync` de
      `SqlProfessionalAccountRepository` (ahora `internal`). Cada escritura bloquea la fila del centro (`UPDLOCK`) para ordenar los
      cambios de estructura y comprobar código y nombre sin carreras.
    - Auditoría sin datos: `UNIT_CREATE`, `UNIT_RENAME`, `UNIT_DEACTIVATE`, `UNIT_ACTIVATE`.
    - El `OperacionId` del formulario es el id de la unidad: reenviar no la duplica; con otro código o nombre, conflicto.
  - **Tests:** `AdministracionEstructuraTests` (integración), `CenterUnitTests` (unitarios) y
    `Estructura_AdministracionCreaYRenombraUnaUnidad_YOtroPerfilNoEntra` en `ProfessionalAccountScreensTests` (funcional).

- **Perfil de plataforma: alta de un centro nuevo (script `0026`; ADR `0006-perfil-plataforma.md`)** — 2026-10-01. Cierra el hueco del
  primer ámbito de Administración de un centro nuevo.
  - **Qué hace:** un séptimo perfil, `PLATAFORMA`, con la pantalla `/Plataforma` (lista de centros y «Nuevo centro»). El formulario
    crea en una transacción el centro, su primera unidad, la cuenta de su primer administrador (nueva, con nombre), su ámbito de
    Administración y la concesión de la unidad. Desde ahí el administrador usa Estructura, Usuarios y el alta de residentes.
  - **Decisiones del usuario (2026-10-01):** pantalla dentro de la app (no script ni herramienta); perfil de sistema nuevo (no lista
    en configuración); centro + unidad + administrador en un solo paso.
  - **Suposiciones aprobadas con el plan:**
    - el operador pertenece a un centro reservado «Plataforma» (id fijo), porque `ambitos_perfil.centro_id` es `NOT NULL` y toda la
      autorización trabaja con un centro; sin unidades ni residentes;
    - la primera cuenta de plataforma es la raíz de confianza y la pone un seed (desarrollo) o un `INSERT` único (entorno real);
    - el administrador es siempre una cuenta nueva; un identificador existente es un conflicto;
    - el operador no ve datos clínicos ni de residentes, no se concede desde Usuarios y se suspende o revoca por SQL.
  - **Implementación:** script `0026_perfil_plataforma` (`PLATAFORMA` en `CK_ps_profile` y `CK_audit_profile`, el centro reservado y
    `TR_ps_platform_center`, THROW 50440/50441); seed `dev_seed_plataforma.sql` (cuenta `dev-plataforma`). Dominio
    `Domain/Platform/NewCenter.cs`; puertos `Ports/IPlatformCenters.cs`; `PlatformApplicationService`; `SqlPlatformCenterDirectory` y
    `SqlPlatformCenterRepository` (bloquea la fila del centro reservado). Auditoría sin datos: `CENTER_CREATE`, `UNIT_CREATE`,
    `ACCOUNT_CREATE`, `PROFILE_SCOPE_GRANT` y `PROFILE_UNIT_GRANT`. El `OperacionId` del formulario es el id del centro.
  - **Tests:** `PlataformaCentrosTests` (integración, incluido el recorrido del administrador nuevo y la denegación en los demás
    servicios), `NewCenterTests` (unitarios) y `Plataforma_CreaUnCentro_…` en `ProfessionalAccountScreensTests` (funcional).

- **Auditoría administrativa (historia 9; ADM-28 / AUD-01 a AUD-03; sin script)** — 2026-10-01.
  - **Qué hace:** `/Administracion/Auditoria` lista, del más reciente al más antiguo, las acciones administrativas del centro del ámbito
    con cuándo, qué, quién la hizo y con qué perfil, y la cuenta afectada, la unidad y el residente si constan. Se filtra por periodo, tipo de
    acción y cuenta afectada.
  - **Decisiones del usuario (2026-10-01):**
    - solo eventos administrativos, con una lista cerrada de acciones en código (los clínicos no salen);
    - lista filtrable sin totales: filtros por periodo, tipo de acción y cuenta afectada; nada de contadores por persona, orden ni filtro
      por actor (AUD-02: la auditoría no es un ranking de trabajadores).
  - **Suposiciones aprobadas con el plan:**
    - acciones incluidas, por código y sea cual sea el perfil que las hizo: cuentas, perfiles y permisos (concesiones y revocaciones de
      perfil, unidad, residente y permiso), estructura (`CENTER_CREATE` y las de unidades) y residentes y familias (alta, corrección de
      identidad, familiares, autorizaciones y contacto urgente). Fuera: las clínicas, `CLINICAL_DETAIL_READ` y `REFERENCE_RANGES_UPDATE`;
    - ámbito («COND ámbito» de la matriz): el centro del ámbito activo y, en los eventos con unidad, las unidades concedidas al ámbito;
    - periodo por defecto de 30 días, máximo 366, y como mucho 500 filas con aviso;
    - consultar la auditoría no se audita (AUD-01 no lo pide).
  - **Implementación:** sin script (`IX_audit_scope_time` cubre el acceso por centro y fecha). `Domain/Audit/AdministrativeAudit.cs`;
    `Ports/IAdministrativeAuditDirectory.cs`; `SqlAdministrativeAuditDirectory` (una consulta que repite el ámbito de Administración,
    limita a la lista y resuelve los nombres con `LEFT JOIN`); `AdministracionApplicationService.ListAuditAsync`. Los tipos de la
    lectura no tienen texto libre ni contadores. Etiquetas en `AuditActionDisplay`.
  - **Tests:** `AdministracionAuditoriaTests` (integración, con el test por reflexión de AUD-02 y el de solo inserción),
    `AdministrativeAuditTests` (unitarios), `AuditActionDisplayTests` y `Auditoria_Muestra…` en `ProfessionalAccountScreensTests` (funcionales).

- **Turnos y equipos, primer bloque (historia 4; ADM-14, ADM-15, ADM-17; ORG-02, ORG-04 parcial; script `0027`)** — 2026-10-01.
  - **Qué hace:**
    - `/Administracion/Turnos`: catálogo de turnos del centro (nombre y horas). Crear, renombrar, inactivar y reactivar.
    - `/Administracion/Equipos`: equipos con nombre dentro de las unidades del ámbito. Crear, renombrar, inactivar y reactivar; `MiembrosEquipo` añade y da
      de baja cuentas.
    - `/Administracion/Planificacion` (dos semanas, filtro por unidad) y `PlanificarTurno`: un equipo en un turno para un rango de fechas y días de
      la semana, con aviso de solapamientos que Administración decide; `RetirarPlanificacion`.
  - **Decisiones del usuario (2026-10-01):**
    - alcance: catálogo + planificación puntual por unidad y fecha (una o varias fechas), con solapamientos que decide Administración; **sin
      recurrencias ni excepciones** (ADM-16);
    - un equipo es una entidad propia con nombre dentro de una unidad, a la que se asignan personas y turnos.
  - **Suposiciones aprobadas con el plan:**
    - un turno tiene nombre y horas; fin igual o anterior al inicio cruza la medianoche; las horas no se editan (para otras, otro turno);
    - un equipo es de una sola unidad, con nombre único en ella; nada se borra;
    - miembros: cuentas activas con un perfil vigente de Auxiliar, Enfermería o Medicina con la unidad del equipo concedida; una persona puede estar
      en varios equipos; alta y baja con historial;
    - una fila de planificación es (unidad, equipo, turno, fecha); se crea en lote y se retira, nunca se edita; mismo equipo y turno en la misma
      fecha, no dos veces (se omite);
    - conflictos (ADM-17): el mismo equipo en turnos que se solapan, y una persona en dos equipos con turnos que se solapan, calculados con los
      intervalos reales (cruce de medianoche incluido) y solo entre unidades del ámbito; con ellos, solo se guarda con una justificación obligatoria de
      hasta 500 caracteres, que queda en las fechas afectadas;
    - fechas: de 1 a 62 distintas, de hoy a un año vista; solo se retiran las de hoy en adelante;
    - planificar no concede acceso a nada; auditoría con categoría «Turnos y equipos».
  - **Decisión de implementación:** la confirmación de un solapamiento lleva una huella (SHA-256) de los que se enseñaron. Si cambian entre la
    vista y la confirmación, no se confirma y se vuelven a enseñar.
  - **Implementación:** script `0027_turnos_equipos` (`turnos_catalogo`, `equipos`, `equipos_miembros`, `planificacion_turnos`; triggers que impiden cambiar
    horas, centro o unidad, borrar, y reescribir miembros o planificación: solo se revoca o se retira; `UX_sch_active`, `UX_tm_active`).
    - Dominio `Domain/Scheduling/` (`Shift`, `Team`, `SchedulePlan`: fechas, justificación y `FindConflicts` puras);
    - puertos `ISchedulingDirectory`/`ISchedulingRepository` (catálogo, equipos y miembros) e `ISchedulePlanDirectory`/`ISchedulePlanRepository`
      (planificación);
    - `Sql*` correspondientes: cada escritura repite el ámbito de Administración y bloquea la fila del centro; la planificación recalcula los
      conflictos dentro de la transacción;
    - controlador parcial (`AdministracionController.Turnos.cs` y `.Planificacion.cs`);
    - auditoría sin datos: `SHIFT_*`, `TEAM_*`, `TEAM_MEMBER_ADD/REMOVE` (recurso: la cuenta, con la unidad del equipo), `SCHEDULE_CREATE/RETIRE`.
  - **Tests:** `AdministracionTurnosTests` y `AdministracionPlanificacionTests` (integración), `SchedulingTests` y `SchedulePlanTests` (unitarios),
    `TurnosYEquipos_…` y `Planificacion_ConSolapamiento_…` en `ProfessionalAccountScreensTests` (funcionales).

- **Turnos recurrentes con excepciones (historia 4; ADM-16; sin script)** — 2026-10-01.
  - **Qué hace:** una serie son las fechas guardadas en un mismo envío de «Planificar un turno» (el lote ya existía). Hasta **367 fechas** por
    serie (de hoy a un año vista), así que «de lunes a viernes durante todo el año» cabe en un envío. `Planificar un turno` admite
    **«Fechas a saltar»** (festivos; `AAAA-MM-DD` o `DD/MM/AAAA`, separadas por comas, espacios o líneas); `Ver serie` (`/Administracion/SeriePlanificacion`)
    enseña sus fechas activas, retira una fecha suelta (como antes) o **retira la serie desde un día** (`RetirarSerie`; lo anterior a hoy no se toca).
  - **Decisiones del usuario (2026-10-01):** series con horizonte de un año (no una regla sin fecha final) y excepciones tanto al crear como después.
  - **Suposiciones aprobadas con el plan:**
    - se sigue guardando una fila por fecha, así que los solapamientos se calculan igual y la BD no cambia (sin script: `lote_id` ya era la serie);
    - las fechas saltadas no se guardan (no hay fila ni regla que las recree); las que caen fuera del rango o de los días elegidos se ignoran;
    - retirar la serie hace un solo evento de auditoría `SCHEDULE_SERIES_RETIRE` (recurso: el lote, con la unidad), no uno por fecha; retirar una
      fecha suelta sigue siendo `SCHEDULE_RETIRE`;
    - sin fechas activas desde esa fecha, la retirada es un conflicto («ya estaba retirada»); un lote ajeno o de otro ámbito, acceso denegado.
  - **Implementación:** `SchedulePlan.MaxDates` pasa a 367 y la ventana de consulta tiene su propia constante (`MaxListDays` = 62);
    `ISchedulePlanDirectory.FindSeriesAsync` y `ISchedulePlanRepository.RetireSeriesAsync` (`SqlSchedulePlanDirectory`/`SqlSchedulePlanRepository`);
    `FindScheduleSeriesAsync`/`RetireScheduleSeriesAsync` en `AdministracionTurnosApplicationService`; `ScheduleEntry` lleva `BatchId`;
    `PlanFormModel.Saltar`, `SkippedDates()` y `Dates()`; vista `SeriePlanificacion.cshtml`.
  - **Tests:** integración (`AdministracionPlanificacionTests`: serie de 367 fechas, retirar desde una fecha con un solo evento de auditoría, pasado,
    conflicto al repetir, accesos denegados), unitarios (`SchedulePlanTests`) y funcionales (`PlanFormModelTests`; la prueba de la pantalla de
    planificación recorre fechas a saltar inválidas, serie, retirada y acceso denegado a Enfermería).

- **Edificios, plantas, habitaciones y plazas (historia 2; script `0029`)** — 2026-10-02. **Fases 1 y 2 hechas.**
  - **Decisiones del usuario (2026-10-02):** este bloque cubre edificios, plantas, habitaciones y plazas (el organigrama queda fuera: ningún
    documento lo define) y habitación y plaza son opcionales en el alta de residente. Mover a un residente de habitación o plaza es un traslado
    y espera a CJ.
  - **Suposiciones aprobadas con el plan:**
    - edificios y plantas son del centro (como el catálogo de turnos): cualquier Administración del centro los gestiona; nombre único por centro
      (edificio) y por edificio (planta); habitaciones y plazas serán de una unidad (fase 2);
    - una unidad puede tener edificio y planta (opcionales); `TR_units_guard` se recreó para permitir cambiarlos (centro, código y fecha de creación
      siguen inmutables) y `unidades.edificio_id`/`planta_id` tienen ahora claves foráneas compuestas;
    - nada se borra; inactivar un edificio exige que no tenga plantas ni unidades activas, y una planta, que no tenga unidades activas; reactivar
      una planta exige el edificio activo; crear una planta, un edificio activo; colocar una unidad exige edificio y planta activos y de ese edificio;
    - colocar una unidad solo agrupa: no da acceso a nadie ni cambia dónde está ningún residente.
  - **Qué hace la fase 1:** `/Administracion/Edificios` (crear edificios y plantas, renombrar, inactivar y reactivar), `UbicacionUnidad` (una sola lista
    «Edificio (sin planta)» / «Edificio · Planta» por unidad) y la columna «Edificio y planta» de Estructura. Auditoría `BUILDING_*`, `FLOOR_*` y
    `UNIT_LOCATE`.
  - **Implementación:** script `0029` (las cuatro tablas, sus triggers 50461–50468, claves foráneas de `unidades` y de `intervalos_ubicacion_residente`,
    e índice único `UX_rli_place_active`; antes de crearlas comprueba que no hay ids huérfanos); `Domain/Structure/CenterLayout.cs`;
    `ICenterLayoutDirectory`/`ICenterLayoutRepository` con `SqlCenterLayoutDirectory`/`SqlCenterLayoutRepository` (patrón de `SqlCenterStructureRepository`:
    ámbito repetido, bloqueo del centro, idempotencia por `OperacionId`); métodos en `AdministracionEstructuraApplicationService`; controlador parcial
    `AdministracionController.Edificios.cs`; vistas `Edificios`, `NombreEstructura` y `UbicacionUnidad`.
  - **Tests:** `AdministracionEdificiosTests` (integración), `CenterLayoutTests` (unitarios) y `Edificios_…` en `ProfessionalAccountScreensTests` (funcional).
  - **Fase 2 (habitaciones y plazas, alta y ficha):**
    - habitaciones y plazas son de una unidad del ámbito (como los equipos): nombre único por unidad y por habitación; crear exige la unidad (o la
      habitación) activa; inactivar una habitación o plaza exige que no tenga un residente ubicado; reactivar una plaza exige la habitación activa;
    - **alta de residente** (`Residents/Create`, la misma pantalla para Administración y para Enfermería con el permiso de alta): una sola lista opcional
      «Ubicación en la unidad» agrupada por unidad, con «Habitación 12» y «Habitación 12 · Cama A» (`r:{id}` / `p:{id}`). El servidor deduce habitación,
      edificio y planta de lo elegido y de la unidad, **ignora** el edificio y la planta que mande el cliente y valida habitación y plaza (activas, de esa
      unidad y centro, plaza libre); una plaza ocupada es `PLACE_OCCUPIED` (conflicto), otra elección no válida es `PLACE_INVALID`/`ROOM_INVALID`. Dos altas
      a la vez en la misma plaza: gana una (índice `UX_rli_place_active`) y la otra recibe `PLACE_OCCUPIED`;
    - la ficha administrativa muestra habitación y plaza en el historial de ubicación;
    - `/Administracion/Habitaciones?unidadId=` (habitaciones con sus plazas y su ocupación), `NombreHabitacion`/`NombrePlaza` (vista `NombreEstructura`
      compartida con edificios y plantas), `EstadoHabitacion`/`EstadoPlaza`; auditoría `ROOM_*` y `PLACE_*` con la unidad;
    - `ILocationOptionsDirectory`/`SqlLocationOptionsDirectory` y el caso de uso `ListActiveScopeLocations` (hermano de `ListActiveScopeUnits`) dan las
      opciones del alta; `SqlResidentRepository.ResolveLocationAsync` las valida dentro de la transacción;
    - **tests:** `AdministracionHabitacionesTests` (integración: crear, rechazos, renombrar y estado, alta con plaza, alta rechazada, 6 altas simultáneas, inactivar
      con residentes, opciones del alta por perfil, triggers e índice) y `Habitaciones_…` en `ProfessionalAccountScreensTests` (funcional).

## Pendiente

En el orden propuesto (cada bloque se planifica antes de construirlo):

1. **Traslado y baja/reactivación del residente:** bloqueado por CJ (`docs/pendientes-cj/traslado-y-baja-residente.html`).
2. **El organigrama y los cargos (ADM-07):** ningún documento los define; antes hay que preguntar a CJ. Los turnos recurrentes con excepciones (ADM-16), el equipo entrante de los seguimientos y los edificios, plantas, habitaciones y plazas ya están hechos (2026-10-01/02).
3. **Publicaciones familiares (historias 5 y 6), citas (7 y 8) y panel completo (10).**

Huecos de lo ya construido:

- **Alta de residente:** desde el 2026-10-01 la unidad se elige entre las del ámbito activo, y esas unidades se crean, renombran e
  inactivan desde Estructura (bloque 5). Un centro nuevo ya no necesita SQL: el perfil de plataforma lo crea con su primera unidad y
  su administrador. Solo la primera cuenta de plataforma se pone fuera de la aplicación (raíz de confianza, ADR 0006).
- **Inactivar una unidad:** se comprueba que no tenga ubicaciones vigentes dentro de la transacción, pero un alta de residente que
  llegue a la vez no se bloquea (el alta autoriza la unidad antes, y la clave foránea no mira el estado). Es una carrera muy
  estrecha; si importa, habrá que comprobar el estado de la unidad al insertar la ubicación.
- **Turnos y equipos:** las recurrencias son series de hasta un año (una fila por fecha, sin regla abierta): lo que quede después hay que volver a planificarlo. Los seguimientos ya eligen el equipo entrante entre los equipos de la unidad (`0028`), pero no miran el turno planificado.
  Inactivar un equipo o un turno no retira lo ya planificado. Un equipo o turno ya usado no se corrige (las horas
  no cambian: se crea otro). La planificación no se edita: se retira y se vuelve a planificar. La vista solo enseña dos semanas, sin cuadrícula
  semanal ni vista por persona. Los conflictos se calculan entre unidades del ámbito de quien planifica; otra Administración con otro ámbito
  puede planificar sin ver lo de este.
- **Auditoría:** solo cubre las acciones que existen hoy. Las de horarios, citas y retiradas de publicaciones se añadirán a la lista cerrada
  cuando se construyan. No se audita la propia consulta, ni hay exportación. Los eventos de acciones sin unidad (cuentas y perfiles) los
  ve cualquier Administración del centro, aunque la cuenta afectada tenga unidades de otro ámbito.
- **Estructura:** no hay forma de conceder una unidad ya existente a otro ámbito de Administración ni de ver las del centro que no
  estén en el ámbito propio; el organigrama (ADM-07) y los cargos tampoco existen.
- **Niveles de ubicación:** edificios, plantas, habitaciones y plazas existen (script `0029`) y se eligen al dar de alta, pero **cambiar de habitación o de plaza**
  (y liberar una plaza al dar de baja) es un traslado y espera a CJ (`docs/pendientes-cj/traslado-y-baja-residente.html`). Enfermería, Medicina y Auxiliar
  no ven aún la habitación ni la plaza del residente (solo la ficha administrativa). Una unidad o una habitación que se inactive justo mientras llega un
  alta a ella no se bloquea (carrera muy estrecha, como la de las unidades).
- **Usuarios:** no se vincula una cuenta que ya existe en otro centro (llegará con las invitaciones del proveedor de identidad);
  no se restringe por residente a Enfermería, Medicina o Dirección; no hay «Mi cuenta» (ADM-30). Fuera de estas pantallas, el
  nombre de la cuenta solo se usa en la firma del PDF de derivación (2026-10-01). Los permisos no se eligen en el alta ni
  en «Conceder perfil», sino después en la pantalla del perfil.
- **Familiares:** un familiar no se puede vincular a un segundo residente (habría que crearlo otra vez) ni desvincular; la edición
  no tiene token de concurrencia (gana la última). La «fecha efectiva» de ADM-11 es siempre el momento del cambio. Ninguna cuenta
  de familiar existe todavía: llegará con el Portal Familiar y el proveedor de identidad.
