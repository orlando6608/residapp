using System.ComponentModel.DataAnnotations;
using ResidApp.Application.Ports;
using ResidApp.Domain.Baseline;
using ResidApp.Domain.Baseline.Catalogs;

namespace ResidApp.Web.Models;

/// <summary>ENF-20: la ficha del residente más el borrador activo propio, si existe.</summary>
public sealed record BaselineDraftHubViewModel(ScopeResidentSummary Resident, BaselineDraftDetail? Draft);

/// <summary>ENF-19/ENF-20 "crear borrador": los campos comunes de versión, exigidos desde la creación en
/// este alcance (ver CreateBaselineDraft.cs).</summary>
public sealed class CreateBaselineDraftFormModel
{
    [Required]
    public Guid ResidenteId { get; set; }

    [Required(ErrorMessage = "Elige un motivo.")]
    [Display(Name = "Motivo")]
    public BaselineReason? Motivo { get; set; }

    [Required(ErrorMessage = "Elige una fuente de información.")]
    [Display(Name = "Fuente de la información")]
    public InformationSourceCode? FuenteInformacionComun { get; set; }

    [Display(Name = "Fuente — especificar")]
    [StringLength(500)]
    public string? FuenteInformacionComunOtroTexto { get; set; }

    [Required(ErrorMessage = "Indica la fecha de la información.")]
    [Display(Name = "Fecha de la información")]
    public DateOnly? FechaInformacionComun { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Required]
    public Guid OperacionId { get; set; }
}

/// <summary>ENF-20 "cancelar el borrador propio con motivo".</summary>
public sealed class CancelBaselineDraftFormModel
{
    [Required]
    public Guid ResidenteId { get; set; }

    [Required(ErrorMessage = "Indica un motivo para cancelar el borrador.")]
    [StringLength(1000)]
    [Display(Name = "Motivo de la cancelación")]
    public string Motivo { get; set; } = string.Empty;
}

/// <summary>
/// ENF-20: un único modelo de formulario para las nueve áreas (superconjunto de todos los campos
/// posibles), en vez de nueve clases casi idénticas — AuxiliarController.BuildAreas ya sentó el precedente
/// de resolver "una acción, varias formas posibles" con un solo modelo. El controlador decide, según
/// AreaCode, qué subconjunto de campos leer y a qué record de dominio (Answers/*.cs) construirlo; esa
/// construcción ya aplica las reglas cruzadas de cada área.
/// </summary>
public sealed class BaselineAreaFormModel
{
    [Required]
    public Guid ResidenteId { get; set; }

    [Required]
    public BaselineArea AreaCode { get; set; }

    [StringLength(1000)]
    [Display(Name = "Observación")]
    public string? Observacion { get; set; }

    // Movilidad
    public MobilityDisplacementCode? DisplacementModeCode { get; set; }
    public MobilityAidCode? TechnicalAidCode { get; set; }
    [StringLength(500)]
    public string? TechnicalAidOtherText { get; set; }
    public MobilityTransferCode? TransferCode { get; set; }

    // Alimentación
    public FeedingRouteCode? RouteCode { get; set; }
    public FoodTextureCode? FoodTextureCode { get; set; }
    [StringLength(500)]
    public string? FoodTextureOtherText { get; set; }
    public LiquidConsistencyCode? LiquidConsistencyCode { get; set; }
    public FeedingAssistanceCode? AssistanceCode { get; set; }
    public SwallowingPrecautionsCode? SwallowingPrecautionsCode { get; set; }
    [StringLength(500)]
    public string? SwallowingPrecautionsText { get; set; }

    // Continencia
    public ContinenceValueCode? UrinationCode { get; set; }
    public ContinenceValueCode? BowelCode { get; set; }
    public List<ContinenceManagementCode> ManagementCodes { get; set; } = [];
    [StringLength(500)]
    public string? ManagementOtherText { get; set; }

    // Aseo/higiene
    public PersonalCareCode? PersonalCareAssistanceCode { get; set; }
    public BathingCode? BathingAssistanceCode { get; set; }

    // Cognición
    public CognitionCategoryCode? CategoryCode { get; set; }
    public EtiologyCode? EtiologyCode { get; set; }
    [StringLength(500)]
    public string? EtiologyOtherText { get; set; }
    public GdsCode? GdsCode { get; set; }
    public InformationSourceCode? ClinicalReferenceSourceCode { get; set; }
    [StringLength(500)]
    public string? ClinicalReferenceSourceOtherText { get; set; }
    [StringLength(50)]
    public string? ClinicalReferenceDate { get; set; }

    // Comunicación
    public ComprehensionCode? ComprehensionCode { get; set; }
    public ExpressionCode? ExpressionCode { get; set; }
    public List<CommunicationFormCode> UsualFormsCodes { get; set; } = [];
    [StringLength(500)]
    public string? UsualFormOtherText { get; set; }

    // Conducta
    public BehaviorStatusCode? StatusCode { get; set; }
    public List<BehaviorPatternCode> PatternCodes { get; set; } = [];
    [StringLength(500)]
    public string? PatternOtherText { get; set; }

    // Sueño
    public List<SleepPatternCode> SleepPatternCodes { get; set; } = [];

    // Ayudas habituales
    public List<UsualAidCode> AidCodes { get; set; } = [];
    [StringLength(500)]
    public string? OtherSupportProductText { get; set; }
    [StringLength(500)]
    public string? OtherSupportText { get; set; }
}

/// <summary>ENF-21: una opción elegida por cada uno de los diez ítems, codificada como el propio
/// BarthelItemCode -> option_code elegido (BarthelCatalog.Options), igual de simple que
/// RegistrarCambioFormModel.AreaTexto.</summary>
public sealed class BarthelFormModel
{
    [Required]
    public Guid ResidenteId { get; set; }

    [Required(ErrorMessage = "Indica la fecha de la valoración.")]
    [Display(Name = "Fecha de la valoración")]
    public DateOnly? FechaValoracion { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public Dictionary<string, string> Opciones { get; set; } = new();
}
