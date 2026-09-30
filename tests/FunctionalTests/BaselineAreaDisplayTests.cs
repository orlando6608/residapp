using System.ComponentModel.DataAnnotations;
using System.Reflection;
using ResidApp.Domain.Baseline.Answers;
using ResidApp.Domain.Baseline.Catalogs;
using ResidApp.Web.Models;

namespace ResidApp.FunctionalTests;

/// <summary>Resumen de las áreas del basal (AUX-03, confirmación del basal, informe de derivación) y etiquetas
/// en español de sus catálogos, que también usan los &lt;select&gt; del formulario del basal.</summary>
public class BaselineAreaDisplayTests
{
    [Fact]
    public void Catalogos_TodosLosValoresTienenEtiquetaEnEspanol()
    {
        var catalogs = typeof(ComprehensionCode).Assembly.GetTypes()
            .Where(t => t.IsEnum && t.Namespace == typeof(ComprehensionCode).Namespace)
            .ToList();
        Assert.Equal(23, catalogs.Count);

        var missing = catalogs
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => string.IsNullOrWhiteSpace(f.GetCustomAttribute<DisplayAttribute>()?.Name))
            .Select(f => $"{f.DeclaringType!.Name}.{f.Name}")
            .ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Resumen_DespliegaLasListas_ConSusEtiquetas()
    {
        var communication = BaselineAreaDisplay.Summarize(new CommunicationAreaAnswer(
            ComprehensionCode.NecesitaFrasesSencillasRepeticionOApoyo, ExpressionCode.ComunicacionPrincipalmenteNoVerbal,
            [CommunicationFormCode.Gestos, CommunicationFormCode.Otra], "Pictogramas"));
        var aids = BaselineAreaDisplay.Summarize(new UsualAidsAreaAnswer(
            [UsualAidCode.Gafas, UsualAidCode.Audifono], null, null));

        Assert.Equal(
            ["Necesita frases sencillas, repetición o apoyo", "Comunicación principalmente no verbal", "Gestos", "Otra", "Pictogramas"],
            communication);
        Assert.Equal(["Gafas", "Audífono"], aids);
    }
}
