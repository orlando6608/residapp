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
   **Bloqueado por una decisión de CJ:** la matriz de permisos del prototipo marca `CLINICAL_DETAIL_READ` como «aprobado; ámbito/finalidades
   pendientes». Se le han preguntado en `docs/pendientes-cj/decisiones-direccion-basal-derivacion.html` (tema 1); no se construye ni se inventan hasta su respuesta.
2. **Bloque 3 — derivaciones y comunicación familiar en solo lectura (historia 5; DIR-12, DIR-13).** La comunicación familiar depende de que
   exista la aprobación y publicación (Administración y Familia).
3. **Revisión de calidad de proceso (DIR-11 del boceto):** «cumplimiento de hitos definidos y excepciones». Nadie ha definido qué hitos
   ni con qué plazos; es una decisión clínica que habría que pedir a CJ con un documento en `docs/pendientes-cj/`.

Huecos de lo ya construido:

- **Seguimientos vencidos por periodo:** los indicadores no los incluyen. El inicio da los vencidos de hoy, pero no se guarda cuándo dejó
  un seguimiento de estar abierto (solo el cierre del evento), así que «vencido en el periodo» habría que deducirlo.

- **Responsables de equipo:** el detalle operativo no los muestra porque no hay equipos ni turnos hasta que exista Administración.
- **Lectura de basal (`/Baseline/Direction`):** desde 2026-10-02 toma el ámbito y el centro del ámbito activo y el residente se
  elige en un desplegable (`DireccionApplicationService.ListResidentsAsync`, con el mismo criterio de ámbito que la autorización de
  la lectura). El propósito sigue siendo la única finalidad provisional, sin la lista de finalidades válidas que fijará CJ (tema 1).
  Un residente sin ningún basal firmado ya no se deniega como un acceso no autorizado: tras autorizar, la consulta devuelve una lista
  vacía, sin auditoría, y el resultado dice «Este residente todavía no tiene ningún basal firmado.». Quien no está autorizado sigue
  recibiendo el mensaje neutro (`SqlBaselineRepositoryTests`).
