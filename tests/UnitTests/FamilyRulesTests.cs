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

public class ResidentFamilyAtAdmissionTests
{
    private static NewResidentFamilyInput Row(
        string? name = "Ana Ruiz", string? relationship = "Hija", string? phone = "600123456", bool referent = false, bool guardian = false,
        bool priority = false) => new(name, relationship, phone, null, referent, guardian, priority);

    private static readonly NewResidentFamilyInput Blank = new(null, " ", null, "", false, false, false);

    [Fact]
    public void Validate_IgnoraLasFilasEnBlanco_YConservaLasMarcas()
    {
        var result = ResidentFamilyAtAdmission.Validate([Row(referent: true, priority: true), Blank, Row("Luis Gil", "Hijo", "600765432", guardian: true), Blank]);

        Assert.Equal(2, result.Count);
        Assert.True(result[0].Data.IsReferent && result[0].IsPriorityContact && !result[0].Data.IsLegalGuardian);
        Assert.True(result[1].Data.IsLegalGuardian && !result[1].IsPriorityContact);
        Assert.Throws<DomainValidationException>(() => ResidentFamilyAtAdmission.Validate([Blank, Blank]));
        Assert.Empty(ResidentFamilyAtAdmission.Validate(null));
    }

    [Fact]
    public void Validate_UnaFilaAMediasUnTelefonoMalo_SinPrioritarioOMasDeCinco_SonInvalidos()
    {
        var invalid = new[]
        {
            new[] { Row(phone: null, priority: true) },
            new[] { Row(relationship: " ", priority: true) },
            new[] { Row(phone: "abc", priority: true) },
            new[] { new NewResidentFamilyInput(null, null, null, null, true, false, false) },
            new[] { Row(referent: true) },
            new NewResidentFamilyInput[] { Blank },
            Array.Empty<NewResidentFamilyInput>(),
            Enumerable.Range(0, 6).Select(i => Row($"Familiar {i}", priority: true)).ToArray(),
        };

        foreach (var rows in invalid)
        {
            Assert.Equal(FamilyMember.InvalidCode, Assert.Throws<DomainValidationException>(() => ResidentFamilyAtAdmission.Validate(rows)).Message);
        }
    }

    [Fact]
    public void LasMarcasDeReferenteYTutorCambianLaVersionYElIgual()
    {
        var data = new FamilyMemberData("Ana Ruiz", "Hija", "600123456", null);

        Assert.NotEqual(data.Version, (data with { IsReferent = true }).Version);
        Assert.NotEqual(data.Version, (data with { IsLegalGuardian = true }).Version);
        Assert.False(data.SameAs(data with { IsReferent = true }));
        Assert.True((FamilyMember.Validate("Ana Ruiz", "Hija", "600123456", null, true, true)).IsLegalGuardian);
    }
}

public class EmergencyContactSetTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid();

    [Fact]
    public void Current_RecorreLasDesignacionesPorOrden()
    {
        Assert.Empty(EmergencyContactSet.Current([]));
        Assert.Equal([A, B], EmergencyContactSet.Current([("AGREGAR", A), ("AGREGAR", B), ("AGREGAR", A)]));
        Assert.Equal([B, C], EmergencyContactSet.Current([("AGREGAR", A), ("AGREGAR", B), ("QUITAR", A), ("AGREGAR", C)]));
        // Una designación de antes de los varios contactos sustituía al anterior, y sin vínculo los quitaba todos.
        Assert.Equal([C], EmergencyContactSet.Current([("REEMPLAZAR", A), ("REEMPLAZAR", B), ("REEMPLAZAR", C)]));
        Assert.Empty(EmergencyContactSet.Current([("REEMPLAZAR", A), ("REEMPLAZAR", null)]));
        Assert.Equal([B, C], EmergencyContactSet.Current([("REEMPLAZAR", A), ("REEMPLAZAR", B), ("AGREGAR", C)]));
    }
}
