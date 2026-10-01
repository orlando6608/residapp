# Retomar la construcción en una sesión nueva

Estado a 2026-10-01. Actualízalo al cerrar cada bloque de trabajo, para que la siguiente sesión (con
Claude, ChatGPT o una persona) arranque sin reconstruir el contexto.

## Prompt para empezar

> Retomamos la construcción de ResidApp. Lee `CLAUDE.md`, este fichero
> (`docs/prompts-ia/continuar-construccion.md`), `docs/tareas/alta-prioridad/pendientes-enfermeria.md` y
> `docs/tareas/alta-prioridad/pendientes-medicina.md`, `docs/tareas/alta-prioridad/pendientes-direccion.md` y
> `docs/tareas/alta-prioridad/pendientes-administracion.md`.
> Mira si CJ ha completado algo en `docs/pendientes-cj/`. Después comprueba que la suite pasa en verde
> contra la base local y propón un plan para la siguiente tarea pendiente antes de tocar código.

## Dónde estamos
- **Administración, turnos y equipos, primer bloque (historia 4; ADM-14/15/17; script `0027`; sin cambios en el seed):** hecho el
  2026-10-01 en `main`, pusheado y desplegado en Azure (push de `4b3fef4`, run 36913920922 en verde con `build-and-test` y `deploy`, que aplicó `0027`). Dos commits: fase 1 (`838f6f8`, catálogo de turnos, equipos y miembros) y fase 2
  (planificación puntual con conflictos). El script `0027` lleva las cuatro tablas, la de planificación incluida.
  - **Qué hace:** `/Administracion/Turnos`, `/Equipos` (con `MiembrosEquipo`), `/Planificacion` (dos semanas, por unidad) y `PlanificarTurno`
    (un equipo en un turno para un rango de fechas y días de la semana, de 1 a 62 fechas). El servidor avisa de dos solapamientos (el mismo
    equipo en turnos que se solapan; una persona en dos equipos con turnos que se solapan, con el cruce de medianoche) y Administración decide:
    solo se guarda con una justificación, que queda en las fechas afectadas. Las horas de un turno no cambian nunca. Detalle, decisiones y
    suposiciones en `pendientes-administracion.md`.
  - **Pendiente de este bloque:** recurrencias y excepciones (ADM-16) y usar los equipos en los seguimientos de Enfermería y Medicina (siguen con
    texto libre).
  - **Detalle técnico que conviene no redescubrir:**
    - la confirmación de un solapamiento lleva una huella SHA-256 (`ConflictosVistos`) de los que se enseñaron; si cambian, no se confirma;
    - el reenvío de un lote ya guardado enseña «Todas esas fechas ya tienen planificado ese equipo…» (la vista previa corre antes que la
      escritura); el token de lote (`lote_id`) protege el servicio, no esa pantalla;
    - `AdministracionController` es ahora `partial` (`.Turnos.cs`, `.Planificacion.cs`); el parámetro `service` del constructor primario vale en todas
      las partes. **El servicio ya se dividió** (refactor posterior): `AdministracionApplicationService` (residentes y cuentas),
      `AdministracionEstructuraApplicationService` (unidades y auditoría) y `AdministracionTurnosApplicationService` (turnos, equipos y
      planificación); los tres reciben `AdministrationAccessResolver` (el ámbito activo de Administración, que antes era privado). El
      controlador recibe los tres (`service`, `estructura`, `turnos`). En los tests: `Build`, `BuildEstructura` y `BuildTurnos`;
    - **heredocs largos en Git Bash:** varios comandos con un heredoc largo y comillas simples fallaron con «unexpected EOF» sin ejecutar nada.
      Escribe los ficheros con la herramienta de escritura y aplica los `sed` aparte.
  - **Verificación:**
    - suite en verde antes (207, 259 y 46) y después 3 veces en cada fase (224, 267 y 47 tras la fase 1; 232, 276 y 48 tras la 2), más 3 vueltas
      tipo CI con BD nueva y una BD temporal con los 27 scripts y los seeds aplicados dos veces (ya borrada);
    - curl en local con `dev-integrado-administracion` sobre la unidad del escenario integrado: tres turnos (uno nocturno, que sale «termina al
      día siguiente»); dos equipos con `dev-integrado-enfermeria` y `-medicina` como miembros (los elegibles son los de Auxiliar, Enfermería y Medicina
      de la unidad, sin Administración ni Dirección); planificar Mañana en 3 fechas; el mismo equipo y turno otra vez da el aviso de ya planificado; Tarde del
      otro equipo el primer día avisa de que Medicina está en los dos y confirmar sin justificación se rechaza; con ella se guarda y sale
      «Solapamiento justificado»; la noche de un día y la mañana del siguiente no avisan; retirar una fecha funciona y repetirlo avisa; la auditoría
      tiene `SCHEDULE_CREATE` ×6, `SCHEDULE_RETIRE`, `TEAM_CREATE` y `TEAM_MEMBER_ADD`;
    - `dev-integrado-enfermeria`, `-medicina`, `-auxiliar` y `-direccion` reciben «No se puede acceder a esta operación» en Turnos, Equipos y
      Planificación (en `PlanificarTurno` la vi vacía y se corrigió para que también lo diga);
    - dos ejecuciones filtradas de los tests nuevos dieron errores de compilación y de orden de mis propios tests, ya corregidos; ningún fallo de
      entorno en esta tanda.
  - **Datos de prueba en la base local:** turnos «Manana/Tarde/Noche prueba», equipos «Equipo A/B prueba» (en la unidad de la prueba de estructura) y
    «Equipo I1/I2 prueba» (en la del escenario integrado) con su planificación, en octubre de 2026. No se limpian (nada se borra).
  - Copia previa: `ResidApp-antes-0027-20261001.bak`.
- **Administración, auditoría administrativa (historia 9, ADM-28 / AUD-01 a AUD-03; sin script):** hecho el 2026-10-01
  y desplegado en Azure (push de `4c8b389`, run 36905131323 en verde con `build-and-test` y `deploy`; sin script, Azure sigue en `0026`; la pantalla no se ha probado allí).
  - **Qué hace:** `/Administracion/Auditoria` (tarjeta «Auditoría» del inicio) lista, del más reciente al más antiguo, las acciones
    administrativas del centro del ámbito (cuentas, perfiles y permisos, estructura, residentes y familias) con cuándo, qué, quién y con
    qué perfil, y la cuenta afectada, la unidad y el residente si constan. Filtros por periodo (30 días por defecto, máximo 366), tipo de
    acción y cuenta afectada; nunca por quien actuó ni con contadores (AUD-02). Como mucho 500 filas, con aviso. Los eventos con unidad
    solo salen si la unidad está concedida al ámbito. Detalle, decisiones y suposiciones en `pendientes-administracion.md`.
  - **Lista cerrada** (`Domain/Audit/AdministrativeAudit.cs`): una acción nueva no aparece hasta añadirla allí **y** a
    `AuditActionDisplay` (un test comprueba que todas tengan etiqueta). Quedan fuera las clínicas, `CLINICAL_DETAIL_READ` y
    `REFERENCE_RANGES_UPDATE`.
  - **Verificación:**
    - suite en verde antes (196, 251 y 43) y después 3 veces (207, 259 y 46), más 3 vueltas tipo CI con BD nueva (no hay script, así
      que no hizo falta BD temporal);
    - curl en local con `dev-integrado-administracion`: en el periodo por defecto salen 20 tipos de acción administrativa (altas de
      residente, autorizaciones, contacto urgente, permisos, unidades, cuentas…) y ninguno clínico ni con código interno, aunque la
      base tiene unos 97 mil eventos de auditoría; el filtro por acción, el periodo sin eventos, el periodo imposible y los filtros mal
      formados responden bien; `dev-integrado-enfermeria`, `-medicina`, `-auxiliar` y `-direccion` reciben «No se puede acceder a esta
      operación»;
    - `AdministracionAuditoriaTests` (incluido el test por reflexión de AUD-02) comprueban que los tipos de la lectura no tienen texto libre, contadores ni
      filtro por actor.
- **Perfil de plataforma: alta de un centro nuevo (script `0026`; seed `dev_seed_plataforma.sql`; ADR
  `docs/decisiones-arquitectura/0006-perfil-plataforma.md`):** hecho el 2026-10-01 y desplegado en Azure (push de `4029380`, run 36902277867 en verde con `build-and-test` y `deploy`, que aplicó `0026` y el seed: `dev-plataforma` existe en Azure; no se ha probado la pantalla allí).
  - **Qué hace:** séptimo perfil, `PLATAFORMA`, con `/Plataforma`: lista de centros y «Nuevo centro», que crea en una transacción el
    centro, su primera unidad, la cuenta de su primer administrador y su ámbito de Administración con la unidad concedida. El
    operador pertenece a un centro reservado «Plataforma» (`5F3A1C00-0000-4000-8000-000000000001`), porque `ambitos_perfil.centro_id`
    es `NOT NULL`. No ve datos clínicos y los demás servicios lo deniegan. Cuenta de desarrollo: `dev-plataforma`. Detalle, decisiones
    y suposiciones en `pendientes-administracion.md`.
  - **Raíz de confianza:** la primera cuenta de plataforma de un entorno real se inserta por SQL una sola vez (plantilla: el seed). Si
    se hace el push, **hay que crear esa cuenta en Azure** para poder usar la pantalla; el seed de desarrollo ya crea `dev-plataforma`
    allí si el pipeline aplica los seeds (Azure es de desarrollo).
  - **Aviso:** un perfil nuevo toca todos los `switch` por perfil. Se revisaron `SystemProfileDisplay`, `ProfilePermissions` y
    `ProfessionalAccount`, y `ElPerfilDePlataforma_NoEntraEnNingunOtroServicio` comprueba que Administración, Enfermería, Medicina y
    Dirección lo deniegan (Auxiliar no se probó porque su servicio no se construye desde el test). Si se añade un servicio nuevo, añádelo.
  - **Verificación:**
    - suite en verde antes (184, 239 y 42) y después 3 veces (196, 251 y 43), más 3 vueltas tipo CI con BD nueva y una BD temporal
      con los 26 scripts y todos los seeds, aplicados dos veces (una sola cuenta `dev-plataforma`; ya borrada);
    - dos ejecuciones filtradas de los tests nuevos fallaron con «tiempo de espera durante la fase previa al inicio de sesión» (la
      máquina tenía 2,2 GB libres); tras `dotnet build-server shutdown` pasaron los 12 en 1 s. Es el patrón de «Suite lenta»;
    - curl en local con `dev-plataforma`: el Inicio ofrece «Centros»; el formulario vacío da los errores en español; se crea
      «Residencia de prueba plataforma (ficticia)» (`PRUEBA-PLAT-1`, unidad `PLANTA-1`, administrador `dev-prueba-plat-admin`); el
      reenvío no la duplica; el mismo identificador de acceso con otro centro da «Ya existe» y no crea nada; la lista no incluye el
      centro reservado. Con `dev-prueba-plat-admin`: su Inicio es el de Administración, Estructura muestra su unidad, el alta de
      residente la ofrece ya elegida y da de alta «Residente prueba centro nuevo (ficticio)», que sale en su lista;
    - `/Plataforma` da «No se puede acceder a esta operación» a `dev-integrado-enfermeria`, `-medicina`, `-direccion`, `-auxiliar` y
      `-administracion`, y al administrador del centro nuevo.
  - **Datos de prueba en la base local:** el centro «Residencia de prueba plataforma (ficticia)» con su administrador y un residente.
    Hay además centros `FUNC-CENTER-…` de los tests funcionales, que no se limpian.
  - Copia previa: `ResidApp-antes-0026-20261001.bak`.
