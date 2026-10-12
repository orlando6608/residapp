using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Appointments;

/// <summary>En qué punto está una solicitud de cita (CIT-S02 a CIT-S04). Solo la aceptación de la propuesta produce una cita confirmada.</summary>
public enum AppointmentRequestStatus
{
    [Code("PENDIENTE")] [Display(Name = "Pendiente de gestión")] Pendiente,
    [Code("PROPUESTA")] [Display(Name = "Propuesta del centro")] Propuesta,
    [Code("ACEPTADA")] [Display(Name = "Aceptada")] Aceptada,
    [Code("CANCELADA")] [Display(Name = "Cancelada")] Cancelada,
    [Code("CERRADA")] [Display(Name = "Cerrada por el centro")] Cerrada,
}

/// <summary>El motivo categorizado de la solicitud (CIT-S01). Lista cerrada a confirmar con CJ.</summary>
public enum AppointmentRequestReason
{
    [Code("SEGUIMIENTO_GENERAL")] [Display(Name = "Seguimiento general")] SeguimientoGeneral,
    [Code("RESULTADOS")] [Display(Name = "Resultados de pruebas o informes")] Resultados,
    [Code("MEDICACION")] [Display(Name = "Medicación o tratamiento")] Medicacion,
    [Code("CAMBIO_RECIENTE")] [Display(Name = "Un cambio reciente en el residente")] CambioReciente,
    [Code("INFORMACION_CUIDADOS")] [Display(Name = "Información sobre los cuidados")] InformacionCuidados,
    [Code("OTRO")] [Display(Name = "Otro motivo")] Otro,
}

/// <summary>La parte del día en que la familia puede atender la cita (disponibilidad aproximada, CIT-S01).</summary>
public enum AppointmentDayPart
{
    [Code("INDIFERENTE")] [Display(Name = "Me da igual")] Indiferente,
    [Code("MANANA")] [Display(Name = "Por la mañana")] Manana,
    [Code("TARDE")] [Display(Name = "Por la tarde")] Tarde,
}

/// <summary>Qué le pasó a una solicitud (CIT-C03).</summary>
public enum AppointmentRequestAction
{
    [Code("CREADA")] [Display(Name = "Solicitada")] Creada,
    [Code("PROPUESTA")] [Display(Name = "Propuesta del centro")] Propuesta,
    [Code("OTRO_HORARIO")] [Display(Name = "Otro horario pedido")] OtroHorario,
    [Code("ACEPTADA")] [Display(Name = "Aceptada")] Aceptada,
    [Code("CANCELADA")] [Display(Name = "Cancelada")] Cancelada,
    [Code("CERRADA")] [Display(Name = "Cerrada")] Cerrada,
}

/// <summary>Cuándo puede la familia, aproximadamente (CIT-S01): los días de la semana (1 lunes … 7 domingo, sin repetir; ninguno es «cualquier día») y la
/// parte del día.</summary>
public sealed record AppointmentAvailability(IReadOnlyList<int> Days, AppointmentDayPart DayPart)
{
    /// <summary>Los días como se guardan: «1,3,5» ordenados y sin repetir; vacío si da igual el día.</summary>
    public string DaysCode => string.Join(',', Days);

    public static AppointmentAvailability FromCodes(string days, AppointmentDayPart dayPart) =>
        new(days.Length == 0 ? [] : days.Split(',').Select(int.Parse).ToList(), dayPart);

    public bool Matches(DateOnly day, TimeOnly time)
    {
        var isoDay = day.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)day.DayOfWeek;
        var partOk = DayPart switch
        {
            AppointmentDayPart.Manana => time < new TimeOnly(14, 0),
            AppointmentDayPart.Tarde => time >= new TimeOnly(14, 0),
            _ => true,
        };
        return partOk && (Days.Count == 0 || Days.Contains(isoDay));
    }
}

/// <summary>Lo que la familia pide al solicitar una cita (CIT-S01): equipo, modalidad, motivo categorizado, un comentario breve opcional y su
/// disponibilidad aproximada. Nunca un profesional.</summary>
public sealed record AppointmentRequestInput(
    AppointmentTeam Team, AppointmentModality Modality, AppointmentRequestReason Reason, string? Comment, AppointmentAvailability Availability)
{
    public const int MaxCommentLength = 300;
    public const string InvalidCode = "APPOINTMENT_REQUEST_INVALID";

    /// <summary>Validada: equipo, modalidad y motivo elegidos, comentario de hasta 300 caracteres (vacío es ninguno) y días de 1 a 7; los repetidos se
    /// unifican y se ordenan.</summary>
    public static AppointmentRequestInput Validate(
        AppointmentTeam? team, AppointmentModality? modality, AppointmentRequestReason? reason, string? comment, IEnumerable<int>? days, AppointmentDayPart? dayPart)
    {
        var distinctDays = (days ?? []).Distinct().OrderBy(d => d).ToList();
        var normalizedComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (team is null || modality is null || reason is null || dayPart is null
            || distinctDays.Any(d => d is < 1 or > 7)
            || normalizedComment is { Length: > MaxCommentLength })
        {
            throw new DomainValidationException(InvalidCode);
        }

        return new AppointmentRequestInput(team.Value, modality.Value, reason.Value, normalizedComment, new AppointmentAvailability(distinctDays, dayPart.Value));
    }
}

/// <summary>Reglas de la propuesta y del cierre de una solicitud.</summary>
public static class AppointmentRequestRules
{
    public const int MaxProfessionalLength = 100;
    public const string ProposalInvalidCode = "APPOINTMENT_PROPOSAL_INVALID";

    /// <summary>Una propuesta está viva mientras su hora no haya empezado: después caduca (se calcula, no se guarda) y deja de reservar el hueco.</summary>
    public static bool IsProposalAlive(DateTimeOffset proposedStartsAt, DateTimeOffset now) => proposedStartsAt > now;

    /// <summary>El profesional interno que Administración apunta a una propuesta (opcional, hasta 100 caracteres). Solo lo ve Administración.</summary>
    public static string? NormalizeProfessional(string? professional) =>
        string.IsNullOrWhiteSpace(professional)
            ? null
            : professional.Trim() is { Length: <= MaxProfessionalLength } trimmed ? trimmed : throw new DomainValidationException(ProposalInvalidCode);
}
