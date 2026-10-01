using ResidApp.Domain.Platform;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>Perfil de plataforma (0026): datos de alta de un centro.</summary>
public class NewCenterTests
{
    [Fact]
    public void DatosValidos_SeRecortanYLosCodigosSeGuardanEnMayusculas()
    {
        var data = NewCenter.Validate(" cen-1 ", "  Residencia Norte ", "ud_1", "Planta 1", " admin.norte ", " Ana Ruiz ");

        Assert.Equal("CEN-1", data.CenterCode);
        Assert.Equal("Residencia Norte", data.CenterName);
        Assert.Equal("UD_1", data.Unit.Code);
        Assert.Equal("admin.norte", data.Administrator.Subject);
        Assert.Equal("Ana Ruiz", data.Administrator.DisplayName);
    }

    [Theory]
    [InlineData("plataforma", "Centro", "u1", "Unidad", "admin-1", "Ana")]
    [InlineData(" PlataForma ", "Centro", "u1", "Unidad", "admin-1", "Ana")]
    [InlineData("c", "Centro", "u1", "Unidad", "admin-1", "Ana")]
    [InlineData("cen 1", "Centro", "u1", "Unidad", "admin-1", "Ana")]
    [InlineData("cen-1", null, "u1", "Unidad", "admin-1", "Ana")]
    [InlineData("cen-1", "Centro", "u 1", "Unidad", "admin-1", "Ana")]
    [InlineData("cen-1", "Centro", "u1", " ", "admin-1", "Ana")]
    [InlineData("cen-1", "Centro", "u1", "Unidad", "ad", "Ana")]
    [InlineData("cen-1", "Centro", "u1", "Unidad", "admin 1", "Ana")]
    [InlineData("cen-1", "Centro", "u1", "Unidad", "admin-1", null)]
    public void DatosInvalidos_SeRechazan_ConUnSoloCodigo(
        string? centerCode, string? centerName, string? unitCode, string? unitName, string? subject, string? adminName) =>
        Assert.Equal(NewCenter.InvalidCode, Assert.Throws<DomainValidationException>(
            () => NewCenter.Validate(centerCode, centerName, unitCode, unitName, subject, adminName)).Message);

    [Fact]
    public void ElCentroReservado_TieneUnIdFijo() =>
        Assert.Equal(new Guid("5F3A1C00-0000-4000-8000-000000000001"), PlatformCenter.Id);
}
