using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;
using ResidApp.Web.Models;

namespace ResidApp.FunctionalTests;

/// <summary>ENF-02: filtrar por nombre (sin mayúsculas ni acentos), unidad y estado la bandeja de cambios ordinarios.</summary>
public class PendingChangeFilterTests
{
    private static readonly UnitId UnidadA = UnitId.From(Guid.NewGuid());
    private static readonly UnitId UnidadB = UnitId.From(Guid.NewGuid());

    private static PendingChangeSummary Change(string resident, UnitId unit, string unitName, ClinicalEventStatus status) => new(
        Guid.NewGuid(), ClinicalEventOrigin.CambioAuxiliar, ResidentId.From(Guid.NewGuid()), resident, unit, unitName,
        [], null, SystemProfile.Auxiliar, DateTimeOffset.UtcNow, null, null, status);

    private static readonly IReadOnlyList<PendingChangeSummary> Changes =
    [
        Change("José Álvarez", UnidadA, "Planta 1", ClinicalEventStatus.Pendiente),
        Change("Josefa Núñez", UnidadB, "Planta 2", ClinicalEventStatus.EnValoracion),
        Change("María Pérez", UnidadA, "Planta 1", ClinicalEventStatus.Pendiente),
    ];

    private static IEnumerable<string> Names(PendingChangeFilter filter) =>
        PendingChangeListViewModel.From(Changes, filter).Items.Select(c => c.ResidentDisplayName);

    [Fact]
    public void SinFiltro_MuestraTodos_ConSusUnidadesYEstados()
    {
        var model = PendingChangeListViewModel.From(Changes, new PendingChangeFilter());

        Assert.Equal(3, model.Items.Count);
        Assert.Equal(3, model.TotalCount);
        Assert.Equal(["Planta 1", "Planta 2"], model.Units.Select(u => u.Name));
        Assert.Equal([ClinicalEventStatus.EnValoracion, ClinicalEventStatus.Pendiente], model.Statuses);
    }

    [Fact]
    public void Nombre_SinDistinguirMayusculasNiAcentos()
    {
        Assert.Equal(["José Álvarez", "Josefa Núñez"], Names(new PendingChangeFilter { Q = "  jose " }));
        Assert.Equal(["Josefa Núñez"], Names(new PendingChangeFilter { Q = "NUNEZ" }));
        Assert.Empty(Names(new PendingChangeFilter { Q = "Gómez" }));
    }

    [Fact]
    public void UnidadYEstado_SeCombinanConElNombre()
    {
        Assert.Equal(["José Álvarez", "María Pérez"], Names(new PendingChangeFilter { Unidad = UnidadA.Value }));
        Assert.Equal(["Josefa Núñez"], Names(new PendingChangeFilter { Estado = ClinicalEventStatus.EnValoracion }));
        Assert.Equal(["María Pérez"],
            Names(new PendingChangeFilter { Q = "maria", Unidad = UnidadA.Value, Estado = ClinicalEventStatus.Pendiente }));
    }
}
