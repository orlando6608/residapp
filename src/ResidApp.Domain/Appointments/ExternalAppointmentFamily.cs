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
