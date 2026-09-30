# Pendientes del vertical Enfermería

Estado al 2026-09-29 (tras la historia 6 completa). Historias de referencia: [`docs/historias-usuarios/enfermeria.md`](../../historias-usuarios/enfermeria.md).
Flujos: [`valoracion-escalado-enfermeria.md`](../../flujos-clinicos/valoracion-escalado-enfermeria.md),
[`gestion-basal-barthel.md`](../../flujos-clinicos/gestion-basal-barthel.md),
[`derivacion-urgencias.md`](../../flujos-clinicos/derivacion-urgencias.md).

## Hecho

- **Historia 1 (bandejas), parcial**: bandeja prioritaria y bandeja de ordinarios
  (`EnfermeriaController/Prioritarios`, `/Ordinarios`) con los cambios que registra Auxiliar y los eventos
  propios de Enfermería, compartidas por unidad, ordenadas de más antiguo a más reciente y con su estado
  real; detalle del evento con la observación original, la temperatura y el basal vigente resumido
  (`/DetalleCambio`).
- **Historia 2 (valorar un evento)** — 2026-09-28: "Empezar valoración" (`/EmpezarValoracion`) registra
  profesional y hora sin crear propiedad; formulario de valoración con hallazgos, valoración, actuaciones,
  comunicaciones, resultado y constantes opcionales (`/Valoracion`, "guardar borrador"). Concurrencia
  optimista por revisión del evento: con una versión desactualizada se exige recargar y no se sobrescribe
  el trabajo ajeno. Script `0007_enfermeria_valoracion`: tabla común `eventos_asistenciales` (ciclo de vida
  de cualquier evento, con el mismo id que su origen) y `valoraciones_enfermeria`.
- **Historia 3 (cerrar y decidir la comunicación familiar)** — 2026-09-28: "Pasar a decisión asistencial"
  desde el detalle con la valoración guardada (`/Decision`, las cuatro salidas; solo cerrar está activa) y
  cierre (`/Cerrar`) con resumen de la valoración y decisión explícita "No comunicar" / "Preparar
  comunicación" (tipo ordinaria o relevante y texto). El cierre exige una valoración guardada, es
  idempotente (`CLINICAL_EVENT_CLOSE` en `operaciones_idempotencia`) y con concurrencia optimista; el
  evento pasa a CERRADO, sale de las bandejas y la valoración queda CERRADA e inmutable. La comunicación
  preparada se guarda en `comunicaciones_familiares` como PENDIENTE_APROBACION y se lista en
  `/Comunicaciones` (tarjeta del inicio). Cada guardado de la valoración deja además una versión inmutable
  en `valoraciones_enfermeria_versiones`. Script `0009_enfermeria_cierre_evento`. Desde `0013` el cierre es
  común con Medicina (`ClinicalEventCloser`) y el formulario de comunicación es el parcial
  `Shared/_ComunicacionFamiliarFormulario`; un evento escalado no lo cierra Enfermería, sino Medicina.
- **Historia 4 (seguimiento y transferencia)** — 2026-09-28: "Iniciar seguimiento" desde la decisión
  asistencial (`/IniciarSeguimiento`, fecha prevista y/o criterio e indicaciones de continuidad); el
  evento pasa a EN_SEGUIMIENTO, sale de ordinarios y prioritarios y entra en la bandeja `/Seguimientos`
  (tarjeta del inicio con contador y vencidos). En `/Seguimiento`: registrar actuaciones, reprogramar con
  justificación, transferir al equipo o turno entrante con nota y confirmar la recepción, cada acción con
  su autoría. Un seguimiento vencido sigue abierto y visible. "Resolver" vuelve a `/Decision`, desde donde
  se cierra (la valoración sigue en borrador durante el seguimiento y se cierra al cerrar el evento).
  Tablas de solo inserción `seguimientos` y `seguimiento_acciones`; script `0010_enfermeria_seguimiento`.