- **Administración, bloque 5: estructura del centro, unidades (ADM-05; historia 2, primer bloque; script `0025`; sin cambios
  en el seed):** hecho el 2026-10-01 y desplegado en Azure (push de `0a48f3f`, mismo run 36902277867, que aplicó `0025`).
  - **Qué hace:** `/Administracion/Estructura` lista las unidades concedidas al ámbito de Administración (activas e
    inactivas, con código, estado y residentes ubicados), con «Nueva unidad», «Cambiar nombre» e «Inactivar»/«Reactivar». Una
    unidad nueva se concede al ámbito de quien la crea; una con residentes ubicados no se inactiva; código y nombre no se
    repiten en el centro; nada se borra (`TR_units_guard` y `TR_units_no_delete`). Detalle, decisiones y suposiciones en
    `pendientes-administracion.md`.
  - **Verificación:**
    - suite en verde antes (174, 233 y 41) y después 3 veces (184, 239 y 42), más 3 vueltas tipo CI con BD nueva y una BD
      temporal con los 25 scripts y el seed (sin errores, ya borrada);
    - una primera ejecución filtrada de los tests nuevos dio un fallo en `Inactivar_NoValeConResidentesUbicados_…` (41 s); no se
      guardó su mensaje y no se repitió en 7 ejecuciones posteriores (la misma tanda filtrada una vez, la suite completa 3 veces
      y las 3 vueltas tipo CI). No está explicado: puede ser el patrón de «Suite lenta» (se había compilado justo antes), pero no
      está confirmado. Si vuelve, guarda la salida completa;
    - curl en local con `dev-integrado-administracion`: lista inicial, formulario vacío (errores en español), alta de
      «Prueba estructura (ficticia)» (código `PRUEBA-EST-1`; el reenvío no la duplica, el código o el nombre repetidos dan «Ya existe»,
      el código con espacio da el aviso), renombrar (y renombrar igual, rechazado), inactivar (sale del alta de residentes; repetir
      avisa), reactivar (vuelve al alta), inactivar la unidad del escenario con 8 residentes (rechazado), y la auditoría tiene
      `UNIT_CREATE`, `UNIT_RENAME`, `UNIT_DEACTIVATE` y `UNIT_ACTIVATE`. `dev-integrado-enfermeria` recibe «No se puede acceder a
      esta operación» y no ve «Nueva unidad». El POST de Enfermería se probó con el token de la pantalla de entrada, así que su
      rechazo lo cubre el test de integración, no ese curl;
    - la unidad de prueba «Prueba estructura renombrada (ficticia)» queda activa en la base local.
  - Copia previa: `ResidApp-antes-0025-20261001.bak`.
  - Al empezar la sesión se comprobó que `origin/main` y `HEAD` eran el mismo commit (`bf90bda`) y que CJ no había contestado nada.
- **Pantalla de alta de residente según el permiso (sin script):** hecho el 2026-10-01, en `origin/main` (commit `bf90bda`; no se ha revisado su run en Azure). Antes,
  `GET /Residents/Create` abría el formulario a cualquier ámbito y solo se denegaba al guardar. Ahora, con la misma
  regla que `ResidentBaselinePolicy` (Administración siempre; Enfermería con `RESIDENT_IDENTITY_CREATE`), sin permiso sale
  un aviso en lugar del formulario; guardar sigue autorizándose en el servidor. Test
  `AltaDeResidente_ElFormularioSoloSaleSiElAmbitoPuedeDarDeAlta` (falla sin el cambio); suite en verde 3 veces (174, 233
  y 41); curl en local: `dev-multi` (todos sus ámbitos), `dev-integrado-enfermeria` y `dev-prueba-enfermeria-nueva` ven el
  aviso y `dev-integrado-administracion` el formulario; concedido el permiso a `dev-prueba-enfermeria-nueva` vio el
  formulario y dio de alta «Prueba alta pantalla permiso (ficticia)»; revocado otra vez, vuelve el aviso.
- **«Firmar borrador de basal» en el Inicio por permiso (sin script):** hecho el 2026-10-01, en `origin/main` (commit `23e9616`; no se ha revisado su run en Azure). La
  tarjeta sale a Enfermería y Medicina solo con `BASELINE_INITIAL_COMPLETE` o `BASELINE_REEVALUATE` (firmar exige uno u
  otro según el motivo del borrador). Test `Inicio_FirmarBorradorDeBasal_SoloConUnPermisoDeBasal` (falla sin el cambio);
  suite en verde 3 veces (174, 233 y 37); curl en local: `dev-multi` (Enfermería o Medicina, sin permisos) no la ve y
  `dev-integrado-enfermeria`, `dev-integrado-medicina` y `dev-prueba-enfermeria-nueva` sí.
- **Administración, bloque 4: permisos configurables (historia 4; script `0024`; sin cambios en el seed) e Inicio por
  permiso:** hecho el 2026-10-01 y desplegado en Azure (push de `d6629b3`, run 36880340272 en verde). En Azure, con
  `dev-integrado-administracion` sobre `prueba-azure-usuarios-b3` (Enfermería): sus tres permisos salen «no concedidos»;
  conceder «Dar de alta residentes» la hace aparecer en su Inicio; repetirlo avisa del conflicto; `CLINICAL_DETAIL_READ`
  se rechaza; revocarlo la quita y deja el permiso en «Revocado». Quedó sin permisos.
  - **Qué hace:** sección «Permisos» en la pantalla de cada perfil, con el catálogo de ese perfil (Enfermería: alta,
    basal inicial y reevaluación; Medicina: basal inicial, reevaluación y rangos; Dirección: lectura clínica detallada y
    rangos) y «Conceder»/«Revocar». El Inicio solo enseña «Alta de residente» y «Rangos de referencia» con el permiso.
    Detalle, decisiones y suposiciones en `pendientes-administracion.md`.
  - **Verificación:**
    - suite en verde antes (173, 231 y 34) y después 3 veces (174, 233 y 35), más 3 vueltas tipo CI y una BD temporal con
      scripts y seed;
    - curl en local con `dev-integrado-administracion` sobre `dev-prueba-enfermeria-nueva`:
      - la sección muestra sus tres permisos «no concedidos»;
      - concederle «Dar de alta residentes» hace aparecer la tarjeta en su Inicio y da de alta «Prueba alta Enfermeria
        (ficticio)»; repetir la concesión avisa del conflicto; revocarlo quita la tarjeta y deniega el alta;
      - `CLINICAL_DETAIL_READ` y `REFERENCE_RANGES_MANAGE` se rechazan («no es de este perfil»);
      - sin «Basal inicial» no crea el borrador del basal y con él sí;
      - la ficha propia sigue sin acciones;
      - `dev-integrado-direccion` ve «Rangos de referencia» y `dev-integrado-medicina` (sin el permiso) no.
  - **Antes de empezar,** verificado en Azure el push de `c913af8` (runs 36875998442 y 36875998509 en verde). Como
    `prueba-azure-usuarios-b3` se registró el evento «Prueba manual firma con nombre azure: disnea.» sobre «Prueba familiares
    azure (ficticio)», se valoró, se activó el protocolo y se firmó la derivación. El PDF dice «Firmado por Prueba usuarios
    azure (ficticia) (Enfermería, cuenta prueba-azure-usuarios-b3)», con todos los títulos en negrita. Queda abierto, sin
    llamada.
- **Dos correcciones tras el bloque 3 (sin script):** hechas el 2026-10-01 y desplegadas en Azure (push de `c913af8`).
  - **Firma del PDF de derivación:** lleva el nombre visible de la cuenta (`ActiveProfileScope.AccountDisplayName`), su
    perfil y su identificador; sin nombre, como antes. Test `FirmaDerivacionNombreTests`; PDF real revisado.
  - **Ámbito de Dirección:** `SqlSupervisionDirectory.FindScopeAsync` dice «limitado a determinados residentes» con
    cualquier fila de `ambitos_perfil_residente`, aunque esté revocada, como la evidencia de autorización (ADR 0004).
    Test `Ambito_ConSuUnicaAsignacionRevocada_SigueRestringido`.
  - Suite en verde 3 veces (173, 230 y 34) y 3 vueltas tipo CI con BD nueva.
- **PDF de derivación generados a la vez (sin script):** corregido el 2026-10-01 y desplegado en Azure (push de
  `c913af8`). En el PDF de un test, «residente» salía sin negrita en «Identificación del residente y del centro». Un PDF generado solo salía bien; de 16
  generados en paralelo, 2 tenían cambios de fuente de más en el contenido de la página. `ReferralReportPdfRenderer`
  serializa ahora el renderizado con un `Lock` estático; 200 en paralelo salieron idénticos. Test
  `ReferralReportPdfRendererTests` (64 en paralelo comparados con uno solo): falla 3 de 3 sin el candado y pasa con él.
  Suite en verde 3 veces (173, 231 y 34) y 3 vueltas tipo CI (las primeras 3 se hundieron con 1,8 GB libres; tras
  `dotnet build-server shutdown`, limpias).
- **Administración, bloque 3: usuarios profesionales, perfiles, unidades y residentes de Auxiliar (historia 4 sin turnos
  ni permisos; ADM-12 y ADM-13; script `0023`; sin cambios en el seed):** hecho el 2026-10-01 y desplegado en Azure (push
  de `31ab320`, run 36869186733 en verde con `build-and-test` y `deploy`, que aplicó `0023`).
  - **Prueba en Azure (2026-10-01)** con `dev-integrado-administracion`: alta de `prueba-azure-usuarios-b3` («Prueba usuarios
    azure (ficticia)», Enfermería), que entra y ve los 5 residentes; suspenderla deja su sesión sin acceso y reactivarla la
    devuelve; Auxiliar concedido con «Prueba familiares azure (ficticio)» asignado, que es el único que ve; la última unidad
    no se revoca; revocar el Auxiliar deja su sesión sin acceso. `dev-integrado-enfermeria` y `-direccion` no abren
    Usuarios, y la ficha propia es de solo lectura. La cuenta queda activa con Enfermería y el Auxiliar revocado.
  - **Qué hace:** lista de usuarios del centro, alta (identificador de acceso, nombre, perfil y unidades), ficha con nombre,
    suspender/reactivar y conceder perfil, y pantalla de cada perfil con sus unidades, sus residentes (Auxiliar) y revocar.
    Los permisos configurables llegaron en el bloque 4. Detalle, decisiones y suposiciones en
    `pendientes-administracion.md`.
  - **Verificación:**
    - suite en verde antes (161, 219 y 32) y después 3 veces (173, 228 y 34), más 3 vueltas tipo CI con BD nueva y una BD
      temporal con todos los scripts y el seed (sin errores, ya borrada);
    - curl en local con `dev-integrado-administracion`: alta de `dev-prueba-enfermeria-nueva` (el reenvío no duplica; el
      mismo identificador en mayúsculas da «Ya existe»), que entra como Enfermería y ve los 5 residentes de la unidad;
      suspenderla deja su sesión abierta en «No se puede acceder» y sin ámbitos al volver a entrar; suspender otra vez avisa;
      reactivar la devuelve; conceder Auxiliar (ya no ofrece Enfermería), asignar «Prueba familiares (ficticio)» y verlo solo
      a él desde Auxiliar; revocar la última unidad por POST, rechazado; revocar el perfil Auxiliar deja la sesión Auxiliar
      abierta sin acceso; cambiar el nombre y repetirlo, rechazado;
    - `dev-integrado-enfermeria`, `-medicina`, `-auxiliar` y `-direccion` reciben «No se puede acceder a esta operación» en
      la lista y la ficha; la ficha de `dev-integrado-administracion` es de solo lectura; la auditoría tiene cada acción.
  - Una vuelta de los tests de Administración y otra del test funcional nuevo fallaron por tiempos de espera (también
    `sqlcmd` dio *login timeout*), con 1,5 GB libres; tras `dotnet build-server shutdown` pasaron (lección «Suite lenta»).
