# Pendientes del vertical Dirección / Coordinación Clínica

Estado al 2026-10-01 (tras los bloques 1 y 4). Historias de referencia: [`docs/historias-usuarios/direccion-coordinacion-clinica.md`](../../historias-usuarios/direccion-coordinacion-clinica.md).
Flujo: [`supervision-clinica-direccion.md`](../../flujos-clinicos/supervision-clinica-direccion.md); wireframe
[`direccion-coordinacion-clinica.md`](../../bocetos-pantallas/wireframes-funcionales/direccion-coordinacion-clinica.md).

## Hecho

- **Bloque 1 (supervisión operativa, solo lectura; DIR-01 a DIR-04, DIR-17; historias 1, 2 y 8)** — 2026-09-30.
  - **Qué hace:** inicio de Dirección (`/Direccion`) con los contadores por unidad del ámbito y su denominador (episodios abiertos,
    sin valorar, en valoración, escalados, seguimientos y vencidos, indicaciones pendientes y no realizadas, protocolos urgentes);
    `/Direccion/Pendientes` con la lista de episodios abiertos, filtrable por tipo de pendiente y unidad; `/Direccion/Episodio` con el
    detalle operativo (estado, origen, seguimiento, hitos por perfil y hora); `/Direccion/Ambito` con el centro, las unidades y los
    permisos vigentes del ámbito. La tarjeta del inicio convive con la consulta auditada del basal, que ya existía.
  - **Decisiones del usuario (2026-09-30):** alcance = panel + pendientes + detalle operativo + mi ámbito; se muestra el nombre del
    residente y la unidad, nunca la observación ni texto clínico.
  - **Suposiciones aprobadas con el plan:**
    - basta un ámbito activo de `DIRECCION_CLINICA`, sin permiso ni auditoría (la matriz de permisos da «Ver panel de supervisión
      clínica» y «Abrir detalle operativo de episodio» a Dirección);
    - «cierres pendientes» = episodios abiertos (estado distinto de `CERRADO`), y el denominador de cada unidad son sus abiertos;
    - no hay equipos ni turnos (Administración): el detalle dice que el responsable es el equipo de la unidad.
  - **Implementación:** sin script. `ISupervisionDirectory` / `SqlSupervisionDirectory` (predicado de ámbito propio, gemelo de
    `SqlChangeInboxDirectory.ScopedEventsFrom`, que fija Enfermería y Medicina) y `DireccionApplicationService`, que exige un ámbito
    activo de la cuenta con perfil Dirección. Los tipos de supervisión (`SupervisionEpisode`, `SupervisionMilestone`) no tienen ningún
    campo de texto clínico: lo comprueba un test por reflexión. Un episodio cerrado, ajeno o inexistente da el mismo mensaje neutro.
  - **Tests:** `DireccionSupervisionTests` (integración).
- **Bloque 4 (indicadores agregados, evolución e informe imprimible; DIR-08, DIR-09, DIR-10, DIR-16; historias 4 y 7)** — 2026-10-01.
  - **Qué hace:** `/Direccion/Indicadores?desde=&hasta=` (tarjeta «Indicadores e informe de actividad» del inicio). Para cada unidad del
    ámbito y en total, como «n de m»:
    - **actividad:** episodios registrados en el periodo (por origen y prioritarios) y cerrados en el periodo;
    - **escalados y urgencias:** de los registrados, escalados, con protocolo urgente y con informe de derivación firmado;
    - **indicaciones médicas** emitidas en el periodo: leídas, realizadas, no realizadas y sin resolver;
    - **continuidad (DIR-09):** transferencias de seguimiento de Enfermería y de Medicina, y cuántas tienen la recepción confirmada.

    Debajo, la evolución mes a mes (DIR-10), con los meses de los extremos recortados al periodo. La página explica cómo se cuenta
    cada indicador. «Imprimir / guardar como PDF» es el informe de actividad (DIR-16): una cabecera de informe y estilos de impresión,
    sin menú ni formulario.
  - **Decisiones del usuario (2026-10-01):**
    - los cuatro grupos de indicadores, por unidad y en total, nunca por profesional;
    - periodo con «desde» y «hasta» (por defecto, los últimos 30 días, hoy incluido; máximo 366) y evolución mes a mes;
    - el informe es la versión imprimible de la página, sin librerías ni descargas.
  - **Suposiciones aprobadas con el plan:**
    - un episodio entra en el periodo por su registro; los escalados, protocolos y derivaciones cuentan lo que le pasó después, hasta
      hoy, y las indicaciones se muestran con su estado de hoy;
    - sin porcentajes, metas ni colores de alerta, para que nada parezca un juicio automático (DIR-08);
    - sin permiso ni auditoría, como el bloque 1: no entrega contenido clínico ni datos de personas (matriz: «Ver indicadores
      agregados» y «Generar informes agregados», SI para Dirección).
  - **Implementación:** sin script. `SqlSupervisionDirectory.ListIndicatorFactsAsync` (la regla de ámbito del bloque 1, extraída a
    `ScopedEventsFrom`/`ScopedEventsWhere`, sin el filtro de abiertos) devuelve hechos con solo unidad, códigos y fecha: ni texto, ni
    residente, ni cuenta. `SupervisionIndicatorRules.Aggregate` (función pura) los cuenta por unidad, en total y por mes.
    `DireccionApplicationService.ReadIndicatorsAsync` exige el ámbito de Dirección y rechaza un periodo imposible.
  - **Tests:** `SupervisionIndicatorRulesTests` (unitarios), `IndicatorPeriodFilterTests` (funcionales) y los `Indicadores_…` de
    `DireccionSupervisionTests` (integración, con la comprobación por reflexión de que los tipos no tienen texto ni identificadores).

