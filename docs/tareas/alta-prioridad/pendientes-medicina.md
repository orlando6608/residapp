# Pendientes del vertical Medicina

Estado al 2026-09-29 (tras la historia 7). Historias de referencia: [`docs/historias-usuarios/medicina.md`](../../historias-usuarios/medicina.md).
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
- **Historia 6, bloque 1 (protocolo urgente)** — 2026-09-29: salida "activar protocolo urgente" de la conducta
  (`/Medicina/ActivarProtocolo`, MED-13), desde EN_VALORACION_MEDICA (con la valoración guardada),
  CON_INDICACION_PENDIENTE o EN_SEGUIMIENTO_MEDICO. El evento pasa a PROTOCOLO_URGENTE_MEDICO, sale de las
  bandejas y entra en `/Medicina/Protocolos` (tarjeta del inicio). Las indicaciones emitidas siguen visibles.
  `/Medicina/Protocolo` es la misma pantalla que la de Enfermería (DER-01): actuaciones, evolución y contactos
  con servicios. Desde el protocolo solo se cierra (`/Medicina/Cerrar`). El detalle y las decisiones están en
  `pendientes-enfermeria.md`; script `0015_protocolo_urgente`.
- **Historia 6, bloque 2 (derivación a Urgencias)** — 2026-09-29: "Derivar a Urgencias" desde el protocolo
  urgente de Medicina (`/Medicina/Derivar`, MED-14/MED-16), con la misma pantalla y las mismas reglas que
  Enfermería (DER-01):
  - datos automáticos que no se editan, más el motivo y la información adicional;
  - vista previa obligatoria con huella SHA-256;
  - firma idempotente y PDF inmutable;
  - intentos de llamada a la familia;
  - al cerrar, al menos un intento y una comunicación Relevante.

  El evento sigue en PROTOCOLO_URGENTE_MEDICO tras firmar. Enfermería ve el informe en el detalle del evento
  y descarga el PDF, pero no firma ni registra llamadas en un protocolo de Medicina. El detalle, las
  decisiones y los huecos están en `pendientes-enfermeria.md`; script `0016_derivacion_urgencias`.
- **Historia 7 (evento propio de Medicina)** — 2026-09-29: MED-11 del PRD, pantallas MED-18 a MED-20 del
  wireframe (en el código, "MED-11" es la bandeja de seguimientos médicos).
  - **Entrada:** tarjeta "Residentes" del inicio de Medicina (`/Medicina/Residentes`), ficha
    (`/Medicina/Residente`, basal vigente en solo lectura) y "Registrar evento" (`/Medicina/RegistrarEvento`)
    con los mismos campos que Enfermería: observación, clasificación y datos clínicos opcionales.
  - **Reutilización:** `ListScopeResidents`, `FindScopeResident` y `RegisterClinicalEvent` reciben el perfil
    pedido, y el ámbito activo tiene que ser de ese perfil.
  - **Registro:** el evento (origen `EVENTO_MEDICINA`) nace ya en EN_VALORACION_MEDICA, iniciado por quien lo
    registra, y se pasa a `/Medicina/Valoracion`. Desde ahí sigue el ciclo de un escalado sin simularlo.
  - **Visibilidad:** entra en la bandeja "Escalados y eventos propios" (`ScopedEventsFrom` deja ver a Medicina
    los eventos de origen Medicina), así que otra médica de la unidad puede continuarlo.
  - **Base de datos:** `CK_ea_inicio` impide que entre en un estado de Enfermería. Enfermería lo ve en el
    detalle si recibe una indicación suya.
  - **Decisiones del usuario (2026-09-29):**
    - directo a la valoración, sin estado "pendiente médica";
    - misma bandeja que los escalados;
    - entrada por la lista y la ficha de residentes;
    - mismos campos que Enfermería.

  Script `0017_medicina_evento_propio`. En `0018_evento_clinico_perfil_origen`, la BD pasa a exigir que el
  perfil que registra el evento coincida con su origen: `MEDICINA` con `EVENTO_MEDICINA` y `ENFERMERIA` con
  `EVENTO_ENFERMERIA`.
