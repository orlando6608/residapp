using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;
using ResidApp.Shared;
using ResidApp.Web.Models;

namespace ResidApp.FunctionalTests;

/// <summary>ADM-02: buscar y filtrar la lista de residentes de Administración, y la edad calculada (RES-03).</summary>
public class AdministrativeResidentListTests
{
    private static readonly UnitId PlantaUno = UnitId.From(Guid.NewGuid());
    private static readonly UnitId PlantaDos = UnitId.From(Guid.NewGuid());
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static AdministrativeResidentSummary Resident(string name, UnitId unit, string unitName) => new(
        ResidentId.From(Guid.NewGuid()), name, new DateOnly(1940, 1, 1), DocumentedSexCode.NoConsta, unit, unitName, DateTimeOffset.UtcNow);

    private static readonly IReadOnlyList<AdministrativeResidentSummary> All =
    [
        Resident("José Álvarez", PlantaUno, "Planta 1"),
        Resident("Josefa Núñez", PlantaDos, "Planta 2"),
        Resident("María Pérez", PlantaUno, "Planta 1"),
    ];

    [Fact]
    public void SinFiltro_MuestraTodos_YOfreceLasUnidades()
    {
        var model = AdministrativeResidentListViewModel.From(All, new AdministrativeResidentFilter(), Today);

        Assert.Equal(3, model.Residents.Count);
        Assert.Equal(["Planta 1", "Planta 2"], model.Units.Select(u => u.Name));
    }

    [Fact]
    public void Nombre_SinMayusculasNiAcentos_YUnidad()
    {
        var byName = AdministrativeResidentListViewModel.From(All, new AdministrativeResidentFilter { Q = "JOSE" }, Today);
        var byUnit = AdministrativeResidentListViewModel.From(All, new AdministrativeResidentFilter { Q = "jose", Unidad = PlantaDos.Value }, Today);

        Assert.Equal(["José Álvarez", "Josefa Núñez"], byName.Residents.Select(r => r.DisplayName));
        Assert.Equal("Josefa Núñez", Assert.Single(byUnit.Residents).DisplayName);
        Assert.Equal(3, byUnit.TotalCount);
    }

    [Theory]
    [InlineData("1940-10-01", 86)]
    [InlineData("1940-10-02", 85)]
    [InlineData("1940-02-29", 86)]
    public void Edad_SeCalculaDesdeLaFechaDeNacimiento(string birthDate, int age) =>
        Assert.Equal(age, AdministrativeResidentDisplay.Age(DateOnly.Parse(birthDate), Today));
}
