using ResidApp.Application.Ports;
using ResidApp.Shared;
using ResidApp.Web.Models;

namespace ResidApp.FunctionalTests;

/// <summary>ENF-17/MED-19: buscar por nombre (sin mayúsculas ni acentos) y filtrar por estado basal y unidad la lista de
/// residentes del ámbito.</summary>
public class ResidentListFilterTests
{
    private static readonly UnitId UnidadA = UnitId.From(Guid.NewGuid());
    private static readonly UnitId UnidadB = UnitId.From(Guid.NewGuid());

    private static readonly IReadOnlyList<ScopeResidentSummary> Residents =
    [
        new(ResidentId.From(Guid.NewGuid()), "José Álvarez", UnidadA, "Planta 1", true),
        new(ResidentId.From(Guid.NewGuid()), "Josefa Núñez", UnidadB, "Planta 2", false),
        new(ResidentId.From(Guid.NewGuid()), "María Pérez", UnidadA, "Planta 1", false),
    ];

    private static IEnumerable<string> Names(ResidentListFilter filter) =>
        ResidentListViewModel.From(Residents, filter).Residents.Select(r => r.DisplayName);

    [Fact]
    public void SinFiltro_MuestraTodos_YLasUnidadesConResidentes()
    {
        var model = ResidentListViewModel.From(Residents, new ResidentListFilter());

        Assert.Equal(3, model.Residents.Count);
        Assert.Equal(3, model.TotalCount);
        Assert.Equal(["Planta 1", "Planta 2"], model.Units.Select(u => u.Name));
    }

    [Fact]
    public void Nombre_SinDistinguirMayusculasNiAcentos()
    {
        Assert.Equal(["José Álvarez", "Josefa Núñez"], Names(new ResidentListFilter { Q = "  jose " }));
        Assert.Equal(["Josefa Núñez"], Names(new ResidentListFilter { Q = "NUNEZ" }));
        Assert.Empty(Names(new ResidentListFilter { Q = "Gómez" }));
    }

    [Fact]
    public void EstadoBasalYUnidad_SeCombinanConElNombre()
    {
        Assert.Equal(["José Álvarez"], Names(new ResidentListFilter { Basal = ResidentBaselineFilter.Vigente }));
        Assert.Equal(["Josefa Núñez", "María Pérez"], Names(new ResidentListFilter { Basal = ResidentBaselineFilter.Pendiente }));
        Assert.Equal(["José Álvarez", "María Pérez"], Names(new ResidentListFilter { Unidad = UnidadA.Value }));
        Assert.Equal(["María Pérez"],
            Names(new ResidentListFilter { Q = "maria", Basal = ResidentBaselineFilter.Pendiente, Unidad = UnidadA.Value }));
    }
}