- **Historia 5 (escalado a Medicina)** — 2026-09-28: "Escalar a Medicina" desde la decisión asistencial
  (con la valoración guardada o al resolver un seguimiento) en `/Escalar`, que muestra la información
  reunida (parcial `Shared/_InformacionReunida`) y exige el motivo. El evento pasa a ESCALADO_MEDICINA, sale
  de las bandejas de Enfermería y la valoración queda CERRADA. Tabla de solo inserción `escalados_medicina`;
  script `0011_enfermeria_escalado_medicina`. Del lado de Medicina se construyó solo su historia 1 en
  lectura (`MedicinaController`: inicio, bandeja `/Medicina/Escalados` y detalle `/Medicina/Escalado`):
  un ámbito de Medicina solo ve eventos escalados de sus unidades. Cuenta de desarrollo
  `dev-integrado-medicina`.
- **Historia 6, bloque 1 (protocolo urgente)** — 2026-09-29: "Activar protocolo urgente" desde la decisión
  asistencial (con la valoración guardada o al resolver un seguimiento) en `/ActivarProtocolo`. Es un solo
  paso con nota opcional, para no retrasar la atención. El evento pasa a PROTOCOLO_URGENTE, sale de las
  bandejas y entra en `/Enfermeria/Protocolos` (tarjeta del inicio). En `/Protocolo` se registran actuaciones,
  evolución y contactos con servicios (servicio y hora del contacto, no futura, solo en la trazabilidad
  interna, DER-04), cada uno con su autoría. Desde el protocolo solo se cierra (el cierre común). **Decisiones
  del usuario (2026-09-29):**
  - dos bloques;
  - desde el protocolo, derivar o cerrar, sin escalar;
  - el protocolo pertenece al perfil que lo activa (Medicina tiene PROTOCOLO_URGENTE_MEDICO) y el otro lo ve
    en solo lectura;
  - las horas se leen y muestran en la hora local del servidor, y el usuario configura Europe/Madrid en la
    Azure Web App.

  Módulo común a los dos perfiles: `UrgentProtocolWriter`, parciales `Shared/_ProtocoloUrgente*`. Tablas de
  solo inserción `protocolos_urgentes` y `protocolo_urgente_registros`; script `0015_protocolo_urgente`.
- **Historia 6, bloque 2 (derivación a Urgencias)** — 2026-09-29: "Derivar a Urgencias" desde el protocolo
  urgente del propio perfil (`/Derivar`, DER-01 a DER-06, común con Medicina).
  - **Informe:** los datos automáticos los reúne `ReferralReportBuilder` (Web) y no se editan:
    identificación y centro, basal con Barthel, cognición, comunicación y el resto de áreas, observación,
    valoraciones, constantes y oxigenoterapia, actuaciones y evolución. Nunca incluye CFS, los contactos con
    servicios ni el campo "Comunicaciones" de la valoración de Enfermería (DER-04). El profesional escribe el
    motivo (obligatorio) y la información adicional (opcional).
  - **Vista previa y firma:** la vista previa es obligatoria y muestra la huella SHA-256 del contenido. Firmar
    exige la misma huella: si el protocolo cambió, se enseña la vista previa nueva. La firma es idempotente
    (`REFERRAL_REPORT_SIGN`) y electrónica simple (cuenta, fecha y hora, y huella).
  - **PDF:** se genera con PDFsharp-MigraDoc 6.2.4 y la fuente Liberation Sans incrustada (OFL). Se guarda
    inmutable en `informes_derivacion` con su huella, y se descarga desde los dos perfiles con auditoría
    `REFERRAL_REPORT_DOWNLOAD`.
  - **Tras firmar:** el evento sigue en el protocolo y se registran los intentos de llamada a la familia
    (`intentos_llamada_familia`: a quién en texto libre, hora no futura, resultado y nota).
  - **Cierre de un evento derivado:** exige al menos un intento y una comunicación Relevante; "No
    comunicar" no se ofrece.
  - **Decisiones del usuario (2026-09-29):**
    - el evento sigue en el protocolo;
    - datos fijos y dos campos editables;
    - actualización relevante y llamada obligatorias al cerrar;
    - PDF descargable por los dos perfiles con auditoría.

  Script `0016_derivacion_urgencias`.