- **Contacto urgente en la derivación y en la ficha de Enfermería y Medicina (sin script):** hecho el 2026-10-01 y
  desplegado en Azure (push de `fc360fa`, run 36858712803 en verde con `build-and-test` y `deploy`).
  - **Prueba en Azure (2026-10-01)** sobre «Prueba familiares azure (ficticio)»:
    - las fichas de `dev-integrado-enfermeria` y `-medicina` muestran a Tomás (622 555 666, `tel:622555666`, sin correo);
    - evento «Prueba manual contacto urgente azure: disnea.» registrado, valorado, con protocolo y derivado (la vista previa
      no incluye el contacto); el protocolo muestra a Tomás y precarga «Tomás Pérez (ficticio) (Hijo)», y la llamada
      «Contactado» se registró con ese texto. Queda abierto, sin cerrar;
    - la hora de esa llamada quedó en 12:03 en vez de ~14:03 porque el script la calculó con `TZ=Europe/Madrid date` en Git
      Bash (la lección de más abajo: usa `date` sin `TZ`); es un dato de prueba, no un fallo de la app;
    - `dev-integrado-auxiliar` y `-direccion` reciben «No se puede acceder a esta operación» en las fichas y el protocolo, y
      el episodio de Dirección no muestra el contacto.
  - **Qué hace:** tras firmar la derivación, el protocolo muestra el contacto urgente (teléfono con `tel:`) y precarga «A
    quién se llama»; la ficha de Enfermería y de Medicina tiene la tarjeta «Contacto urgente». Detalle en
    `pendientes-administracion.md`.
  - **Verificación:**
    - suite en verde antes (161, 216 y 29) y después 3 veces (161, 219 y 32), más 3 vueltas tipo CI con BD nueva;
    - curl en local sobre «Prueba familiares (ficticio)»: las fichas de Enfermería y Medicina dicen que no hay contacto; tras
      designar a Tomás como Administración, lo muestran (sin el correo); evento «Prueba manual contacto urgente: disnea.»
      registrado, valorado, con protocolo (sin bloque de contacto antes de derivar) y derivado (la vista previa no lo
      incluye); el protocolo muestra el contacto y precarga «Tomás Pérez (ficticio) (Hijo)», y la llamada se registra con
      ese texto;
    - Auxiliar y Dirección no ven el contacto en ninguna pantalla (las de Enfermería y Medicina les dan acceso denegado; el
      episodio de Dirección no lo muestra).
  - Dos vueltas de la suite fallaron por tiempos de espera de ejecución con 0,8–1,2 GB libres (sin bloqueos en SQL Server);
    las mismas pruebas pasaron en cuanto hubo memoria (lección «Suite lenta»).
- **Administración, bloque 2: familiares, autorizaciones y contacto urgente (historia 3; ADM-08 a ADM-11 del wireframe,
  FAM-01; script `0022`; sin cambios en el seed):** hecho el 2026-10-01 y desplegado en Azure (push de `380552b`, run
  36855312637 en verde con `build-and-test` y `deploy`, que aplicó `0022`).
  - **Prueba en Azure (2026-10-01)** con `dev-integrado-administracion` sobre «Prueba familiares azure (ficticio)», dado de
    alta para eso:
    - Lucía (el reenvío del alta no la duplica) y Tomás añadidos, sin autorización;
    - autorización de Lucía: activar antes de abrir rechazado, abrir, activar hasta el 31/12/2026, suspender sin motivo
      rechazado, suspender, reactivar y suspender con número antiguo (conflicto); queda **Activa**;
    - contacto urgente: Lucía, repetido (rechazado), Tomás, número antiguo (conflicto); queda **Tomás**, con su teléfono
      editado a 622 555 666.

    `dev-integrado-enfermeria`, `-medicina`, `-auxiliar` y `-direccion` reciben «No se puede acceder a esta operación» en las
    cuatro pantallas, sin ver datos del familiar, y su POST de suspensión no cambia nada.
  - **Qué hace:** sección «Familiares» y «Contacto urgente» en la ficha administrativa; pantallas Añadir/Editar familiar,
    Autorización (Pendiente → Activa → Suspendida / Revocada / Caducada, con historial) y Contacto urgente. Nada da acceso
    todavía: el Portal Familiar no existe. Detalle, decisiones y suposiciones en `pendientes-administracion.md`.
  - **Verificación:**
    - suite en verde antes de empezar (133, 208 y 27) y después 3 veces seguidas (161, 216 y 29), más 3 vueltas tipo CI con
      BD nueva (23 scripts);
    - `0022` probado a mano en una BD desechable con todos los scripts y el seed (20 casos: transiciones, motivo, numeración,
      inmutabilidad, contacto de otro residente, borrados);
    - curl en local con `dev-integrado-administracion` sobre «Prueba familiares (ficticio)»: añadir (el reenvío no duplica, el
      teléfono «llamar» se rechaza), activar antes de abrir rechazado, abrir, activar con fecha, suspender sin motivo
      rechazado, suspender, activar con fecha pasada rechazado, reactivar, revocar con número antiguo (conflicto), revocar y
      sin más cambios; contacto designado, repetido (rechazado), cambiado, con número antiguo (conflicto), quitado y quitado
      otra vez (rechazado); edición del teléfono y edición sin cambios (rechazada);
    - `dev-integrado-enfermeria`, `-medicina`, `-auxiliar` y `-direccion` reciben «No se puede acceder a esta operación» en
      las cuatro pantallas, sin ver ningún dato del familiar, y sus POST no cambian nada.
  - La primera vuelta de la suite volvió a dar fallos por tiempo de espera (0,8 GB libres); con `dotnet build-server
    shutdown` salió limpia (lección «Suite lenta»).
  - Copia previa: `ResidApp-antes-0022-20261001.bak`.
- **Correcciones (sin script), 2026-10-01:** desplegadas en Azure (push de `2816ce3`, run 36839380770 en verde con
  `build-and-test` y `deploy`). Prueba en Azure: el alta con `dev-integrado-administracion` muestra la unidad vacía y los
  errores en español (unidad vacía o «no-es-un-guid»), sin crear ningún residente; los indicadores del 15/08 al 01/10 con
  `dev-integrado-direccion` siguen dando 4 registrados y 3 cerrados (no hay hechos cerca de medianoche).
  - **Indicadores de Dirección, días en UTC:** las fechas se guardan en UTC, pero el periodo se comparaba con días locales y
    se agrupaba por la fecha UTC, así que un hecho entre las 00:00 y las 02:00 de España contaba en el día (y a fin de mes,
    en el mes) anterior. Ahora `SupervisionIndicatorRules.UtcBounds` convierte los límites del periodo a UTC y `Aggregate`
    agrupa por el día de la zona horaria local del servidor (`TimeZoneInfo.Local`, Europe/Madrid en Azure). Tests nuevos en
    `SupervisionIndicatorRulesTests` (cambio de día en verano y en invierno y el día de 25 horas); el de cambio de día falla
    con el código anterior.
  - **Alta de residente:** `UnidadId` empezaba con el identificador de ceros y sus mensajes salían en inglés. Ahora es
    anulable y empieza vacío; los campos visibles llevan mensajes en español, y `Program.cs` traduce los mensajes del enlace
    del modelo (p. ej., «no-es-un-guid» no es un valor válido para Unidad), que valen para todos los formularios. Test
    funcional `Create_UnidadEmpiezaVacia_YSusErroresSalenEnEspañol` (hoy
    `Create_LaUnidadSeEligeEntreLasDelAmbito_YSusErroresSalenEnEspañol`).
- **Selector de unidad en el alta (sin script), 2026-10-01:** desplegado en Azure (push de `6a433fe`, run 36841516152 en
  verde con `build-and-test` y `deploy`). Prueba en Azure: `dev-integrado-administracion`, `-enfermeria` y `-auxiliar` ven la
  unidad integrada ya elegida; el alta de «Prueba selector unidad azure (ficticio)» como Administración llega a la
  confirmación y sale en la lista, y Auxiliar recibe «No se puede acceder a esta operación».
  **Qué hace:** la unidad ya no se escribe a
  mano: se elige en un `<select>` con las unidades concedidas y activas del ámbito activo
  (`IProfileScopeDirectoryProvider.ListUnitsAsync`, con las mismas condiciones con las que `SqlAuthorizationEvidenceProvider`
  autoriza el alta; caso de uso `ListActiveScopeUnits`). Con una sola unidad viene elegida; con varias empieza en «Elige una
  unidad»; sin ninguna, un aviso sustituye al formulario. El alta sigue autorizando la unidad recibida, así que el selector
  solo orienta. Tests: `ScopeUnitsTests` (integración: revocadas, inactivas, sin conceder, ámbito ajeno y otro centro) y el
  funcional del alta con una y dos unidades. Probado en local con curl: el alta de `dev-integrado-administracion` con la unidad
  del selector llega a la confirmación y Auxiliar sigue recibiendo «No se puede acceder a esta operación».
- **Administración, bloque 1: residentes, ficha administrativa y corrección de identidad (ADM-01 a ADM-03, RES-01,
  RES-03, RES-04; script `0021`; cambia el seed):** hecho el 2026-10-01 y desplegado en Azure (push de `c650ba2`, run
  36833525743 en verde con `build-and-test` y `deploy`, que aplicó `0021` y el seed).
  - **Prueba en Azure (2026-10-01)** con `dev-integrado-administracion` (ámbito único, se selecciona solo):
    - inicio, lista (2 residentes integrados) y «DÓS» encuentra solo a Residente Integrado Dos;
    - ficha de Residente Integrado Dos con su intervalo de ubicación y sin correcciones;
    - alta de «prueba correccion identidad azure (ficticio)» (`4dec3da0-c937-4a35-bf4f-0cf1540380f1`, «No consta») y
      corrección a Mujer con motivo; sin motivo y sin cambios se rechaza, y el reenvío da conflicto;
    - la primera corrección guardó «Correcci%F3n» porque curl en Git Bash envió las tildes en Latin-1 (fallo del envío de
      prueba, no de la app). Una segunda corrección enviada en UTF-8 desde fichero la dejó en «Prueba Corrección Identidad
      Azure (ficticio)»; la ficha muestra las dos. Para probar con tildes por curl, pasa el valor con `--data-urlencode
      "campo@fichero"`.

    `dev-integrado-enfermeria`, `-medicina`, `-auxiliar` y `-direccion` reciben «No se puede acceder a esta operación» en
    la lista, la ficha y el formulario.
  - **Qué hace:** `/Administracion` (inicio), `/Administracion/Residentes` (lista con edad, sexo, unidad y fecha de alta,
    con búsqueda y filtro), `/Administracion/Residente` (ficha con historial de ubicación y de correcciones) y
    `/Administracion/CorregirIdentidad` (con motivo obligatorio). Sin basal ni contenido clínico.
  - **Sin traslado ni baja:** `D1-P04` está diferida en los flujos; se ha preguntado a CJ en
    `docs/pendientes-cj/traslado-y-baja-residente.html`. El detalle y las decisiones están en `pendientes-administracion.md`
    (fichero nuevo).
  - **Verificación:**
    - suite local en verde antes de empezar (125, 201 y 21) y después 3 veces seguidas (131, 206 y 26);
    - 3 vueltas tipo CI con BD nueva en verde;
    - BD temporal con todos los scripts (22) y el seed aplicado dos veces: la cuenta nueva queda una sola vez;
    - `0021` probado a mano en una BD desechable (8 casos del trigger y de la tabla);
    - curl con `dev-integrado-administracion`:
      - inicio (2 residentes), lista, «DÓS» encuentra a Residente Integrado Dos y un filtro mal formado se ignora;
      - ficha de Residente Integrado Dos con su historial de ubicación;
      - alta de «prueba correccion identidad (ficticio)» y su corrección a «Prueba Corrección Identidad (ficticio)», Mujer,
        con motivo; el reenvío da conflicto, sin motivo se rechaza y sin cambios también;
      - la ficha muestra la corrección y la auditoría tiene `RESIDENT_CREATE` y `RESIDENT_IDENTITY_CORRECT`;
    - `dev-integrado-enfermeria`, `-medicina`, `-auxiliar` y `-direccion` reciben «No se puede acceder a esta operación».
  - La primera vuelta completa dio 188 fallos y otra 52, todos tiempos de espera al iniciar sesión en SQL Server, con
    1 GB libre. Ver la lección «Suite lenta»: se resolvió liberando los nodos de MSBuild.
  - Copia previa: `ResidApp-antes-0021-20261001.bak`.
