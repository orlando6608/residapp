# Pendientes del vertical Medicina

Estado al 2026-09-28. Historias de referencia: [`docs/historias-usuarios/medicina.md`](../../historias-usuarios/medicina.md).
Flujo: [`valoracion-conducta-medicina.md`](../../flujos-clinicos/valoracion-conducta-medicina.md); wireframe
[`medicina.md`](../../bocetos-pantallas/wireframes-funcionales/medicina.md).

## Hecho

- **Historia 1 (bandeja de escalados)** — 2026-09-28: inicio de Medicina (`MedicinaController/Index`,
  contadores de escalados e indicaciones), bandeja `/Medicina/Escalados` (escalados pendientes y en
  valoración médica, con motivo, constantes, actuaciones y antigüedad) y detalle `/Medicina/Escalado` con la
  información reunida de solo lectura (parcial `Shared/_InformacionReunida`). Un ámbito de Medicina solo ve
  eventos escalados de sus unidades (`SqlChangeInboxDirectory.ScopedEventsFrom`). Script `0011`.
- **Historia 2 (valoración médica)** — 2026-09-28: "Iniciar valoración médica" (`/EmpezarValoracion`)
  registra profesional y hora sin propiedad permanente (estado EN_VALORACION_MEDICA); formulario con
  hallazgos y exploración, valoración, actuaciones y constantes (`/Valoracion`, parcial compartido
  `Shared/_ConstantesFormulario`); concurrencia optimista por la revisión del evento; cada guardado deja una
  versión inmutable (`valoraciones_medicas_versiones`). Nunca modifica la documentación de Enfermería.
- **Historia 3 (indicaciones y su cumplimiento)** — 2026-09-28: conducta médica (`/Conducta`, las cuatro
  salidas; solo "registrar indicaciones" activa) e indicación (`/Indicacion`: texto, fecha prevista o
  criterio, información adicional, sin prioridad). El evento pasa a CON_INDICACION_PENDIENTE y se pueden
  añadir más; la valoración médica ya no se edita. `/Medicina/Indicaciones` muestra lectura, realización,
  incidencias y vencidas. Enfermería las lee y registra (historia 7 de Enfermería). Script
  `0012_medicina_valoracion_indicaciones`.
- Tests: `MedicinaApplicationServiceTests` (integración) y `MedicalAssessmentTests` (unitarios).
- Cuenta de desarrollo `dev-integrado-medicina` en el escenario integrado.

## Pendiente

En el orden propuesto:

1. **Historia 4 — cierre médico** (`MED-10`, `MED-12`): idempotente, sin segundo cierre de Enfermería, con la
   decisión de comunicación familiar. Reutiliza el cierre de Enfermería (`CloseAsync`,
   `comunicaciones_familiares`); añade la transición desde EN_VALORACION_MEDICA/CON_INDICACION_PENDIENTE a un
   estado de cierre y cierra la valoración médica. Hay que decidir qué pasa con las indicaciones aún
   pendientes al cerrar (hoy "no caducan": ¿impiden el cierre o siguen visibles para Enfermería?).
2. **Historia 5 — seguimiento médico y continuidad** (`MED-07` a `MED-09`, pantallas MED-10 a MED-12):
   parecido al seguimiento de Enfermería, pero con "objetivo" y la decisión explícita de transferir o
   conservar al cambio de turno.
3. **Historia 6 — protocolo urgente y derivación** (común con Enfermería, `DER-01` a `DER-06`).
4. **Historia 7 — evento propio de Medicina** (`MED-11`): mismo ciclo sin reenviarlo desde Enfermería;
   exige que un ámbito de Medicina vea eventos no escalados propios.
5. **Historias 8 y 9 — basal e historial/corrección**.

Huecos de lo ya construido:

- "Resolver" desde CON_INDICACION_PENDIENTE solo permite registrar más indicaciones hasta que existan las
  demás salidas de la conducta.
- Los eventos escalados salen de las bandejas de Enfermería y no hay todavía una lista de "mis escalados"
  en Enfermería: llegará con el historial (historia 11 de Enfermería).