- **Historia 7 (indicaciones de Medicina)** — 2026-09-28: bandeja `/Enfermeria/Indicaciones` (tarjeta del
  inicio con contador y no leídas), compartida por unidad, con "Confirmar lectura" y, ya leída,
  "Registrar como realizada" o "como no realizada" con incidencia obligatoria (`/ProgresoIndicacion`).
  Hitos distintos, con concurrencia optimista por la revisión de la indicación; ninguna caduca, tampoco si
  Medicina cierra el evento (historia 4 de Medicina, 2026-09-28): siguen aquí hasta registrar su
  resultado. Tabla `indicaciones_medicas`, script `0012_medicina_valoracion_indicaciones` (lo que emite Medicina está en
  `pendientes-medicina.md`).
- **Historia 8 (evento propio)**: registro de un evento observado por Enfermería (`/RegistrarEvento`,
  tabla `eventos_clinicos`, script `0006`), inmutable; al guardarlo se continúa en su detalle y entra en
  las bandejas con su autoría real. Desde `0017` Medicina registra también su evento propio (historia 7 de
  Medicina) con los mismos casos de uso; ese evento no entra en las bandejas de Enfermería, que lo ve en el
  detalle ("evento propio de Medicina", sin valoración de Enfermería) si recibe una indicación suya. Desde
  `0018` la BD exige que el perfil que registra el evento coincida con su origen (clave foránea compuesta
  `FK_ea_evento_clinico_perfil`).
- **Historias 9 y 10 (basal)**: crear borrador, completar las 9 áreas y el Barthel, cancelar, confirmar y
  firmar (`EnfermeriaBasalController` → `BaselineController/Sign`), con concurrencia optimista e
  idempotencia (script `0005_enfermeria_borrador_basal`).
- Residentes del ámbito y ficha del residente (`/Residentes`, `/Residente`).
- Tests de integración: `EnfermeriaApplicationServiceTests` (incluidos el cierre, el seguimiento, el protocolo urgente y el
  escalado), `MedicinaApplicationServiceTests` (incluidas las indicaciones leídas y registradas por Enfermería), `SqlChangeInboxDirectoryTests`,
  `SqlClinicalEventRepositoryTests`, `SqlEnfermeriaResidentDirectoryTests`, `SqlBaselineRepositoryDraftTests`.

- **Historia 11, bloque 1 (Historial: eventos cerrados y versiones del basal)** — 2026-09-30, común con la
  historia 9 de Medicina. «Ver historial» desde la ficha (`/Enfermeria/Historial`, vista común
  `Shared/Historial`):
  - **Eventos cerrados (HIS-01, ENF-23):** los del residente, con la misma regla de ámbito que las bandejas,
    del cierre más reciente al más antiguo. Se abren en el detalle de solo lectura que ya existía.
  - **Contexto de su fecha (HIS-03):** cada evento guarda al crearse la versión del basal y el intervalo de
    ubicación vigentes (`eventos_contexto`, de solo inserción). Lo escriben `SqlClinicalEventRepository` y
    `SqlDailyClosureRepository` en la misma transacción que el evento. Los eventos anteriores se rellenaron en
    el script, deduciéndolo del histórico inmutable. El detalle de un evento cerrado muestra ese contexto en
    vez del basal actual.
  - **Versiones del basal (ENF-24):** vigente e históricas, con motivo, perfil firmante, fecha, Barthel y la
    versión a la que sustituyó. Se habilitó en `RequestAuthorizationContext` la rama `BaselineHistory` para
    Enfermería y Medicina, que la política ya permitía; Dirección sigue entrando solo por su lectura
    auditada.
  - **Decisiones del usuario (2026-09-30):**
    - este bloque = eventos cerrados + historial del basal;
    - instantánea guardada, no deducida en cada lectura;
    - Medicina ve los mismos eventos que en sus bandejas;
    - la línea temporal (HIS-02) se abriría con un permiso nuevo. **Sustituida en el bloque 2:** por ámbito,
      como la matriz de permisos del prototipo.
  - **Suposiciones confirmadas:** el historial del basal no pide permiso adicional (la política heredada, no
    el boceto ENF-19); se listan las versiones sin abrir sus nueve áreas.

  Script `0019_historial_contexto_evento`; tests en `HistorialTests`.
