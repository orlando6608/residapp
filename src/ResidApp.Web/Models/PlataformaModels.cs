using System.ComponentModel.DataAnnotations;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Structure;
using ResidApp.Domain.Platform;
using ResidApp.Shared;

namespace ResidApp.Web.Models;

/// <summary>Alta de un centro por el operador de plataforma. OperacionId es el id del centro nuevo, así que reenviar no lo duplica.
/// Los códigos del centro y de la unidad no se piden: la aplicación los deriva de los nombres.</summary>
public sealed class NewCenterFormModel
{
    public Guid OperacionId { get; set; }

    [Required(ErrorMessage = "Escribe el nombre del centro.")]
    [StringLength(CenterUnit.MaxNameLength, ErrorMessage = "El nombre del centro no puede pasar de {1} caracteres.")]
    [Display(Name = "Nombre del centro")]
    public string? NombreCentro { get; set; }

    /// <summary>El logo del centro, opcional: después lo cambia su Administración.</summary>
    public IFormFile? Logo { get; set; }

    /// <summary>La zona horaria del centro, de las que ofrece Plataforma (script 0109). Se elige al crearlo; después solo Plataforma la corrige.</summary>
    [Required(ErrorMessage = "Elige la zona horaria del centro.")]
    [Display(Name = "Zona horaria del centro")]
    public string? ZonaHoraria { get; set; } = CenterTimeZoneRules.DefaultZoneCode;

    [Required(ErrorMessage = "Escribe el nombre de la primera unidad.")]
    [StringLength(CenterUnit.MaxNameLength, ErrorMessage = "El nombre de la unidad no puede pasar de {1} caracteres.")]
    [Display(Name = "Nombre de la primera unidad")]
    public string? NombreUnidad { get; set; }

    [Required(ErrorMessage = "Escribe el identificador de acceso del administrador.")]
    [StringLength(ProfessionalAccount.MaxSubjectLength, MinimumLength = ProfessionalAccount.MinSubjectLength,
        ErrorMessage = "El identificador de acceso lleva entre {2} y {1} caracteres.")]
    [Display(Name = "Identificador de acceso del administrador")]
    public string? IdentificadorAdministrador { get; set; }

    [Required(ErrorMessage = "Escribe el nombre del administrador.")]
    [StringLength(ProfessionalAccount.MaxDisplayNameLength, ErrorMessage = "El nombre del administrador no puede pasar de {1} caracteres.")]
    [Display(Name = "Nombre del administrador")]
    public string? NombreAdministrador { get; set; }
}

public sealed record NewCenterViewModel(NewCenterFormModel Form, IReadOnlyList<CenterTimeZone> Zones);

/// <summary>
/// Activar, renovar o apagar un módulo de un centro (ADR 0015). Lleva siempre cuándo, cómo y con qué referencia pidió el centro el cambio (lo que prueba el plazo de apagado del anexo):
/// referencias opacas, nunca nombres de personas. Las fechas de vigencia son días de Madrid; <see cref="SolicitudRecibidaEn"/> es la hora local de Madrid de un campo datetime-local.
/// </summary>
public sealed class ModuleChangeFormModel
{
    public Guid CentroId { get; set; }

    public string? ModuloCodigo { get; set; }

    public DateOnly? VigenteDesde { get; set; }

    public DateOnly? VigenteHasta { get; set; }

    public string? ReferenciaAnexo { get; set; }

    public string? VersionAnexo { get; set; }

    /// <summary>Códigos de las condiciones de la ficha que se han marcado como comprobadas.</summary>
    public List<string> Condiciones { get; set; } = [];

    public ResidApp.Domain.Modules.ModuleDisableReason? MotivoBaja { get; set; }

    public DateTime? SolicitudRecibidaEn { get; set; }

    public ResidApp.Domain.Modules.ModuleRequestChannel? Canal { get; set; }
}

/// <summary>Los módulos de un centro para la sección de Plataforma, con si la clave global del proveedor de cada uno está puesta.</summary>
public sealed record PlatformCenterModulesViewModel(
    Guid CenterId, IReadOnlyList<ResidApp.Application.UseCases.PlatformApplicationService.CenterModuleView> Modules, IReadOnlySet<string> ProviderKeyMissing,
    DateOnly Today);