- **Dirección Clínica, bloque 4: indicadores agregados, evolución e informe imprimible (DIR-08, DIR-09, DIR-10, DIR-16; sin
  script):** hecho el 2026-10-01 y desplegado en Azure (push de `60b3c86`, run 36827738565 en verde con `build-and-test` y
  `deploy`).
  - **Prueba en Azure (2026-10-01)** con `dev-integrado-direccion`:
    - el periodo por defecto da 4 registrados (3 de Enfermería y 1 de Medicina), 3 cerrados, 1 escalado, 1 protocolo, 1 derivación
      y 1 indicación sin resolver, que cuadran con los datos de prueba de Azure anotados más abajo;
    - el periodo 15/08–01/10 da los tres meses, con «1 de octubre de 2026» (ICU de Linux);
    - las fechas mal formadas se ignoran, y `desde > hasta` y más de 366 días dan su mensaje;
    - la página no muestra residentes.

    `dev-integrado-enfermeria`, `-medicina` y `-auxiliar` reciben «No se puede acceder a esta operación».
  - **Qué hace:** `/Direccion/Indicadores?desde=&hasta=` muestra, por unidad y en total, los indicadores como «n de m». Son cuatro
    grupos (actividad, escalados y urgencias, indicaciones médicas y continuidad entre turnos), con la evolución mes a mes debajo.
  - **Informe:** «Imprimir / guardar como PDF» deja solo el informe de actividad agregado. Las decisiones y suposiciones están en
    `pendientes-direccion.md`.
  - **Verificación:**
    - suite local en verde antes de empezar (121, 199 y 17) y después 3 veces seguidas (125, 201 y 21);
    - 3 vueltas tipo CI con BD nueva en verde;
    - `IndicatorPeriodFilterTests` y `LocalizationTests` en un contenedor Linux (nombres de los meses con ICU);
    - curl con `dev-integrado-direccion`:
      - el periodo por defecto cuadra con una consulta `sqlcmd` de control: 15 registrados (1, 12 y 2 por origen; 5 prioritarios),
        12 cerrados, 5 escalados, 4 protocolos, 2 derivaciones, 4 indicaciones y una transferencia recibida por perfil;
      - el periodo 15/08–01/10 da tres meses recortados;
      - las fechas mal formadas se ignoran, y `desde > hasta` y más de 366 días dan su mensaje;
      - la página no muestra residentes;
    - `dev-integrado-enfermeria`, `-medicina` y `-auxiliar` reciben «No se puede acceder a esta operación»;
    - impresión revisada con Edge sin interfaz (`msedge --headless --print-to-pdf` sobre la página guardada con `<base href>`): la
      primera versión cortaba las tablas anchas (`table-responsive`), y se corrigió con estilos de impresión en la propia vista.
- **Dirección Clínica, bloque 1: supervisión operativa en solo lectura (DIR-01 a DIR-04, DIR-17; sin script):** hecho el
  2026-09-30 y desplegado en Azure (push de `04c68ce`, run 36762590727 en verde con `build-and-test` y `deploy`). `/Direccion` (contadores por unidad con denominador),
  `/Direccion/Pendientes` (filtro por tipo y unidad), `/Direccion/Episodio` (hitos sin texto) y `/Direccion/Ambito`. Sin
  contenido clínico: los tipos de supervisión no tienen campos de texto. El detalle y las suposiciones están en
  `pendientes-direccion.md` (fichero nuevo).
  - **Prueba en Azure (2026-09-30)** con `dev-integrado-direccion`: inicio con 1 episodio abierto (el escalado de
    ejemplo de Residente Integrado Dos, «en valoración médica»), la fila de la unidad con sus denominadores, pendientes
    con el filtro «Escalado a Medicina» («1 de 1») y valores mal formados ignorados, y el episodio con sus 4 hitos
    (registro, valoración de Enfermería, escalado y valoración médica) sin el texto de la observación. Un episodio
    inexistente vuelve a Pendientes y «Mi ámbito» muestra el centro, la unidad y sus dos permisos.
    `dev-integrado-enfermeria` y `-medicina` reciben «No se puede acceder a esta operación».
  - **Verificación:**
    - suite local en verde 3 veces seguidas (121, 199 y 17) y 3 vueltas tipo CI con BD nueva en verde;
    - curl con `dev-integrado-direccion`: inicio (Residente Integrado Uno y Dos, 3 abiertos), pendientes con filtro y valores
      mal formados ignorados, los 3 episodios sin ningún texto de observación, episodio inexistente vuelve a Pendientes y
      «Mi ámbito»; `dev-integrado-enfermeria`, `-medicina` y `-auxiliar` reciben «No se puede acceder a esta operación».
- **Filtro de la bandeja de cambios ordinarios (ENF-02; sin script):** hecho el 2026-09-30 y desplegado en Azure (push de `8c22c13`, run 36753447680 en verde con `build-and-test` y `deploy`).
  Filtro por nombre, unidad y estado en `/Enfermeria/Ordinarios`, que completa la historia 1
  de Enfermería. El detalle está en `pendientes-enfermeria.md`.
  - **Verificación:**
    - suite local en verde (121, 194 y 17) en la 3.ª vuelta seguida; las dos primeras (la primera tras compilar)
      dieron tiempos de espera de conexión, el patrón de «Suite lenta» (SQL Server responde en 0,1 s desde
      `sqlcmd` y las pruebas aisladas pasan);
    - vueltas tipo CI con BD nueva: la primera (52 fallos por tiempos de espera) y las dos siguientes limpias;
    - curl con `dev-integrado-enfermeria`: sin filtro, nombre («DOS»), estado, valores mal formados ignorados,
      «Ningún evento coincide con la búsqueda.» y «Limpiar»; `dev-integrado-medicina` recibe «No se puede acceder
      a esta operación».

- **Eventos abiertos en la ficha del residente (ENF-18, MED-20; sin script):** hecho el 2026-09-30 y desplegado
  en Azure (push de `685b683`, run 36743950450 en verde con `build-and-test` y `deploy`). En Azure, Residente
  Integrado Dos muestra en las dos fichas el escalado de prueba «En valoración médica» (su «Ver detalle» abre en
  Medicina) y Residente Integrado Uno, «Este residente no tiene eventos abiertos.». Las fichas de Enfermería y
  Medicina muestran los eventos abiertos que ve cada perfil, con su estado y «Ver detalle» (parcial
  `Shared/_EventosAbiertos`). El detalle está en `pendientes-enfermeria.md`.
  - **Verificación:**
    - suite local en verde (121, 194 y 14) y 3 vueltas tipo CI con BD nueva;
    - curl en local:
      - Enfermería ve en Residente Integrado Uno el cambio prioritario de Auxiliar («Dolor / malestar») y en
        Residente Integrado Dos el evento propio de Medicina en valoración médica y el suyo pendiente;
      - Medicina ve en Residente Integrado Dos solo su evento propio, y en Residente Integrado Uno «Este
        residente no tiene eventos abiertos.»;
      - cada «Ver detalle» abre `DetalleCambio` o `Escalado`.
- **Escalados abiertos en Enfermería (sin script):** hecho el 2026-09-30 y desplegado en Azure (push de `3f241d6`,
  run 36733282186 en verde con `build-and-test` y `deploy`).
  - **Prueba en Azure (2026-09-30):** sin escalados, la tarjeta marca 0 y la lista dice «No hay escalados
    abiertos.». Con `dev-integrado-medicina` la lista da «No se puede acceder a esta operación». Después,
    `dev-integrado-enfermeria` registró, valoró y escaló «Prueba manual escalados abiertos: tos productiva.» sobre
    Residente Integrado Dos. La tarjeta pasó a 1 y la lista lo mostró con «Escalado por ti», el motivo y la fecha.
    Al empezar Medicina su valoración, pasó a «En valoración médica». Se deja abierto en Azure como ejemplo.
  - **Qué hace:** tarjeta «Escalados a Medicina» en el inicio de Enfermería y lista `/Enfermeria/Escalados` con
    los eventos que la Enfermería de sus unidades escaló y siguen abiertos en Medicina, marcando «Escalado por
    ti». El detalle está en `pendientes-enfermeria.md`.
  - **Verificación:**
    - suite local en verde (121, 193 y 14) y 3 vueltas tipo CI con BD nueva;
    - curl en local sobre Residente Integrado Dos, con el evento «Prueba manual escalados abiertos: tos
      productiva.», que queda cerrado en la base local:
      - `dev-integrado-enfermeria` lo registra, lo valora y lo escala. La tarjeta pasa a 1 y la lista lo
        muestra «Escalado a Medicina», con «Escalado por ti», el motivo, la unidad y la fecha;
      - `dev-integrado-medicina` recibe «No se puede acceder a esta operación» en `/Enfermeria/Escalados`;
      - al empezar la valoración médica la lista muestra «En valoración médica»;
      - al cerrarlo Medicina, la tarjeta vuelve a 0, la lista dice «No hay escalados abiertos.» y el evento
        aparece en el Historial.
- **Buscador de las listas de residentes (ENF-17, MED-19; sin script):** hecho el 2026-09-30 y desplegado en
  Azure (push de `e6720f2`, run 36722073738 en verde con `build-and-test` y `deploy`). En Azure, con las cuentas
  integradas de Enfermería y Medicina: «uno», «DÓS» (sin acentos ni mayúsculas, con ICU en Linux), estado basal
  y una búsqueda sin coincidencias. Buscar por nombre (sin mayúsculas ni acentos) y filtrar por estado basal y
  unidad, al pulsar «Buscar», en Enfermería y Medicina (parcial `Shared/_FiltroResidentes`). El detalle está en
  `pendientes-enfermeria.md`.
  - **Verificación:**
    - suite local en verde (121, 192 y 14) y 3 vueltas tipo CI con BD nueva;
    - curl con `dev-integrado-enfermeria` y `dev-integrado-medicina`: nombre en mayúsculas y con acentos
      («fictÍCIO Dos»), estado basal, unidad por URL, «N de M residentes», «Ningún residente coincide»,
      «Limpiar», valores mal formados ignorados, y sin ámbito vuelve a la búsqueda tras elegirlo. Los dos
      residentes integrados comparten unidad, así que el desplegable de unidad no aparece en local: se
      prueba en `ResidentListFilterTests`.
- **Historia 8 de Medicina: gestionar el basal (sin script; cambia el seed):** hecho el 2026-09-30 y
  desplegado en Azure (push de `6131d66`, run 36717481881 en verde con `build-and-test` y `deploy`, incluido
  «Aplicar esquema y seed en Azure SQL»).
  - **Prueba en Azure (2026-09-30)** con `dev-integrado-medicina`: la ficha de los dos residentes integrados
    ofrece «Crear borrador de basal», y el módulo del basal abre con las migas de Medicina. No se creó
    ningún borrador, para no tocar el recorrido guiado de CJ, que parte de un Residente Integrado Uno sin
    basal.
  - **Qué hace:** con permiso de basal, la ficha de Medicina ofrece «Crear borrador de basal» o «Iniciar
    reevaluación», que abren el módulo común `EnfermeriaBasal`. Sus migas y redirecciones siguen al perfil
    activo. `dev-integrado-medicina` recibe los dos permisos en el seed del escenario integrado. El detalle
    y las decisiones están en `pendientes-medicina.md` (historia 8).
  - **Verificación:**
    - suite local en verde (121, 192 y 11);
    - vueltas tipo CI con BD nueva: la primera dio 3 fallos en tests no relacionados y tardó 1 min 13 s (se
      solapó con la aplicación del seed a la base local); las tres siguientes, limpias en unos 10 s;
    - el seed, aplicado dos veces a una BD temporal, deja cada permiso una sola vez;
    - curl con `dev-integrado-medicina` sobre Residente Integrado Dos: botón en la ficha, migas de Medicina,
      borrador de alta, nueve áreas, Barthel, confirmación y firma (versión 1, firmada por Medicina, que queda
      en la base local);
    - con los permisos revocados a mano, el botón desaparece y crear el borrador da «No se puede acceder a
      esta operación». Al reaplicar el seed se vuelven a conceder;
    - Enfermería conserva sus migas, y un residente ajeno vuelve a la lista del perfil activo.
