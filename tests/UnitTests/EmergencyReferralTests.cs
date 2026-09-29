using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

public class EmergencyReferralTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<ReferralReportSection> Automatic =
        [new("Identificación del residente y del centro", true, ["Nombre: Residente de prueba"])];

    [Fact]
    public void Informe_ConMotivo_SeCompone_ConLasSeccionesDelProfesionalAlFinal()
    {
        var content = ReferralReportContent.Compose(Automatic, new ReferralReportInput(" Disnea brusca. ", " Portador de marcapasos. "));

        Assert.Equal(
            new[] { "Identificación del residente y del centro", ReferralReportContent.ReasonTitle, ReferralReportContent.AdditionalInformationTitle },
            content.Sections.Select(s => s.Title));
        Assert.Equal(new[] { true, false, false }, content.Sections.Select(s => s.Automatic));
        Assert.Equal("Disnea brusca.", content.Sections[1].Lines.Single());
        Assert.Equal(2, ReferralReportContent.Compose(Automatic, new ReferralReportInput("Disnea.", "  ")).Sections.Count);
    }

    [Fact]
    public void Informe_SinMotivo_ODemasiadoLargo_SeRechaza()
    {
        foreach (var build in new Func<ReferralReportInput>[]
        {
            () => new ReferralReportInput("  ", null),
            () => new ReferralReportInput(new string('a', ReferralReportInput.MaxReasonLength + 1), null),
            () => new ReferralReportInput("Disnea.", new string('a', ReferralReportInput.MaxAdditionalInformationLength + 1)),
        })
        {
            Assert.Equal("REFERRAL_REPORT_INVALID", Assert.Throws<DomainValidationException>(build).Message);
        }
    }

    [Fact]
    public void Informe_SinDatosAutomaticos_OConUnaSeccionNoAutomatica_SeRechaza()
    {
        var input = new ReferralReportInput("Disnea.", null);
        Assert.Throws<DomainValidationException>(() => ReferralReportContent.Compose([], input));
        Assert.Throws<DomainValidationException>(() => ReferralReportContent.Compose([new("Motivo", false, ["x"])], input));
    }

    [Fact]
    public void Huella_EsEstableConElMismoContenido_YCambiaSiCambiaCualquierDato()
    {
        var hash = ReferralReportContent.Compose(Automatic, new ReferralReportInput("Disnea.", null)).Hash();

        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.Equal(hash, ReferralReportContent.Compose(Automatic, new ReferralReportInput(" Disnea. ", " ")).Hash());
        Assert.NotEqual(hash, ReferralReportContent.Compose(Automatic, new ReferralReportInput("Disnea súbita.", null)).Hash());
        Assert.NotEqual(hash, ReferralReportContent.Compose(
            [new("Identificación del residente y del centro", true, ["Nombre: Otro residente"])], new ReferralReportInput("Disnea.", null)).Hash());
    }

    [Fact]
    public void Llamada_ConSusDatos_SeAcepta_YAdmiteDesfaseDeRelojes()
    {
        var attempt = FamilyCallAttempt.Create(" Su hija ", Now.AddMinutes(-3), FamilyCallResult.NoContesta, "  ", Now);

        Assert.Equal("Su hija", attempt.Contact);
        Assert.Equal(Now.AddMinutes(-3), attempt.CalledAt);
        Assert.Equal(FamilyCallResult.NoContesta, attempt.Result);
        Assert.Null(attempt.Note);
        Assert.Equal(Now.AddMinutes(4), FamilyCallAttempt.Create("Su hija", Now.AddMinutes(4), FamilyCallResult.Contactado, null, Now).CalledAt);
    }

    [Fact]
    public void Llamada_Incompleta_Futura_ODemasiadoLarga_SeRechaza()
    {
        foreach (var build in new Func<FamilyCallAttempt>[]
        {
            () => FamilyCallAttempt.Create(" ", Now, FamilyCallResult.Contactado, null, Now),
            () => FamilyCallAttempt.Create("Su hija", null, FamilyCallResult.Contactado, null, Now),
            () => FamilyCallAttempt.Create("Su hija", Now, null, null, Now),
            () => FamilyCallAttempt.Create("Su hija", Now, (FamilyCallResult)99, null, Now),
            () => FamilyCallAttempt.Create("Su hija", Now.AddMinutes(10), FamilyCallResult.Contactado, null, Now),
            () => FamilyCallAttempt.Create(new string('a', FamilyCallAttempt.MaxContactLength + 1), Now, FamilyCallResult.Contactado, null, Now),
            () => FamilyCallAttempt.Create("Su hija", Now, FamilyCallResult.Contactado, new string('a', FamilyCallAttempt.MaxNoteLength + 1), Now),
        })
        {
            Assert.Equal("FAMILY_CALL_ATTEMPT_INVALID", Assert.Throws<DomainValidationException>(build).Message);
        }
    }
}
