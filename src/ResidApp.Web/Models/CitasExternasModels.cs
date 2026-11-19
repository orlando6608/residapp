using ResidApp.Application.Ports;
using ResidApp.Domain.Appointments;
using ResidApp.Shared;

namespace ResidApp.Web.Models;

/// <summary>Un residente que se puede elegir al registrar una cita, o cuyo nombre se muestra en una lista filtrada.</summary>
public sealed record ExternalAppointmentResidentOption(Guid Id, string Name, string? Location);

/// <summary>
/// El formulario de una cita externa (script 0101). Los selectores llevan códigos de lista cerrada (los mismos de la base); el día y la hora vienen de <c>input type=date</c> y
/// <c>type=time</c>. «Confirmado» es una casilla: «sin determinar» siempre queda pendiente y «no necesita» confirmado (lo corrige <see cref="ToAppointment"/>, no es un error).
/// MotivoCodigo y MotivoLinea solo se usan al reprogramar (cambiar el día o una hora ya puesta) y al cancelar.
/// </summary>
public sealed class ExternalAppointmentFormModel
{
    public Guid ResidenteId { get; set; }
    public Guid CitaId { get; set; }
    /// <summary>Identificador de operación del formulario de registrar (script 0102): un reenvío del mismo formulario no crea otra cita.</summary>
    public Guid OperacionId { get; set; }
    /// <summary>De qué cita viene esta, si se registra con «Registrar la siguiente cita» (script 0103). Solo viaja el identificador: los valores los lee el servidor.</summary>
    public Guid? CitaOrigenId { get; set; }
    public int VersionVista { get; set; }
    public DateOnly? Fecha { get; set; }
    public TimeOnly? Hora { get; set; }
    public string? Tipo { get; set; }
    public string? Especialidad { get; set; }
    public string? Lugar { get; set; }
    public string? TransporteModo { get; set; }
    public bool TransporteConfirmado { get; set; }
    public string? AcompananteModo { get; set; }
    public bool AcompananteConfirmado { get; set; }
    public bool PrepAyuno { get; set; }
    public bool PrepInforme { get; set; }
    public bool PrepTarjeta { get; set; }
    public bool PrepMedicacion { get; set; }
    public bool PrepSilla { get; set; }
    public bool PrepOtra { get; set; }
    public string? PrepTexto { get; set; }
    public string? Visibilidad { get; set; }
    public string? MotivoCodigo { get; set; }
    public string? MotivoLinea { get; set; }

    /// <summary>Convierte lo escrito en la ficha de la cita, o lanza DomainValidationException si no es válido.</summary>
    public ExternalAppointment ToAppointment()
    {
        if (Fecha is not { } date
            || !EnumCode.TryParseCode<ExternalAppointmentType>(Tipo ?? "", out var type)
            || !EnumCode.TryParseCode<ExternalTransportMode>(TransporteModo ?? "", out var transport)
            || !EnumCode.TryParseCode<ExternalCompanionMode>(AcompananteModo ?? "", out var companion)
            || !EnumCode.TryParseCode<ExternalFamilyVisibility>(Visibilidad ?? "", out var visibility))
        {
            throw new DomainValidationException(ExternalAppointment.InvalidCode);
        }

        ExternalAppointmentSpecialty? specialty = null;
        if (!string.IsNullOrWhiteSpace(Especialidad))
        {
            specialty = EnumCode.TryParseCode<ExternalAppointmentSpecialty>(Especialidad, out var parsed) ? parsed : throw new DomainValidationException(ExternalAppointment.InvalidCode);
        }

        var transportState = transport != ExternalTransportMode.SinDeterminar && TransporteConfirmado ? ExternalArrangementState.Confirmado : ExternalArrangementState.Pendiente;
        var companionState = companion == ExternalCompanionMode.NoNecesita || (companion != ExternalCompanionMode.SinDeterminar && AcompananteConfirmado)
            ? ExternalArrangementState.Confirmado : ExternalArrangementState.Pendiente;
        return new ExternalAppointment(
            date, Hora, type, specialty, Lugar, transport, transportState, companion, companionState,
            new ExternalPreparation(PrepAyuno, PrepInforme, PrepTarjeta, PrepMedicacion, PrepSilla, PrepOtra, PrepTexto), visibility);
    }

