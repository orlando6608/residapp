# Pendientes del vertical Administración

Estado al 2026-10-01 (tras el bloque 4). Historias de referencia: [`docs/historias-usuarios/administracion.md`](../../historias-usuarios/administracion.md).
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

## Pendiente

En el orden propuesto (cada bloque se planifica antes de construirlo):

1. **Traslado y baja/reactivación del residente:** bloqueado por CJ (`docs/pendientes-cj/traslado-y-baja-residente.html`).
2. **Turnos y equipos (historia 4):** sustituirían el «equipo o turno entrante» en texto libre de los seguimientos.
3. **Publicaciones familiares (historias 5 y 6), citas (7 y 8), auditoría administrativa (9) y panel completo (10).**

Huecos de lo ya construido:

- **Alta de residente:** desde el 2026-10-01 la unidad se elige entre las del ámbito activo. Sigue sin haber pantalla de estructura
  del centro (historia 2): las unidades y sus concesiones se crean por SQL.
- **Niveles de ubicación:** los intervalos admiten edificio, planta, habitación y plaza, pero no hay tablas ni pantallas para ellos
  (historia 2); la ficha solo muestra la unidad.
- **Usuarios:** no se vincula una cuenta que ya existe en otro centro (llegará con las invitaciones del proveedor de identidad);
  no se restringe por residente a Enfermería, Medicina o Dirección; no hay «Mi cuenta» (ADM-30). Fuera de estas pantallas, el
  nombre de la cuenta solo se usa en la firma del PDF de derivación (2026-10-01). Los permisos no se eligen en el alta ni
  en «Conceder perfil», sino después en la pantalla del perfil.
- **Familiares:** un familiar no se puede vincular a un segundo residente (habría que crearlo otra vez) ni desvincular; la edición
  no tiene token de concurrencia (gana la última). La «fecha efectiva» de ADM-11 es siempre el momento del cambio. Ninguna cuenta
  de familiar existe todavía: llegará con el Portal Familiar y el proveedor de identidad.
