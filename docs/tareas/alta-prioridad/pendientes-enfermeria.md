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
- **Historia 8 (evento propio)**: registro de un evento observado por Enfermería (`/RegistrarEvento`,
  tabla `eventos_clinicos`, script `0006`), inmutable; al guardarlo se continúa en su detalle y entra en
  las bandejas con su autoría real.
- **Historias 9 y 10 (basal)**: crear borrador, completar las 9 áreas y el Barthel, cancelar, confirmar y
  firmar (`EnfermeriaBasalController` → `BaselineController/Sign`), con concurrencia optimista e
  idempotencia (script `0005_enfermeria_borrador_basal`).
- Residentes del ámbito y ficha del residente (`/Residentes`, `/Residente`).
- Tests de integración: `EnfermeriaApplicationServiceTests`, `SqlChangeInboxDirectoryTests`,
  `SqlClinicalEventRepositoryTests`, `SqlEnfermeriaResidentDirectoryTests`, `SqlBaselineRepositoryDraftTests`.

## Pendiente

En el orden propuesto de construcción:

1. **Historia 3 — cerrar y decidir la comunicación familiar** (`ENF-06`, `ENF-12`): "pasar a decisión
   asistencial" desde la valoración; cierre idempotente como una de las cuatro salidas; los eventos
   cerrados salen de las bandejas. Extiende los estados de `eventos_asistenciales` y cierra el borrador de
   `valoraciones_enfermeria`.
2. **Historia 4 — seguimiento y transferencia** (`ENF-07`, `ENF-08`).
3. **Historia 5 — escalado a Medicina** (`ENF-09`). Comparte tablas con el vertical Medicina.
4. **Historia 7 — indicaciones de Medicina** (`ENF-10`). Depende de Medicina.
5. **Historia 6 — protocolo urgente y derivación a Urgencias** (`DER-01` a `DER-06`, común con Medicina).
6. **Historia 11 — historial de eventos y versiones del basal** (`HIS-01` a `HIS-03`, común con Medicina).

Huecos de lo ya construido:

- Las tarjetas de seguimientos, indicaciones y comunicaciones familiares de `Views/Enfermeria/Index.cshtml`
  son marcadores "Próximamente".
- La valoración en borrador se sobrescribe en cada guardado: se conserva quién y cuándo la tocó (fila en
  `eventos_auditoria` por guardado), pero no el contenido de las versiones intermedias. A validar con CJ
  si hace falta ese histórico antes de cerrar la valoración (historia 3).
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
