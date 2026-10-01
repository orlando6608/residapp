# Pendientes del vertical Administración

Estado al 2026-10-01 (tras el bloque 2). Historias de referencia: [`docs/historias-usuarios/administracion.md`](../../historias-usuarios/administracion.md).
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

## Pendiente

En el orden propuesto (cada bloque se planifica antes de construirlo):

1. **Traslado y baja/reactivación del residente:** bloqueado por CJ (`docs/pendientes-cj/traslado-y-baja-residente.html`).
2. **Usuarios profesionales, perfiles, ámbitos y permisos (historia 4 sin turnos):** hoy se conceden por SQL. Las cuentas no tienen
   nombre hasta que exista el proveedor de identidad real.
3. **Turnos y equipos (historia 4):** sustituirían el «equipo o turno entrante» en texto libre de los seguimientos.
4. **Publicaciones familiares (historias 5 y 6), citas (7 y 8), auditoría administrativa (9) y panel completo (10).**

Huecos de lo ya construido:

- **Alta de residente:** desde el 2026-10-01 la unidad se elige entre las del ámbito activo. Sigue sin haber pantalla de estructura
  del centro (historia 2): las unidades y sus concesiones se crean por SQL.
- **Niveles de ubicación:** los intervalos admiten edificio, planta, habitación y plaza, pero no hay tablas ni pantallas para ellos
  (historia 2); la ficha solo muestra la unidad.
- **Familiares:** un familiar no se puede vincular a un segundo residente (habría que crearlo otra vez) ni desvincular; la edición
  no tiene token de concurrencia (gana la última). La «fecha efectiva» de ADM-11 es siempre el momento del cambio. Ninguna cuenta
  de familiar existe todavía: llegará con el Portal Familiar y el proveedor de identidad.