- **Contenido de una versión del basal (historia 11 de Enfermería, 9 de Medicina; sin script):** hecho el
  2026-09-30 y desplegado en Azure (push de `7669c20`, run 36715209771 en verde con `build-and-test` y
  `deploy`). En Azure no hay basal firmado: se comprobó que una versión inexistente vuelve al Historial, en
  Enfermería y Medicina, y que los eventos cerrados sin basal no muestran «Ver ese basal».
  - **Qué hace:** «Ver versión» en el Historial y «Ver ese basal» en el detalle de un evento cerrado abren
    `VersionBasal` (Enfermería y Medicina, vista común `Shared/VersionBasal`): cabecera, fuente y fecha de la
    información, las nueve áreas y el Barthel por ítems. Los ítems del Barthel tienen nombre en español, que
    usa también su formulario. El detalle está en `pendientes-enfermeria.md` (historia 11, bloque 4).
  - **Verificación:**
    - suite local en verde (121, 190 y 11) y 3 vueltas tipo CI con BD nueva;
    - curl con `dev-integrado-enfermeria` y `dev-integrado-medicina` sobre Residente Integrado Uno: la versión 1
      con sus nueve áreas y los diez ítems; «Ver ese basal» en los 9 eventos cerrados de Enfermería y en los
      de Medicina; una versión inexistente vuelve al Historial y un residente ajeno a Residentes;
    - el formulario del Barthel con los nombres en español, sobre un borrador de prueba que después se
      canceló.
- **Nombre de campo en el resumen del basal (sin script):** hecho el 2026-09-30 y desplegado en Azure (push
  de `85b43a4`, run 36712405469 en verde con `build-and-test` y `deploy`). En Azure ningún residente del
  escenario integrado tiene basal firmado, así que el resumen con áreas solo se probó en local.
  - **Qué cambia:** `BaselineAreaDisplay.Summarize` da una línea «Campo: valores» por cada campo con dato,
    con el nombre en español de las 35 propiedades de las nueve respuestas de área (`[Display]` en
    `Domain/Baseline/Answers`). Las listas se unen con comas. El informe de derivación escribe cada área como
    «Comunicación — Comprensión: …; Expresión: …».
  - **Test:** `BaselineAreaDisplayTests` comprueba que todo campo de respuesta tiene nombre.
  - **Verificación:**
    - suite local y 3 vueltas tipo CI en verde (una cuarta dio 5 fallos por lentitud y se repitió limpia);
    - curl de `Auxiliar/Basal`.
- **Resumen y etiquetas del basal (sin script):** hecho el 2026-09-30 y desplegado en Azure (push de
  `b8e378d`, run 36710274311 en verde con `build-and-test` y `deploy`).
  - **Prueba en Azure:** con `dev-integrado-enfermeria`, los desplegables de motivo y fuente del basal salen en
    español. `Auxiliar/Basal` responde sin nombres de tipo, pero en Azure Residente Integrado Uno no tiene
    basal firmado, así que no hay áreas que ver: el resumen con áreas solo se probó en local.
  - **Qué cambia:**
    - `BaselineAreaDisplay.Summarize` despliega las respuestas de varias opciones, que antes escribían el
      nombre del tipo de lista;
    - los 151 valores de los catálogos del basal, más `BaselineReason`, llevan `[Display(Name)]` en
      español, transcripción literal de su código;
    - lo usan los `<select>` y las casillas del formulario del basal, la fuente de información, los
      resúmenes y el informe de derivación, que deja su propio resumen y usa el común.
  - **Test:** `BaselineAreaDisplayTests` (funcional) comprueba que todo valor de catálogo tiene etiqueta.
  - **Verificación:**
    - suite local en verde y 3 vueltas tipo CI;
    - curl de `Auxiliar/Basal`, del formulario del basal (7 áreas) y de `Confirmar` con un área de varias
      opciones, sobre un borrador de prueba que después se canceló.

    La primera vuelta local falló por tiempos de espera: SQL Server tenía 216 MB en memoria y tardaba 5 s en
    una consulta trivial (ver «Suite lenta»). Las siguientes pasaron.
- **Parte médica en el detalle de Enfermería (sin script):** hecho el 2026-09-30 y desplegado en Azure
  (push de `ae45d44`, run 36706361068 en verde con `build-and-test` y `deploy`).
  - **Prueba en Azure (2026-09-30)** con `dev-integrado-enfermeria`:
    - el evento propio de Medicina de Residente Integrado Uno muestra la valoración médica, las indicaciones
      y «Ir a Indicaciones»;
    - sus dos eventos sin escalar no muestran nada nuevo.

    En Azure no hay ningún escalado para esos residentes (Residente Integrado Dos no tiene eventos), así que
    las tres tarjetas de un escalado solo se probaron en local.
  - **Qué hace:** el detalle de un evento escalado, o de uno propio de Medicina con indicaciones, muestra en
    solo lectura la valoración médica, las indicaciones y el seguimiento médico, con «Ir a Indicaciones» si
    queda alguna pendiente. El detalle está en `pendientes-enfermeria.md`.
  - **Verificación:**
    - suite local en verde (121, 189 y 7);
    - 3 vueltas tipo CI con BD nueva;
    - curl con `dev-integrado-enfermeria` sobre los 10 eventos de Residente Integrado Uno: tarjetas en los 3
      escalados con valoración médica y en el evento propio de Medicina, y ninguna en los 6 sin escalar.
- **Historial, bloque 3: corrección y rectificación (COR-01/COR-02, script `0020`):** hecho el 2026-09-30 y
  desplegado en Azure (push de `8554dd2`, run 36699847694 en verde con `build-and-test` y `deploy`, incluido
  «Aplicar esquema y seed en Azure SQL»).
  - **Prueba en Azure (2026-09-30)** con `dev-integrado-enfermeria`, sobre Residente Integrado Uno:
    - los dos eventos cerrados antiguos ofrecen «Añadir rectificación»;
    - una rectificación añadida se ve en el detalle y en la línea temporal, y su reenvío da conflicto;
    - «Corregir valoración» fuera de la ventana vuelve al detalle.

    Con `dev-integrado-medicina`: su evento propio ofrece «Añadir rectificación», y el evento no escalado no
    se abre. En Azure no se probó una corrección dentro de la ventana, para no crear un evento solo para
    eso; sí se probó en local.
  - **Política provisional del usuario:** el prototipo no concreta los «objetos habilitados» y `roadmap.md`
    deja la política al centro y al responsable de protección de datos.
    - Solo se corrigen las valoraciones de Enfermería y médica, y solo su autor.
    - Dentro de 6 h (`Correccion:VentanaHoras`), versión corregida con motivo.
    - Después, rectificación añadida.

    El detalle y las suposiciones aprobadas están en `pendientes-enfermeria.md` (historia 11, bloque 3).
  - **Verificación:**
    - suite local en verde 3 veces (121 unitarios, 189 de integración y 7 funcionales);
    - 3 vueltas tipo CI con BD nueva en verde;
    - BD temporal con seed (21 scripts, sin errores) ya borrada;
    - curl con `dev-integrado-enfermeria` sobre Residente Integrado Uno:
      - registro, valoración y cierre de un evento, y corrección dentro de la ventana, con la hora límite, el
        aviso y el motivo;
      - el reenvío del formulario da conflicto;
      - la línea temporal muestra la versión original y la corrección;
      - rectificación de un evento antiguo (sin motivo se rechaza).
    - curl con `dev-integrado-medicina`: rectificación de la valoración médica; ve también la de Enfermería.
      `dev-integrado-auxiliar` no entra en ninguna de las cuatro pantallas.
  - Copia previa: `ResidApp-antes-0020-20260930.bak`.
- **Historial, bloque 2: línea temporal (HIS-02, sin script):** hecho el 2026-09-30 y desplegado en Azure
  (push de `429eae7`, run 36685732287 en verde con `build-and-test` y `deploy`). «Ver línea temporal» desde
  el Historial y desde el detalle de cada evento, en Enfermería y Medicina: cada evento visible con sus hitos
  y su texto, más las versiones del basal y los cambios de ubicación.
  - **Prueba en Azure (2026-09-30)**, sobre Residente Integrado Uno:
    - Enfermería ve 15 hitos, con el PDF de derivación descargable, y Medicina 5;
    - los días salen en español en Linux («martes, 29 de septiembre de 2026») y las horas en hora de España;
    - los enlaces van y vuelven entre la línea temporal y el detalle.
  - **Decisión del usuario:** por ámbito, como la matriz de permisos del prototipo. Sustituye a la del permiso
    nuevo. El detalle está en `pendientes-enfermeria.md` (historia 11, bloque 2).
  - **Verificación:**
    - suite local en verde 3 veces (113 unitarios, 184 de integración y 7 funcionales);
    - 3 vueltas tipo CI con BD nueva en verde;
    - curl con `dev-integrado-enfermeria` y `dev-integrado-medicina`:
      - Residente Integrado Uno: 61 hitos para Enfermería y 38 para Medicina, con los enlaces al detalle en los
        dos sentidos;
      - Residente Integrado Dos: el informe de derivación y las llamadas, con la descarga del PDF;
      - `dev-integrado-auxiliar` no entra.
  - Antes, en el mismo día: push de `c1a0b11` (solo documentación, run 36683643083 en verde) y prueba del
    bloque 1 en Azure con los dos perfiles: Enfermería ve 3 eventos cerrados de Residente Integrado Uno y
    Medicina 1, con el contexto de su fecha (ese residente no tiene basal firmado en Azure).
- **Historial, bloque 1 (historia 11 de Enfermería, 9 de Medicina; script `0019`):** hecho el 2026-09-30 y
  desplegado en Azure: push de `5deb007`, run 36679533522 en verde con `build-and-test` y `deploy` (incluido
  «Aplicar esquema y seed en Azure SQL», con el relleno de la instantánea). El detalle y las decisiones están
  en `pendientes-enfermeria.md` (historia 11, bloque 1).
  - «Ver historial» desde la ficha, en Enfermería y Medicina: eventos cerrados con el basal y la ubicación de
    su fecha (HIS-01, HIS-03) y versiones firmadas del basal (ENF-24).
  - **Verificación:**
    - suite local (113 unitarios, 183 de integración y 7 funcionales) en verde en 6 de 8 vueltas; las 4
      últimas, seguidas y en 5-10 s. Ver la lección «Suite lenta o con tiempos de espera» por las 2 que
      fallaron;
    - 3 vueltas tipo CI con BD nueva en verde;
    - BD temporal con seed (20 scripts, sin errores) ya borrada;
    - relleno en local: 4986 eventos, todos con su instantánea (10 con versión de basal);
    - curl con `dev-integrado-enfermeria` y `dev-integrado-medicina` sobre Residente Integrado Uno (8 y 4
      eventos cerrados, basal versión 1) y Dos (sin basal); detalle cerrado con el basal de su fecha y
      detalle abierto con el vigente; `dev-integrado-auxiliar` no entra.
  - Copia previa: `ResidApp-antes-0019-20260930.bak`.
