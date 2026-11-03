using ResidApp.Domain.Residents;

namespace ResidApp.UnitTests;

/// <summary>El plazo de conservación de una baja: 15 años desde la baja (art. 12 de la Llei 21/2000, Ley 16/2010) y su estado respecto de hoy.</summary>
public class ResidentDischargeTests
{
    [Fact]
    public void ElPlazoEsDeQuinceAnosDesdeLaBaja_ContadosEnFechaUtc()
    {
        Assert.Equal(15, ResidentDischarge.RetentionYears);
        Assert.Equal(new DateOnly(2041, 11, 3), ResidentDischarge.RetainUntil(new DateTimeOffset(2026, 11, 3, 9, 30, 0, TimeSpan.FromHours(1))));
        // 00:30 en Madrid (UTC+1) es todavía el día anterior en UTC: el plazo se cuenta con la fecha UTC, como el script 0086.
        Assert.Equal(new DateOnly(2041, 11, 2), ResidentDischarge.RetainUntil(new DateTimeOffset(2026, 11, 3, 0, 30, 0, TimeSpan.FromHours(1))));
        // Una baja del 29 de febrero vence el 29 de febrero de un año bisiesto o, si no lo es, el 28 (DateOnly.AddYears).
        Assert.Equal(new DateOnly(2039, 2, 28), ResidentDischarge.RetainUntil(new DateTimeOffset(2024, 2, 29, 12, 0, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData("2026-11-02", ResidentRetentionStatus.Vencido)]       // ayer fue el último día
    [InlineData("2026-11-03", ResidentRetentionStatus.VenceEnBreve)]  // «hasta el día»: hoy todavía se conserva
    [InlineData("2027-11-03", ResidentRetentionStatus.VenceEnBreve)]  // justo dentro de 12 meses
    [InlineData("2027-11-04", ResidentRetentionStatus.Vigente)]       // un día más allá de los 12 meses
    [InlineData("2041-11-03", ResidentRetentionStatus.Vigente)]
    public void ElEstadoDelPlazoSeCalculaRespectoDeHoy(string retainUntil, ResidentRetentionStatus expected) =>
        Assert.Equal(expected, ResidentDischarge.RetentionStatus(DateOnly.Parse(retainUntil), new DateOnly(2026, 11, 3)));
}
