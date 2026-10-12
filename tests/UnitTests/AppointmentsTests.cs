using ResidApp.Domain.Appointments;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>Citas con reserva directa (CIT-C01, CIT-D01): la configuración, las franjas y los huecos que se pueden reservar.</summary>
public class AppointmentsTests
{
    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
    private static readonly AppointmentServiceSettings Reservation = new(AppointmentServiceMode.ReservaDirecta, 24, 14);

    /// <summary>Lunes de 10:00 a 12:00 en citas de 30 minutos (huecos a las 10:00, 10:30, 11:00 y 11:30).</summary>
    private static AppointmentWindow MondayMorning(AppointmentTeam team = AppointmentTeam.Medico) =>
        new(Guid.NewGuid(), team, 1, new TimeOnly(10, 0), new TimeOnly(12, 0), 30);

    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute = 0)
    {
        var local = new DateTime(year, month, day, hour, minute, 0);
        return new DateTimeOffset(local, Madrid.GetUtcOffset(local));
    }

    private static IReadOnlyList<AppointmentSlot> Generate(
        IEnumerable<AppointmentWindow> windows, DateTimeOffset now, AppointmentServiceSettings? settings = null,
        IEnumerable<AppointmentBlock>? blocks = null, IEnumerable<AppointmentSlot>? taken = null,
        AppointmentTeam team = AppointmentTeam.Medico, AppointmentModality modality = AppointmentModality.Presencial) =>
        AppointmentSlots.Generate(windows, blocks ?? [], taken ?? [], team, modality, settings ?? Reservation, now, Madrid);

    [Fact]
    public void LaConfiguracion_AdmiteLosTresModos_ConSusLimites_YSoloUnoDaReservaOSolicitudes()
    {
        Assert.Equal(new AppointmentServiceSettings(AppointmentServiceMode.ReservaDirecta, 0, 1),
            AppointmentServiceSettings.Validate(AppointmentServiceMode.ReservaDirecta, 0, 1));
        Assert.True(AppointmentServiceSettings.Validate(AppointmentServiceMode.ReservaDirecta, 720, 180).BookingEnabled);
        Assert.False(AppointmentServiceSettings.Validate(AppointmentServiceMode.Desactivado, 24, 30).BookingEnabled);
        var requests = AppointmentServiceSettings.Validate(AppointmentServiceMode.SolicitudPrevia, 24, 30);
        Assert.True(requests.RequestsEnabled);
        Assert.False(requests.BookingEnabled);
        Assert.False(AppointmentServiceSettings.Validate(AppointmentServiceMode.ReservaDirecta, 24, 30).RequestsEnabled);
        Assert.False(AppointmentServiceSettings.Validate(AppointmentServiceMode.Desactivado, 24, 30).RequestsEnabled);
        foreach (var (mode, notice, horizon) in new (AppointmentServiceMode?, int?, int?)[]
                 {
                     (null, 24, 30), (AppointmentServiceMode.SolicitudPrevia, 721, 30), (AppointmentServiceMode.ReservaDirecta, -1, 30),
                     (AppointmentServiceMode.ReservaDirecta, 721, 30), (AppointmentServiceMode.ReservaDirecta, 24, 0),
                     (AppointmentServiceMode.ReservaDirecta, 24, 181), (AppointmentServiceMode.ReservaDirecta, null, 30),
                     (AppointmentServiceMode.ReservaDirecta, 24, null),
                 })
        {
            var error = Assert.Throws<DomainValidationException>(() => AppointmentServiceSettings.Validate(mode, notice, horizon));
            Assert.Equal("APPOINTMENT_SERVICE_INVALID", error.Message);
        }
    }

    [Fact]
    public void UnaFranja_TieneDiaHorasYDuracionValidos_YDosFranjasDelMismoEquipoYDiaNoSeSolapan()
    {
        var valid = AppointmentWindow.Validate(Guid.NewGuid(), AppointmentTeam.Medico, 1, new TimeOnly(10, 0, 30), new TimeOnly(12, 0), 30);
        foreach (var (team, day, start, end, minutes) in new (AppointmentTeam?, int?, TimeOnly?, TimeOnly?, int?)[]
                 {
                     (null, 1, new TimeOnly(10, 0), new TimeOnly(12, 0), 30), (AppointmentTeam.Medico, 0, new TimeOnly(10, 0), new TimeOnly(12, 0), 30),
                     (AppointmentTeam.Medico, 8, new TimeOnly(10, 0), new TimeOnly(12, 0), 30), (AppointmentTeam.Medico, 1, null, new TimeOnly(12, 0), 30),
                     (AppointmentTeam.Medico, 1, new TimeOnly(12, 0), new TimeOnly(10, 0), 30), (AppointmentTeam.Medico, 1, new TimeOnly(10, 0), new TimeOnly(10, 0), 30),
                     (AppointmentTeam.Medico, 1, new TimeOnly(10, 0), new TimeOnly(12, 0), 4), (AppointmentTeam.Medico, 1, new TimeOnly(10, 0), new TimeOnly(12, 0), 241),
                     (AppointmentTeam.Medico, 1, new TimeOnly(10, 0), new TimeOnly(10, 20), 30), (AppointmentTeam.Medico, 1, new TimeOnly(10, 0), new TimeOnly(12, 0), null),
                 })
        {
            var error = Assert.Throws<DomainValidationException>(() => AppointmentWindow.Validate(Guid.NewGuid(), team, day, start, end, minutes));
            Assert.Equal("APPOINTMENT_WINDOW_INVALID", error.Message);
        }

        var after = valid with { Id = Guid.NewGuid(), Start = new TimeOnly(11, 0), End = new TimeOnly(13, 0) };
        var adjacent = valid with { Id = Guid.NewGuid(), Start = new TimeOnly(12, 0), End = new TimeOnly(13, 0) };
        Assert.Equal(new TimeOnly(10, 0), valid.Start);
        Assert.True(valid.Overlaps(after));
        Assert.False(valid.Overlaps(adjacent));
        Assert.False(valid.Overlaps(valid with { Team = AppointmentTeam.Enfermeria }));
        Assert.False(valid.Overlaps(valid with { IsoDayOfWeek = 2 }));
    }

    [Fact]
    public void LosHuecos_SalenDeLaFranja_CadaDuracion_YEnOrden()
    {
        var now = Local(2026, 10, 11, 9);

        var slots = Generate([MondayMorning()], now, Reservation with { MaximumHorizonDays = 7 });

        // Lunes 12 de octubre: 10:00, 10:30, 11:00 y 11:30; el lunes siguiente (19) queda fuera del horizonte de 7 días.
        Assert.Equal(new[] { Local(2026, 10, 12, 10), Local(2026, 10, 12, 10, 30), Local(2026, 10, 12, 11), Local(2026, 10, 12, 11, 30) },
            slots.Select(s => s.StartsAt));
        Assert.All(slots, s => Assert.Equal(TimeSpan.FromMinutes(30), s.EndsAt - s.StartsAt));
    }

    [Fact]
    public void LaAntelacionMinima_QuitaLosHuecosDemasiadoCercanos_YElHorizonteLosDemasiadoLejanos()
    {
        var window = MondayMorning();

        var closeToStart = Generate([window], Local(2026, 10, 12, 9, 30), Reservation with { MinimumNoticeHours = 1, MaximumHorizonDays = 1 });
        var exactlyNotice = Generate([window], Local(2026, 10, 11, 10), Reservation with { MinimumNoticeHours = 24, MaximumHorizonDays = 1 });
        var farAway = Generate([window], Local(2026, 10, 12, 8), Reservation with { MinimumNoticeHours = 0, MaximumHorizonDays = 8 });

        Assert.Equal(new[] { Local(2026, 10, 12, 10, 30), Local(2026, 10, 12, 11), Local(2026, 10, 12, 11, 30) }, closeToStart.Select(s => s.StartsAt));
        Assert.Equal(Local(2026, 10, 12, 10), exactlyNotice[0].StartsAt);
        Assert.Equal(8, farAway.Count);
        Assert.Equal(Local(2026, 10, 19, 11, 30), farAway[^1].StartsAt);
    }

    [Fact]
    public void UnBloqueo_QuitaSusHuecos_SoloDeSuEquipo_YDeSuModalidadSiLaTiene()
    {
        var now = Local(2026, 10, 11, 9);
        var settings = Reservation with { MaximumHorizonDays = 7 };
        var window = MondayMorning();
        var nurseWindow = MondayMorning(AppointmentTeam.Enfermeria);
        var block = new AppointmentBlock(Guid.NewGuid(), AppointmentTeam.Medico, null, Local(2026, 10, 12, 10), Local(2026, 10, 12, 11), "Sesión clínica");
        var onlyPhone = block with { Modality = AppointmentModality.Telefonica };

        var blocked = Generate([window], now, settings, blocks: [block]);
        var otherTeam = Generate([window, nurseWindow], now, settings, blocks: [block], team: AppointmentTeam.Enfermeria);
        var phoneBlockInPerson = Generate([window], now, settings, blocks: [onlyPhone]);
        var phoneBlockByPhone = Generate([window], now, settings, blocks: [onlyPhone], modality: AppointmentModality.Telefonica);

        Assert.Equal(new[] { Local(2026, 10, 12, 11), Local(2026, 10, 12, 11, 30) }, blocked.Select(s => s.StartsAt));
        Assert.Equal(4, otherTeam.Count);
        Assert.Equal(4, phoneBlockInPerson.Count);
        Assert.Equal(2, phoneBlockByPhone.Count);
    }

    [Fact]
    public void UnaCitaDelEquipo_QuitaTodoHuecoQueSeSolape_AunqueSeaDeOtraDuracion()
    {
        var now = Local(2026, 10, 11, 9);
        var settings = Reservation with { MaximumHorizonDays = 7 };
        var taken = new AppointmentSlot(Local(2026, 10, 12, 10, 15), Local(2026, 10, 12, 10, 45));

        var slots = Generate([MondayMorning()], now, settings, taken: [taken]);

        Assert.Equal(new[] { Local(2026, 10, 12, 11), Local(2026, 10, 12, 11, 30) }, slots.Select(s => s.StartsAt));
    }

    [Fact]
    public void UnaFranjaEnUnaHoraQueNoExiste_NoDaHuecosEnEsaHora_YLosDemasSiguenEnSuSitio()
    {
        // El 29 de marzo de 2026 a las 02:00 los relojes de Madrid pasan a las 03:00: la hora de 02:00 a 03:00 no existe.
        var sunday = new AppointmentWindow(Guid.NewGuid(), AppointmentTeam.Medico, 7, new TimeOnly(1, 0), new TimeOnly(4, 0), 60);
        var now = new DateTimeOffset(2026, 3, 27, 9, 0, 0, TimeSpan.FromHours(1));

        var slots = Generate([sunday], now, Reservation with { MinimumNoticeHours = 0, MaximumHorizonDays = 3 });

        Assert.Equal(new[] { new DateTimeOffset(2026, 3, 29, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero) },
            slots.Select(s => s.StartsAt.ToUniversalTime()));
    }

    [Fact]
    public void LaFamilia_PuedeCambiarUnaCitaHastaLaAntelacionMinima()
    {
        var start = Local(2026, 10, 12, 10);
        var settings = Reservation with { MinimumNoticeHours = 24 };

        Assert.True(AppointmentRules.CanFamilyChange(start, start.AddHours(-24), settings));
        Assert.False(AppointmentRules.CanFamilyChange(start, start.AddHours(-24).AddTicks(1), settings));
        Assert.False(AppointmentRules.CanFamilyChange(start, start.AddHours(1), settings));
    }

    [Fact]
    public void UnBloqueo_TieneEquipoRangoYMotivoValidos()
    {
        var start = Local(2026, 10, 12, 10);
        var block = AppointmentBlock.Validate(Guid.NewGuid(), AppointmentTeam.Medico, null, start, start.AddHours(1), "  Sesión clínica  ");
        Assert.Equal("Sesión clínica", block.Reason);
        foreach (var invalid in new Func<AppointmentBlock>[]
                 {
                     () => AppointmentBlock.Validate(Guid.NewGuid(), null, null, start, start.AddHours(1), "Motivo"),
                     () => AppointmentBlock.Validate(Guid.NewGuid(), AppointmentTeam.Medico, null, start, start, "Motivo"),
                     () => AppointmentBlock.Validate(Guid.NewGuid(), AppointmentTeam.Medico, null, start, start.AddHours(-1), "Motivo"),
                     () => AppointmentBlock.Validate(Guid.NewGuid(), AppointmentTeam.Medico, null, start, start.AddHours(1), "  "),
                     () => AppointmentBlock.Validate(Guid.NewGuid(), AppointmentTeam.Medico, null, start, start.AddHours(1), new string('x', 201)),
                 })
        {
            Assert.Equal("APPOINTMENT_BLOCK_INVALID", Assert.Throws<DomainValidationException>(invalid).Message);
        }
    }
}