    public ExternalAppointmentReasonCode? ReasonOrNull() =>
        EnumCode.TryParseCode<ExternalAppointmentReasonCode>(MotivoCodigo ?? "", out var reason) ? reason : null;

    /// <summary>Una cita nueva: nada elegido de lo que el personal tiene que decidir (el tipo, el transporte y el acompañante) y visible para la familia, como dice el diseño.</summary>
    public static ExternalAppointmentFormModel ForNew(Guid residentId, DateOnly today) => new()
    {
        ResidenteId = residentId,
        OperacionId = Guid.NewGuid(),
        Fecha = today,
        TransporteModo = ExternalTransportMode.SinDeterminar.ToCode(),
        AcompananteModo = ExternalCompanionMode.SinDeterminar.ToCode(),
        Visibilidad = ExternalFamilyVisibility.Visible.ToCode(),
    };

    public static ExternalAppointmentFormModel From(ExternalAppointmentRecord record) => new()
    {
        ResidenteId = record.ResidentId.Value,
        CitaId = record.Id,
        VersionVista = record.Version,
        Fecha = record.Appointment.Date,
        Hora = record.Appointment.Time,
        Tipo = record.Appointment.Type.ToCode(),
        Especialidad = record.Appointment.Specialty?.ToCode(),
        Lugar = record.Appointment.Place,
        TransporteModo = record.Appointment.TransportMode.ToCode(),
        TransporteConfirmado = record.Appointment.TransportState == ExternalArrangementState.Confirmado,
        AcompananteModo = record.Appointment.CompanionMode.ToCode(),
        AcompananteConfirmado = record.Appointment.CompanionState == ExternalArrangementState.Confirmado,
        PrepAyuno = record.Appointment.Preparation.Fasting,
        PrepInforme = record.Appointment.Preparation.LastReport,
        PrepTarjeta = record.Appointment.Preparation.HealthCard,
        PrepMedicacion = record.Appointment.Preparation.Medication,
        PrepSilla = record.Appointment.Preparation.Wheelchair,
        PrepOtra = record.Appointment.Preparation.Other,
        PrepTexto = record.Appointment.Preparation.Text,
        Visibilidad = record.Appointment.FamilyVisibility.ToCode(),
    };
}

/// <summary>La lista «Próximas»: las citas entre dos días, con quién la mira y si puede registrar. ResidentId y ResidentName, si se filtra por un residente. Overdue son las «por cerrar».</summary>
public sealed record ExternalAppointmentListViewModel(
    IReadOnlyList<ExternalAppointmentRecord> Items, bool CanManage, DateOnly From, DateOnly To, DateOnly Today, SystemProfile Profile, Guid? ResidentId, string? ResidentName,
    IReadOnlyList<ExternalAppointmentRecord> Overdue);

/// <summary>El formulario de registrar o cambiar. Residents solo se rellena al registrar desde la lista (sin residente elegido); ResidentName, al registrar con residente o al cambiar.</summary>
public sealed record ExternalAppointmentFormViewModel(
    bool IsNew, ExternalAppointmentFormModel Form, string? ResidentName, IReadOnlyList<ExternalAppointmentResidentOption>? Residents, string? Error, DateOnly? OriginDate = null);

/// <summary>La ficha de una cita. FollowUp (la siguiente cita prevista y la ya registrada) y Note (la versión vigente de la nota «al terminar», solo Enfermería y Medicina) salen de consultas aparte: Administración no recibe la nota. Profile es quien mira.</summary>
public sealed record ExternalAppointmentDetailViewModel(
    ExternalAppointmentRecord Record, bool CanManage, DateOnly Today, SystemProfile Profile = SystemProfile.Administracion, ExternalAppointmentFollowUp? FollowUp = null, ExternalNoteVersion? Note = null);

