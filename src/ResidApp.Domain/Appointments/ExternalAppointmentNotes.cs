using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Appointments;

/// <summary>Qué cambia para el centro tras la cita: lista cerrada de la nota «al terminar» (DPO, apartado 7, punto 2; script 0103).</summary>
public enum ExternalNoteResult
{
    [Code("SIN_CAMBIOS")] [Display(Name = "Sin cambios")] SinCambios,
    [Code("NUEVA_CITA")] [Display(Name = "Hay que pedir una nueva cita")] NuevaCita,
    [Code("PENDIENTE_INFORME")] [Display(Name = "Pendiente de informe")] PendienteInforme,
    [Code("CAMBIO_PAUTA")] [Display(Name = "Cambio de pauta")] CambioPauta,
    [Code("TRASLADO")] [Display(Name = "Traslado")] Traslado,
    [Code("OTRA")] [Display(Name = "Otra")] Otra,
}

/// <summary>Por qué se retira una nota: lista cerrada, sin texto (la nota retirada no se borra; sus versiones se conservan).</summary>
public enum ExternalNoteRetractionReason
{
    [Code("ERROR_DE_REGISTRO")] [Display(Name = "Error de registro")] ErrorDeRegistro,
    [Code("DATOS_DE_OTRA_PERSONA")] [Display(Name = "Datos de otra persona")] DatosDeOtraPersona,
    [Code("OTRO")] [Display(Name = "Otro")] Otro,
}

/// <summary>
/// Lo que se anota de una cita realizada, ya validado (script 0103): el resultado de la lista cerrada, una línea opcional de 150 caracteres (solo qué debe hacer o saber el centro: ni
/// diagnósticos ni resultados ni dosis ni nombres) y, también opcional, la siguiente cita prevista como una fecha o «en N meses» (de 1 a 36), nunca las dos. La fecha, si la hay, se compara
/// con el día de la cita en la aplicación y en la base.
/// </summary>
public sealed record ExternalAppointmentNote
{
    public const int MaxTextLength = 150;
    public const int MaxNextMonths = 36;

    public ExternalNoteResult Result { get; }
    public string? Text { get; }
    public DateOnly? NextDate { get; }
    public int? NextMonths { get; }

    public ExternalAppointmentNote(ExternalNoteResult result, string? text, DateOnly? nextDate, int? nextMonths)
    {
        var line = ExternalAppointmentText.Line(text, MaxTextLength);
        if (!Enum.IsDefined(result) || (nextDate is not null && nextMonths is not null) || (nextMonths is { } months && (months < 1 || months > MaxNextMonths)))
        {
            throw new DomainValidationException(ExternalAppointment.InvalidCode);
        }

        Result = result;
        Text = line;
        NextDate = nextDate;
        NextMonths = nextMonths;
    }

    /// <summary>La siguiente cita prevista debe ser posterior al día de la cita realizada.</summary>
    public ExternalAppointmentNote RequireNextAfter(DateOnly appointmentDate) =>
        NextDate is { } next && next <= appointmentDate ? throw new DomainValidationException(ExternalAppointment.InvalidCode) : this;
}

/// <summary>
/// La siguiente cita prevista de la versión vigente de la nota (una fecha o «en N meses»), sin el resultado, la línea ni el autor. Es lo único de la nota que ve Administración; vacía si no
/// hay nota, está retirada o no lleva siguiente cita (script 0103; DPO, segunda lectura, apartado a).
/// </summary>
public sealed record ExternalNextVisit(DateOnly? Date, int? Months)
{
    public static ExternalNextVisit None { get; } = new(null, null);

    public bool IsSet => Date is not null || Months is not null;

    /// <summary>Una fecha sugerida para el formulario de la siguiente cita: la prevista, o la del día de la cita más N meses. Nada si no hay.</summary>
    public DateOnly? Suggested(DateOnly appointmentDate) => Date ?? (Months is { } months ? appointmentDate.AddMonths(months) : null);
}
