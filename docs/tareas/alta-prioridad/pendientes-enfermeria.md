# Pendientes del vertical Enfermería

Estado al 2026-09-28. Historias de referencia: [`docs/historias-usuarios/enfermeria.md`](../../historias-usuarios/enfermeria.md).
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
  en `valoraciones_enfermeria_versiones`. Script `0009_enfermeria_cierre_evento`.
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
- **Historia 8 (evento propio)**: registro de un evento observado por Enfermería (`/RegistrarEvento`,
  tabla `eventos_clinicos`, script `0006`), inmutable; al guardarlo se continúa en su detalle y entra en
  las bandejas con su autoría real.
- **Historias 9 y 10 (basal)**: crear borrador, completar las 9 áreas y el Barthel, cancelar, confirmar y
  firmar (`EnfermeriaBasalController` → `BaselineController/Sign`), con concurrencia optimista e
  idempotencia (script `0005_enfermeria_borrador_basal`).
- Residentes del ámbito y ficha del residente (`/Residentes`, `/Residente`).
- Tests de integración: `EnfermeriaApplicationServiceTests` (incluidos el cierre, el seguimiento y el
  escalado), `MedicinaApplicationServiceTests`, `SqlChangeInboxDirectoryTests`,
  `SqlClinicalEventRepositoryTests`, `SqlEnfermeriaResidentDirectoryTests`, `SqlBaselineRepositoryDraftTests`.

## Pendiente

En el orden propuesto de construcción:

1. **Historia 7 — indicaciones de Medicina** (`ENF-10`). Depende de que Medicina pueda registrar
   indicaciones (valoración médica y conducta, historias 2 y 3 de Medicina), que no existen todavía.
2. **Historia 6 — protocolo urgente y derivación a Urgencias** (`DER-01` a `DER-06`, común con Medicina).
   No depende de Medicina para activarse desde Enfermería: es la cuarta salida de `/Decision`.
3. **Historia 11 — historial de eventos y versiones del basal** (`HIS-01` a `HIS-03`, común con Medicina).
   Puede mostrar ya las versiones de la valoración (`valoraciones_enfermeria_versiones`), que hoy se
   guardan pero no se ven.

Huecos de lo ya construido:

- La tarjeta de indicaciones de `Views/Enfermeria/Index.cshtml` es un marcador "Próximamente".
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