public sealed record ExternalAppointmentCancelViewModel(ExternalAppointmentRecord Record, ExternalAppointmentFormModel Form, string? Error);

/// <summary>Los textos de las citas externas para las pantallas.</summary>
public static class ExternalAppointmentDisplay
{
    public static string Date(DateOnly date) => date.ToString("dd/MM/yyyy");

    public static string Time(ExternalAppointment appointment) => appointment.Time is { } time ? time.ToString("HH:mm") : "Por confirmar";

    /// <summary>«Falta transporte», «falta acompañante» o ambos; null si está todo cubierto.</summary>
    public static string? Missing(ExternalAppointment appointment) =>
        (appointment.TransportState, appointment.CompanionState) switch
        {
            (ExternalArrangementState.Pendiente, ExternalArrangementState.Pendiente) => "falta transporte y acompañante",
            (ExternalArrangementState.Pendiente, _) => "falta transporte",
            (_, ExternalArrangementState.Pendiente) => "falta acompañante",
            _ => null,
        };

    public static string Transport(ExternalAppointment appointment) =>
        EnumDisplay.Label(appointment.TransportMode) + (appointment.TransportMode == ExternalTransportMode.SinDeterminar ? "" : appointment.TransportState == ExternalArrangementState.Confirmado ? " (confirmado)" : " (pendiente)");

    public static string Companion(ExternalAppointment appointment) =>
        EnumDisplay.Label(appointment.CompanionMode) + (appointment.CompanionMode is ExternalCompanionMode.SinDeterminar or ExternalCompanionMode.NoNecesita ? ""
            : appointment.CompanionState == ExternalArrangementState.Confirmado ? " (confirmado)" : " (pendiente)");

    /// <summary>Qué hay que llevar o hacer, en una línea; null si no hay nada.</summary>
    public static string? Preparation(ExternalPreparation preparation)
    {
        var parts = new List<string>();
        if (preparation.Fasting) { parts.Add("ayuno"); }
        if (preparation.LastReport) { parts.Add("último informe"); }
        if (preparation.HealthCard) { parts.Add("tarjeta sanitaria"); }
        if (preparation.Medication) { parts.Add("medicación"); }
        if (preparation.Wheelchair) { parts.Add("silla de ruedas"); }
        if (preparation.Other) { parts.Add("otra"); }
        if (preparation.Text is { } text) { parts.Add(text); }
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    public static string StatusBadge(ExternalAppointmentStatus status) => status switch
    {
        ExternalAppointmentStatus.Programada => "text-bg-primary",
        ExternalAppointmentStatus.ConfirmadaHospital => "text-bg-success",
        ExternalAppointmentStatus.Realizada => "text-bg-secondary",
        ExternalAppointmentStatus.NoAcudida => "text-bg-warning",
        _ => "text-bg-light border",
    };
}

/// <summary>El bloque de la ficha del residente: sus citas abiertas de los próximos días, las «por cerrar» (abiertas con el día pasado) y si quien mira puede registrar.</summary>
public sealed record ExternalAppointmentResidentBlockViewModel(
    Guid ResidentId, IReadOnlyList<ExternalAppointmentRecord> Open, bool CanManage, IReadOnlyList<ExternalAppointmentRecord> Overdue, ExternalFamilyResidentState? Family = null);

/// <summary>
/// Calendario del personal, mensual o semanal (PR 3). Items son las citas visibles entre From y To (ya filtradas por unidad y por «solo las que tienen algo sin cubrir»); Reference es el mes (su día 1)
/// o el lunes de la semana que se mira. Units son las unidades de las citas de ese periodo, para el filtro.
/// </summary>
public sealed record ExternalAppointmentCalendarViewModel(
    string Mode, DateOnly From, DateOnly To, DateOnly Reference, DateOnly Today, IReadOnlyList<ExternalAppointmentRecord> Items, bool CanManage,
    Guid? UnitId, bool OnlyUncovered, IReadOnlyList<ExternalAppointmentUnitOption> Units);

public sealed record ExternalAppointmentUnitOption(Guid Id, string Name);

/// <summary>Textos del calendario, con los nombres de mes y de día escritos aquí para no depender de la cultura del servidor.</summary>
public static class ExternalAppointmentCalendarDisplay
{
    private static readonly string[] Months =
        ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];