- **Historia 11, bloque 2 (línea temporal, HIS-02)** — 2026-09-30, común con Medicina (MED-03, MED-24).
  «Ver línea temporal» desde el Historial y desde el detalle de cualquier evento (`/Enfermeria/LineaTemporal`,
  vista común `Shared/LineaTemporal`), agrupada por días y del hito más reciente al más antiguo.
  - **Contenido:** cada evento visible, abierto o cerrado, con sus hitos y su texto:
    - registro y escalado;
    - cada versión guardada de las valoraciones de Enfermería y médica (por fin se ven
      `valoraciones_*_versiones`);
    - indicaciones emitidas, leídas y resueltas;
    - inicio y acciones de los seguimientos de los dos perfiles;
    - protocolo urgente con sus registros y contactos;
    - informe de derivación firmado (con descarga del PDF) e intentos de llamada;
    - comunicación preparada y cierre.

    También entran las versiones del basal y los cambios de ubicación del residente. Autoría por perfil, sin
    nombres.
  - **Implementación:** `ReadResidentTimeline` comprueba primero con `FindScopeResident` que el residente está
    en el ámbito (deny-by-default). `SqlChangeInboxDirectory.ListTimelineAsync` (fichero
    `SqlChangeInboxDirectory.Timeline.cs`) hace una consulta tipada por fuente, limitada a los eventos de
    `ScopedEventsFrom`, y los hitos son records tipados (`TimelineEntry.*`, `Ports/ResidentTimeline.cs`). Sin
    script nuevo.
  - **Decisiones del usuario (2026-09-30):**
    - **autorización por ámbito**, como la matriz de permisos del prototipo (fila «Ver línea temporal
      completa»: Enfermería y Medicina `LECT ámbito`, Dirección `COND-LECT clínica auditada`), sin permiso ni
      auditoría nuevos; sustituye a la del permiso nuevo del bloque 1, que se tomó sin conocer la matriz;
    - hitos con su texto;
    - sin los cierres cotidianos de Auxiliar «sin cambios» y «no valorable»;
    - página propia, cargada solo cuando se pide («plegada»).

  Tests en `MedicinaApplicationServiceTests` (recorrido completo y visibilidad de Medicina) y `HistorialTests`
  (versiones del basal y acceso denegado).