## Pendiente

En el orden propuesto:

1. **Bloque 2 — lectura clínica auditada (historias 3 y 6; DIR-05, DIR-06, DIR-07, DIR-14, DIR-15):** detalle clínico con permiso, finalidad
   válida, ámbito y auditoría registrada **antes** de entregar el contenido; línea temporal (HIS-02, que reutilizaría `ReadResidentTimeline`),
   historial de eventos cerrados y correcciones y rectificaciones en solo lectura.
   **Decisión de CJ (2026-10-06, `docs/pendientes-cj/archivados/decisiones-direccion-basal-derivacion.respuestas.json`, tema 1) y mecanismo hecho el 2026-10-07 (script `0038`):**
   - **Finalidades de Dirección:** «Revisión de continuidad asistencial», «Revisión de una incidencia o reclamación asistencial» y
     «Verificación de trazabilidad documental» (`ClinicalDetailAccessPurpose`; la antigua `SUPERVISION_CLINICA` ya no se ofrece y solo
     sobrevive en las filas de auditoría anteriores). **La cuarta, «revisión de calidad asistencial», NO está:** CJ dice que debería pertenecer
     al perfil de Coordinación Clínica, pero en el modelo Dirección y Coordinación son el mismo perfil (`DIRECCION_CLINICA`); se añadirá cuando se decida cómo
     distinguirlas. **Respondido por CJ el 2026-10-07** (`docs/pendientes-cj/archivados/aclaraciones-respuestas-cj.html`, 3.1): se mantiene el perfil «Dirección / Coordinación Clínica» y la finalidad de calidad queda tras un **permiso específico** que Administración asigna según la designación del centro (no por el cargo de director). **Sin construir todavía:** el permiso nuevo y la cuarta finalidad.
   - **Justificación siempre:** texto breve (hasta 300 caracteres) en cada declaración, «sin copiar información clínica ni datos personales innecesarios»;
     se guarda en la declaración y en cada fila de auditoría (`eventos_auditoria.justificacion`).
   - **Una declaración por residente, 1 hora** (`AccesoClinico:DuracionMinutos` en `appsettings.json`, ajustable más adelante por centro): vale para
     todas las pantallas clínicas autorizadas de ese residente; termina al cambiar de residente, de perfil activo o de ámbito (`ProfileScopeController`),
     al cerrar sesión (`DevAuthController.Logout`) o al cumplirse la hora. Cada apertura sigue comprobando permiso y ámbito en SQL y escribiendo su propia
     fila de auditoría antes de entregar el contenido. Tabla `declaraciones_acceso_clinico` (solo se crea y se termina; con seguridad por filas),
     cookie `residapp_clinical_access` (solo el id; SQL revalida todo), `ClinicalAccessDeclarations` (consultar la vigente y terminarlas).
   - **DIR-05 hecho el 2026-10-07 (sin script):** `/Baseline/Direction` con «Basal vigente» muestra ahora el contenido de la versión vigente (nueve áreas y Barthel por
     ítem), con el parcial `_ContenidoVersionBasal` (el mismo de Enfermería/Medicina, `VersionBasal`). `ReadDirectionBaseline` devuelve `DirectionBaselineRead(Headers, Content)`: primero
     la lectura auditada («auditoría o nada», sin cambios en `ReadAsClinicalDirectionAsync`) y solo después `ReadVersionAsync` de la versión que acaba de auditar; «Historial de basal» sigue
     devolviendo solo cabeceras. `eventos_auditoria.tipo_recurso` no tiene `CHECK` de valores, así que no hizo falta script. **Decisión mía, sin confirmar:** el contenido de versiones
     históricas no se entrega aquí (llegará con DIR-15, versiones vinculadas). Test: `Direccion_LeeElContenidoDelBasalVigenteAuditado_ElHistorialSoloCabeceras_YSinPermisoNada`
     (falla si el historial entrega contenido). Comprobado por HTTP en local con un residente ficticio con basal firmado.
   - **DIR-06 y DIR-07 hechos el 2026-10-07 (sin script):** en el mismo `/Baseline/Direction`, dos tipos de recurso nuevos, «Línea temporal» (`RESIDENT_TIMELINE`) y «Historial de eventos cerrados»
     (`CLOSED_EVENTS_HISTORY`), con acciones propias en la política (`ResidentTimelineRead`, `ClosedEventsHistoryRead`: solo `DIRECCION_CLINICA` con `CLINICAL_DETAIL_READ`, finalidad y
     declaración). **Se audita el residente (una fila por lectura, `recurso_id` = residente), no cada evento** (decisión mía, sin confirmar). `ReadAsClinicalDirectionAsync` hace la
     auditoría con el mismo SQL y devuelve cabeceras vacías para estos tipos; solo entonces `ReadDirectionBaseline` pide `ListDirectionTimelineAsync` / `ListDirectionClosedEventsAsync`
     (`SqlChangeInboxDirectory`, con `DirectionScopedEventsFrom`, el gemelo de `ScopedEventsFrom` para Dirección: todos los eventos de las unidades y residentes del ámbito, sin la regla
     de Medicina; el SQL de la línea temporal y del historial se parametrizó por ese fragmento, sin duplicarse). Parciales `_LineaTemporalLista` y `_EventosCerradosLista`, compartidos con
     Enfermería y Medicina; sin `DetailAction` no hay enlaces a eventos ni al PDF (son de otros perfiles). Tests: `Direccion_LeeLineaTemporalYEventosCerrados_Auditados_ConTodosLosEventosDeSuAmbito`
     (falla con el ámbito de Enfermería). La línea temporal incluye el contenido clínico de los eventos (valoraciones, indicaciones, protocolo): es el detalle que pedía DIR-06.
   - **DIR-15 hecho el 2026-10-07 (sin script):** tercer tipo de recurso nuevo, «Correcciones y rectificaciones» (`ASSESSMENT_AMENDMENTS`, acción `AssessmentAmendmentsRead`), misma declaración y auditoría (una fila por lectura). Se construye en
     Application a partir de la línea temporal del ámbito de Dirección (`ResidentAmendmentHistory.From`, función pura): por cada valoración (Enfermería o Medicina, por evento) corregida o rectificada, el original (la última versión guardada), cada
     corrección (contenido corregido y motivo) y cada rectificación, y todos los basales firmados como versiones vinculadas (cabeceras de `ReadHistoryAsync`: vigente, «sustituye a la versión n», firmante, Barthel; nunca como edición). **Decisión de
     Orlando (2026-10-07): se ve el nombre de quien corrigió.** Cada hito lleva `TimelineEntry.AuthorName` (el `nombre_visible` de la cuenta, `0023`; sin nombre: «cuenta sin nombre registrado»), que solo se rellena si `ListDirectionTimelineAsync` se llama con `includeAuthorNames: true` (solo DIR-15; ni la línea temporal de DIR-06 ni las de Enfermería y Medicina lo llevan). Vista `_CorreccionesRectificaciones` y `_CamposHito`. Tests:
     `ResidentAmendmentHistoryTests` (3, unitarios) y `Direccion_LeeCorreccionesYRectificaciones_Auditadas_ConLosBasalesComoVersionesVinculadas` (falla sin las versiones del basal).
   - **DIR-14 hecho el 2026-10-07 (sin script):** cuarto tipo de recurso, «Trazabilidad clínica» (`CLINICAL_TRACEABILITY`, acción `ClinicalTraceabilityRead`), con la misma declaración y auditoría (una fila por lectura, que aparece la primera en la lista).
     `SqlChangeInboxDirectory.ListDirectionTraceabilityAsync` lee `eventos_auditoria` del residente (hasta 500 hitos, los más recientes primero, con aviso si hay más): hora, nombre visible de la cuenta (o «cuenta sin nombre registrado»), perfil, unidad,
     acción, recurso y, en las lecturas de Dirección, la finalidad (nunca la justificación ni texto clínico). **Solo las acciones de la lista cerrada `ClinicalTraceability.ActionCodes`** (39: eventos, valoraciones, indicaciones, seguimientos, protocolo urgente,
     derivación, llamadas y comunicaciones a la familia, basal y lecturas de Dirección); las administrativas (alta, familiares, permisos) tienen su auditoría en ADM-28 y no entran. Repite en SQL el ámbito de Dirección activo y que la unidad del hito esté
     concedida a él. Etiquetas en `ClinicalTraceabilityDisplay` (una acción nueva en la lista sin etiqueta hace fallar `ClinicalTraceabilityDisplayTests`). **Decisiones mías, sin confirmar:** qué acciones cuentan como «hitos clínicos» (lo anterior), incluir las
     propias lecturas de Dirección (útil para saber quién vio el expediente) y no mostrar la justificación. Test: `Direccion_LeeLaTrazabilidadClinica_Auditada_SoloConAccionesClinicas` (falla sin el filtro de acciones). No es un ranking: solo por residente, sin recuentos por persona.
   - **DIR-12, informe de derivación firmado, hecho el 2026-10-07 (sin script) — con lo que el bloque 2 queda completo salvo la 4ª finalidad:** quinto tipo de recurso, «Informes de derivación firmados» (`REFERRAL_REPORTS`, acción `ReferralReportsRead`),
     que lista los informes del residente (evento, fecha de firma, perfil y nombre de quien firmó; sin su contenido; sale de la línea temporal de Dirección con `includeAuthorNames`). Cada PDF se abre con `GET /Baseline/InformeDerivacion?residenteId=&eventoId=`
     (`DownloadDirectionReferralReport`, `SqlReferralReportRepository.DownloadAsDirectionAsync`): exige la declaración de acceso vigente del residente (la cookie `residapp_clinical_access`; sin ella o caducada, vuelve a la consulta con un aviso para declarar
     de nuevo) y la comprobación de la política; el SQL vuelve a exigir en una sola consulta cuenta activa, ámbito de Dirección, permiso `CLINICAL_DETAIL_READ`, evento de ese residente en una unidad concedida y la declaración, y **audita cada descarga**
     (`CLINICAL_DETAIL_READ` sobre `REFERRAL_REPORT`, con la finalidad y la justificación guardadas en la declaración, no las que mande quien llama) en la misma transacción. Dirección no genera ni firma informes. `Derivaciones` enlaza a la consulta de cada residente
     con informe firmado. Tests: `Direccion_ListaYDescargaElInformeFirmado_ConDeclaracionVigente_YCadaDescargaQuedaAuditada` (incluye llamadas directas al repositorio: falla sin la comprobación de la declaración en SQL). **Decisión mía, sin confirmar:** el nombre de
     quien firmó se ve (como en DIR-15), pero el motivo del informe solo está dentro del PDF. Comprobado por HTTP con un PDF real.
   - **Notas del repositorio de lectura:**
     `ReadAsClinicalDirectionAsync` (`SqlBaselineRepository`) es el patrón «auditoría o nada»; `SqlChangeInboxDirectory.ScopedEventsFrom` fija
     `perfil_codigo IN ('ENFERMERIA','MEDICINA')` y no sirve a Dirección tal cual (`SqlSupervisionDirectory.ScopedEventsFrom` es el gemelo); no reutilizar la ruta de
     Enfermería/Medicina (`HistorialTests` fija que Dirección recibe acceso denegado ahí).
   - **Defecto corregido de paso:** `CK_audit_direction_read` aceptaba una finalidad nula (`NULL = 'X'` da UNKNOWN y un `CHECK` acepta UNKNOWN); la nueva restricción lleva `proposito_codigo IS NOT NULL`.
