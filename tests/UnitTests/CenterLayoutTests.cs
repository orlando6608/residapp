using ResidApp.Domain.Structure;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>Historia 2 (0029): nombres de edificios, plantas, habitaciones y plazas.</summary>
public class CenterLayoutTests
{
    public static TheoryData<Func<string?, string>, string> Validators => new()
    {
        { CenterLayout.ValidateBuildingName, CenterLayout.BuildingInvalidCode },
        { CenterLayout.ValidateFloorName, CenterLayout.FloorInvalidCode },
        { CenterLayout.ValidateRoomName, CenterLayout.RoomInvalidCode },
        { CenterLayout.ValidatePlaceName, CenterLayout.PlaceInvalidCode },
    };

    [Theory]
    [MemberData(nameof(Validators))]
    public void Nombre_SeRecorta(Func<string?, string> validate, string _) =>
        Assert.Equal("Planta Norte", validate("  Planta Norte "));

    [Theory]
    [MemberData(nameof(Validators))]
    public void Nombre_VacioOMuyLargo_EsInvalidoConSuPropioCodigo(Func<string?, string> validate, string code)
    {
        foreach (var name in new[] { null, "", "   ", new string('a', CenterLayout.MaxNameLength + 1) })
        {
            Assert.Equal(code, Assert.Throws<DomainValidationException>(() => validate(name)).Message);
        }

        Assert.Equal(CenterLayout.MaxNameLength, validate(new string('a', CenterLayout.MaxNameLength)).Length);
    }
}
