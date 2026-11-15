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

    [Fact]
    public void CodigosCandidatos_SonLaBaseYLuegoSufijosDel2Al99_ConLaLongitudMaximaDeLaBase_CabenEnElCodigo()
    {
        var candidates = CenterUnit.CodeCandidates("PLANTA-1").ToList();
        var longest = CenterUnit.CodeCandidates(new string('A', 60)).Last();

        Assert.Equal("PLANTA-1", candidates[0]);
        Assert.Equal("PLANTA-1-2", candidates[1]);
        Assert.Equal("PLANTA-1-99", candidates[^1]);
        Assert.Equal(99, candidates.Count);
        Assert.True(longest.Length <= CenterUnit.MaxCodeLength);
        Assert.All(CenterUnit.CodeCandidates(new string('A', 60)), c => CenterUnit.Validate(c, "x"));
    }

    [Fact]
    public void CodigoDerivadoDelNombre_CumpleLasReglasDelCodigo()
    {
        var data = CenterUnit.Validate(ResidApp.Application.Intercambio.ExchangeText.UnitCode("Residencia Nuestra Señora de la Paz"), "x");

        Assert.Equal("RESIDENCIA-NUESTRA-SENORA-DE-LA-PAZ", data.Code);
    }
}
