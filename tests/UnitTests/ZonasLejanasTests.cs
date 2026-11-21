using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Appointments;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>
/// Zonas a varias horas de UTC y con cambio de hora a medianoche (estudio de zonas fuera de España). Con Madrid y Canarias el «día UTC» y el «día del centro» solo difieren una o dos horas; con
/// UTC-5 difieren unas cinco cada tarde-noche, y en una zona que cambia de hora a las 00:00 esa hora no existe (o se repite) y <c>ConvertTimeToUtc</c> lanzaría una excepción. Las zonas de estas pruebas
/// son de prueba: ninguna nombra a un centro.
/// </summary>
public class ZonasLejanasTests
{
    private static readonly TimeZoneInfo NuevaYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    // Zonas cuyo cambio de hora cae a las 00:00 (a elegir la primera que lo tenga en 2026–2027 con los datos de zonas de esta máquina).
    private static readonly string[] CandidatasMedianoche = ["Asia/Beirut", "Africa/Cairo", "America/Santiago", "America/Asuncion"];

    private static (TimeZoneInfo Zone, DateOnly GapDay)? ZonaConHuecoEnMedianoche()
    {
        foreach (var id in CandidatasMedianoche)
        {
            TimeZoneInfo zone;
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                continue;
            }

            for (var day = new DateOnly(2026, 1, 1); day < new DateOnly(2028, 1, 1); day = day.AddDays(1))
            {
                if (zone.IsInvalidTime(DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified)))
                {
                    return (zone, day);
                }
            }
        }

        return null;
    }

    [Theory]
    [InlineData("2026-01-15", "2026-01-15T05:00:00")] // Nueva York, invierno: UTC-5
    [InlineData("2026-07-15", "2026-07-15T04:00:00")] // verano: UTC-4
    public void ElDiaEmpiezaALaMedianocheDelCentro_NoALaDeUtc_ConUnaZonaLejana(string day, string expectedUtc)
    {
        Assert.Equal(DateTime.Parse(expectedUtc), CenterTime.StartOfDayUtc(DateOnly.Parse(day), NuevaYork));
    }

    [Fact]
    public void ElDiaEmpiezaALaMedianocheDeMadrid_UnaHoraAntesDeLaDeUtc()
    {
        Assert.Equal(new DateTime(2026, 1, 14, 23, 0, 0), CenterTime.StartOfDayUtc(new DateOnly(2026, 1, 15), Madrid));
    }

    [Fact]
    public void ALas20HorasDeNuevaYork_TodavíaEsHoy_YACeroHorasEsMañana()
    {
        using var _ = CenterTime.Use(NuevaYork);

        // 01:00 UTC del 16 de enero son las 20:00 del 15 en Nueva York; las 05:00 UTC son las 00:00 del 16.
        Assert.Equal(new DateOnly(2026, 1, 15), CenterTime.TodayAt(new DateTimeOffset(2026, 1, 16, 1, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 1, 15), CenterTime.TodayAt(new DateTimeOffset(2026, 1, 16, 4, 59, 59, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 1, 16), CenterTime.TodayAt(new DateTimeOffset(2026, 1, 16, 5, 0, 0, TimeSpan.Zero)));

        var period = AdministrationOverviewApplicationService.PeriodAt(new DateTimeOffset(2026, 1, 16, 1, 0, 0, TimeSpan.Zero), NuevaYork);
        Assert.Equal(new DateOnly(2026, 1, 15), period.Today);
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 5, 0, 0, TimeSpan.Zero), period.TodayStart);
        Assert.Equal(new DateTimeOffset(2026, 1, 16, 5, 0, 0, TimeSpan.Zero), period.TomorrowStart);
    }

    [Fact]
    public void UnHechoDeLas20HorasCuentaEnElDiaDelCentro_NoEnElDiaSiguienteDeUtc()
    {
        // 01:00 UTC del 16 son las 20:00 del 15 en Nueva York: cuenta en el día 15 del centro.
        var (from, toExclusive) = SupervisionIndicatorRules.UtcBounds(new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 15), NuevaYork);
        var fact = new DateTime(2026, 1, 16, 1, 0, 0, DateTimeKind.Utc);

        Assert.True(fact >= from && fact < toExclusive);
    }

    [Fact]
    public void UnaZonaQueCambiaDeHoraAMedianoche_TieneUnDiaDe23Horas_YNingunaFuncionDeLimitesLanza()
    {
        var found = ZonaConHuecoEnMedianoche();
        Assert.True(found is not null, "Ninguna zona candidata tiene un hueco a las 00:00 en 2026–2027 con los datos de zonas de esta máquina: añade otra a CandidatasMedianoche.");
        var (zone, gapDay) = found!.Value;

        var start = CenterTime.StartOfDayUtc(gapDay, zone);
        var next = CenterTime.StartOfDayUtc(gapDay.AddDays(1), zone);
        Assert.Equal(TimeSpan.FromHours(23), next - start);

        // El día del cambio mide 23 horas y los límites, el panel de Administración y los indicadores no lanzan (antes lanzaba ArgumentException).
        var (from, toExclusive) = SupervisionIndicatorRules.UtcBounds(gapDay, gapDay, zone);
        Assert.Equal((start, next), (from, toExclusive));

        foreach (var offset in new[] { -8, -1, 0, 1 })
        {
            var now = new DateTimeOffset(DateTime.SpecifyKind(gapDay.AddDays(offset).ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Utc));
            var period = AdministrationOverviewApplicationService.PeriodAt(now, zone);
            Assert.True(period.TodayStart < period.TomorrowStart && period.TomorrowStart < period.ComingEnd);
        }
    }

    [Fact]
    public void UnaFranjaDeLasCeroHorasEnUnaZonaQueCambiaAMedianoche_NoGeneraElHuecoQueNoExiste_NiLanza()
    {
        var (zone, gapDay) = ZonaConHuecoEnMedianoche() ?? throw new InvalidOperationException("sin zona de prueba");
        var window = new AppointmentWindow(
            Guid.NewGuid(), AppointmentTeam.Medico, gapDay.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)gapDay.DayOfWeek, new TimeOnly(0, 0), new TimeOnly(2, 0), 30);
        var now = new DateTimeOffset(CenterTime.StartOfDayUtc(gapDay, zone), TimeSpan.Zero).AddHours(-30);

        var slots = AppointmentSlots.Generate(
            [window], [], [], AppointmentTeam.Medico, AppointmentModality.Presencial, new AppointmentServiceSettings(AppointmentServiceMode.ReservaDirecta, 0, 3), now, zone);
        Assert.DoesNotContain(slots, slot => TimeZoneInfo.ConvertTime(slot.StartsAt, zone).Hour == 0 && TimeZoneInfo.ConvertTime(slot.StartsAt, zone).Day == gapDay.Day);
    }

    [Fact]
    public void UnaHoraQueSeRepite_SeEntiendeComoLaPrimeraVez_LaDelHorarioDeVerano()
    {
        // En Madrid el 25/10/2026 las 02:30 ocurren dos veces (UTC+2 y luego UTC+1).
        var instant = CenterTime.FromLocal(new DateTime(2026, 10, 25, 2, 30, 0), Madrid);

        Assert.Equal(TimeSpan.FromHours(2), instant.Offset);
        Assert.Equal(new DateTime(2026, 10, 25, 0, 30, 0), instant.UtcDateTime);
    }
}
