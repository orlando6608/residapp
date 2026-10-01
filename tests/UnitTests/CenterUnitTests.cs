using ResidApp.Domain.Structure;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>ADM-05 (0025): datos de una unidad nueva del centro.</summary>
public class CenterUnitTests
{
    [Theory]
    [InlineData("pl-1", "PL-1")]
    [InlineData(" Planta_Norte ", "PLANTA_NORTE")]
    [InlineData("UD", "UD")]
    public void Codigo_SeRecortaYSeGuardaEnMayusculas(string code, string expected) =>
        Assert.Equal(new CenterUnitData(expected, "Planta Norte"), CenterUnit.Validate(code, "  Planta Norte "));

    [Theory]
    [InlineData(null, "Planta")]
    [InlineData("a", "Planta")]
    [InlineData("pl 1", "Planta")]
    [InlineData("planta-ñ", "Planta")]
    [InlineData("pl-1", " ")]
    [InlineData("pl-1", null)]
    public void DatosInvalidos_SeRechazan(string? code, string? name) =>
        Assert.Equal(CenterUnit.InvalidCode, Assert.Throws<DomainValidationException>(() => CenterUnit.Validate(code, name)).Message);

    [Fact]
    public void Codigo_YNombre_TienenLimiteDeLongitud()
    {
        Assert.Equal(CenterUnit.MaxCodeLength, CenterUnit.Validate(new string('a', CenterUnit.MaxCodeLength), "x").Code.Length);
        Assert.Throws<DomainValidationException>(() => CenterUnit.Validate(new string('a', CenterUnit.MaxCodeLength + 1), "x"));
        Assert.Equal(CenterUnit.MaxNameLength, CenterUnit.ValidateName(new string('n', CenterUnit.MaxNameLength)).Length);
        Assert.Throws<DomainValidationException>(() => CenterUnit.ValidateName(new string('n', CenterUnit.MaxNameLength + 1)));
    }
}
