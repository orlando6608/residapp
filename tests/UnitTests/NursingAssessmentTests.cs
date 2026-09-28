using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

public class NursingAssessmentTests
{
    private static VitalSigns Vitals(
        decimal? temperature = null, int? systolic = null, int? diastolic = null, int? saturation = null,
        RespiratorySupportCode? support = null, decimal? flow = null, string? otherName = null, string? otherValue = null, string? otherUnit = null) =>
        new(temperature, systolic, diastolic, null, null, saturation, support, flow, null, otherName, otherValue, otherUnit);

    [Fact]
    public void VitalSigns_ConValoresCoherentes_SeAceptan()
    {
        var vitals = Vitals(37.5m, 120, 70, 95, RespiratorySupportCode.Oxigenoterapia, 2m, "Dolor (EVA)", " 6 ", null);

        Assert.Equal("6", vitals.OtherValue);
        Assert.False(vitals.IsEmpty);
    }

    [Theory]
    [InlineData(120, null)]
    [InlineData(null, 70)]
    public void VitalSigns_TensionConUnaSolaCifra_SeRechaza(int? systolic, int? diastolic) =>
        AssertInvalid(() => Vitals(systolic: systolic, diastolic: diastolic));

    [Fact]
    public void VitalSigns_SaturacionFueraDeRango_SeRechaza() => AssertInvalid(() => Vitals(saturation: 101));

    [Fact]
    public void VitalSigns_ValorNoPositivo_SeRechaza() => AssertInvalid(() => Vitals(temperature: 0m));

    [Theory]
    [InlineData(null)]
    [InlineData(RespiratorySupportCode.AireAmbiente)]
    public void VitalSigns_FlujoSinOxigenoterapia_SeRechaza(RespiratorySupportCode? support) =>
        AssertInvalid(() => Vitals(support: support, flow: 2m));

    [Theory]
    [InlineData("Dolor", null, null)]
    [InlineData(null, "6", null)]
    [InlineData(null, null, "mmHg")]
    public void VitalSigns_OtraConstanteIncompleta_SeRechaza(string? name, string? value, string? unit) =>
        AssertInvalid(() => Vitals(otherName: name, otherValue: value, otherUnit: unit));

    [Fact]
    public void Content_SinNingunDato_SeRechaza()
    {
        var ex = Assert.Throws<DomainValidationException>(() => new NursingAssessmentContent(" ", null, "", null, null, Vitals()));
        Assert.Equal("NURSING_ASSESSMENT_EMPTY", ex.Code);
    }

    [Fact]
    public void Content_SoloConConstantes_SeAcepta()
    {
        var content = new NursingAssessmentContent(null, null, null, null, null, Vitals(temperature: 38.1m));

        Assert.Null(content.Findings);
        Assert.Equal(38.1m, content.Vitals.TemperatureCelsius);
    }

    [Fact]
    public void Content_TextoDemasiadoLargo_SeRechaza()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            new NursingAssessmentContent(new string('x', NursingAssessmentContent.MaxTextLength + 1), null, null, null, null, Vitals()));
        Assert.Equal("NURSING_ASSESSMENT_TEXT_TOO_LONG", ex.Code);
    }

    private static void AssertInvalid(Func<VitalSigns> build)
    {
        var ex = Assert.Throws<DomainValidationException>(() => build());
        Assert.Equal("NURSING_ASSESSMENT_VITALS_INVALID", ex.Code);
    }
}