/// <summary>Los vencimientos próximos y los vencidos sin renovar de todos los centros.</summary>
public sealed record PlatformExpiringModulesViewModel(IReadOnlyList<ResidApp.Application.Ports.PlatformExpiringModule> Modules, DateOnly Today);

/// <summary>Textos en español de lo que ve Plataforma de los módulos: los enums de lista cerrada y las condiciones de la ficha de privacidad.</summary>
public static class ModuleDisplay
{
    public static string Channel(ResidApp.Domain.Modules.ModuleRequestChannel channel) => channel switch
    {
        ResidApp.Domain.Modules.ModuleRequestChannel.Correo => "Correo",
        ResidApp.Domain.Modules.ModuleRequestChannel.Telefono => "Teléfono",
        ResidApp.Domain.Modules.ModuleRequestChannel.Ticket => "Ticket",
        _ => "Otro",
    };

    public static string Reason(ResidApp.Domain.Modules.ModuleDisableReason reason) => reason switch
    {
        ResidApp.Domain.Modules.ModuleDisableReason.PeticionCentro => "Petición del centro",
        ResidApp.Domain.Modules.ModuleDisableReason.FinContrato => "Fin del contrato",
        ResidApp.Domain.Modules.ModuleDisableReason.Impago => "Impago",
        _ => "Otro motivo",
    };

    /// <summary>El texto de cada condición de la ficha (el de <c>docs/privacidad/ficha-modulo-dictado-voz.md</c>); un código nuevo del catálogo sin texto aquí se enseña con su código.</summary>
    public static string Condition(string code) => code switch
    {
        ResidApp.Domain.Modules.CenterModules.ConditionAnnexSigned =>
            "El anexo de módulos del contrato está firmado y vale como instrucción documentada del centro, con el plazo de apagado fijado.",
        ResidApp.Domain.Modules.CenterModules.ConditionZeroRetention =>
            "Hay retención cero por escrito del proveedor (Mistral), con datos de salud cubiertos, región UE y entrenamiento desactivado.",
        ResidApp.Domain.Modules.CenterModules.ConditionImpactAssessment => "La evaluación de impacto del centro cubre este módulo.",
        ResidApp.Domain.Modules.CenterModules.ConditionStaffInformed => "El personal y los residentes (o sus representantes) están informados.",
        _ => code,
    };

    public static string Status(ResidApp.Application.UseCases.PlatformApplicationService.CenterModuleView view, DateOnly today) => view switch
    {
        { Last: null } => "Nunca activado",
        { Last.Enabled: false } => "Apagado",
        { State: ResidApp.Domain.Modules.ModuleState.Active } => $"Activo hasta el {view.Last!.ValidUntil:dd/MM/yyyy}",
        _ when view.Last!.ValidFrom > today => $"Empieza el {view.Last.ValidFrom:dd/MM/yyyy}",
        _ => $"Vencido el {view.Last.ValidUntil:dd/MM/yyyy}",
    };
}

/// <summary>Corregir la zona horaria de un centro (script 0109). Lleva lo que Plataforma vio afectado, que el servidor vuelve a contar al guardar: si ha cambiado, pide confirmar de nuevo.</summary>
public sealed class CenterTimeZoneFormModel
{
    public Guid CentroId { get; set; }

    public string? ZonaHoraria { get; set; }

    public CenterTimeZoneChangeReason? Motivo { get; set; }

    public int FranjasVistas { get; set; }

    public int CitasVistas { get; set; }

    public bool Confirmo { get; set; }
}

public sealed record PlatformCenterTimeZoneViewModel(Guid CenterId, CenterTimeZoneState State, IReadOnlyList<CenterTimeZone> Zones);

/// <summary>Los textos de la zona horaria del centro en pantalla: lista cerrada de motivos y su historia.</summary>
public static class CenterTimeZoneDisplay
{
    public static string Reason(CenterTimeZoneChangeReason reason) => reason switch
    {
        CenterTimeZoneChangeReason.ErrorDeAlta => "Se eligió mal al dar de alta el centro",
        CenterTimeZoneChangeReason.CambioDeUbicacion => "El centro ha cambiado de ubicación",
        _ => reason.ToString(),
    };

    /// <summary>El motivo de una fila de la historia, por su código de base de datos.</summary>
    public static string ReasonOfCode(string code) =>
        code == "ALTA" ? "Alta del centro" : EnumCode.TryParseCode<CenterTimeZoneChangeReason>(code, out var reason) ? Reason(reason) : code;
}
