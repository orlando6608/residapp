using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Appointments;

/// <summary>Tipo de cita fuera del centro (CJ, 1.2 de citas-externas-modulo-cj; script 0101).</summary>
public enum ExternalAppointmentType
{
    [Code("PRUEBA")] [Display(Name = "Prueba (analítica, radiografía, ecografía…)")] Prueba,
    [Code("CONSULTA")] [Display(Name = "Consulta")] Consulta,
    [Code("TRATAMIENTO")] [Display(Name = "Tratamiento (hospital de día, fisioterapia…)")] Tratamiento,
    [Code("OTRA")] [Display(Name = "Otra")] Otra,
}

/// <summary>Especialidad de la cita: lista cerrada, sin texto libre (diseño, valor 5; lista del DPO, apartado 3.1, pendiente de revisión por CJ). «Otra» no lleva texto.</summary>
public enum ExternalAppointmentSpecialty
{
    [Code("GERIATRIA")] [Display(Name = "Geriatría")] Geriatria,
    [Code("MEDICINA_INTERNA")] [Display(Name = "Medicina interna")] MedicinaInterna,
    [Code("CARDIOLOGIA")] [Display(Name = "Cardiología")] Cardiologia,
    [Code("NEUMOLOGIA")] [Display(Name = "Neumología")] Neumologia,
    [Code("DIGESTIVO")] [Display(Name = "Digestivo")] Digestivo,
    [Code("ENDOCRINOLOGIA")] [Display(Name = "Endocrinología")] Endocrinologia,
    [Code("NEFROLOGIA")] [Display(Name = "Nefrología")] Nefrologia,
    [Code("NEUROLOGIA")] [Display(Name = "Neurología")] Neurologia,
    [Code("TRAUMATOLOGIA_REHAB")] [Display(Name = "Traumatología y rehabilitación")] TraumatologiaRehabilitacion,
    [Code("OFTALMOLOGIA")] [Display(Name = "Oftalmología")] Oftalmologia,
    [Code("OTORRINOLARINGOLOGIA")] [Display(Name = "Otorrinolaringología")] Otorrinolaringologia,
    [Code("DERMATOLOGIA")] [Display(Name = "Dermatología")] Dermatologia,
    [Code("ODONTOLOGIA")] [Display(Name = "Odontología")] Odontologia,
    [Code("PODOLOGIA")] [Display(Name = "Podología")] Podologia,
    [Code("ANALISIS")] [Display(Name = "Análisis")] Analisis,
    [Code("RADIOLOGIA_ECOGRAFIA")] [Display(Name = "Radiología y ecografía")] RadiologiaEcografia,
    [Code("SALUD_MENTAL")] [Display(Name = "Salud mental")] SaludMental,
    [Code("ONCOLOGIA")] [Display(Name = "Oncología")] Oncologia,
    [Code("RADIOTERAPIA")] [Display(Name = "Radioterapia")] Radioterapia,
    [Code("INFECCIOSAS")] [Display(Name = "Enfermedades infecciosas")] Infecciosas,
    [Code("CONDUCTAS_ADICTIVAS")] [Display(Name = "Conductas adictivas")] ConductasAdictivas,
    [Code("PALIATIVOS")] [Display(Name = "Cuidados paliativos")] Paliativos,
    [Code("GENETICA")] [Display(Name = "Genética")] Genetica,
    [Code("GINECOLOGIA")] [Display(Name = "Ginecología")] Ginecologia,
    [Code("UROLOGIA")] [Display(Name = "Urología")] Urologia,
    [Code("PROCTOLOGIA")] [Display(Name = "Proctología")] Proctologia,
    [Code("OTRA")] [Display(Name = "Otra")] Otra,
}

/// <summary>Cómo llega el residente a la cita (ficha 2.1 de CJ).</summary>
public enum ExternalTransportMode
{
    [Code("SIN_DETERMINAR")] [Display(Name = "Sin determinar")] SinDeterminar,
    [Code("FAMILIA")] [Display(Name = "La familia")] Familia,
    [Code("CENTRO")] [Display(Name = "El centro")] Centro,
    [Code("AMBULANCIA")] [Display(Name = "Ambulancia")] Ambulancia,
}

