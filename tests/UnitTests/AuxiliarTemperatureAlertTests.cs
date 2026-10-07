using ResidApp.Domain.Auxiliar;

namespace ResidApp.UnitTests;

public class AuxiliarTemperatureAlertTests
{
    [Theory]
    [InlineData(null, AuxiliarTemperatureAlert.Ninguno)]
    [InlineData(35.5, AuxiliarTemperatureAlert.Ninguno)]
    [InlineData(37.0, AuxiliarTemperatureAlert.Ninguno)]
    [InlineData(37.1, AuxiliarTemperatureAlert.Seguimiento)]
    [InlineData(37.9, AuxiliarTemperatureAlert.Seguimiento)]
    [InlineData(38.0, AuxiliarTemperatureAlert.Seguimiento)]
    [InlineData(38.1, AuxiliarTemperatureAlert.AvisarEnfermeria)]
    [InlineData(40.0, AuxiliarTemperatureAlert.AvisarEnfermeria)]
    public void Evaluate_UmbralesEstrictosDe37Y38(double? celsius, AuxiliarTemperatureAlert expected) =>
        Assert.Equal(expected, AuxiliarTemperatureAlerts.Evaluate((decimal?)celsius));
}