- **Historia 9, bloque 1 (Historial)** — 2026-09-30: «Ver historial» desde la ficha
  (`/Medicina/Historial`), con la misma vista que Enfermería: eventos cerrados escalados o propios de
  Medicina (la regla de sus bandejas), cada uno con el basal y la ubicación de su fecha (HIS-03), y las
  versiones firmadas del basal. El detalle, las decisiones y el script `0019` están en
  `pendientes-enfermeria.md` (historia 11, bloque 1).
- **Historia 9, bloque 2 (línea temporal, MED-03 y MED-24)** — 2026-09-30: «Ver línea temporal» desde el
  Historial y desde el detalle de cualquier evento (`/Medicina/LineaTemporal`), con la misma vista que
  Enfermería. Solo entran los eventos que ve Medicina, con cada hito y su texto, más las versiones del basal y
  los cambios de ubicación. **Decisión del usuario:** por ámbito, como la matriz de permisos del prototipo,
  sin permiso aparte. El detalle está en `pendientes-enfermeria.md` (historia 11, bloque 2).
- **Historia 9, bloque 3 (corrección y rectificación, COR-01/COR-02, MED-23)** — 2026-09-30: quien guardó
  por última vez la valoración médica puede corregirla desde `/Medicina/Escalado` cuando ya no se edita de
  forma normal: tras la conducta, el seguimiento, el protocolo o el cierre.
  - **Corregir valoración** (`/Medicina/CorregirValoracion`): dentro de las 6 h siguientes a ese guardado,
    con motivo obligatorio.
  - **Añadir rectificación** (`/Medicina/RectificarValoracion`, vista común `Shared/RectificarValoracion`):
    después de esas 6 h.

  Medicina ve también las correcciones y rectificaciones de la valoración de Enfermería en la información
  reunida. La política provisional, las decisiones y el script `0020` están en `pendientes-enfermeria.md`
  (historia 11, bloque 3).
- **Historia 9, bloque 4 (contenido de una versión del basal)** — 2026-09-30: «Ver versión» en el Historial
  y «Ver ese basal» en el detalle de un evento cerrado (`/Medicina/VersionBasal`), con la misma vista que
  Enfermería: las nueve áreas y el Barthel por ítems. El detalle está en `pendientes-enfermeria.md`
  (historia 11, bloque 4).
- Tests: `MedicinaApplicationServiceTests` (integración), `MedicalAssessmentTests`, `FollowUpTests`,
  `UrgentProtocolTests` y `EmergencyReferralTests` (unitarios), y `ReferralReportBuilderTests` (funcional).
- Cuenta de desarrollo `dev-integrado-medicina` en el escenario integrado.

## Pendiente

En el orden propuesto:

1. **Historia 8 — basal** y **lo que queda de la historia 9** (común con la historia 11 de Enfermería; ver
   `pendientes-enfermeria.md`).

Huecos de lo ya construido:

- **Evento propio (historia 7):**
  - la ficha del residente (MED-20) enlaza al Historial (eventos cerrados y versiones del basal), pero no
    muestra los eventos abiertos del residente;
  - no tiene el acceso a crear o reevaluar el basal, que es la historia 8;
  - la lista de residentes (MED-19) no tiene buscador, igual que la de Enfermería.

- Derivación a Urgencias: los mismos huecos que en Enfermería (firmante sin nombre, sin corrección del
  informe, contacto familiar en texto libre); ver `pendientes-enfermeria.md`.
- **Seguimiento médico (historia 5)**: no hay equipos ni turnos reales (vertical Administración). El
  equipo o turno entrante es texto libre y la fecha prevista es solo fecha, sin hora, como en Enfermería.
- Los eventos escalados salen de las bandejas de Enfermería. Cuando Medicina los cierra aparecen en el
  Historial del residente, pero mientras siguen abiertos en Medicina no hay una lista de "mis escalados" en
  Enfermería.