    private static readonly string[] DaysShort = ["Lun", "Mar", "Mié", "Jue", "Vie", "Sáb", "Dom"];

    public static string Month(int month) => Months[month - 1];

    public static string MonthTitle(DateOnly firstOfMonth) => $"{char.ToUpperInvariant(Months[firstOfMonth.Month - 1][0])}{Months[firstOfMonth.Month - 1][1..]} de {firstOfMonth.Year}";

    /// <summary>Lun, Mar… (la semana empieza en lunes).</summary>
    public static string DayShort(DateOnly day) => DaysShort[((int)day.DayOfWeek + 6) % 7];

    public static string DayLong(DateOnly day) => $"{DayShort(day)} {day.Day}";

    public static DateOnly MondayOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    public static string WeekTitle(DateOnly monday)
    {
        var sunday = monday.AddDays(6);
        if (monday.Year != sunday.Year)
        {
            return $"{monday.Day} de {Month(monday.Month)} de {monday.Year} – {sunday.Day} de {Month(sunday.Month)} de {sunday.Year}";
        }

        return monday.Month == sunday.Month
            ? $"{monday.Day} – {sunday.Day} de {Month(sunday.Month)} de {sunday.Year}"
            : $"{monday.Day} de {Month(monday.Month)} – {sunday.Day} de {Month(sunday.Month)} de {sunday.Year}";
    }

    /// <summary>Las iniciales del nombre («Antonio Marín» → «A. M.»): en la cuadrícula del mes, donde no cabe el nombre, y para no enseñar nombres enteros de pasada.</summary>
    public static string Initials(string name) =>
        string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3).Select(part => char.ToUpperInvariant(part[0]) + "."));

    /// <summary>
    /// La marca de una cita: la palabra y la clase de color (el color nunca va solo). Una cerrada o cancelada dice su estado; una abierta con el día ya pasado, «Por cerrar» (decisión de Orlando,
    /// 2026-11-18); una abierta futura, qué falta, «Por confirmar» si todo está resuelto pero aún no hay hora (diseño, valor 9) o «Cubierta».
    /// </summary>
    public static (string Text, string Css) Mark(ExternalAppointmentRecord record, DateOnly today)
    {
        if (!ExternalAppointmentRules.IsOpen(record.Status))
        {
            return (EnumDisplay.Label(record.Status), "secondary");
        }

        if (record.Appointment.Date < today)
        {
            return ("Por cerrar", "warning");
        }

        if (ExternalAppointmentDisplay.Missing(record.Appointment) is { } missing)
        {
            return (char.ToUpperInvariant(missing[0]) + missing[1..], "danger");
        }

        return record.Appointment.TimeToConfirm ? ("Por confirmar", "warning") : ("Cubierta", "success");
    }
}

/// <summary>
/// El formulario de la nota «al terminar» (script 0103). VersionVista es el número de la versión vigente que vio quien escribe (0 si no había); OperacionId evita la versión doble. La siguiente cita
/// es una de tres cosas: ninguna, una fecha o «en N meses».
/// </summary>
public sealed class ExternalAppointmentNoteFormModel
{
    public Guid CitaId { get; set; }
    public int VersionVista { get; set; }
    public Guid OperacionId { get; set; }
    public string? Resultado { get; set; }
    public string? Texto { get; set; }
    /// <summary>«ninguna», «fecha» o «meses».</summary>
    public string? Siguiente { get; set; }
    public DateOnly? SiguienteFecha { get; set; }
    public int? SiguienteMeses { get; set; }
    public string? MotivoRetirada { get; set; }

