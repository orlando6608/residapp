# Pendientes del vertical Enfermería

Estado al 2026-09-28. Historias de referencia: [`docs/historias-usuarios/enfermeria.md`](../../historias-usuarios/enfermeria.md).
Flujos: [`valoracion-escalado-enfermeria.md`](../../flujos-clinicos/valoracion-escalado-enfermeria.md),
[`gestion-basal-barthel.md`](../../flujos-clinicos/gestion-basal-barthel.md),
[`derivacion-urgencias.md`](../../flujos-clinicos/derivacion-urgencias.md).

## Hecho

- **Historia 1 (bandejas), parcial**: bandeja prioritaria y bandeja de cambios ordinarios
  (`EnfermeriaController/Prioritarios`, `/Ordinarios`) con los cambios que registra Auxiliar, compartidas por
  unidad y ordenadas de más antiguo a más reciente; detalle del cambio con la observación original, la
  temperatura y el basal vigente resumido (`/DetalleCambio`). Solo lectura: todo cambio figura como "Pendiente".
- **Historia 8 (evento propio)**: registro de un evento observado por Enfermería (`/RegistrarEvento`,
  tabla `eventos_clinicos`, script `0006`), inmutable.
- **Historias 9 y 10 (basal)**: crear borrador, completar las 9 áreas y el Barthel, cancelar, confirmar y
  firmar (`EnfermeriaBasalController` → `BaselineController/Sign`), con concurrencia optimista e
  idempotencia (script `0005_enfermeria_borrador_basal`).
- Residentes del ámbito y ficha del residente (`/Residentes`, `/Residente`).
- Tests de integración: `EnfermeriaApplicationServiceTests`, `SqlChangeInboxDirectoryTests`,
  `SqlClinicalEventRepositoryTests`, `SqlEnfermeriaResidentDirectoryTests`, `SqlBaselineRepositoryDraftTests`.

## Pendiente

En el orden propuesto de construcción:

1. **Historia 2 — valorar un evento** (`ENF-03` a `ENF-05`): empezar valoración desde la bandeja, con
   concurrencia optimista, sin alterar la observación original; hallazgos, actuaciones y constantes opcionales.
2. **Historia 3 — cerrar y decidir la comunicación familiar** (`ENF-06`, `ENF-12`): cierre idempotente
   como una de las cuatro salidas; los eventos cerrados salen de las bandejas.
3. **Historia 4 — seguimiento y transferencia** (`ENF-07`, `ENF-08`).
4. **Historia 5 — escalado a Medicina** (`ENF-09`). Comparte tablas con el vertical Medicina.
5. **Historia 7 — indicaciones de Medicina** (`ENF-10`). Depende de Medicina.
6. **Historia 6 — protocolo urgente y derivación a Urgencias** (`DER-01` a `DER-06`, común con Medicina).
7. **Historia 11 — historial de eventos y versiones del basal** (`HIS-01` a `HIS-03`, común con Medicina).

Huecos de lo ya construido:

- Los eventos propios de Enfermería (historia 8) no aparecen todavía en ninguna bandeja; deberían
  entrar en el mismo ciclo de valoración que los recibidos de Auxiliar.
- Las tarjetas de seguimientos, indicaciones y comunicaciones familiares de `Views/Enfermeria/Index.cshtml`
  son marcadores "Próximamente".
- Historia 9: la aportación de otro profesional autorizado a un borrador ajeno (permiso
  `BASELINE_DRAFT_CONTRIBUTE`) no está construida; hoy solo el autor del borrador puede editarlo.

Al construir cada historia, actualizar el Manual de usuario (`src/ResidApp.Web/Views/Home/Manual.cshtml`),
sección "Próximamente".