/// <summary>Quién acompaña al residente (ficha 2.1 de CJ).</summary>
public enum ExternalCompanionMode
{
    [Code("SIN_DETERMINAR")] [Display(Name = "Sin determinar")] SinDeterminar,
    [Code("FAMILIAR")] [Display(Name = "Un familiar")] Familiar,
    [Code("PERSONAL_CENTRO")] [Display(Name = "Personal del centro")] PersonalCentro,
    [Code("LO_LLEVA_TRANSPORTE")] [Display(Name = "Lo lleva el transporte")] LoLlevaTransporte,
    [Code("NO_NECESITA")] [Display(Name = "No necesita")] NoNecesita,
}

/// <summary>Si el transporte o el acompañante están resueltos.</summary>
public enum ExternalArrangementState
{
    [Code("PENDIENTE")] [Display(Name = "Pendiente")] Pendiente,
    [Code("CONFIRMADO")] [Display(Name = "Confirmado")] Confirmado,
}

/// <summary>Estado de la cita. «Reprogramada» no es un estado: es un cambio de fecha que queda en el historial (diseño, valor 10). Realizada, no acudida y cancelada son finales.</summary>
public enum ExternalAppointmentStatus
{
    [Code("PROGRAMADA")] [Display(Name = "Programada")] Programada,
    [Code("CONFIRMADA_HOSPITAL")] [Display(Name = "Confirmada por el hospital")] ConfirmadaHospital,
    [Code("REALIZADA")] [Display(Name = "Realizada")] Realizada,
    [Code("NO_ACUDIDA")] [Display(Name = "No se acudió")] NoAcudida,
    [Code("CANCELADA")] [Display(Name = "Cancelada")] Cancelada,
}

/// <summary>Qué ve la familia de la cita, elegido a mano en cada una (diseño, decisión 7 de Orlando): visible por defecto; sin especialidad (se ve el lugar, no el tipo detallado ni la
/// especialidad); u oculta (ni se ve ni hay avisos ni «Me encargo yo»).</summary>
public enum ExternalFamilyVisibility
{
    [Code("VISIBLE")] [Display(Name = "Visible (tipo, especialidad y lugar)")] Visible,
    [Code("SIN_ESPECIALIDAD")] [Display(Name = "Sin especialidad (se ve el lugar)")] SinEspecialidad,
    [Code("OCULTA")] [Display(Name = "Oculta para la familia")] Oculta,
}

/// <summary>Qué le pasó a la cita en el historial.</summary>
public enum ExternalAppointmentChangeAction
{
    [Code("CREADA")] [Display(Name = "Registrada")] Creada,
    [Code("MODIFICADA")] [Display(Name = "Modificada")] Modificada,
    [Code("REPROGRAMADA")] [Display(Name = "Reprogramada")] Reprogramada,
    [Code("ESTADO_CAMBIADO")] [Display(Name = "Estado cambiado")] EstadoCambiado,
    [Code("CANCELADA")] [Display(Name = "Cancelada")] Cancelada,
}

/// <summary>Motivo de reprogramar o cancelar: lista cerrada, sin datos clínicos (DPO, apartado 3).</summary>
public enum ExternalAppointmentReasonCode
{
    [Code("CAMBIO_DEL_HOSPITAL")] [Display(Name = "Cambio del hospital")] CambioDelHospital,
    [Code("FALTA_TRANSPORTE_O_ACOMPANANTE")] [Display(Name = "Falta transporte o acompañante")] FaltaTransporteOAcompanante,
    [Code("CAMBIO_EN_EL_CENTRO")] [Display(Name = "Cambio en el centro")] CambioEnElCentro,
    [Code("PETICION_DE_LA_FAMILIA")] [Display(Name = "A petición de la familia")] PeticionDeLaFamilia,
    [Code("ERROR_DE_REGISTRO")] [Display(Name = "Error de registro")] ErrorDeRegistro,
    [Code("OTRO")] [Display(Name = "Otro")] Otro,
}

