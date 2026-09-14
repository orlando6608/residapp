using System.ComponentModel.DataAnnotations;
using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;

namespace ResidApp.Web.Models;

/// <summary>ENF-18: identidad mínima del residente del ámbito más el resumen del basal vigente (null si
/// todavía no tiene ninguno firmado).</summary>
public sealed record EnfermeriaResidentDetailViewModel(ScopeResidentSummary Resident, CurrentBaselineSummary? Baseline);

/// <summary>ENF-01: contadores de las dos bandejas ya construidas (grupo E4). Seguimientos, indicaciones y
/// comunicaciones siguen sin contador propio hasta que existan esos grupos.</summary>
public sealed record EnfermeriaInicioViewModel(int Ordinarios, int Prioritarios);

/// <summary>ENF-04: detalle de un cambio recibido más el resumen del basal vigente del residente (null si
/// todavía no tiene ninguno firmado), igual que EnfermeriaResidentDetailViewModel.</summary>
public sealed record EnfermeriaChangeDetailViewModel(PendingChangeDetail Change, CurrentBaselineSummary? Baseline);

/// <summary>ENF-02/ENF-03 "antigüedad": tiempo transcurrido desde el registro, en la unidad más grande que
/// tenga sentido (días, horas o minutos) — no hace falta más precisión para ordenar visualmente una
/// bandeja.</summary>
public static class ElapsedTimeDisplay
{
    public static string Since(DateTimeOffset occurredAt)
    {
        var elapsed = DateTimeOffset.UtcNow - occurredAt;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }
        if (elapsed.TotalDays >= 1)
        {
            return $"{(int)elapsed.TotalDays} d";
        }
        if (elapsed.TotalHours >= 1)
        {
            return $"{(int)elapsed.TotalHours} h {elapsed.Minutes} min";
        }
        return $"{Math.Max(1, elapsed.Minutes)} min";
    }
}

/// <summary>ENF-16 "Registrar un evento observado por Enfermería", en su alcance mínimo: solo registrar y
/// guardar. Clasificacion reutiliza el mismo catálogo cerrado que AUX-10 (Ordinario/Prioritario); a
/// diferencia de RegistrarCambioFormModel (Auxiliar), aquí no hay motivo prioritario ni aviso directo: ese
/// ritual existe para que Auxiliar alerte a Enfermería antes de que exista una bandeja que lo recoja, y
/// aquí Enfermería ya es autora y destinataria a la vez.</summary>
public sealed class RegistrarEventoFormModel
{
    [Required]
    public Guid ResidenteId { get; set; }

    [Required]
    public Guid OperacionId { get; set; }

    [Required(ErrorMessage = "Indica qué observas.")]
    [StringLength(2000)]
    [Display(Name = "Observación")]
    public string Observacion { get; set; } = string.Empty;

    [Required(ErrorMessage = "Elige una clasificación.")]
    [Display(Name = "Clasificación inicial")]
    public DailyChangeClassification Clasificacion { get; set; } = DailyChangeClassification.Ordinario;

    [StringLength(2000)]
    [Display(Name = "Datos clínicos pertinentes")]
    public string? DatosClinicosPertinentes { get; set; }
}
