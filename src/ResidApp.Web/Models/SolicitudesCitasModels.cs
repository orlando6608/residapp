using System.ComponentModel.DataAnnotations;
using ResidApp.Application.Ports;
using ResidApp.Domain.Appointments;

namespace ResidApp.Web.Models;

/// <summary>Textos de las solicitudes de cita en español (nunca los nombres de los enums en pantalla).</summary>
public static class AppointmentRequestDisplay
{
    public static string Label(AppointmentRequestStatus status) => status switch
    {
        AppointmentRequestStatus.Pendiente => "Pendiente de gestión",
        AppointmentRequestStatus.Propuesta => "Propuesta del centro",
        AppointmentRequestStatus.Aceptada => "Aceptada",
        AppointmentRequestStatus.Cancelada => "Cancelada",
        AppointmentRequestStatus.Cerrada => "Cerrada por el centro",
        _ => status.ToString(),
    };

    /// <summary>La propuesta cuya hora ya empezó se enseña como caducada, no como «propuesta del centro».</summary>
    public static string Label(AppointmentRequestStatus status, bool proposalExpired) =>
        status == AppointmentRequestStatus.Propuesta && proposalExpired ? "Propuesta caducada" : Label(status);

    public static string Badge(AppointmentRequestStatus status, bool proposalExpired) => status switch
    {
        AppointmentRequestStatus.Pendiente => "text-bg-warning",
        AppointmentRequestStatus.Propuesta => proposalExpired ? "text-bg-secondary" : "text-bg-primary",
        AppointmentRequestStatus.Aceptada => "text-bg-success",
        _ => "text-bg-secondary",
    };

    public static string Label(AppointmentRequestReason reason) => reason switch
    {
        AppointmentRequestReason.SeguimientoGeneral => "Seguimiento general",
        AppointmentRequestReason.Resultados => "Resultados de pruebas o informes",
        AppointmentRequestReason.Medicacion => "Medicación o tratamiento",
        AppointmentRequestReason.CambioReciente => "Un cambio reciente en el residente",
        AppointmentRequestReason.InformacionCuidados => "Información sobre los cuidados",
        AppointmentRequestReason.Otro => "Otro motivo",
        _ => reason.ToString(),
    };

    public static string Label(AppointmentDayPart part) => part switch
    {
        AppointmentDayPart.Indiferente => "Me da igual",
        AppointmentDayPart.Manana => "Por la mañana",
        AppointmentDayPart.Tarde => "Por la tarde",
        _ => part.ToString(),
    };

    public static string Label(AppointmentRequestAction action) => action switch
    {
        AppointmentRequestAction.Creada => "Solicitada",
        AppointmentRequestAction.Propuesta => "Horario propuesto por el centro",
        AppointmentRequestAction.OtroHorario => "Otro horario pedido",
        AppointmentRequestAction.Aceptada => "Propuesta aceptada",
        AppointmentRequestAction.Cancelada => "Cancelada",
        AppointmentRequestAction.Cerrada => "Cerrada por el centro",
        _ => action.ToString(),
    };

    /// <summary>La disponibilidad en una frase: «Lunes, miércoles · por la tarde».</summary>
    public static string Describe(AppointmentAvailability availability)
    {
        var days = availability.Days.Count == 0 ? "Cualquier día" : string.Join(", ", availability.Days.Select(AppointmentDisplay.Day));
        return availability.DayPart == AppointmentDayPart.Indiferente ? days : days + " · " + Label(availability.DayPart).ToLowerInvariant();
    }
}

/// <summary>El formulario con el que la familia solicita una cita (CIT-S01). OperacionId hace idempotente la doble pulsación.</summary>
public sealed class SolicitudCitaFormModel
{
    [Required(ErrorMessage = "Elige con quién quieres la cita.")]
    [Display(Name = "Con quién")]
    public AppointmentTeam? Equipo { get; set; }

    [Required(ErrorMessage = "Elige cómo quieres la cita.")]
    [Display(Name = "Cómo")]
    public AppointmentModality? Modalidad { get; set; }

    [Required(ErrorMessage = "Elige el motivo de la cita.")]
    [Display(Name = "Motivo")]
    public AppointmentRequestReason? Motivo { get; set; }

    [StringLength(AppointmentRequestInput.MaxCommentLength, ErrorMessage = "El comentario puede tener hasta 300 caracteres.")]
    [Display(Name = "Comentario (opcional)")]
    public string? Comentario { get; set; }

    [Display(Name = "Días en que puedes")]
    public List<int> Dias { get; set; } = [];

    [Display(Name = "Parte del día")]
    public AppointmentDayPart? ParteDelDia { get; set; } = AppointmentDayPart.Indiferente;

    public Guid OperacionId { get; set; } = Guid.NewGuid();
}

/// <summary>Solicitar una cita: el servicio del centro (modalidades de cada equipo) y el formulario.</summary>
public sealed record PortalFamiliarNuevaSolicitudViewModel(
    FamilyPortalResident Resident, FamilyAppointmentsHome Home, SolicitudCitaFormModel Form, string? Warning = null);

/// <summary>Una solicitud de la familia con su propuesta y su historial.</summary>
public sealed record PortalFamiliarSolicitudViewModel(FamilyPortalResident Resident, FamilyAppointmentRequest Request);

/// <summary>La bandeja de solicitudes de Administración. OnlyOpen deja solo las pendientes y las que tienen una propuesta.</summary>
public sealed record SolicitudesCitasViewModel(IReadOnlyList<AdministrationAppointmentRequest> Requests, bool OnlyOpen, DateTimeOffset Now);

public sealed class ProponerCitaFormModel
{
    [Required]
    public Guid SolicitudId { get; set; }

    [Required(ErrorMessage = "Elige el hueco que propones.")]
    [Display(Name = "Hueco propuesto")]
    public DateTimeOffset? Inicio { get; set; }

    [StringLength(AppointmentRequestRules.MaxProfessionalLength, ErrorMessage = "El nombre puede tener hasta 100 caracteres.")]
    [Display(Name = "Profesional (solo para el centro, opcional)")]
    public string? Profesional { get; set; }
}

public sealed record ProponerCitaViewModel(AdministrationAppointmentRequest Request, IReadOnlyList<AppointmentSlot> Slots, ProponerCitaFormModel Form);

public sealed class CerrarSolicitudFormModel
{
    [Required]
    public Guid SolicitudId { get; set; }

    [Required(ErrorMessage = "Escribe el motivo del cierre.")]
    [StringLength(AppointmentReason.MaxLength)]
    [Display(Name = "Motivo")]
    public string? Motivo { get; set; }
}

public sealed record CerrarSolicitudViewModel(AdministrationAppointmentRequest Request, CerrarSolicitudFormModel Form);
