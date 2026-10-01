using ResidApp.Domain.Audit;
using ResidApp.Web.Models;

namespace ResidApp.FunctionalTests;

/// <summary>ADM-28: toda acción de la lista tiene su etiqueta en español, y toda categoría también.</summary>
public class AuditActionDisplayTests
{
    [Fact]
    public void TodaAccionAdministrativa_TieneEtiquetaPropia()
    {
        foreach (var (code, _) in AdministrativeAudit.Actions)
        {
            var label = AuditActionDisplay.Label(code);
            Assert.NotEqual("Acción administrativa", label);
            Assert.DoesNotContain("_", label);
        }

        Assert.Equal(AdministrativeAudit.Actions.Select(a => AuditActionDisplay.Label(a.Code)).Distinct().Count(), AdministrativeAudit.Actions.Count);
    }

    [Fact]
    public void TodaCategoria_TieneNombreEnEspañol_YElDesplegableLasAgrupaTodas()
    {
        Assert.All(Enum.GetValues<AuditCategory>(), category => Assert.DoesNotMatch("[a-z][A-Z]", AuditActionDisplay.CategoryLabel(category)));
        Assert.Equal(Enum.GetValues<AuditCategory>().Length, Enum.GetValues<AuditCategory>().Select(AuditActionDisplay.CategoryLabel).Distinct().Count());
        Assert.Equal(Enum.GetValues<AuditCategory>().Length, AuditActionDisplay.Groups().Count());
        Assert.Equal(AdministrativeAudit.Actions.Count, AuditActionDisplay.Groups().Sum(g => g.Actions.Count()));
    }
}