2. **Bloque 3 — derivaciones y comunicación familiar en solo lectura (historia 5; DIR-12, DIR-13).**
   - **Derivaciones (DIR-12), hecho el 2026-10-02 (sin script; suposiciones mías, sin confirmar con el usuario ni con CJ):** `/Direccion/Derivaciones`
     (tarjeta del inicio) lista los **episodios abiertos** del ámbito con protocolo urgente, del más reciente al más antiguo, con quién y cuándo activó el
     protocolo, si el informe está firmado (cuándo y por qué perfil), el número de llamadas a la familia y la última, y un resumen (en curso, firmadas,
     sin firmar, con llamadas). `SupervisionReferral` no tiene texto clínico, ni el contacto, ni el resultado de las llamadas, ni el contenido del informe
     (lo comprueba un test por reflexión); sin permiso ni auditoría, como el resto de la supervisión operativa. Solo abiertos: los cerrados salen en los
     indicadores agregados, no por nombre. Pedido a CJ que lo confirme (solo abiertos; auditoría) en `docs/pendientes-cj/continuidad-supervision-comunicacion.html`
     (tema 2). Consultar el informe firmado (permiso clínico y declaración de finalidad) está hecho desde el 2026-10-07: ver el bloque 2, DIR-12.
   - **Comunicación familiar (DIR-13):** depende de que exista la aprobación y publicación (Administración y Familia).