- **Historia 11, bloque 3 (corrección y rectificación, COR-01/COR-02)** — 2026-09-30, común con Medicina
  (historia 9, MED-23).
  - **Decisiones del usuario (2026-09-30), política provisional:** el prototipo no concreta qué objetos se
    habilitan (matriz v0.2.1: «COND objeto habilitado», «COND política clínica») y `roadmap.md` la da como
    pendiente del centro y del responsable de protección de datos.
    - solo se corrigen las valoraciones de Enfermería y médica;
    - dentro de la ventana, versión corregida con motivo obligatorio, que pasa a ser la que se muestra;
    - fuera de ella, rectificación añadida (texto y motivo) que no cambia la valoración;
    - ventana global de 6 h en `appsettings.json` (`Correccion:VentanaHoras`).
  - **Suposiciones aprobadas con el plan:**
    - el autor es la cuenta que guardó la última versión ordinaria (`valoraciones_*_versiones`);
    - la ventana empieza en ese guardado y las correcciones no la amplían;
    - solo se ofrece cuando la valoración ya no se guarda de forma normal (el evento salió de `EN_VALORACION`
      o `EN_VALORACION_MEDICA`);
    - la versión corregida es la que usan todas las lecturas, incluido un informe de derivación aún sin
      firmar.
  - **Pantallas:** en el detalle (`Enfermeria/DetalleCambio`, `Medicina/Escalado`), el autor ve «Corregir
    valoración» (con la hora límite) o «Añadir rectificación». Los demás ven el aviso de la corrección y las
    rectificaciones, también en la información reunida (`Shared/_CorreccionesValoracion`). La línea temporal
    muestra la versión original, cada corrección con su motivo y cada rectificación.
  - **Implementación:** script `0020_correccion_valoraciones`.
    - **Tablas de solo inserción:** `valoraciones_enfermeria_correcciones`, `valoraciones_medicas_correcciones`
      (contenido completo y motivo) y `valoraciones_rectificaciones`.
    - **Triggers:** `TR_ve_guard` y `TR_vm_guard` solo dejan cambiar una valoración `CERRADA` si su contenido,
      autor y hora coinciden con una corrección registrada.
    - **Código:** `SqlAssessmentCorrectionRepository` bloquea la valoración y comprueba en una transacción la
      disponibilidad, el autor, la ventana y el número de correcciones o rectificaciones, que es el token
      contra el doble envío. No toca la revisión del evento.
  - **Tests:** `CorreccionTests`, `CorreccionMedica_…` en `MedicinaApplicationServiceTests` y
    `AssessmentCorrectionTests` (unitarios).
- **Parte médica en el detalle de Enfermería** — 2026-09-30.
  - **Qué muestra:** el detalle de un evento escalado, o de uno propio de Medicina con indicaciones, enseña en
    solo lectura la valoración médica (con sus correcciones y rectificaciones), las indicaciones con su estado
    y el seguimiento médico (parcial `Shared/_ParteMedicaResumen`).
  - **Implementación:** los datos ya llegaban en `PendingChangeDetail.Medical`, así que no hay cambios de
    backend ni de esquema.
  - **Decisiones del usuario (2026-09-30):**
    - las indicaciones se muestran en solo lectura, con «Ir a Indicaciones» si queda alguna por leer o por
      realizar; se confirman y registran en esa bandeja;
    - entran valoración, indicaciones y seguimiento.
- **Historia 11, bloque 4 (contenido de una versión del basal, ENF-24)** — 2026-09-30, común con Medicina
  (historia 9).
  - **Qué muestra:** «Ver versión» en la tabla de versiones del Historial, y «Ver ese basal» en el detalle de
    un evento cerrado (el basal de su fecha, HIS-03), abren `VersionBasal` (vista común
    `Shared/VersionBasal`), en solo lectura: la cabecera de la versión, la fuente y la fecha de la
    información, las nueve áreas con el resumen «Campo: valores» y su observación, y el Barthel con la opción
    y los puntos de cada ítem.
  - **Decisiones del usuario (2026-09-30):** el Barthel se muestra con sus diez ítems, no solo el total, y
    el basal de la fecha del evento cerrado también enlaza a su versión.
  - **Implementación:** sin script. `ReadBaselineHistory.ExecuteVersionAsync` usa la misma autorización que
    el historial (`BaselineHistoryRead`, sin auditoría); `SqlBaselineRepository.ReadVersionAsync` lee la
    versión por su número dentro del residente y del centro, y devuelve null si no existe. Los ítems del
    Barthel (`BarthelItemCode`) tienen ya su nombre en español (el del contrato de datos del prototipo), que
    usa también el formulario del Barthel, donde antes salía el nombre interno («UsoRetrete»).
  - **Tests:** `VersionDelBasal_…` y el acceso denegado en `HistorialTests`, y
    `Barthel_CadaItemTieneNombreEnEspanol_…` en `BaselineAreaDisplayTests`.

