using ResidApp.Domain.Enfermeria;

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
}