/// <summary>Las casillas de «qué preparar» más una línea corta opcional. Solo lo que hay que llevar o hacer para salir; las indicaciones clínicas no van aquí (DPO, apartado 3).</summary>
public sealed record ExternalPreparation(bool Fasting, bool LastReport, bool HealthCard, bool Medication, bool Wheelchair, bool Other, string? Text)
{
    public const int MaxTextLength = 100;
    public static ExternalPreparation None { get; } = new(false, false, false, false, false, false, null);
}

/// <summary>Reglas comunes de los textos cortos de las citas externas (una línea, con límite).</summary>
public static class ExternalAppointmentText
{
    /// <summary>La línea recortada, null si está vacía; ExternalAppointment.InvalidCode si pasa del límite o lleva saltos de línea o caracteres de control.</summary>
    public static string? Line(string? text, int maxLength)
    {
        var line = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        return line is null ? null
            : line.Length > maxLength || line.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.LineSeparator
                or System.Globalization.UnicodeCategory.ParagraphSeparator)
                ? throw new DomainValidationException(ExternalAppointment.InvalidCode)
                : line;
    }
}

/// <summary>
/// Lo que se registra de una cita externa (script 0101), ya validado: el día del centro (su hora, no UTC) y la hora opcional (vacía es «por confirmar»), el tipo y la
/// especialidad de lista cerrada, el lugar (una línea de 80, solo el nombre del centro sanitario), el transporte y el acompañante con su modo y su estado, qué preparar y qué ve la
/// familia. Una cita nueva nace programada.
/// </summary>
public sealed record ExternalAppointment
{
    public const string InvalidCode = "EXTERNAL_APPOINTMENT_INVALID";
    public const string NotFoundCode = "EXTERNAL_APPOINTMENT_NOT_FOUND";
    public const string ClosedCode = "EXTERNAL_APPOINTMENT_CLOSED";
    public const string RevisionConflictCode = "EXTERNAL_APPOINTMENT_REVISION_CONFLICT";
    public const int MaxPlaceLength = 80;

    public DateOnly Date { get; }
    public TimeOnly? Time { get; }
    public ExternalAppointmentType Type { get; }
    public ExternalAppointmentSpecialty? Specialty { get; }
    public string? Place { get; }
    public ExternalTransportMode TransportMode { get; }
    public ExternalArrangementState TransportState { get; }
    public ExternalCompanionMode CompanionMode { get; }
    public ExternalArrangementState CompanionState { get; }
    public ExternalPreparation Preparation { get; }
    public ExternalFamilyVisibility FamilyVisibility { get; }

    /// <summary>Cubierta: transporte y acompañante confirmados («no necesita» queda confirmado solo). Si no, falta algo.</summary>
    public bool Covered => TransportState == ExternalArrangementState.Confirmado && CompanionState == ExternalArrangementState.Confirmado;

    /// <summary>Sin hora, la cita sale como «Por confirmar».</summary>
    public bool TimeToConfirm => Time is null;