- **Avisos tras el evento propio de Medicina (script `0018`):** resueltos el 2026-09-29 y desplegados en
  Azure. Push de `5bb39c6`: el run 36630575234 terminó en verde, con `build-and-test` y `deploy` (incluido
  «Aplicar esquema y seed en Azure SQL»), así que `0018` está aplicado en Azure SQL.
  - La ficha del residente de Enfermería ya no dice que la bandeja y la valoración «llegan en un grupo
    posterior».
  - `0018_evento_clinico_perfil_origen` impone en la BD que el perfil de `eventos_clinicos` coincida con el
    origen de `eventos_asistenciales`:
    - columna calculada persistida `evento_clinico_perfil`;
    - clave foránea compuesta `FK_ea_evento_clinico_perfil` sobre `UX_ec_perfil`;
    - vale NULL en `CAMBIO_AUXILIAR`, así que ahí no se comprueba;
    - la prueba el test `OrigenDistintoDelPerfilQueRegistra_LoRechazaLaClaveForanea`, que falla si se quita la
      clave.
  - El inicio (`Home/Index`) muestra solo las fichas del perfil activo, leído de la cookie, y su título es
    «Inicio». **Decisión del usuario:** filtrar solo por perfil, sin consultar permisos, porque el servidor ya
    deniega. Familiar ve «Tu perfil todavía no tiene pantallas disponibles». Nueva frase en el Manual
    (`#entrar-ambito`).
  - **Verificación:**
    - suite local en verde 3 veces (113 unitarios, 178 de integración y 7 funcionales);
    - 3 vueltas tipo CI con BD nueva en verde;
    - BD temporal con seed (19 scripts, sin errores) ya borrada;
    - curl del inicio con `dev-integrado-*`, `dev-admin`, `dev-multi` (Familiar) y sin identidad;
    - registro de un evento de Enfermería y otro de Medicina, con «Prueba manual 0018 …: se puede ignorar»,
      sobre el mismo residente de la base local.
  - Copia previa: `ResidApp-antes-0018-20260929.bak`.
- **Evento propio de Medicina (historia 7 de Medicina, script `0017`):** hecho el 2026-09-29 y desplegado en
  Azure; hasta `0017` está aplicado en Azure SQL. El detalle y las decisiones están en
  `pendientes-medicina.md`. Verificación:
  - suite local en verde 3 veces (113 unitarios, 176 de integración y 7 funcionales);
  - 3 vueltas tipo CI con BD nueva en verde;
  - BD temporal con seed (18 scripts, sin errores) ya borrada;
  - prueba manual con curl:
    - `dev-integrado-medicina`: residentes, ficha, registro con tildes, POST repetido con el mismo
      `OperacionId` (mismo evento, una sola fila), valoración, bandeja, detalle e indicación;
    - `dev-integrado-enfermeria`: ve la indicación y el detalle («Es un evento propio de Medicina…»), y el
      evento no está en sus bandejas;
    - cierre por Medicina («Cerrado por Medicina» para Enfermería).
  - Evento de prueba en la base local: «Prueba manual evento propio Medicina: soplo sistólico no conocido.»,
    sobre Residente Integrado Uno, cerrado sin comunicar y con una indicación pendiente de lectura.
  - **Desplegado en Azure:** push de `eccc69c`; el run 36621253595 terminó en verde, con `build-and-test` y
    `deploy`.
  - **Prueba en Azure (2026-09-29),** sobre «Residente Integrado Uno (ficticio)», con los textos «Prueba
    técnica, se puede ignorar»:
    - con `dev-integrado-medicina`: registro (sale como «observado por Medicina el 29/09/2026 21:50», en hora
      de España), valoración e indicación;
    - con `dev-integrado-enfermeria`: ve la indicación, abre el detalle («Es un evento propio de
      Medicina…») y el evento no está en sus bandejas;
    - cierre por Medicina sin comunicar: Enfermería ve «Cerrado por Medicina» y la indicación sigue en su
      bandeja.
  - Copia previa a `0017`:
    `C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\Backup\ResidApp-antes-0017-20260929.bak`.
- El script `0016` (bloque 2 de la historia 6, derivación a Urgencias en Enfermería y Medicina) se
  desplegó en Azure antes que `0017`: el pipeline de `ca855f2` (run 36607704572) terminó en verde el 2026-09-29, con PDFs
  reales generados en los tests del CI (Linux). El último push (`1ed5382`, solo documentación, run
  36609356298) también terminó en verde.
- **Prueba en Azure de la derivación (2026-09-29):** `dev-integrado-enfermeria`, sobre «Residente
  Integrado Uno (ficticio)», con textos «Prueba técnica, se puede ignorar». Se hizo el recorrido completo:
  evento, valoración, protocolo, vista previa, firma, descarga del PDF, llamada y cierre con una
  comunicación Relevante, que queda pendiente de aprobación en Comunicaciones. El PDF salió correcto: la
  firma dice 19:54, en hora de España (17:54 UTC), y la fuente incrustada y las tildes se ven bien en Linux.
  La hora de su intento de llamada quedó como 17:53 en vez de 19:53: se calculó con `TZ=Europe/Madrid date`
  en Git Bash, que no aplica la zona. Es un registro de prueba inmutable y no afecta a nada más. Para
  calcular horas locales en pruebas con curl, usa el `date` de Git Bash sin `TZ`, porque la máquina ya
  está en hora de Madrid.
- La corrección de la cultura (`es-ES` fija en `Program.cs` con los patrones cortos, `LocalizationTests`)
  está desplegada desde `6f61791` (run 36582061750), y en Azure las fechas salen como `29/09/2026 15:53`
  (antes `09/29/2026`). El pipeline de `21b293f` falló en los tests por la diferencia de formato entre
  Linux y Windows (ver la lección «Cultura en Azure»).
- **Zona horaria (hecho):** la app lee y muestra las horas en la hora local del servidor. La Web App
  `app-residapp-dev` (Linux) tiene `WEBSITE_TIME_ZONE=Europe/Madrid`. Se comprobó el 2026-09-29 con el
  evento «Prueba de hora, se puede ignorar» (Residente Integrado Uno, cerrado con «No comunicar»): se
  registró a las 13:53 UTC y la app mostró las 15:53.
- El primer pipeline de `0013` (`5325ef4`) falló en los tests por interbloqueos en una BD recién creada, y
  no llegó a desplegar. Se corrigió en `f970d83` (`FORCESEEK`, ver las lecciones). En `b060542` se ordenaron
  además las opciones de área en `SqlChangeInboxDirectory.FindAsync`, porque un test fallaba de vez en
  cuando.
- Verificación de `0014`: 6 vueltas tipo CI con BD nueva y 13 vueltas contra la base local en verde. Hubo
  **una** vuelta local fallida en `SeguimientoMedico_Vencido_SigueEnLaBandeja_YSeResuelveConIndicacionOCierre`,
  que tardó 85 s frente a los 5 habituales, sin mensaje guardado. No quedó ningún `xml_deadlock_report`, y el
  log de SQL Server avisa a esa hora de que su memoria se había paginado («performance degradation»). Lo
  más probable es un tiempo de espera por presión de memoria en la máquina, pero no está confirmado. Si
  vuelve a fallar, guarda la salida completa (`--logger "console;verbosity=normal"`).
- Verificación de `0015`: 3 vueltas tipo CI con BD nueva, BD temporal con seed y 5 vueltas locales en
  verde. Dos vueltas locales dieron unos 50 fallos por tiempo de espera agotado (unos 30 s, incluso en tests
  que no tocan el protocolo):
  - las dos eran la primera ejecución de `dotnet test`, que además compilaba;
  - no había bloqueos, interbloqueos ni crecimientos lentos de ficheros (traza por defecto);
  - el log de SQL Server avisaba de memoria paginada, y la máquina tenía 1,9 GB libres de 15,7 GB;
  - repetidas con `--no-build`, pasaron enteras.

  Causa probable: presión de memoria en esta máquina, la misma que en el fallo de `0014`. Consejo:
  `dotnet build src/ResidApp.sln` primero y después `dotnet test src/ResidApp.sln --no-build`.
- Verificación de `0016`:
  - suite local en verde, 3 vueltas tipo CI con BD nueva en verde, y BD temporal con seed (17 scripts, sin
    errores) ya borrada;
  - PDF generado en un contenedor Linux (Docker, `mcr.microsoft.com/dotnet/sdk:10.0`) y revisado: fuente
    incrustada, tildes, «Ñ» y «O₂»;
  - prueba manual con curl en los dos perfiles:
    - derivar sin motivo (rechazado);
    - vista previa (sin el 112 ni las comunicaciones);
    - firmar con una huella antigua tras registrar otra evolución: enseña la vista previa nueva;
    - firmar, repetir la firma, descargar el PDF desde los dos perfiles y cerrar sin llamada, que se
      rechaza;
    - forzar «No comunicar» (rechazado);
    - registrar la llamada y cerrar con una comunicación Relevante.
- El estado de un run se consulta sin autenticación en
  `https://api.github.com/repos/orlando6608/residapp/actions/runs?branch=main`, y el de cada job y paso en
  `.../actions/runs/<id>/jobs`. Los logs piden autenticación y `gh` no está instalado.
- CJ no ha completado nada nuevo (comprobado también en GitHub el 2026-10-01): `decisiones-direccion-basal-derivacion.html`
  sigue con sus 7 respuestas «por definir», y
  `docs/pendientes-cj/rangos-referencia-constantes.html` sigue con 19 huecos «por definir» (14 celdas de la
  tabla y 5 respuestas; antes se contaban mal como 17). El 2026-10-01 se añadió
  `docs/pendientes-cj/traslado-y-baja-residente.html` (6 respuestas).
- Residente/Basal y Auxiliar (historias 1-6) están completados. Enfermería está en curso: historias 1 a 10
  y 11 (salvo la lectura de Dirección Clínica, que es de su vertical), más los rangos de
  referencia de constantes (fase 1 y su pantalla). Medicina está en curso: historias 1 a 8 (escalados,
  valoración médica, indicaciones, cierre médico, seguimiento médico con continuidad entre turnos, protocolo
  urgente, derivación a Urgencias, evento propio y basal con permiso) y 9 (con la misma salvedad que la 11).
