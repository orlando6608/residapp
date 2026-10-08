using ResidApp.Domain.Enfermeria;

namespace ResidApp.UnitTests;

/// <summary>Comunicados a la familia (CJ, 2026-10-07; continuidad-supervision-comunicacion 5.2): 1 hora de margen y después una hora fija.</summary>
public class FamilyCommunicationScheduleTests
{
    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    private static DateTimeOffset Local(int month, int day, int hour, int minute) =>
        new(new DateTime(2026, month, day, hour, minute, 0), Madrid.GetUtcOffset(new DateTime(2026, month, day, hour, minute, 0)));

    [Fact]
    public void ScheduledAt_ElMismoDia_SiElMargenTerminaAntesDeLas19()
    {
        var scheduled = FamilyCommunicationSchedule.ScheduledAt(Local(10, 8, 9, 30), Madrid);

        Assert.Equal(Local(10, 8, 19, 0), scheduled);
    }

    [Fact]
    public void ScheduledAt_ElDiaSiguiente_SiElMargenTerminaDespuesDeLas19()
    {
        // Preparada a las 18:30: con 1 h de margen son las 19:30, ya pasadas las 19:00 de ese día.
        var scheduled = FamilyCommunicationSchedule.ScheduledAt(Local(10, 8, 18, 30), Madrid);

        Assert.Equal(Local(10, 9, 19, 0), scheduled);
    }

    [Fact]
    public void ScheduledAt_ElMargenQueTerminaJustoALas19_PublicaEseDia()
    {
        Assert.Equal(Local(10, 8, 19, 0), FamilyCommunicationSchedule.ScheduledAt(Local(10, 8, 18, 0), Madrid));
        Assert.Equal(Local(10, 9, 19, 0), FamilyCommunicationSchedule.ScheduledAt(Local(10, 8, 18, 1), Madrid));
    }

    [Fact]
    public void ScheduledAt_RespetaElCambioDeHora_YLaZonaDelServidor()
    {
        // El 25 de octubre de 2026 termina el horario de verano: las 19:00 locales pasan de UTC+2 a UTC+1.
        var summer = FamilyCommunicationSchedule.ScheduledAt(Local(10, 24, 10, 0), Madrid);
        var winter = FamilyCommunicationSchedule.ScheduledAt(Local(10, 26, 10, 0), Madrid);

        Assert.Equal(new DateTimeOffset(2026, 10, 24, 19, 0, 0, TimeSpan.FromHours(2)), summer);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 19, 0, 0, TimeSpan.FromHours(1)), winter);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 17, 0, 0, TimeSpan.Zero), summer.ToUniversalTime());
    }

    [Fact]
    public void CanCorrect_SoloDuranteElMargen_YSinPublicacionAnticipada()
    {
        var prepared = Local(10, 8, 9, 30);

        Assert.True(FamilyCommunicationSchedule.CanCorrect(prepared, null, prepared));
        Assert.True(FamilyCommunicationSchedule.CanCorrect(prepared, null, prepared.AddMinutes(59)));
        Assert.False(FamilyCommunicationSchedule.CanCorrect(prepared, null, prepared.AddHours(1)));
        Assert.False(FamilyCommunicationSchedule.CanCorrect(prepared, prepared.AddMinutes(10), prepared.AddMinutes(20)));
    }

    [Fact]
    public void IsPublished_CuandoLlegaSuHoraOAdministracionLaPublicoAntes()
    {
        var prepared = Local(10, 8, 9, 30);
        var scheduled = FamilyCommunicationSchedule.ScheduledAt(prepared, Madrid);

        Assert.False(FamilyCommunicationSchedule.IsPublished(prepared, null, scheduled.AddMinutes(-1), Madrid));
        Assert.True(FamilyCommunicationSchedule.IsPublished(prepared, null, scheduled, Madrid));
        Assert.True(FamilyCommunicationSchedule.IsPublished(prepared, scheduled.AddHours(-5), prepared.AddMinutes(1), Madrid));
    }
}
