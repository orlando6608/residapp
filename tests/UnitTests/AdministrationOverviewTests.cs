using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>ADM-01: los límites del día y de los próximos días del inicio de Administración, en la zona del centro, y el resumen de comunicados.</summary>
public class AdministrationOverviewTests
{
    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    [Fact]
    public void Hoy_EsElDiaNaturalDeLaZonaDelCentro_NoElDiaUtc()
    {
        // 22:30 UTC del 24/10 ya es 00:30 del 25/10 en Madrid (CEST, +2): hoy es el 25.
        var period = AdministrationOverviewApplicationService.PeriodAt(new DateTimeOffset(2026, 10, 24, 22, 30, 0, TimeSpan.Zero), Madrid);

        Assert.Equal(new DateOnly(2026, 10, 25), period.Today);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero), period.TodayStart);
    }

    [Fact]
    public void LosLimites_RespetanElCambioDeHora_ElDiaQueTermina_Dura25Horas()
    {
        // El 25/10/2026 termina el horario de verano: ese día local dura 25 horas. Hoy es el sábado 24/10 (CEST, +2).
        var period = AdministrationOverviewApplicationService.PeriodAt(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero), Madrid);

        Assert.Equal(new DateTimeOffset(2026, 10, 23, 22, 0, 0, TimeSpan.Zero), period.TodayStart);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero), period.TomorrowStart);
        // Los próximos 7 días son del 25/10 al 31/10 incluidos: acaban a medianoche local del 1/11 (CET, +1).
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 23, 0, 0, TimeSpan.Zero), period.ComingEnd);
    }

    private static FamilyCommunicationStatusView Communication(
        DateTimeOffset scheduledAt, bool published, DateTimeOffset? publishedEarlyAt = null, bool withdrawn = false) =>
        new(new AdministrationFamilyCommunication(
                Guid.NewGuid(), Guid.NewGuid(), ResidentId.From(Guid.NewGuid()), "Residente", null, FamilyCommunicationType.Ordinaria, "Texto",
                scheduledAt.AddHours(-3), publishedEarlyAt, scheduledAt, [],
                withdrawn ? new FamilyCommunicationWithdrawalNote("Motivo", scheduledAt) : null),
            scheduledAt, published, published && !withdrawn);

    [Fact]
    public void ResumenDeComunicados_CuentaLosProgramados_YLosPublicadosEnLosUltimosSieteDias()
    {
        var now = new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);
        var summary = AdministrationCommunicationsSummary.From(
        [
            Communication(now.AddHours(8), published: false),
            Communication(now.AddDays(2), published: false),
            Communication(now.AddDays(-1), published: true),
            Communication(now.AddDays(-7), published: true),
            Communication(now.AddDays(-8), published: true),
            // Se publicó antes de su hora, hace dos días: cuenta por el momento en que salió, no por el programado.
            Communication(now.AddDays(3), published: true, publishedEarlyAt: now.AddDays(-2)),
            // Un comunicado retirado ya no está publicado.
            Communication(now.AddDays(-1), published: true, withdrawn: true),
        ], now);

        Assert.Equal(2, summary.Scheduled);
        Assert.Equal(3, summary.PublishedRecently);
    }

    [Fact]
    public void SinNadaPendiente_SoloSiNoHayResidentesSinHabitacion_NiAutorizacionesNiSolicitudes()
    {
        Assert.True(new AdministrationOverview(0, 0, 0, 0, 0, 0, 5, 9).NothingPending);
        Assert.False(new AdministrationOverview(1, 0, 0, 0, 0, 0, 0, 0).NothingPending);
        Assert.False(new AdministrationOverview(0, 1, 0, 0, 0, 0, 0, 0).NothingPending);
        Assert.False(new AdministrationOverview(0, 0, 1, 0, 0, 0, 0, 0).NothingPending);
        Assert.False(new AdministrationOverview(0, 0, 0, 1, 0, 0, 0, 0).NothingPending);
        Assert.False(new AdministrationOverview(0, 0, 0, 0, 1, 0, 0, 0).NothingPending);
        Assert.False(new AdministrationOverview(0, 0, 0, 0, 0, 1, 0, 0).NothingPending);
    }
}
