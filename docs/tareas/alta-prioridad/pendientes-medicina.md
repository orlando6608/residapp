# Pendientes del vertical Medicina

Estado al 2026-09-29 (tras la historia 5). Historias de referencia: [`docs/historias-usuarios/medicina.md`](../../historias-usuarios/medicina.md).
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
- **Historia 4 (cierre médico)** — 2026-09-28: salida "cerrar el evento" de la conducta (`/Medicina/Cerrar`,
  MED-15 a MED-17) desde EN_VALORACION_MEDICA con la valoración guardada o desde CON_INDICACION_PENDIENTE.
  Muestra el resumen y los pendientes, decide la comunicación familiar con el mismo mecanismo que Enfermería y
  cierra de forma idempotente. El evento queda CERRADO, sin segundo cierre de Enfermería, y su detalle dice
  "Cerrado por Medicina". El cierre es común a los dos perfiles (`ClinicalEventCloser`). **Decisión del
  usuario:** se puede cerrar con indicaciones sin resolver. Siguen en la bandeja de Enfermería, y en
  `/Medicina/Indicaciones` siguen las no resueltas y las no realizadas después del cierre. Script
  `0013_medicina_cierre_evento` (solo el trigger de transiciones).
- **Historia 5 (seguimiento médico y continuidad)** — 2026-09-29: salida "iniciar seguimiento médico" de
  la conducta (`/Medicina/IniciarSeguimiento`, MED-10). Se inicia desde EN_VALORACION_MEDICA (con la
  valoración guardada) o desde CON_INDICACION_PENDIENTE, con objetivo obligatorio y fecha prevista o
  criterio. El evento pasa a EN_SEGUIMIENTO_MEDICO, sale de Escalados y entra en la bandeja compartida
  `/Medicina/Seguimientos` (MED-11; tarjeta del inicio con contador y vencidos). En `/Medicina/Seguimiento`
  se puede registrar una revisión, reprogramar con justificación y, al terminar el turno, decidir
  explícitamente entre transferir al equipo o turno entrante (texto libre y nota) y conservarlo para la
  próxima revisión propia (nota opcional). La recepción de una transferencia es opcional (MED-12). "Resolver"
  vuelve a la conducta: cerrar o registrar una indicación, que pasa el evento a CON_INDICACION_PENDIENTE y
  termina el seguimiento. **Decisiones del usuario (2026-09-29):** un solo seguimiento médico por evento;
  equipo responsable implícito (Medicina de la unidad), como en Enfermería; recepción opcional. Tablas de
  solo inserción `seguimientos_medicos` y `seguimiento_medico_acciones` (las de Enfermería no sirven,
  porque un evento escalado desde un seguimiento de Enfermería ya tiene su fila). Script
  `0014_medicina_seguimiento`. Se reutilizan `FollowUpPlan`, `FollowUpAction` (con el tipo nuevo
  `Conservacion`) y `FollowUpDetail`.
- Tests: `MedicinaApplicationServiceTests` (integración), `MedicalAssessmentTests` y `FollowUpTests` (unitarios).
- Cuenta de desarrollo `dev-integrado-medicina` en el escenario integrado.

## Pendiente

En el orden propuesto:

1. **Historia 6 — protocolo urgente y derivación** (común con Enfermería, `DER-01` a `DER-06`).
2. **Historia 7 — evento propio de Medicina** (`MED-11`): mismo ciclo sin reenviarlo desde Enfermería;
   exige que un ámbito de Medicina vea eventos no escalados propios.
3. **Historias 8 y 9 — basal e historial/corrección**.

Huecos de lo ya construido:

- La conducta médica todavía no ofrece el protocolo urgente; llegará con la historia 6.
- **Seguimiento médico (historia 5)**: no hay equipos ni turnos reales (vertical Administración). El
  equipo o turno entrante es texto libre y la fecha prevista es solo fecha, sin hora, como en Enfermería.
- Los eventos escalados salen de las bandejas de Enfermería y no hay todavía una lista de "mis escalados"
  en Enfermería: llegará con el historial (historia 11 de Enfermería).
