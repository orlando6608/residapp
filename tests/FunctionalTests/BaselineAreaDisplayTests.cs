using System.ComponentModel.DataAnnotations;
using System.Reflection;
using ResidApp.Domain.Baseline;
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
    public void Respuestas_TodosLosCamposTienenNombreEnEspanol()
    {
        var answers = typeof(IBaselineAreaAnswer).Assembly.GetTypes()
            .Where(t => t.IsClass && typeof(IBaselineAreaAnswer).IsAssignableFrom(t))
            .ToList();
        Assert.Equal(9, answers.Count);

        var missing = answers
            .SelectMany(t => t.GetProperties())
            .Where(p => string.IsNullOrWhiteSpace(p.GetCustomAttribute<DisplayAttribute>()?.Name))
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}")
            .ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Barthel_CadaItemTieneNombreEnEspanol_YLaOpcionSuEtiqueta()
    {
        Assert.Equal(
            ["Comer", "Lavarse", "Vestirse", "Arreglarse", "Deposición", "Micción", "Uso del retrete", "Traslado cama-sillón",
                "Deambulación", "Escaleras"],
            Enum.GetValues<BarthelItemCode>().Select(item => EnumDisplay.Label(item)));
        Assert.Equal("Supervisión o mínima ayuda",
            BaselineVersionDisplay.BarthelOption(new BarthelItem(BarthelItemCode.TrasladoCamaSillon, "SUPERVISION_O_MINIMA_AYUDA", 10)));
    }

    [Fact]
    public void Resumen_DiceElCampoDeCadaValor_YDespliegaLasListas()
    {
        var communication = BaselineAreaDisplay.Summarize(new CommunicationAreaAnswer(
            ComprehensionCode.NecesitaFrasesSencillasRepeticionOApoyo, ExpressionCode.ComunicacionPrincipalmenteNoVerbal,
            [CommunicationFormCode.Gestos, CommunicationFormCode.Otra], "Pictogramas"));
        var aids = BaselineAreaDisplay.Summarize(new UsualAidsAreaAnswer(
            [UsualAidCode.Gafas, UsualAidCode.Audifono], null, null));
        var personalCare = BaselineAreaDisplay.Summarize(new PersonalCareAreaAnswer(PersonalCareCode.NoDocumentado, BathingCode.NoDocumentado));

        Assert.Equal(
            ["Comprensión: Necesita frases sencillas, repetición o apoyo", "Expresión: Comunicación principalmente no verbal",
                "Formas habituales de comunicación: Gestos, Otra", "Otra forma de comunicación: Pictogramas"],
            communication);
        Assert.Equal(["Ayudas habituales: Gafas, Audífono"], aids);
        Assert.Equal(["Aseo personal: No documentado", "Baño o ducha: No documentado"], personalCare);
    }
}
