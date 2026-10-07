using ResidApp.Application.Authorization;
using ResidApp.Application.Ports;
using ResidApp.Shared;
using ResidApp.Web.Models;

namespace ResidApp.FunctionalTests;

/// <summary>DIR-14: toda acción de la lista de trazabilidad tiene su etiqueta en español, distinta de las demás, y todo recurso
/// auditable por Dirección también.</summary>
public class ClinicalTraceabilityDisplayTests
{
    [Fact]
    public void TodaAccionClinica_TieneEtiquetaPropiaEnEspañol()
    {
        Assert.All(ClinicalTraceability.ActionCodes, code =>
        {
            Assert.True(ClinicalTraceabilityDisplay.HasLabel(code), code);
            Assert.DoesNotContain("_", ClinicalTraceabilityDisplay.Action(code));
        });
        Assert.Equal(ClinicalTraceability.ActionCodes.Count, ClinicalTraceability.ActionCodes.Distinct().Count());
        Assert.Equal(
            ClinicalTraceability.ActionCodes.Count, ClinicalTraceability.ActionCodes.Select(ClinicalTraceabilityDisplay.Action).Distinct().Count());
    }

    [Fact]
    public void TodoTipoDeRecursoLeidoPorDireccion_SeVeEnEspañol_YLasFinalidadesAnterioresSeAvisan()
    {
        foreach (var resource in Enum.GetValues<ClinicalResourceType>())
        {
            var label = ClinicalTraceabilityDisplay.Resource(resource.ToCode());
            Assert.NotEqual("Registro clínico", label);
            Assert.DoesNotContain("_", label);
        }
        Assert.Null(ClinicalTraceabilityDisplay.Purpose(null));
        Assert.Equal("Finalidad anterior (supervisión clínica)", ClinicalTraceabilityDisplay.Purpose("SUPERVISION_CLINICA"));
        Assert.Equal("Revisión de continuidad asistencial", ClinicalTraceabilityDisplay.Purpose("CONTINUIDAD_ASISTENCIAL"));
    }
}
