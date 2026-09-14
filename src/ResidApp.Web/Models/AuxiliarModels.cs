using ResidApp.Application.Ports;
using ResidApp.Domain.Baseline;
using ResidApp.Domain.Baseline.Answers;

namespace ResidApp.Web.Models;

/// <summary>AUX-02/AUX-03: identidad mínima del residente asignado más el resumen del basal vigente
/// (null si todavía no tiene ninguno firmado).</summary>
public sealed record AuxiliarResidentDetailViewModel(AssignedResidentSummary Resident, CurrentBaselineSummary? Baseline);

public static class BaselineAreaDisplay
{
    public static string Label(BaselineArea area) => area switch
    {
        BaselineArea.Movilidad => "Movilidad",
        BaselineArea.Alimentacion => "Alimentación",
        BaselineArea.Continencia => "Continencia",
        BaselineArea.AseoHigiene => "Aseo e higiene",
        BaselineArea.Cognicion => "Cognición",
        BaselineArea.Comunicacion => "Comunicación",
        BaselineArea.Conducta => "Conducta",
        BaselineArea.Sueno => "Sueño",
        BaselineArea.AyudasHabituales => "Ayudas habituales",
        _ => area.ToString(),
    };

    /// <summary>Resumen genérico de una respuesta de área: solo valores (ya en español, como el resto de
    /// catálogos de dominio), nunca los nombres de propiedad en inglés del record C#. Suficiente para
    /// AUX-03 ("necesarios para el cuidado cotidiano"), sin un formateador propio por área.</summary>
    public static IReadOnlyList<string> Summarize(IBaselineAreaAnswer answer) =>
        answer.GetType().GetProperties()
            .Select(property => property.GetValue(answer))
            .Where(value => value is not null && value is not string { Length: 0 })
            .Select(value => value!.ToString()!)
            .ToList();
}
