using System.ComponentModel.DataAnnotations;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;

namespace ResidApp.Web.Models;

/// <summary>Una fila de la pantalla de rangos: mínimo y máximo de una constante; ambos vacíos = sin rango.
/// Se envían como type="number", que ASP.NET Core interpreta en cultura invariante (el decimal va con punto).</summary>
public sealed class RangoReferenciaFila
{
    public VitalSignCode Constante { get; set; }

    [Range(typeof(decimal), "0.1", "9999", ParseLimitsInInvariantCulture = true, ErrorMessage = "Indica un valor positivo.")]
    [Display(Name = "Mínimo")]
    public decimal? Minimo { get; set; }

    [Range(typeof(decimal), "0.1", "9999", ParseLimitsInInvariantCulture = true, ErrorMessage = "Indica un valor positivo.")]
    [Display(Name = "Máximo")]
    public decimal? Maximo { get; set; }
}

/// <summary>Formulario de rangos de referencia del centro. Version es la leída al abrir la pantalla
/// (concurrencia optimista).</summary>
public sealed class RangosReferenciaFormModel
{
    public int Version { get; set; }

    public List<RangoReferenciaFila> Rangos { get; set; } = new();

    /// <summary>Una fila por constante configurable, en el orden del catálogo, con los valores vigentes.</summary>
    public static RangosReferenciaFormModel From(ReferenceRangesView view) => new()
    {
        Version = view.Version,
        Rangos = Enum.GetValues<VitalSignCode>().Select(code =>
        {
            var range = view.Ranges.FirstOrDefault(r => r.Code == code);
            return new RangoReferenciaFila { Constante = code, Minimo = range?.Min, Maximo = range?.Max };
        }).ToList(),
    };
}

/// <summary>View es null cuando el ámbito activo no tiene acceso (no se revela ningún dato del centro).</summary>
public sealed record RangosReferenciaViewModel(ReferenceRangesView? View, RangosReferenciaFormModel Form);
