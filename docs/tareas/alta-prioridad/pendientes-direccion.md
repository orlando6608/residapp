# Pendientes del vertical Dirección / Coordinación Clínica

Estado al 2026-09-30 (tras el bloque 1). Historias de referencia: [`docs/historias-usuarios/direccion-coordinacion-clinica.md`](../../historias-usuarios/direccion-coordinacion-clinica.md).
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

## Pendiente

En el orden propuesto:

1. **Bloque 2 — lectura clínica auditada (historias 3 y 6; DIR-05, DIR-06, DIR-07, DIR-14, DIR-15):** detalle clínico con permiso, finalidad
   válida, ámbito y auditoría registrada **antes** de entregar el contenido; línea temporal (HIS-02, que reutilizaría `ReadResidentTimeline`),
   historial de eventos cerrados y correcciones y rectificaciones en solo lectura.
   **Bloqueado por una decisión de CJ:** la matriz de permisos del prototipo marca `CLINICAL_DETAIL_READ` como «aprobado; ámbito/finalidades
   pendientes». Hay que pedirle las finalidades válidas (`docs/pendientes-cj/`) antes de construirlo; no se inventan.
2. **Bloque 3 — derivaciones y comunicación familiar en solo lectura (historia 5; DIR-12, DIR-13).** La comunicación familiar depende de que
   exista la aprobación y publicación (Administración y Familia).
3. **Bloque 4 — indicadores e informes agregados (historias 4 y 7; DIR-08 a DIR-11, DIR-16):** con periodo y denominador, sin ranking
   individual ni exportación masiva. Incluye la continuidad entre turnos (transferencias).

Huecos de lo ya construido:

- **Responsables de equipo:** el detalle operativo no los muestra porque no hay equipos ni turnos hasta que exista Administración.
- **Lectura de basal (`/Baseline/Direction`):** sigue pidiendo el propósito como texto libre, sin la lista de finalidades válidas que
  fijará CJ.
