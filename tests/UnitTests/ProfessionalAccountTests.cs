using ResidApp.Domain.Accounts;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>ADM-12/ADM-13 (0023): datos de alta de una cuenta profesional y perfiles que se pueden conceder.</summary>
public class ProfessionalAccountTests
{
    [Theory]
    [InlineData("dev-integrado-enfermeria")]
    [InlineData("ana.ruiz@centro.example")]
    [InlineData("auth0_1234")]
    [InlineData("Enfermería-Noche")]
    public void Identificador_AdmiteLetrasDigitosYSeparadores(string subject) =>
        Assert.Equal(subject, ProfessionalAccount.Validate($" {subject} ", "Ana Ruiz").Subject);

    [Theory]
    [InlineData(null, "Ana Ruiz")]
    [InlineData("ab", "Ana Ruiz")]
    [InlineData("ana ruiz", "Ana Ruiz")]
    [InlineData("ana/ruiz", "Ana Ruiz")]
    [InlineData("ana-ruiz", " ")]
    [InlineData("ana-ruiz", null)]
    public void DatosInvalidos_SeRechazan(string? subject, string? displayName) =>
        Assert.Equal(ProfessionalAccount.InvalidCode, Assert.Throws<DomainValidationException>(
            () => ProfessionalAccount.Validate(subject, displayName)).Message);

    [Fact]
    public void Identificador_YNombre_TienenLimiteDeLongitud()
    {
        Assert.Throws<DomainValidationException>(() => ProfessionalAccount.Validate(new string('a', 201), "Ana"));
        Assert.Throws<DomainValidationException>(() => ProfessionalAccount.Validate("ana-ruiz", new string('a', 201)));
        Assert.Equal(new ProfessionalAccountData(new string('a', 200), "Ana Ruiz"),
            ProfessionalAccount.Validate(new string('a', 200), " Ana Ruiz "));
    }

    [Fact]
    public void Permisos_CadaPerfilTieneLosQueUsa_YBorradorCompartidoNinguno()
    {
        Assert.Equal(["RESIDENT_IDENTITY_CREATE", "BASELINE_INITIAL_COMPLETE", "BASELINE_REEVALUATE"],
            ProfilePermissions.For(SystemProfile.Enfermeria));
        Assert.Equal(["BASELINE_INITIAL_COMPLETE", "BASELINE_REEVALUATE", "REFERENCE_RANGES_MANAGE"],
            ProfilePermissions.For(SystemProfile.Medicina));
        Assert.Equal(["CLINICAL_DETAIL_READ", "REFERENCE_RANGES_MANAGE"], ProfilePermissions.For(SystemProfile.DireccionClinica));
        Assert.All(new[] { SystemProfile.Auxiliar, SystemProfile.Administracion, SystemProfile.Familiar },
            profile => Assert.Empty(ProfilePermissions.For(profile)));
        Assert.DoesNotContain(Enum.GetValues<SystemProfile>(), p => ProfilePermissions.For(p).Contains("BASELINE_DRAFT_CONTRIBUTE"));
    }

    [Fact]
    public void Familiar_NoSeConcedeDesdeUsuarios() =>
        Assert.Equal(
            Enum.GetValues<SystemProfile>().Where(p => p != SystemProfile.Familiar),
            Enum.GetValues<SystemProfile>().Where(ProfessionalAccount.IsGrantable));
}
