using ResidApp.Application.Ports;
using ResidApp.Domain.Appointments;
using ResidApp.Domain.Enfermeria;

namespace ResidApp.Web.Models;

/// <summary>Los cuatro estados que el portal no confunde (portal-familiar.md, historia 6): sesión sin ámbito, sin autorización activa, sin
/// publicaciones y fallo técnico. Un fallo técnico nunca se presenta como «sin novedades».</summary>
public sealed record PortalFamiliarEstadoViewModel(string Titulo, string Mensaje, bool PuedeReintentar = false, bool OtroPerfil = false);

/// <summary>El filtro del historial: sin valor son todas; si no, ordinarias o relevantes. CitasFuera dice si sale el enlace «Citas fuera del centro» (el centro las abrió y el residente no está restringido, haya o no citas).</summary>
public sealed record PortalFamiliarResidenteViewModel(
    FamilyPortalResidentHome Home, FamilyCommunicationType? Tipo, IReadOnlyList<FamilyPortalCommunication> Comunicados, bool CitasFuera = false);

public sealed record PortalFamiliarComunicadoViewModel(
    FamilyPortalResident Resident, FamilyPortalCommunication Comunicado, IReadOnlyList<FamilyPortalCommunication> Correcciones);

/// <summary>Las citas fuera del centro de un residente, para la lista por días y para el mes (Reference es el día 1 del mes que se mira).</summary>
public sealed record PortalFamiliarCitasFueraViewModel(FamilyExternalAppointments Data, DateOnly Reference);

public sealed record PortalFamiliarCitaFueraViewModel(FamilyPortalResident Resident, ExternalAppointmentForFamily Appointment);

/// <summary>Los textos de las citas fuera del centro para la familia. Solo con lo que el modelo de la familia lleva: nunca la especialidad si la cita es «sin especialidad».</summary>
public static class ExternalAppointmentFamilyDisplay
{
    /// <summary>«Consulta fuera del centro» o, con especialidad (nivel «visible»), «Consulta fuera del centro: Cardiología».</summary>
    public static string Title(ExternalAppointmentForFamily appointment) =>
        ExternalFamilyRules.GeneralTypeLabel(appointment.Type) + (appointment.Specialty is { } specialty and not ExternalAppointmentSpecialty.Otra ? ": " + EnumDisplay.Label(specialty) : "");

    public static string Time(ExternalAppointmentForFamily appointment) => appointment.Time is { } time ? time.ToString("HH:mm") : "Por confirmar";

    public static string Day(DateOnly day) => $"{ExternalAppointmentCalendarDisplay.DayShort(day)} {ExternalAppointmentDisplay.Date(day)}";

    /// <summary>«Falta transporte», «falta acompañante» o ambos; null si está todo resuelto.</summary>
    public static string? Missing(ExternalAppointmentForFamily appointment) =>
        (appointment.TransportState, appointment.CompanionState) switch
        {
            (ExternalArrangementState.Pendiente, ExternalArrangementState.Pendiente) => "Falta transporte y acompañante",
            (ExternalArrangementState.Pendiente, _) => "Falta transporte",
            (_, ExternalArrangementState.Pendiente) => "Falta acompañante",
            _ => null,
        };

    public static string Transport(ExternalAppointmentForFamily appointment) =>
        EnumDisplay.Label(appointment.TransportMode) + (appointment.TransportMode == ExternalTransportMode.SinDeterminar ? "" : appointment.TransportState == ExternalArrangementState.Confirmado ? " (confirmado)" : " (pendiente)");

    public static string Companion(ExternalAppointmentForFamily appointment) =>
        EnumDisplay.Label(appointment.CompanionMode) + (appointment.CompanionMode is ExternalCompanionMode.SinDeterminar or ExternalCompanionMode.NoNecesita ? ""
            : appointment.CompanionState == ExternalArrangementState.Confirmado ? " (confirmado)" : " (pendiente)");
}