3. **Revisión de calidad de proceso (DIR-11 del boceto):** «cumplimiento de hitos definidos y excepciones». Nadie ha definido qué hitos
   ni con qué plazos; es una decisión clínica. Preguntado a CJ el 2026-10-02 en `docs/pendientes-cj/continuidad-supervision-comunicacion.html`
   (tema 4: tabla de plazos de 7 hitos y qué hacer con uno fuera de plazo).

Huecos de lo ya construido:

- **Seguimientos pendientes (antes «vencidos») por periodo (2026-10-02, sin script):** los indicadores los incluyen en «Continuidad entre turnos» y en la evolución mensual,
  - **Actualizado el 2026-10-08 (CJ 3.1/3.2):** el seguimiento solo deja de estar abierto al cerrarse el episodio; escalar a Medicina o activar el protocolo urgente ya no lo termina. En pantallas se dice «pendiente».
  como «n de m»: m son los seguimientos abiertos algún día del periodo y n los que tuvieron su fecha prevista pasada algún día, con el plan vigente al
  empezar ese día (`SupervisionIndicatorRules.WasOverdue`, función pura; reprogramar el mismo día en que vence no lo borra). **Es una deducción mía, sin
  confirmar con el usuario ni con CJ.** Un seguimiento deja de estar abierto al cerrarse el episodio, al escalarse (solo Enfermería) o al activarse el
  protocolo urgente, lo primero que ocurra tras iniciarse; **no se guarda cuándo termina por otras vías** (p. ej. una indicación médica), y esos casos
  cuentan hasta el cierre del episodio (las dos cosas, preguntadas a CJ en `docs/pendientes-cj/continuidad-supervision-comunicacion.html`, tema 3). Cada mes de la evolución se calcula por separado (un seguimiento puede contar en varios). Tests:
  `SupervisionIndicatorRulesTests` (la regla, con zona horaria) e `Indicadores_SeguimientosConLaFechaVencidaEnElPeriodo_…` (SQL; falla si se pierde el cierre).

- **Responsables de equipo:** el detalle operativo no los muestra porque no hay equipos ni turnos hasta que exista Administración.
- **Lectura de basal (`/Baseline/Direction`):** desde 2026-10-02 toma el ámbito y el centro del ámbito activo y el residente se
  elige en un desplegable (`DireccionApplicationService.ListResidentsAsync`, con el mismo criterio de ámbito que la autorización de
  la lectura). Desde 2026-10-07 la consulta pide una de las tres finalidades de CJ y una justificación (ver el bloque 2).
  Un residente sin ningún basal firmado ya no se deniega como un acceso no autorizado: tras autorizar, la consulta devuelve una lista
  vacía, sin auditoría, y el resultado dice «Este residente todavía no tiene ningún basal firmado.». Quien no está autorizado sigue
  recibiendo el mensaje neutro (`SqlBaselineRepositoryTests`).