## Pendiente

En el orden propuesto de construcción:

1. **Historia 11, bloques siguientes** (común con Medicina):
   - **Línea temporal para Dirección Clínica:** lectura condicional y auditada, que queda para su vertical.

Huecos de lo ya construido:

- **Derivación a Urgencias (historia 6, bloque 2):**
  - **Firmante sin nombre:** `dbo.cuentas` no tiene nombre, así que el PDF muestra el perfil y el
    identificador de la cuenta hasta que exista el proveedor de identidad real.
  - **Sin corrección:** un informe firmado no se corrige ni tiene nueva versión (la política provisional de
    COR-01/02 solo habilita las valoraciones).
  - **Contacto familiar en texto libre:** a quién se llama se escribe a mano hasta que Administración tenga
    el contacto designado.
  - **Pendiente de CJ:** confirmar si el campo "Comunicaciones" de la valoración debe quedar fuera del
    informe, como se hace hoy por prudencia (DER-04).

- **Seguimiento (historia 4)**: no hay equipos ni turnos (son del vertical Administración). El equipo
  responsable es la Enfermería de la unidad del evento y el equipo o turno entrante de una transferencia es
  texto libre; cuando existan turnos reales habrá que sustituirlo. La fecha prevista es solo fecha (sin
  hora) y vence al día siguiente.
- **Comunicación familiar (historia 3)**: solo se guarda la comunicación preparada, pendiente de
  aprobación. Quedan para Portal Familiar / Administración: aprobarla o volver a editarla (ENF-15), la
  audiencia autorizada (ENF-14), el momento de publicación y la publicación. `comunicaciones_familiares`
  es hoy inmutable (`TR_cf_immutable`); la aprobación tendrá que sustituir ese trigger por una guarda de
  transiciones. Queda por decidir con CJ quién aprueba (el PRD, ENF-12, dice que Enfermería "puede
  aprobar el texto familiar separado").
- Los borradores de valoración anteriores a `0009` solo tienen una versión (su contenido en ese momento):
  las versiones intermedias previas no se guardaron.
- **Rangos de referencia de constantes (fase 1 hecha, sin valores)**: decisiones del 2026-09-28 — aviso
  solo visual, que no bloquea ni cambia clasificación, prioridad ni desenlace; rangos por centro fijados en
  la pantalla "Rangos de referencia de constantes" (`RangosReferenciaController`) con el permiso
  `REFERENCE_RANGES_MANAGE`, que la BD solo deja conceder a Medicina o Dirección/Coordinación Clínica
  (nunca Administración, ADM-29); historial inmutable de cada cambio; rangos por residente en una segunda
  fase. Script `0008`. En desarrollo tiene el permiso `dev-integrado-direccion` (seed del escenario
  integrado). Pendiente de CJ (lo recoge
  [`docs/pendientes-cj/rangos-referencia-constantes.html`](../../pendientes-cj/rangos-referencia-constantes.html),
  que CJ completa con los valores y las respuestas a las preguntas abiertas):
  - Fijar los valores en la pantalla: sin ellos no se muestra ningún aviso.
  - Decidir a quién se concede el permiso en cada centro real. Hoy no hay pantalla para conceder permisos:
    se concede por SQL, como el resto de permisos, hasta que exista el vertical Administración.
- **Rangos de referencia por residente (fase 2, pendiente)**: excepciones individuales (p. ej. objetivo de
  SpO2 88-92 % en EPOC). Queda por decidir con CJ quién las fija (Enfermería o Medicina) y si forman
  parte del basal.
- Historia 9: la aportación de otro profesional autorizado a un borrador ajeno (permiso
  `BASELINE_DRAFT_CONTRIBUTE`) no está construida; hoy solo el autor del borrador puede editarlo.

Al construir cada historia, actualizar el Manual de usuario (`src/ResidApp.Web/Views/Home/Manual.cshtml`),
sección "Próximamente".
