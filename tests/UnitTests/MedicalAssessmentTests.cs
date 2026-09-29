using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Medicina;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

public class MedicalAssessmentTests
{
    private static readonly VitalSigns NoVitals = new(null, null, null, null, null, null, null, null, null, null, null, null);

    [Fact]
    public void Valoracion_ConUnTexto_SeAceptaRecortada() =>
        Assert.Equal("Crepitantes.", new MedicalAssessmentContent("  Crepitantes. ", null, null, NoVitals).FindingsAndExamination);

    [Fact]
    public void Valoracion_Vacia_SeRechaza()
    {
        var ex = Assert.Throws<DomainValidationException>(() => new MedicalAssessmentContent(" ", null, null, NoVitals));
        Assert.Equal("MEDICAL_ASSESSMENT_EMPTY", ex.Message);
    }

    [Fact]
    public void Valoracion_TextoDemasiadoLargo_SeRechaza()
    {
        var ex = Assert.Throws<DomainValidationException>(() => new MedicalAssessmentContent(
            null, new string('a', MedicalAssessmentContent.MaxTextLength + 1), null, NoVitals));
        Assert.Equal("MEDICAL_ASSESSMENT_TEXT_TOO_LONG", ex.Message);
    }

    [Fact]
    public void Indicacion_ConTextoYPlan_SeAcepta()
    {
        var indication = new MedicalIndication(" Control de SpO2. ", new FollowUpPlan(null, "Cada 4 horas."), "  ");

        Assert.Equal("Control de SpO2.", indication.Text);
        Assert.Null(indication.AdditionalInformation);
    }

    [Fact]
    public void Seguimiento_ConObjetivoYPlan_SeAceptaRecortado()
    {
        var followUp = new MedicalFollowUp(new FollowUpPlan(null, "Tras la analítica."), "  Decidir antibiótico. ");

        Assert.Equal("Decidir antibiótico.", followUp.Objective);
        Assert.Equal("Tras la analítica.", followUp.Plan.Criterion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData(MedicalFollowUp.MaxObjectiveLength + 1)]
    public void Seguimiento_SinObjetivoODemasiadoLargo_SeRechaza(object? objective)
    {
        var text = objective is int length ? new string('a', length) : (string?)objective;
        var ex = Assert.Throws<DomainValidationException>(() => new MedicalFollowUp(new FollowUpPlan(null, "Tras la analítica."), text));
        Assert.Equal("MEDICAL_FOLLOW_UP_OBJECTIVE_REQUIRED", ex.Message);
    }

    [Fact]
    public void Indicacion_SinTexto_SeRechaza()
    {
        var ex = Assert.Throws<DomainValidationException>(() => new MedicalIndication(" ", new FollowUpPlan(null, "Cada 4 horas."), null));
        Assert.Equal("MEDICAL_INDICATION_INVALID", ex.Message);
    }

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(false, "Rechaza la medicación.", true)]
    [InlineData(false, " ", false)]
    [InlineData(true, "No aplica.", false)]
    public void Resultado_NoRealizadaExigeIncidencia_YRealizadaNoLaAdmite(bool done, string? incident, bool valid)
    {
        if (valid)
        {
            Assert.Equal(done, new MedicalIndicationOutcome(done, incident).Done);
        }
        else
        {
            var ex = Assert.Throws<DomainValidationException>(() => new MedicalIndicationOutcome(done, incident));
            Assert.Equal("MEDICAL_INDICATION_INCIDENT_INVALID", ex.Message);
        }
    }
}
