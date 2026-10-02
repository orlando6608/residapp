using ResidApp.Domain.Families;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>ADM-08 a ADM-11 (0022): datos del familiar y transiciones de su autorización.</summary>
public class FamilyRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    [Theory]
    [InlineData("600123456")]
    [InlineData("+34 600 12 34 56")]
    [InlineData("(91) 555-12.34")]
    public void Telefono_AdmiteFormatosHabituales(string phone) =>
        Assert.Equal(phone, FamilyMember.Validate("Ana", "Hija", $"  {phone} ", null).Phone);

    [Theory]
    [InlineData(null, "Hija", "600123456", null)]
    [InlineData("Ana", " ", "600123456", null)]
    [InlineData("Ana", "Hija", "12345", null)]
    [InlineData("Ana", "Hija", "1234567890123456", null)]
    [InlineData("Ana", "Hija", "600 12 34 56 ext", null)]
    [InlineData("Ana", "Hija", "600123456", "ana.example.org")]
    [InlineData("Ana", "Hija", "600123456", "ana@@example.org")]
    [InlineData("Ana", "Hija", "600123456", "ana @example.org")]
    [InlineData("Ana", "Hija", "600123456", "@example.org")]
    public void DatosInvalidos_SeRechazan(string? name, string? relationship, string? phone, string? email) =>
        Assert.Equal(FamilyMember.InvalidCode, Assert.Throws<DomainValidationException>(
            () => FamilyMember.Validate(name, relationship, phone, email)).Message);

    [Fact]
    public void CorreoVacio_QuedaSinCorreo_YLosTextosSeRecortan() =>
        Assert.Equal(new FamilyMemberData("Ana Ruiz", "Hija", "600123456", null),
            FamilyMember.Validate(" Ana Ruiz ", " Hija ", "600123456", "   "));

    [Theory]
    [InlineData(null, new[] { FamilyAuthorizationChange.Abrir })]
    [InlineData(FamilyAuthorizationStatus.Pendiente, new[] { FamilyAuthorizationChange.Activar, FamilyAuthorizationChange.Revocar })]
    [InlineData(FamilyAuthorizationStatus.Activa, new[] { FamilyAuthorizationChange.Suspender, FamilyAuthorizationChange.Revocar })]
    [InlineData(FamilyAuthorizationStatus.Suspendida, new[] { FamilyAuthorizationChange.Activar, FamilyAuthorizationChange.Revocar })]
    [InlineData(FamilyAuthorizationStatus.Caducada, new[] { FamilyAuthorizationChange.Activar, FamilyAuthorizationChange.Revocar })]
    [InlineData(FamilyAuthorizationStatus.Revocada, new FamilyAuthorizationChange[0])]
    public void CambiosPosibles_SegunElEstadoEfectivo(FamilyAuthorizationStatus? effective, FamilyAuthorizationChange[] expected) =>
        Assert.Equal(expected, FamilyAuthorizationRules.Allowed(effective));

    [Fact]
    public void Caducada_EsUnaActivaConLaFechaPasada_ElUltimoDiaIncluido()
    {
        Assert.Equal(FamilyAuthorizationStatus.Activa, FamilyAuthorizationRules.Effective(FamilyAuthorizationStatus.Activa, Today, Today));
        Assert.Equal(FamilyAuthorizationStatus.Activa, FamilyAuthorizationRules.Effective(FamilyAuthorizationStatus.Activa, null, Today));
        Assert.Equal(FamilyAuthorizationStatus.Caducada,
            FamilyAuthorizationRules.Effective(FamilyAuthorizationStatus.Activa, Today.AddDays(-1), Today));
        Assert.Equal(FamilyAuthorizationStatus.Suspendida,
            FamilyAuthorizationRules.Effective(FamilyAuthorizationStatus.Suspendida, Today.AddDays(-1), Today));
    }

    [Fact]
    public void Validar_DevuelveLoQueSeGuarda()
    {
        Assert.Equal((FamilyAuthorizationStatus.Pendiente, (DateOnly?)null, (string?)null),
            FamilyAuthorizationRules.Validate(null, FamilyAuthorizationChange.Abrir, null, null, Today));
        Assert.Equal((FamilyAuthorizationStatus.Activa, (DateOnly?)Today, (string?)null),
            FamilyAuthorizationRules.Validate(FamilyAuthorizationStatus.Pendiente, FamilyAuthorizationChange.Activar, Today, null, Today));
        Assert.Equal((FamilyAuthorizationStatus.Revocada, (DateOnly?)null, "Motivo"),
            FamilyAuthorizationRules.Validate(FamilyAuthorizationStatus.Caducada, FamilyAuthorizationChange.Revocar, null, " Motivo ", Today));
    }

    [Theory]
    [InlineData(FamilyAuthorizationStatus.Pendiente, FamilyAuthorizationChange.Activar, -1, null)]
    [InlineData(FamilyAuthorizationStatus.Pendiente, FamilyAuthorizationChange.Activar, null, "No hace falta motivo")]
    [InlineData(FamilyAuthorizationStatus.Activa, FamilyAuthorizationChange.Suspender, null, "  ")]
    [InlineData(FamilyAuthorizationStatus.Activa, FamilyAuthorizationChange.Suspender, 10, "Motivo")]
    [InlineData(FamilyAuthorizationStatus.Activa, FamilyAuthorizationChange.Activar, null, null)]
    [InlineData(FamilyAuthorizationStatus.Caducada, FamilyAuthorizationChange.Suspender, null, "Motivo")]
    [InlineData(FamilyAuthorizationStatus.Revocada, FamilyAuthorizationChange.Activar, null, null)]
    public void Validar_RechazaCambiosQueNoTocan(
        FamilyAuthorizationStatus effective, FamilyAuthorizationChange change, int? validUntilOffset, string? reason) =>
        Assert.Throws<DomainValidationException>(() => FamilyAuthorizationRules.Validate(
            effective, change, validUntilOffset is { } days ? Today.AddDays(days) : null, reason, Today));

    [Theory]
    [InlineData("Hija", "Hija")]
    [InlineData("  Sobrina  ", "Sobrina")]
    public void ValidarRelacion_RecortaLosEspacios(string value, string expected) =>
        Assert.Equal(expected, FamilyMember.ValidateRelationship(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidarRelacion_RechazaLaVacia(string? value) =>
        Assert.Throws<DomainValidationException>(() => FamilyMember.ValidateRelationship(value));

    [Fact]
    public void ValidarRelacion_RechazaLaQuePasaDelMaximo() =>
        Assert.Throws<DomainValidationException>(() => FamilyMember.ValidateRelationship(new string('a', FamilyMember.MaxRelationshipLength + 1)));

    [Fact]
    public void Version_EsEstableYCambiaConCualquierDato()
    {
        var data = new FamilyMemberData("Lucía Pérez", "Hija", "600 123 456", "lucia@example.org");

        Assert.Equal(data.Version, new FamilyMemberData("Lucía Pérez", "Hija", "600 123 456", "lucia@example.org").Version);
        Assert.NotEqual(data.Version, (data with { DisplayName = "Lucia Pérez" }).Version);
        Assert.NotEqual(data.Version, (data with { Relationship = "hija" }).Version);
        Assert.NotEqual(data.Version, (data with { Phone = "600 123 457" }).Version);
        Assert.NotEqual(data.Version, (data with { Email = null }).Version);
        // Los campos no se confunden al unirlos.
        Assert.NotEqual(
            new FamilyMemberData("ab", "c", "600 123 456", null).Version, new FamilyMemberData("a", "bc", "600 123 456", null).Version);
    }
}
