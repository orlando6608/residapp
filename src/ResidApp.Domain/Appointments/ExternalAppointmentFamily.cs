using ResidApp.Shared;

namespace ResidApp.Domain.Appointments;

/// <summary>
/// Reglas de lo que ve la familia de las citas externas (script 0104; docs/tareas/alta-prioridad/pendientes-citas-externas.md, apartado 10). La regla de visibilidad es una sola y se evalúa en cada
/// lectura, sin caché: la familia ve una cita solo si el centro está encendido, el residente no está restringido y la cita no es oculta. Sin fila de configuración, el centro está apagado.
/// </summary>
public static class ExternalFamilyRules
{
    public const string ConflictCode = ExternalAppointment.RevisionConflictCode;

    /// <summary>Hasta cuántos días por delante ve la familia las citas (decisión de Orlando, 2026-11-18). Las de días pasados no se ven nunca.</summary>
    public const int WindowDays = 90;

    // La familia ve las citas abiertas (programadas o confirmadas por el hospital) y, hasta su fecha original, las canceladas; las realizadas y las «no se acudió» no se ven nunca. Lo aplica
    // el SQL de SqlFamilyExternalAppointments, que es la única implementación de la regla.

    /// <summary>El tipo general que ve la familia (nunca la especialidad ni un tipo más detallado): «Prueba / Consulta / Tratamiento fuera del centro», y «Cita fuera del centro» si es «otra».</summary>
    public static string GeneralTypeLabel(ExternalAppointmentType type) => type switch
    {
        ExternalAppointmentType.Prueba => "Prueba fuera del centro",
        ExternalAppointmentType.Consulta => "Consulta fuera del centro",
        ExternalAppointmentType.Tratamiento => "Tratamiento fuera del centro",
        _ => "Cita fuera del centro",
    };
}

/// <summary>Lo que Administración confirma al encender el interruptor del centro: que la residencia ya informó a residentes y familias, que tiene el procedimiento de voluntad del residente y que
/// actualizó su evaluación de impacto (EIPD). Hacen falta las tres.</summary>
public sealed record ExternalFamilyConfirmations(bool Informed, bool ResidentWill, bool ImpactAssessment)
{
    public bool All => Informed && ResidentWill && ImpactAssessment;
}

/// <summary>De qué parte de la cita se encarga un familiar (PR 6; script 0105): el transporte o el acompañamiento. Cada uno se toma por separado.</summary>
public enum ExternalArrangementScope
{
    [Code("TRANSPORTE")] Transporte,
    [Code("ACOMPANANTE")] Acompanante,
}

/// <summary>Lo que la familia puede hacer sobre una cita: «Me encargo yo», «Ya no puedo» (soltar lo asumido), «No puedo» (decir que no puede) y volver atrás en «No puedo».</summary>
public enum FamilyArrangementAction
{
    Take,
    Release,
    Refuse,
    UndoRefuse,
}

/// <summary>Qué pasó al pedir una acción de la familia: no existe o no la puede ver (el mismo 404 de siempre), hecha, ya la tiene otra persona, o ya no hace falta (por ejemplo, el personal ya la dejó resuelta).</summary>
public enum FamilyArrangementOutcome
{
    NotFound,
    Done,
    TakenByAnother,
    NotApplicable,
}

/// <summary>Quién tiene hoy una parte de la cita, visto por una familiar: nadie, ella misma u otra persona de la familia (sin decir quién). El personal con permiso ve el nombre en su pantalla.</summary>
public enum ExternalFamilyHolder
{
    Nobody,
    You,
    Another,
}