- La base local `ResidApp` tiene los scripts `0001` a `0027` registrados en `dbo.scripts_aplicados`
  (en Azure, hasta `0027`). Hay copias previas a `0016` … `0027` en
  `C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\Backup\` (`ResidApp-antes-0016-20260929.bak`,
  `ResidApp-antes-0017-20260929.bak`, `ResidApp-antes-0018-20260929.bak`, `ResidApp-antes-0019-20260930.bak`,
  `ResidApp-antes-0020-20260930.bak`, `ResidApp-antes-0021-20261001.bak`, `ResidApp-antes-0022-20261001.bak`,
  `ResidApp-antes-0023-20261001.bak`, `ResidApp-antes-0024-20261001.bak`, `ResidApp-antes-0025-20261001.bak`, `ResidApp-antes-0026-20261001.bak` y `ResidApp-antes-0027-20261001.bak`). También
  tiene la cuenta `dev-integrado-administracion`, el residente de prueba «Prueba Corrección Identidad (ficticio)», con una
  corrección, «Prueba selector unidad (ficticio)» y «Prueba familiares (ficticio)», con dos familiares (Lucía, autorización
  revocada tras recorrer todos los estados; Tomás, teléfono editado) y el contacto urgente designado, cambiado, quitado y
  vuelto a designar (Tomás); tiene el evento «Prueba manual contacto urgente: disnea.», en protocolo urgente, derivado, con
  una llamada «Contactado» a Tomás, sin cerrar. Y la cuenta `dev-prueba-enfermeria-nueva` («Prueba Enfermería renombrada
  (ficticia)»), dada de alta desde Usuarios: Enfermería vigente en la unidad del escenario (con «Basal inicial»; «Dar de
  alta residentes» concedido y revocado) y un Auxiliar revocado que tuvo asignado a «Prueba familiares (ficticio)». Esa
  cuenta dio de alta a «Prueba alta Enfermeria (ficticio)», que tiene un borrador de basal inicial suyo sin completar.
  La base local tiene además eventos de prueba del escenario integrado:
  - «Prueba manual historia 3: tos.»: cerrado, con comunicación pendiente de aprobación.
  - «Prueba manual historia 4: tos.»: cerrado tras un seguimiento completo.
  - «Prueba manual historia 5: disnea.»: cerrado por Medicina, con una comunicación pendiente y una
    indicación que Enfermería realizó después del cierre. Su motivo de escalado quedó como
    «Desaturaci%F3n…» por la lección de curl de más abajo.
  - «Prueba manual Medicina: fiebre.»: tenía una indicación que Enfermería leyó y realizó. Después pasó a
    seguimiento médico (vencido, con revisión, reprogramación, transferencia con recepción y conservación) y
    se cerró desde el seguimiento.
  - «Prueba manual cierre Enfermería tras 0013: mareo.»: cerrado por Enfermería.
  - «Prueba manual seguimiento médico: disuria.»: escalado, seguimiento médico desde la valoración y
    resuelto con una indicación sin leer. Después, protocolo urgente de Medicina con un contacto, y cerrado
    desde el protocolo.
  - «Prueba manual protocolo urgente: desaturación.»: prioritario de Enfermería, protocolo urgente con
    actuación, contacto con el 112 (hora de 10 minutos antes) y evolución, y cerrado desde el protocolo.
  - «Prueba manual derivacion: desaturación brusca.»: protocolo de Enfermería con un contacto al 112 y dos
    evoluciones, derivado (informe firmado), con una llamada «No contesta» y cerrado con una comunicación
    relevante.
  - «Prueba manual derivacion medica: dolor torácico.»: escalado, protocolo de Medicina, derivado por
    Medicina, con una llamada «Contactado» y cerrado con una comunicación relevante.
  - «Prueba manual escalados abiertos: tos productiva.» (Residente Integrado Dos): escalado por Enfermería,
    valorado y cerrado por Medicina sin comunicación. Residente Integrado Dos tiene además un basal versión 1
    firmado por Medicina (historia 8).
- Suite: 232 unitarios, 276 de integración y 48 funcionales, todos en verde.
- Hay dos scripts con el número `0005` (`0005_auxiliar_opciones_rapidas.sql` y
  `0005_enfermeria_borrador_basal.sql`). Es inofensivo, porque el runner los registra por nombre completo y
  son independientes entre sí. **No los renombres:** el runner los volvería a ejecutar y el despliegue en
  Azure fallaría. Antes de crear un script, comprueba cuál es el último número.

## Siguiente tarea

1. **Revisar las respuestas de CJ:**
   - `docs/pendientes-cj/decisiones-direccion-basal-derivacion.html` (preparado el 2026-09-30, 7 respuestas). Si ha respondido
     al tema 1, el siguiente bloque es el 2 de Dirección (lectura clínica auditada); si ha respondido al tema 2, la aportación a
     un borrador de basal ajeno; el tema 3 es un cambio pequeño en el informe de derivación.
   - `docs/pendientes-cj/traslado-y-baja-residente.html` (preparado el 2026-10-01, 6 respuestas): traslado y baja del
     residente en Administración.
2. **Administración, bloques siguientes** (ver `pendientes-administracion.md`): turnos y equipos (la estructura de unidades ya está hecha; faltan edificios, plantas, habitaciones, plazas y organigrama); después publicaciones,
   citas, auditoría administrativa y panel.
3. **Dirección, bloque 3** (derivaciones y comunicación familiar en solo lectura): necesita la publicación familiar
   (Administración y Familia). La revisión de calidad de proceso (DIR-11) necesita que CJ defina los hitos y plazos.
4. **Familia / Portal Familiar**, que depende de Administración y del proveedor de identidad.

## Avisos abiertos (fuera de alcance, sin corregir)

Se detectaron durante otros bloques. No se han corregido porque quedaban fuera de su alcance; propónselos al
usuario cuando encajen:

- **Inicio y permisos:** desde el bloque 4 de Administración, «Alta de residente» y «Rangos de referencia» solo salen con
  el permiso, y «Firmar borrador de basal» con el de basal inicial o el de reevaluar (2026-10-01). Si cambian las reglas
  de perfiles o permisos de una pantalla, revisa también los `@if` de `Views/Home/Index.cshtml`.- **Datos de prueba en Azure:** Residente Integrado Dos tiene abierto el escalado «Prueba manual escalados
  abiertos: tos productiva.», en valoración médica, que se dejó como ejemplo de la lista de escalados. El
  usuario confirmó el 2026-09-30 que toda la BD de Azure es de desarrollo, así que se pueden crear datos de
  prueba allí.

- **Preguntas para CJ (2026-09-30):** `docs/pendientes-cj/decisiones-direccion-basal-derivacion.html` recoge las finalidades de la lectura clínica de Dirección, la
  aportación a un borrador de basal ajeno y el campo «Comunicaciones» del informe de derivación. Revisa si
  CJ ha respondido antes de proponer el bloque 2 de Dirección o la aportación.
- **CI (anotaciones de GitHub Actions):**
  - `actions/checkout@v4` y `actions/setup-dotnet@v4` usan Node.js 20, que está obsoleto (hoy se fuerzan a
    Node 24);
  - `ubuntu-latest` pasará a Ubuntu 26 a partir del 19 de octubre de 2026. Los dos jobs están fijados a
    `ubuntu-24.04` (el sistema que era `latest`, con la misma ICU), que sigue con el repositorio de `sqlcmd` de
    Ubuntu 22.04 (`.../config/ubuntu/22.04/prod.list`). Hay que revisarlo antes de pasar a Ubuntu 26 o si falla
    «Instalar sqlcmd».

## Reglas de trabajo propias de este repositorio

- **Push:** siempre con confirmación explícita del usuario. El push a `main` aplica los scripts nuevos
  y el seed en Azure SQL.
- **Esquema:** siempre un script nuevo numerado; nunca se edita uno ya aplicado en Azure.
- **Manual:** actualiza `Views/Home/Manual.cshtml` cuando cambie una pantalla que describa o se construya
  un módulo marcado como «Próximamente».
- **Interfaz:** siempre en español, incluidos los enums que aparecen en un `<select>`. Sigue
  `docs/bocetos-pantallas/guia-diseno-sistema-visual.md` (Bootstrap 5, WCAG AA, `btn-lg`, nada de texto
  blanco sobre amarillo).
- **Valores clínicos:** nunca los inventes. Si hace falta uno, se pide a CJ con un documento en
  `docs/pendientes-cj/`.
- **Contradicciones:** si el código contradice `docs/flujos-clinicos/`, para y avisa.
- **Idioma:** responde al usuario siempre en español, también en los mensajes cortos de estado.

## Cómo se ha verificado cada bloque

Repite estos pasos antes de dar un bloque por cerrado:

1. **Plan:** plan aprobado por el usuario antes de tocar código. Las decisiones de producto se le preguntan
   a él, o a CJ con un documento en `docs/pendientes-cj/`.
2. **Copia de la BD local** antes de aplicar un script nuevo:
   `BACKUP DATABASE [ResidApp] TO DISK = N'...\Backup\ResidApp-antes-00XX-AAAAMMDD.bak'`.
3. **Aplicar el script en local:**
   `SQL_SERVER=ACER-ORLANDO SQL_DATABASE=ResidApp SQLCMD_EXTRA="-C" bash database/aplicar-scripts.sh`.
4. **Suite en verde varias veces** contra la BD local y, además, en el entorno tipo CI (ver «Cómo
   reproducir el CI en local» más abajo).
5. **BD temporal nueva** con todos los scripts y el seed (`APLICAR_SEED=1`), que se borra al terminar.
6. **Prueba manual con la app y curl** del flujo completo, con las cuentas `dev-integrado-*`:
   - La app se levanta con `dotnet run --launch-profile http` desde `src/ResidApp.Web`, en
     `http://localhost:5203`.
   - Para entrar, haz un GET de `/DevAuth/Login` para sacar el `__RequestVerificationToken` y un POST
     con `externalSubject=<cuenta>`. Después, `GET /ProfileScope/Select -L`.
   - En cada POST, toma el token de una página que tenga formulario y envíalo con `--data-urlencode`.
7. **Documentación:**
   - `Manual.cshtml`;
   - el pendiente del vertical;
   - este fichero;
   - el checklist y el README si cambia el estado.
8. **Commit en `main` sin push.** El push solo cuando el usuario lo pida. Después, comprueba que el
   pipeline termina en verde, con `build-and-test` y `deploy`.

## Lecciones técnicas que conviene no redescubrir

- **PDFsharp/MigraDoc no admite generar documentos a la vez:** comparten estado de fuentes y un título puede salir con una
  palabra en la fuente equivocada. Cualquier renderizador nuevo debe serializar el renderizado como
  `ReferralReportPdfRenderer`. Para revisar un PDF sin abrirlo, compara el contenido de su página
  (`PdfReader.Open(...).Pages[0].Contents`, `UnfilteredValue`): el texto va con la fuente incrustada y no se busca como
  texto plano.
- **Decimales en formularios:** la cultura del servidor es es-ES. Un decimal enlazado desde
  `type="text"` convierte «37.8» en 378. Usa `type="number" step="0.1"`, que ASP.NET Core enlaza con
  cultura invariante gracias al campo oculto `__Invariant`. En un campo oculto que reenvía un decimal,
  escribe el valor en formato invariante y añade también `__Invariant`.
- **`[Range]` con decimales:** necesita `ParseLimitsInInvariantCulture = true`; si no, da un error 500
  en es-ES.
- **`OUTPUT` en SQL:** no se puede usar `UPDATE ... OUTPUT` sin `INTO` en tablas con triggers. Comprueba
  las filas afectadas con la revisión en el `WHERE`.
- **Códigos de error:** los que lanza la BD o el dominio se traducen a acceso denegado, entrada inválida
  o conflicto mediante patrones en `src/ResidApp.Application/Errors/ApplicationResult.cs`. Un código nuevo
  necesita su patrón allí.
- **Patrones ya asentados** que se reutilizan en vez de reinventarlos:
  - Concurrencia optimista por revisión o versión, con un mensaje de conflicto neutro que conserva lo
    escrito.
  - Tablas de solo inserción con triggers `INSTEAD OF UPDATE, DELETE` que lanzan `THROW`.
  - Comprobación de ámbito deny-by-default en cada caso de uso.
  - Auditoría en `eventos_auditoria`.
  - Cierre de evento común a Enfermería y Medicina en `ClinicalEventCloser`, con la regla de cada perfil en
    `ClinicalEventCloseRule`.
- **Nuevos orígenes del evento:** la vista de «propio» se decide con `Origin != ClinicalEventOrigin.CambioAuxiliar`
  (`_InformacionReunida`, `DetalleCambio`, `ReferralReportBuilder`). Un origen nuevo necesita su
  etiqueta, su rama en `CK_ea_origen` y `CK_ea_inicio`, y revisar `ScopedEventsFrom`. Todo código que
  inserte en `eventos_asistenciales` debe llamar también a `EventContextSnapshot.CaptureAsync` en la misma
  transacción (HIS-03, `0019`); si no, el evento sale en el Historial «Sin datos de contexto».
- **Cambiar una valoración ya cerrada:** desde `0020`, `TR_ve_guard` y `TR_vm_guard` solo lo permiten si
  antes se insertó en `valoraciones_*_correcciones` una fila con el mismo contenido, autor y hora
  (`actualizado_en`), usando el mismo parámetro de fecha en las dos sentencias. La comparación usa
  `INTERSECT`, que trata los `NULL` como iguales. Si se añade una columna de contenido a una valoración, hay
  que añadirla también a su tabla de correcciones y al `INTERSECT` del trigger.