    /// <summary>Convierte lo escrito en la nota, o lanza DomainValidationException si no es válido.</summary>
    public ExternalAppointmentNote ToNote()
    {
        if (!EnumCode.TryParseCode<ExternalNoteResult>(Resultado ?? "", out var result))
        {
            throw new DomainValidationException(ExternalAppointment.InvalidCode);
        }

        return Siguiente switch
        {
            "fecha" when SiguienteFecha is { } date => new ExternalAppointmentNote(result, Texto, date, null),
            "meses" when SiguienteMeses is { } months => new ExternalAppointmentNote(result, Texto, null, months),
            null or "" or "ninguna" => new ExternalAppointmentNote(result, Texto, null, null),
            _ => throw new DomainValidationException(ExternalAppointment.InvalidCode),
        };
    }

    public ExternalNoteRetractionReason? RetractionReasonOrNull() =>
        EnumCode.TryParseCode<ExternalNoteRetractionReason>(MotivoRetirada ?? "", out var reason) ? reason : null;

    public static ExternalAppointmentNoteFormModel For(Guid appointmentId, ExternalNoteVersion? latest) => For(appointmentId, latest, latest is { Retracted: false } ? latest : null);

    /// <summary>VersionVista es siempre la última versión (también si está retirada); los campos parten de la vigente, que no existe si la última es una retirada.</summary>
    private static ExternalAppointmentNoteFormModel For(Guid appointmentId, ExternalNoteVersion? latest, ExternalNoteVersion? current) => new()
    {
        CitaId = appointmentId,
        VersionVista = latest?.Number ?? 0,
        OperacionId = Guid.NewGuid(),
        Resultado = current?.Result?.ToCode(),
        Texto = current?.Text,
        Siguiente = current?.NextDate is not null ? "fecha" : current?.NextMonths is not null ? "meses" : "ninguna",
        SiguienteFecha = current?.NextDate,
        SiguienteMeses = current?.NextMonths,
    };
}

/// <summary>El formulario de la nota. Current es la versión vigente (null si no hay); Error, el aviso si lo escrito no vale.</summary>
public sealed record ExternalAppointmentNoteFormViewModel(ExternalAppointmentRecord Record, ExternalAppointmentNoteFormModel Form, ExternalNoteVersion? Current, string? Error);

/// <summary>Las versiones de la nota, de la más reciente a la más antigua (solo Enfermería y Medicina).</summary>
public sealed record ExternalAppointmentNoteVersionsViewModel(ExternalAppointmentRecord Record, IReadOnlyList<ExternalNoteVersion> Versions);

/// <summary>Textos de la nota «al terminar» y de la siguiente cita.</summary>
public static class ExternalNoteDisplay
{
    /// <summary>«el 12/03/2027» o «en 6 meses (hacia el 18/05/2027)», según lo anotado.</summary>
    public static string Next(ExternalNextVisit visit, DateOnly appointmentDate) =>
        visit.Date is { } date
            ? "el " + ExternalAppointmentDisplay.Date(date)
            : visit.Months is { } months ? $"en {months} {(months == 1 ? "mes" : "meses")} (hacia el {ExternalAppointmentDisplay.Date(appointmentDate.AddMonths(months))})" : "";

    public static string Next(ExternalNoteVersion version, DateOnly appointmentDate) => Next(new ExternalNextVisit(version.NextDate, version.NextMonths), appointmentDate);

    /// <summary>Fecha y hora de una versión, en la hora del servidor (la de Madrid).</summary>
    public static string When(DateTimeOffset at) => at.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
}

/// <summary>La pantalla «Ajustes para la familia» del centro (PR 5; script 0104): si las familias ven las citas externas, cuántas verían y, al encender, las tres confirmaciones. Error es el aviso si lo marcado no vale.</summary>
public sealed record ExternalFamilyCenterViewModel(ExternalFamilyCenterState State, string? Error);

/// <summary>La pantalla del interruptor de un residente (PR 5): si su familia ve las citas externas, cuántas se abrirían o se ven, y si quien mira puede cambiarlo (Administración y Enfermería; Medicina solo mira).</summary>
public sealed record ExternalFamilyResidentViewModel(Guid ResidentId, ExternalFamilyResidentState State, bool CanChange, string? Error);
