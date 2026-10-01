using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>ADM-03 (0021): validación de una corrección de identidad del residente.</summary>
public class ResidentIdentityCorrectionTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    [Fact]
    public void Valida_RecortaElNombreYElMotivo()
    {
        var (identity, reason) = ResidentIdentityCorrection.Validate(
            "  María García  ", new DateOnly(1940, 5, 21), DocumentedSexCode.Mujer, " Error al transcribir. ", Today);

        Assert.Equal(new ResidentIdentity("María García", new DateOnly(1940, 5, 21), DocumentedSexCode.Mujer), identity);
        Assert.Equal("Error al transcribir.", reason);
    }

    [Theory]
    [InlineData(" ", "1940-05-21", 0, "Motivo")]
    [InlineData("María", "2026-10-02", 0, "Motivo")]
    [InlineData("María", "1940-05-21", 99, "Motivo")]
    [InlineData("María", "1940-05-21", 0, " ")]
    public void Rechaza_NombreVacio_FechaFutura_SexoFueraDelCatalogo_YMotivoVacio(string name, string birthDate, int sex, string reason)
    {
        var error = Assert.Throws<DomainValidationException>(() => ResidentIdentityCorrection.Validate(
            name, DateOnly.Parse(birthDate), (DocumentedSexCode)sex, reason, Today));

        Assert.Equal(ResidentIdentityCorrection.InvalidCode, error.Message);
    }

    [Fact]
    public void MismaIdentidad_DistingueMayusculasYAcentos()
    {
        var identity = new ResidentIdentity("maria", new DateOnly(1940, 5, 21), DocumentedSexCode.Mujer);

        Assert.True(identity.SameAs(identity with { }));
        Assert.False(identity.SameAs(identity with { DisplayName = "María" }));
        Assert.False(identity.SameAs(identity with { DocumentedSex = DocumentedSexCode.NoConsta }));
    }
}