    public ExternalAppointment(
        DateOnly date, TimeOnly? time, ExternalAppointmentType type, ExternalAppointmentSpecialty? specialty, string? place,
        ExternalTransportMode transportMode, ExternalArrangementState transportState, ExternalCompanionMode companionMode, ExternalArrangementState companionState,
        ExternalPreparation? preparation, ExternalFamilyVisibility familyVisibility)
    {
        var line = ExternalAppointmentText.Line(place, MaxPlaceLength);
        var prepText = ExternalAppointmentText.Line(preparation?.Text, ExternalPreparation.MaxTextLength);
        if (!Enum.IsDefined(type) || (specialty is { } s && !Enum.IsDefined(s)) || !Enum.IsDefined(transportMode) || !Enum.IsDefined(transportState)
            || !Enum.IsDefined(companionMode) || !Enum.IsDefined(companionState) || !Enum.IsDefined(familyVisibility)
            // «Sin determinar» siempre pendiente; «no necesita» queda confirmado solo (valor 8); la BD impone lo mismo.
            || (transportMode == ExternalTransportMode.SinDeterminar && transportState != ExternalArrangementState.Pendiente)
            || (companionMode == ExternalCompanionMode.SinDeterminar && companionState != ExternalArrangementState.Pendiente)
            || (companionMode == ExternalCompanionMode.NoNecesita && companionState != ExternalArrangementState.Confirmado))
        {
            throw new DomainValidationException(InvalidCode);
        }

        Date = date;
        Time = time is { } t ? new TimeOnly(t.Hour, t.Minute) : null;
        Type = type;
        Specialty = specialty;
        Place = line;
        TransportMode = transportMode;
        TransportState = transportState;
        CompanionMode = companionMode;
        CompanionState = companionState;
        Preparation = (preparation ?? ExternalPreparation.None) with { Text = prepText };
        FamilyVisibility = familyVisibility;
    }

    /// <summary>Al registrar o reprogramar, el día no puede ser anterior a hoy en el centro (la hora del centro la calcula quien llama; la base no lo impide a propósito).</summary>
    public ExternalAppointment RequireNotInPast(DateOnly todayAtCenter) =>
        Date >= todayAtCenter ? this : throw new DomainValidationException(InvalidCode);
}

/// <summary>Reglas de estado y de motivo de las citas externas.</summary>
public static class ExternalAppointmentRules
{
    public const int MaxReasonLineLength = 100;
    public const string DischargeCancelReason = "Cancelada por el sistema al dar de baja al residente.";

    /// <summary>Reprogramar es cambiar el día, o cambiar una hora ya puesta (por otra o por «por confirmar»). Poner la hora por primera vez (de «por confirmar» a una hora) solo completa la ficha:
    /// no es una reprogramación y no pide motivo (decisión de Orlando, 2026-11-18).</summary>
    public static bool IsReschedule(ExternalAppointment before, ExternalAppointment after) =>
        before.Date != after.Date || (before.Time is not null && before.Time != after.Time);

    /// <summary>Programada y confirmada por el hospital están abiertas; realizada, no acudida y cancelada son finales y no vuelven a cambiar.</summary>
    public static bool IsOpen(ExternalAppointmentStatus status) => status is ExternalAppointmentStatus.Programada or ExternalAppointmentStatus.ConfirmadaHospital;

    /// <summary>
    /// Una cita abierta pasa a otro estado; una final, a ninguno (decisión de Orlando, 2026-11-18). «Realizada» y «No se acudió» solo desde el día de la cita en adelante (el día de hoy cuenta: una
    /// cita futura equivocada se cancela o se cambia), y «Confirmada por el hospital» no vuelve a «Programada».
    /// </summary>
    public static bool CanTransition(ExternalAppointmentStatus from, ExternalAppointmentStatus to, DateOnly appointmentDate, DateOnly todayAtCenter) =>
        Enum.IsDefined(from) && Enum.IsDefined(to) && from != to && IsOpen(from)
        && !(from == ExternalAppointmentStatus.ConfirmadaHospital && to == ExternalAppointmentStatus.Programada)
        && (to is not (ExternalAppointmentStatus.Realizada or ExternalAppointmentStatus.NoAcudida) || appointmentDate <= todayAtCenter);

    /// <summary>Motivo de reprogramar o cancelar: el código de la lista cerrada y, opcional, una línea de 100 sin saltos de línea.</summary>
    public static (ExternalAppointmentReasonCode Code, string? Line) Reason(ExternalAppointmentReasonCode? code, string? line) =>
        code is { } c && Enum.IsDefined(c)
            ? (c, ExternalAppointmentText.Line(line, MaxReasonLineLength))
            : throw new DomainValidationException(ExternalAppointment.InvalidCode);
}