- **Sin Python en esta máquina:** para ediciones en lote, usa Edit o `sed`, no scripts de Python.
- **Nuevos estados del evento:** añádelos también a `ClinicalEventStatusDisplay` (`EnfermeriaModels.cs`).
  Si no, la insignia muestra el nombre interno en inglés. Busca además los filtros por estado en los casos
  de uso y en las consultas (`grep` de los `ClinicalEventStatus.` vecinos). Con `EN_SEGUIMIENTO_MEDICO`,
  `ListMedicalIndications` habría ocultado las indicaciones de un evento que pasa de «con indicación
  pendiente» a seguimiento médico.
- **Comandos locales:** la solución está en `src/ResidApp.sln`, así que se ejecuta
  `dotnet test src/ResidApp.sln`. La app se levanta con `dotnet run --launch-profile http` desde
  `src/ResidApp.Web`. Con `--no-launch-profile` no carga los user-secrets y falla por falta de la cadena de
  conexión.
- **Quién puede leer o hacer algo:** antes de proponer una decisión de autorización, consulta la matriz de
  permisos del prototipo
  (`docs/legado-cloudflare/docs/product/permissions/2026-09-06-matriz-permisos-seis-perfiles-v0.2.1.md`). En el
  bloque 1 del Historial se decidió un permiso nuevo para la línea temporal sin mirarla, y hubo que cambiar
  la decisión en el bloque 2, porque la matriz la da por ámbito.
- **`NULL` sin tipo en una consulta de Dapper:** un `NULL AS Columna` suelto llega como `int`, y Dapper no
  encuentra el constructor del record si el parámetro es `string?`. El caso de uso solo devuelve «No se ha
  podido completar la operación». Escribe `CAST(NULL AS NVARCHAR(…))`. En un test, `Assert.True(r.Ok,
  r.Error?.Message)` ayuda a llegar antes al fallo.
- **Suite lenta o con tiempos de espera:** en el bloque 1 del Historial, dos vueltas de la solución completa
  fallaron: la primera con 51 fallos (tiempos de espera de 30 s, y en `DatabaseTriggerTests` fallos
  inmediatos en cascada) y otra con 1. En esas vueltas las pruebas de integración tardaban de 40 s a 2 min en
  vez de 6 s. Cómo se descartó el código:
  - sin `xml_deadlock_report` en `system_health`;
  - la máquina tenía 1,3 GB libres de 15,7;
  - las pruebas de integración solas pasaron enteras en 10 s;
  - una vuelta completa muestreando `sys.dm_exec_requests` cada 2 s no encontró ningún bloqueo de más de
    0,5 s, y tardó 6 s;
  - las 3 vueltas siguientes pasaron en 5-10 s.

  Si vuelve a pasar, repite este diagnóstico antes de tocar código.

  El 2026-10-01 (bloque 1 de Administración) se repitió a lo grande: 188 y 52 fallos, todos «tiempo de espera durante
  la fase previa al inicio de sesión», con 1 GB libre y SQL Server con 267 MB en memoria. Había 13 nodos de MSBuild
  (unos 1,5 GB) que las compilaciones dejan vivos para reutilizarlos. `dotnet build-server shutdown` los cerró, dejó 2,1 GB
  libres, y las 6 vueltas siguientes (3 locales y 3 tipo CI) salieron limpias en 7-13 s. Antes de una tanda de vueltas,
  compila primero y lanza ese comando.
- **Interbloqueos entre tests:** los tres proyectos de test se ejecutan en paralelo contra la misma BD. Un
  `UPDATE` o una lectura por una columna sin índice recorre la tabla entera y puede provocar interbloqueos
  intermitentes. Pasó con `valoraciones_enfermeria.evento_id` y se resolvió con `IX_ve_evento` en `0011`.
  Toda columna por la que se filtre un `UPDATE` necesita un índice. Si un test falla solo a veces, ejecuta
  `dotnet test src/ResidApp.sln` varias veces y busca `xml_deadlock_report` en `system_health`: está en
  `sys.fn_xe_file_target_read_file('system_health*.xel', ...)`, con `sqlcmd -I`.
- **El índice no basta en una BD recién creada, como la de CI.** El plan se compila con las tablas vacías y
  recorre la clave primaria. Pasó en el CI de `0013`: dos guardados de valoración se bloqueaban en `PK_ve` a
  pesar de `IX_ve_evento`. Por eso las sentencias que leen o actualizan la valoración por evento dentro de
  una transacción de escritura llevan `WITH (FORCESEEK)`, y un `UPDATE` con esa pista se escribe
  `UPDATE v ... FROM tabla v WITH (FORCESEEK)`.
- **Cómo reproducir el CI en local:** crea una BD nueva, aplica solo los scripts, sin seed, y ejecuta
  `dotnet test` en Release con `RESIDAPP_TEST_CONNECTION_STRING` apuntando a ella. Hazlo varias veces, con una
  BD nueva en cada vuelta. Los logs del pipeline piden autenticación y `gh` no está instalado. Una vuelta, en
  Git Bash (compila antes con `dotnet build src/ResidApp.sln -c Release`):

  ```bash
  db=ResidAppCi1
  sqlcmd -S ACER-ORLANDO -E -C -b -Q "IF DB_ID('$db') IS NOT NULL BEGIN ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]; END; CREATE DATABASE [$db]"
  SQL_SERVER=ACER-ORLANDO SQL_DATABASE=$db SQLCMD_EXTRA=-C bash database/aplicar-scripts.sh > /dev/null
  RESIDAPP_TEST_CONNECTION_STRING="Server=ACER-ORLANDO;Database=$db;Integrated Security=True;MultipleActiveResultSets=true;TrustServerCertificate=True" \
    dotnet test src/ResidApp.sln --no-build -c Release
  sqlcmd -S ACER-ORLANDO -E -C -Q "ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]"
  ```

  No la solapes con otra carga sobre SQL Server (aplicar el seed, la app con curl, otra compilación): el
  2026-09-30 una vuelta que coincidió con la aplicación del seed a la base local tardó 1 min 13 s y dio 3
  fallos en tests no relacionados; las tres siguientes, sin nada en paralelo, salieron limpias.
- **Estado de un push en GitHub:** busca su run con `.../actions/runs?head_sha=<sha completo>`; la lista con
  `per_page=1` a veces devuelve un run antiguo. Después, `.../actions/runs/<id>/jobs` da la conclusión de
  `build-and-test` y `deploy`.
- **`sqlcmd` y los índices filtrados:** un `UPDATE` manual sobre una tabla con índices filtrados (como
  `permisos_perfil`) falla con «SET options have incorrect settings: 'QUOTED_IDENTIFIER'» si no se lanza con
  `sqlcmd -I`. Los scripts ya lo llevan (`aplicar-scripts.sh` usa `-I`).
- **Filtros por GET:** un valor mal formado en la URL (por ejemplo `basal=xyz` en un enum) deja un error de
  enlace de modelo en inglés en `ModelState`, que sale en el `asp-validation-summary`. En una acción de lista
  que solo filtra, `ModelState.Clear()` al empezar lo ignora (ver `Residentes`).
- **Buscar sin acentos:** `CultureInfo.InvariantCulture.CompareInfo.IndexOf(texto, busqueda,
  CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace)` funciona igual en Windows y en Linux con ICU
  (probado en Azure: «DÓS» encuentra «Dos»). No actives `InvariantGlobalization`, o dejaría de ignorarlos.
- **Recorrido con curl de un escalado:** Enfermería `RegistrarEvento` (`ResidenteId`, `OperacionId`,
  `Observacion`, `Clasificacion`) → `EmpezarValoracion` (`eventoId`, `revision`) → `Valoracion` (`Form.*`, con
  `Form.Revision` de la página) → `Escalar` (`Form.EventoId`, `Form.Revision`, `Form.Motivo`). Medicina
  `EmpezarValoracion` → `Valoracion` (`Form.HallazgosExploracion`, `Form.Valoracion`) → `Cerrar`
  (`Form.OperacionId`, `Form.Comunicacion=NoComunicar`). Lee cada revisión de la página anterior: sube en
  cada paso.
- **Acentos con curl en Git Bash:** `--data-urlencode` recibe los argumentos en Latin-1, así que «ó» llega
  como `%F3` y ASP.NET lo guarda tal cual. Un navegador envía UTF-8 y se guarda bien. En pruebas con curl,
  escribe los acentos ya codificados en UTF-8 (`--data "Form.Motivo=Desaturaci%C3%B3n"`).
- **Razor:** dentro de un bloque `@if`, el texto que sigue a una etiqueta vacía (`<br />Texto`) se interpreta
  como C#. Envuélvelo en un `<span>`.
- **Vistas comunes a dos perfiles:** en una vista parcial, `asp-action` sin `asp-controller` apunta al
  controlador en curso. Así, `Shared/_ProtocoloUrgente` sirve a Enfermería y a Medicina sin pasarle el
  controlador.
- **`CHECK` antes que el trigger:** en un test de BD que fuerza un estado prohibido, un `CHECK` de la fila
  (como `CK_ea_inicio_medico`) salta antes que `TR_ea_transition_guard` y cambia el mensaje esperado.
- **Cultura en Azure:** el contenedor Linux de la Web App corre con la cultura invariante (fechas
  `MM/dd/yyyy`), y Windows con la del usuario. Por eso la cultura `es-ES` se fija en `Program.cs`, y en local
  no se nota si falta. Además, `es-ES` no formatea igual en los dos sistemas:
  - con ICU (Linux) es `d/M/yyyy H:mm`;
  - con Windows es `dd/MM/yyyy HH:mm`.

  Por eso los patrones cortos se fijan a mano. El primer intento (`21b293f`, run 36579577575) no los fijaba,
  y su test falló en el CI por esa diferencia. Cualquier aserción sobre texto formateado debe probarse
  también en Linux:
  - `docker run --rm -v <repo>:/repo:ro mcr.microsoft.com/dotnet/sdk:10.0`, copiando el repo dentro del
    contenedor y quitando `bin` y `obj`;
  - `LocalizationTests` no necesita BD.
- **PDF (PDFsharp-MigraDoc 6.2.4, MIT):**
  - en Linux (Azure y CI), PDFsharp no lee fuentes del sistema y lanza una excepción sin un `IFontResolver`
    propio;
  - la fuente Liberation Sans (OFL, con su licencia en `Infrastructure/Pdf/Fonts/OFL.txt`) va incrustada
    como recurso y la resuelve `ReferralReportPdfRenderer`, así que el PDF sale igual en Windows y Linux;
  - las horas del PDF se escriben con formato fijo en la hora local del servidor.
- **Idempotencia antes que las reglas de unicidad:** si un caso de uso rechaza «ya existe» antes de llegar
  al repositorio, repetir la misma operación da error en vez de devolver su resultado. La regla de «uno por
  evento» va en la BD, después de consultar `operaciones_idempotencia` (como en `ReferralWriter.SignAsync`).
- **Campos ocultos tras un POST:** `asp-for` pinta el valor enviado (ModelState) y no el del modelo. Para
  una revisión o una huella que el servidor actualiza al volver a mostrar la vista, escribe `value="@..."`
  explícito (ver `Shared/_Derivacion`).
- **Verificación manual con curl:** una cuenta con un solo ámbito se autoselecciona, y
  `ProfileScope/Select` redirige. Sigue las redirecciones con `-L`. Guarda cada página con formulario en un
  fichero y lee de él el token, la revisión y el `OperacionId`. En la prueba en Azure del evento propio, una
  captura en variable salió vacía y la indicación se envió sin revisión: la app la rechazó sin guardar nada,
  pero hubo que repetirla.
