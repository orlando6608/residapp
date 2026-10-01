using ResidApp.Application.Ports;
using ResidApp.Web.Models;

namespace ResidApp.FunctionalTests;

/// <summary>DIR-08 a DIR-10: el periodo de los indicadores de Dirección y la etiqueta de cada mes.</summary>
public class IndicatorPeriodFilterTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    [Fact]
    public void SinFechas_SonLosUltimos30Dias_HoyIncluido()
    {
        var filter = new IndicatorPeriodFilter();

        Assert.Equal((new DateOnly(2026, 9, 2), Today), filter.Resolve(Today));
        Assert.Null(filter.Validate(Today));
    }

    [Fact]
    public void SoloHasta_TomaLos30DiasAnteriores_YSoloDesde_LlegaHastaHoy()
    {
        Assert.Equal((new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 31)),
            new IndicatorPeriodFilter { Hasta = new DateOnly(2026, 8, 31) }.Resolve(Today));
        Assert.Equal((new DateOnly(2026, 1, 1), Today), new IndicatorPeriodFilter { Desde = new DateOnly(2026, 1, 1) }.Resolve(Today));
    }

    [Fact]
    public void DesdePosteriorAHasta_OMasDe366Dias_DanUnMensaje()
    {
        var reversed = new IndicatorPeriodFilter { Desde = new DateOnly(2026, 9, 10), Hasta = new DateOnly(2026, 9, 1) };
        var tooLong = new IndicatorPeriodFilter { Desde = new DateOnly(2025, 1, 1), Hasta = new DateOnly(2026, 1, 2) };
        var fullYear = new IndicatorPeriodFilter { Desde = new DateOnly(2025, 1, 1), Hasta = new DateOnly(2026, 1, 1) };

        Assert.Equal("La fecha «desde» no puede ser posterior a la fecha «hasta».", reversed.Validate(Today));
        Assert.Equal("El periodo no puede pasar de 366 días.", tooLong.Validate(Today));
        Assert.Null(fullYear.Validate(Today));
    }

    [Fact]
    public void Mes_EnteroORecortado_SeNombraEnEspanol()
    {
        var counts = new SupervisionIndicatorCounts(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        Assert.Equal("septiembre de 2026",
            SupervisionDisplay.MonthLabel(new SupervisionMonthIndicators(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), counts)));
        Assert.Equal("2–30 de septiembre de 2026",
            SupervisionDisplay.MonthLabel(new SupervisionMonthIndicators(new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 30), counts)));
        Assert.Equal("1 de octubre de 2026",
            SupervisionDisplay.MonthLabel(new SupervisionMonthIndicators(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1), counts)));
    }
}
