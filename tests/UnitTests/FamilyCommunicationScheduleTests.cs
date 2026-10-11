using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

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
    public void ScheduledAt_ConElHorarioDelCentro_UsaSuHoraYSuMargen()
    {
        // Un centro que prefiere las 20:30, después de la cena, y 2 horas de margen para corregir.
        var evening = new FamilyCommunicationScheduleSettings(new TimeOnly(20, 30), TimeSpan.FromHours(2));

        Assert.Equal(Local(10, 8, 20, 30), FamilyCommunicationSchedule.ScheduledAt(Local(10, 8, 9, 30), evening, Madrid));
        Assert.Equal(Local(10, 8, 20, 30), FamilyCommunicationSchedule.ScheduledAt(Local(10, 8, 18, 30), evening, Madrid));
        Assert.Equal(Local(10, 9, 20, 30), FamilyCommunicationSchedule.ScheduledAt(Local(10, 8, 18, 31), evening, Madrid));
        // Sin margen y a las 00:00: sale a la medianoche siguiente, nunca en el pasado.
        var midnight = new FamilyCommunicationScheduleSettings(new TimeOnly(0, 0), TimeSpan.Zero);
        Assert.Equal(Local(10, 9, 0, 0), FamilyCommunicationSchedule.ScheduledAt(Local(10, 8, 9, 30), midnight, Madrid));
        Assert.Equal(FamilyCommunicationScheduleSettings.Default, new FamilyCommunicationScheduleSettings(FamilyCommunicationSchedule.PublicationTime, FamilyCommunicationSchedule.Margin));
    }

    [Fact]
    public void Settings_Validate_AceptaHoraYMargenDeUnDia_YRechazaElResto()
    {
        var valid = FamilyCommunicationScheduleSettings.Validate(new TimeOnly(20, 30, 45), 120);

        Assert.Equal(new FamilyCommunicationScheduleSettings(new TimeOnly(20, 30), TimeSpan.FromHours(2)), valid);
        Assert.Equal(0, FamilyCommunicationScheduleSettings.Validate(new TimeOnly(0, 0), 0).MarginMinutes);
        Assert.Equal(1440, FamilyCommunicationScheduleSettings.Validate(new TimeOnly(23, 59), 1440).MarginMinutes);
        foreach (var (time, margin) in new (TimeOnly?, int?)[] { (null, 60), (new TimeOnly(19, 0), null), (new TimeOnly(19, 0), -1), (new TimeOnly(19, 0), 1441) })
        {
            Assert.Equal(FamilyCommunicationScheduleSettings.InvalidCode,
                Assert.Throws<DomainValidationException>(() => FamilyCommunicationScheduleSettings.Validate(time, margin)).Message);
        }
    }

    /// <summary>CJ, 2026-10-10 (5.2): se corrige hasta que se publica, no solo durante la primera hora.</summary>
    [Fact]
    public void CanCorrect_HastaQueSePublica_YNoDespuesDeLaPublicacionAnticipada()
    {
        var prepared = Local(10, 8, 16, 10);
        var scheduled = FamilyCommunicationSchedule.ScheduledAt(prepared, Madrid);

        Assert.Equal(Local(10, 8, 19, 0), scheduled);
        Assert.True(FamilyCommunicationSchedule.CanCorrect(scheduled, null, prepared));
        Assert.True(FamilyCommunicationSchedule.CanCorrect(scheduled, null, prepared.AddHours(2)));
        Assert.True(FamilyCommunicationSchedule.CanCorrect(scheduled, null, scheduled.AddMinutes(-1)));
        Assert.False(FamilyCommunicationSchedule.CanCorrect(scheduled, null, scheduled));
        Assert.False(FamilyCommunicationSchedule.CanCorrect(scheduled, prepared.AddMinutes(10), prepared.AddMinutes(20)));
    }

    [Fact]
    public void IsPublished_CuandoLlegaSuHoraOSePublicoAntes()
    {
        var prepared = Local(10, 8, 9, 30);
        var scheduled = FamilyCommunicationSchedule.ScheduledAt(prepared, Madrid);

        Assert.False(FamilyCommunicationSchedule.IsPublished(scheduled, null, scheduled.AddMinutes(-1)));
        Assert.True(FamilyCommunicationSchedule.IsPublished(scheduled, null, scheduled));
        Assert.True(FamilyCommunicationSchedule.IsPublished(scheduled, scheduled.AddHours(-5), prepared.AddMinutes(1)));
    }
}
