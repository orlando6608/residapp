# Pendientes del vertical Administración

Estado al 2026-10-01 (tras el bloque 1). Historias de referencia: [`docs/historias-usuarios/administracion.md`](../../historias-usuarios/administracion.md).
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

## Pendiente

En el orden propuesto (cada bloque se planifica antes de construirlo):

1. **Traslado y baja/reactivación del residente:** bloqueado por CJ (`docs/pendientes-cj/traslado-y-baja-residente.html`).
2. **Familiares, autorizaciones y contacto urgente designado (historia 3):** prerrequisito del Portal Familiar y del contacto de la
   derivación (hoy texto libre).
3. **Usuarios profesionales, perfiles, ámbitos y permisos (historia 4 sin turnos):** hoy se conceden por SQL. Las cuentas no tienen
   nombre hasta que exista el proveedor de identidad real.
4. **Turnos y equipos (historia 4):** sustituirían el «equipo o turno entrante» en texto libre de los seguimientos.
5. **Publicaciones familiares (historias 5 y 6), citas (7 y 8), auditoría administrativa (9) y panel completo (10).**

Huecos de lo ya construido:

- **Alta de residente:** desde el 2026-10-01 la unidad se elige entre las del ámbito activo. Sigue sin haber pantalla de estructura
  del centro (historia 2): las unidades y sus concesiones se crean por SQL.
- **Niveles de ubicación:** los intervalos admiten edificio, planta, habitación y plaza, pero no hay tablas ni pantallas para ellos
  (historia 2); la ficha solo muestra la unidad.
