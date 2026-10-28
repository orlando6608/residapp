# Historial tras un traslado: procedimiento de revisión de `TRANSFER_HISTORY_READ`

**BORRADOR — lo valida una persona (DPO real o abogado) antes de usarlo con datos reales.** Fecha del borrador: 2026-10-28.

Etiquetas: **[Hecho]** verificado en el repositorio, **[Suposición]** mía, **[Decidir]** lo fija la residencia, el DPO real o CJ.

**Qué se revisa:** `TRANSFER_HISTORY_READ` es la anotación que el sistema escribe cada vez que alguien abre episodios cerrados de la unidad anterior de un residente trasladado. Lleva quién, cuándo, qué residente, qué se abrió y el **motivo** escrito.

## Lo que el sistema permite hoy [Hecho]

1. Dirección Clínica ve esas anotaciones, con su motivo, en la **trazabilidad clínica de un residente** (`SqlChangeInboxDirectory.Traceability.cs`, línea 22; `ClinicalTraceability.cs`, línea 26). Hasta 500 hitos por residente (`ClinicalTraceability.cs`, línea 16).
2. Solo se ve **residente por residente**. No hay una lista de todas las peticiones del periodo.
3. Para abrir la trazabilidad de un residente, Dirección debe antes hacer su propia lectura declarada, con finalidad y justificación, que también se audita (`ReadDirectionBaseline.cs`, líneas 57 a 62 y 105 a 107). Es decir, **revisar un residente obliga a dejar otra anotación de acceso**.
4. Dirección solo ve las anotaciones de residentes cuya unidad actual está en su ámbito (`...Traceability.cs`, líneas 29 a 32).
5. La revisión en sí **no deja ninguna constancia** en el sistema.
6. Una petición rechazada (el residente no tiene episodios aplicables) no se anota (`SqlTransferHistoryAccess.cs`, líneas 57 a 61).
7. Dirección Clínica no puede pedir historial tras un traslado: solo Enfermería y Medicina (`TransferHistoryAccess.cs`, líneas 41 a 45). **[Suposición]** Una misma persona puede tener varios perfiles; hay que evitar que revise sus propias peticiones.

## Propuesta de procedimiento

**Todo lo que sigue es propuesta mía, salvo lo marcado como decisión.**

**1. Quién revisa.** La **Dirección Clínica** del ámbito donde está el residente. **[Decidir]** La residencia designa un **segundo revisor** (el DPO o la dirección general) para las peticiones hechas por la propia persona revisora y para las de la Dirección Clínica si tuviera además un perfil de Enfermería o Medicina.

**2. Cuándo.**
- **[Decidir]** Propuesta: **semanal durante los tres primeros meses del piloto** (el volumen será bajo) y **mensual después**.
- Revisión **extraordinaria** ante una queja de un residente o familiar, una sospecha del equipo o la sospecha de una brecha de datos.

**3. Cómo, hoy.** Para cada residente trasladado en el periodo, abrir su trazabilidad y leer las anotaciones `TRANSFER_HISTORY_READ`. **Limitación:** no se pueden ver todos los residentes a la vez ni saber cuáles tuvieron peticiones sin abrirlos uno a uno. Esto no es viable con volumen. **Recomendación para Orlando y CJ (cambio de código que no aplico yo):** una vista de Dirección con todas las peticiones del periodo (cuenta, perfil, residente, unidad, hora, número de lecturas y motivo), sin contenido clínico. Mientras no exista, la revisión será por muestreo y el texto de pantalla no debe prometer que Dirección «las revisa».

**4. Qué se considera sospechoso.** Señales para investigar (no son prueba de nada):
- Motivo vacío de sentido («ver», «consulta», «x») o idéntico en todas las peticiones de una misma cuenta.
- Motivo que contiene **datos de salud o nombres** (incumple la norma interna; ver paso 6).
- Petición sobre un residente con el que la cuenta **no tiene otra actividad** después (ninguna valoración, nota o episodio en esa unidad en las horas siguientes). **[Suposición]** Se puede deducir de la auditoría, que registra cuenta, residente y hora.
- Muchas peticiones de una cuenta en poco tiempo, o peticiones **fuera de su turno** (la residencia puede cruzar con la planificación de turnos).
- Peticiones sobre residentes **conocidos** (personas allegadas al personal, familiares de compañeros o personas públicas). **[Decidir]** La residencia debe tener una regla de conflicto de interés para el personal, y el sistema no la conoce.
- Peticiones tras el traslado con mucho retraso (meses) sin motivo asistencial que lo explique.

**5. Qué se hace con un caso dudoso.**
1. Dirección Clínica lo anota (fecha, cuenta, residente, qué vio, por qué lo considera dudoso). **[Decidir]** dónde: hoy el sistema no tiene un registro de revisiones; mientras tanto, un documento interno controlado de la residencia.
2. Pide una explicación escrita a la persona. Si hay una razón asistencial razonable, se cierra y se le recuerda cómo redactar el motivo.
3. Si no la hay, se trata como **acceso indebido**: pasa al DPO.

**6. Qué se hace ante un uso indebido.**
1. **Contener:** Administración **revoca o suspende** el perfil o el ámbito de la cuenta mientras se investiga. **[Hecho]** Las concesiones y revocaciones de permisos quedan auditadas (`SqlProfessionalAccountRepository.cs`, líneas 369 y 393).
2. **Evaluar si es una brecha de datos:** un acceso de un profesional sin necesidad es una pérdida de confidencialidad (art. 33 RGPD). **[Decidir] DPO:** decide si hay que **notificar a la autoridad de control** y, en su caso, a los afectados. El plazo de notificación (72 horas desde que se tiene constancia) **a verificar** en el texto del art. 33.1 y en la guía de la autoridad; conviene dejar escrito en qué momento la residencia «tuvo constancia».
3. **Consecuencias laborales o disciplinarias:** **[Decidir]** la residencia, con asesoría laboral. No es decisión de ResidApp.
4. **Informar al residente o a su representante** si el DPO lo considera necesario (art. 34, **a verificar**).
5. **Medidas para que no se repita:** formación, y revisión de si el motivo o la pantalla favorecen el abuso.
6. **Conservar la prueba:** las anotaciones son inmutables, y no hay que pedir ninguna purga mientras haya una investigación abierta (ver fichero 05).

**7. Motivo con datos de salud o nombres.** No es necesariamente un acceso indebido, pero sí un incumplimiento de la norma interna y un dato que **no debería estar** ahí. Se recuerda a la persona y, cuando exista, se aplica el procedimiento de retirada del texto del motivo (fichero 05).

**8. Constancia de la revisión.** **[Decidir]** Un acta breve por revisión (fecha, periodo revisado, quién, hallazgos). Sin ella la residencia no podrá demostrar que revisa (art. 5.2, responsabilidad proactiva).

## A verificar o decidir

1. **[Decidir] Residencia:** revisor principal y segundo revisor; frecuencia; consecuencias; dónde se guardan las actas.
2. **[Decidir] Orlando/CJ:** vista de revisión de todas las peticiones; registro de revisiones; registro de intentos rechazados; si «las revisa» en pantalla pasa a «puede revisarlas» hasta entonces.
3. **A verificar:** plazo de notificación de brechas y en qué casos hay que notificar a la autoridad (AEPD o APDCAT, según el centro).
4. **Pregunta para el DPO real:** ¿es lícito que la propia revisión exija una lectura declarada de Dirección sobre el residente (punto 3 de «Lo que el sistema permite hoy») o conviene una vista de supervisión de accesos sin lectura clínica?
