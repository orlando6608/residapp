using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

public class VitalSignReferenceRangesTests
{
    private static VitalSigns Vitals(decimal? temperature = null, int? saturation = null, int? systolic = null, int? diastolic = null) =>
        new(temperature, systolic, diastolic, null, null, saturation, null, null, null, null, null, null);

    private static readonly IReadOnlyList<VitalSignRange> Ranges =
    [
        new(VitalSignCode.Temperatura, 35m, 38m),
        new(VitalSignCode.SaturacionO2, 92m, null),
        new(VitalSignCode.TensionSistolica, null, 160m),
    ];

    [Fact]
    public void Evaluate_DentroDelRangoYEnLosLimites_NoAvisa()
    {
        Assert.Empty(VitalSignReferenceRanges.Evaluate(Vitals(temperature: 38m, saturation: 92), Ranges));
        Assert.Empty(VitalSignReferenceRanges.Evaluate(Vitals(temperature: 35m, systolic: 160, diastolic: 90), Ranges));
    }

    [Fact]
    public void Evaluate_FueraDelRango_AvisaConSentidoYRango()
    {
        var alerts = VitalSignReferenceRanges.Evaluate(Vitals(temperature: 38.5m, saturation: 88, systolic: 170, diastolic: 95), Ranges);

        Assert.Equal(3, alerts.Count);
        var temperature = alerts.Single(a => a.Code == VitalSignCode.Temperatura);
        Assert.Equal(VitalSignDeviation.PorEncima, temperature.Deviation);
        Assert.Equal(38.5m, temperature.Value);
        Assert.Equal(VitalSignDeviation.PorDebajo, alerts.Single(a => a.Code == VitalSignCode.SaturacionO2).Deviation);
        Assert.Equal(VitalSignDeviation.PorEncima, alerts.Single(a => a.Code == VitalSignCode.TensionSistolica).Deviation);
    }

    [Fact]
    public void Evaluate_SinValorOSinRango_NoAvisa()
    {
        // Diastólica fuera de cualquier valor razonable, pero el centro no tiene rango para ella.
        Assert.Empty(VitalSignReferenceRanges.Evaluate(Vitals(systolic: 120, diastolic: 200), Ranges));
        Assert.Empty(VitalSignReferenceRanges.Evaluate(Vitals(temperature: 40m), []));
    }

    [Fact]
    public void Evaluate_SaturacionBajaConOxigenoterapia_NoAvisa_PeroConAireAmbienteSi()
    {
        var conOxigeno = new VitalSigns(null, null, null, null, null, 90, RespiratorySupportCode.Oxigenoterapia, 2m, null, null, null, null);
        var aireAmbiente = new VitalSigns(null, null, null, null, null, 90, RespiratorySupportCode.AireAmbiente, null, null, null, null, null);

        Assert.Empty(VitalSignReferenceRanges.Evaluate(conOxigeno, Ranges));
        Assert.Equal(VitalSignCode.SaturacionO2, Assert.Single(VitalSignReferenceRanges.Evaluate(aireAmbiente, Ranges)).Code);
    }

    [Fact]
    public void Evaluate_OtraConstanteFueraDeRango_SigueAvisandoConOxigenoterapia()
    {
        var vitals = new VitalSigns(39m, null, null, null, null, null, RespiratorySupportCode.Oxigenoterapia, 2m, null, null, null, null);

        Assert.Equal(VitalSignCode.Temperatura, Assert.Single(VitalSignReferenceRanges.Evaluate(vitals, Ranges)).Code);
    }

    [Fact]
    public void Suggested_SonLosValoresDeCJ_YTodosSonValidos()
    {
        Assert.Equal(Enum.GetValues<VitalSignCode>().Length, VitalSignReferenceRanges.Suggested.Count);
        foreach (var range in VitalSignReferenceRanges.Suggested)
        {
            VitalSignReferenceRanges.Validate(range);
        }

        var byCode = VitalSignReferenceRanges.Suggested.ToDictionary(r => r.Code);
        Assert.Equal((36m, 37.9m), (byCode[VitalSignCode.Temperatura].Min!.Value, byCode[VitalSignCode.Temperatura].Max!.Value));
        Assert.Equal((90m, 139m), (byCode[VitalSignCode.TensionSistolica].Min!.Value, byCode[VitalSignCode.TensionSistolica].Max!.Value));
        Assert.Equal((60m, 89m), (byCode[VitalSignCode.TensionDiastolica].Min!.Value, byCode[VitalSignCode.TensionDiastolica].Max!.Value));
        Assert.Equal((60m, 100m), (byCode[VitalSignCode.FrecuenciaCardiaca].Min!.Value, byCode[VitalSignCode.FrecuenciaCardiaca].Max!.Value));
        Assert.Equal((12m, 20m), (byCode[VitalSignCode.FrecuenciaRespiratoria].Min!.Value, byCode[VitalSignCode.FrecuenciaRespiratoria].Max!.Value));
        Assert.Equal((95m, 100m), (byCode[VitalSignCode.SaturacionO2].Min!.Value, byCode[VitalSignCode.SaturacionO2].Max!.Value));
        Assert.Equal(70m, byCode[VitalSignCode.Glucemia].Min);
        Assert.Null(byCode[VitalSignCode.Glucemia].Max);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(38.0, 35.0)]
    [InlineData(35.0, 35.0)]
    [InlineData(-1.0, null)]
    public void Validate_RangoIncoherente_SeRechaza(double? min, double? max)
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            VitalSignReferenceRanges.Validate(new VitalSignRange(VitalSignCode.Temperatura, (decimal?)min, (decimal?)max)));
        Assert.Equal("REFERENCE_RANGE_INVALID", ex.Code);
    }

    [Fact]
    public void Validate_SaturacionPorEncimaDe100_SeRechaza() =>
        Assert.Throws<DomainValidationException>(() =>
            VitalSignReferenceRanges.Validate(new VitalSignRange(VitalSignCode.SaturacionO2, 92m, 101m)));

    [Theory]
    [InlineData(92.0, 100.0)]
    [InlineData(92.0, null)]
    [InlineData(null, 100.0)]
    public void Validate_RangoCoherente_SeAcepta(double? min, double? max) =>
        VitalSignReferenceRanges.Validate(new VitalSignRange(VitalSignCode.SaturacionO2, (decimal?)min, (decimal?)max));
}
