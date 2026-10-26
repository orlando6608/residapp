using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Enfermeria;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.PortalFamiliarTests;

namespace ResidApp.IntegrationTests;

/// <summary>Resumen periódico para la familia (script 0074; CJ, tercera 1.1 a 1.5, y decisiones de Orlando del 2026-10-26): una recopilación,
/// calculada al leer, de los comunicados ya publicados en el último periodo cerrado, con la frecuencia del centro; sin nada, no hay resumen.</summary>
public class ResumenPeriodicoTests
{
    private static FamilyCommunicationScheduleApplicationService Schedule(string subject) => new(
        new AdministrationAccessResolver(new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedResumenSessionIdentityProvider(subject)),
        new SqlFamilyCommunicationScheduleRepository(TestDatabase.ConnectionFactory));

    private static async Task SetFrequencyAsync(SeededProfile admin, int version, FamilyCommunicationSummaryFrequency frequency)
    {
        var saved = await Schedule(admin.ExternalSubject).SaveAsync(new SaveFamilyCommunicationScheduleCommand(
            admin.ProfileScopeId, admin.CenterId, version, FamilyCommunicationScheduleSettings.Default.PublicationTime,
            FamilyCommunicationScheduleSettings.Default.MarginMinutes, frequency));
        Assert.True(saved.Ok, saved.Error?.Message);
    }

    private static (DateTimeOffset Start, DateTimeOffset End) Period(FamilyCommunicationSummaryFrequency frequency) =>
        FamilyCommunicationSchedule.LastClosedSummaryPeriod(
            frequency, FamilyCommunicationScheduleSettings.Default.PublicationTime, TimeZoneInfo.Local, DateTimeOffset.UtcNow);

    /// <summary>Un comunicado publicado a la hora indicada (su hora programada), preparado un día antes.</summary>
    private static Task<Guid> PublishedAtAsync(Setup setup, string text, DateTimeOffset at, FamilyCommunicationType type = FamilyCommunicationType.Ordinaria) =>
        AddCommunicationAsync(setup, text, type, at.UtcDateTime.AddDays(-1), at.UtcDateTime);

    private static async Task<FamilyPortalResidentHome> ReadAsync(Setup setup) =>
        (await Portal(setup.Subject).ReadResidentAsync(await QueryAsync(setup.Subject), setup.LinkId)).Value!;

    [Fact]
    public async Task ElResumenSemanalRecogeLoPublicadoEnElUltimoPeriodoCerrado_SinRetiradosNiLoDeFueraDelPeriodo_YSeSumaALosSueltos()
    {
        var setup = await SetupAsync("Residente Resumen Semanal");
        var (start, end) = Period(FamilyCommunicationSummaryFrequency.Semanal);
        var first = await PublishedAtAsync(setup, "Primero del periodo.", start.AddHours(2));
        var second = await PublishedAtAsync(setup, "Relevante del periodo.", end.AddHours(-3), FamilyCommunicationType.Relevante);
        var atTheCut = await PublishedAtAsync(setup, "Justo en el corte.", end);
        var withdrawn = await PublishedAtAsync(setup, "Retirado, no sale.", end.AddHours(-1));
        var atTheStart = await PublishedAtAsync(setup, "Justo en el corte anterior, es del periodo de antes.", start);
        var before = await PublishedAtAsync(setup, "De la semana anterior.", start.AddDays(-2));
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.comunicaciones_familiares_retiradas (id, comunicacion_id, centro_id, residente_id, motivo, retirada_por_cuenta_id, retirada_en)
                VALUES (NEWID(), @withdrawn, @center, @resident, N'Motivo.', @account, SYSUTCDATETIME())
                """, new { withdrawn, center = setup.Admin.CenterId.Value, resident = setup.ResidentId.Value, account = setup.Admin.AccountId.Value });
        }

        var home = await ReadAsync(setup);

        var summary = Assert.IsType<FamilyPortalSummary>(home.Summary);
        Assert.Equal(FamilyCommunicationSummaryFrequency.Semanal, summary.Frequency);
        Assert.Equal((start, end), (summary.PeriodStart, summary.PeriodEnd));
        // Del más antiguo al más reciente, con el texto vigente.
        Assert.Equal([first, second, atTheCut], summary.Items.Select(i => i.Id));
        Assert.Equal(["Primero del periodo.", "Relevante del periodo.", "Justo en el corte."], summary.Items.Select(i => i.Text));
        Assert.Equal(FamilyCommunicationType.Relevante, summary.Items[1].Type);
        // Se suma a los comunicados sueltos: todos siguen en la lista (el retirado, como «ya no está disponible»).
        Assert.All(new[] { first, second, atTheCut, withdrawn, atTheStart, before }, id => Assert.Contains(home.Communications, c => c.Id == id));
        Assert.Equal(FamilyPortalCommunicationKind.Withdrawn, home.Communications.Single(c => c.Id == withdrawn).Kind);
    }

    [Fact]
    public async Task SiEnElPeriodoNoSePublicoNada_NoHayResumen_AunqueHayaComunicadosDeOtrosPeriodos()
    {
        var setup = await SetupAsync("Residente Resumen Vacio");
        var (start, _) = Period(FamilyCommunicationSummaryFrequency.Semanal);
        Assert.Null((await ReadAsync(setup)).Summary);

        // Uno de la semana anterior: sigue visible suelto, pero no es del último periodo cerrado.
        var before = await PublishedAtAsync(setup, "De la semana anterior.", start.AddDays(-3));
        var home = await ReadAsync(setup);

        Assert.Null(home.Summary);
        Assert.Contains(home.Communications, c => c.Id == before);
    }

    [Fact]
    public async Task ElResumenEsNuevoParaLaCuentaHastaQueLoAbre()
    {
        var setup = await SetupAsync("Residente Resumen Nuevo");
        var (start, end) = Period(FamilyCommunicationSummaryFrequency.Semanal);
        await PublishedAtAsync(setup, "Del periodo.", start.AddHours(5));

        var first = await ReadAsync(setup);
        var second = await ReadAsync(setup);

        Assert.True(Assert.IsType<FamilyPortalSummary>(first.Summary).IsNewSince(first.PreviousVisitAt));
        Assert.False(Assert.IsType<FamilyPortalSummary>(second.Summary).IsNewSince(second.PreviousVisitAt));
        Assert.Equal(end, second.Summary!.PeriodEnd);
    }

    [Fact]
    public async Task ConLaFrecuenciaDiaria_ElResumenSoloRecogeLoDelUltimoDia_ElDeOtroCentroNoCambia_YAlVolverASemanalVuelveElDeLaSemana()
    {
        var setup = await SetupAsync("Residente Resumen Diario");
        var other = await SetupAsync("Residente Resumen Otro Centro");
        var (dailyStart, dailyEnd) = Period(FamilyCommunicationSummaryFrequency.Diaria);
        var (weekStart, weekEnd) = Period(FamilyCommunicationSummaryFrequency.Semanal);
        var yesterday = await PublishedAtAsync(setup, "De ayer por la tarde.", dailyStart.AddHours(-1));
        var today = await PublishedAtAsync(setup, "Del último día.", dailyEnd.AddHours(-1));
        var ofTheWeek = await PublishedAtAsync(setup, "Del principio de la semana.", weekStart.AddHours(1));
        await PublishedAtAsync(other, "De otro centro, del principio de la semana.", weekStart.AddHours(1));

        await SetFrequencyAsync(setup.Admin, 0, FamilyCommunicationSummaryFrequency.Diaria);
        var daily = await ReadAsync(setup);
        var otherHome = await ReadAsync(other);

        var dailySummary = Assert.IsType<FamilyPortalSummary>(daily.Summary);
        Assert.Equal(FamilyCommunicationSummaryFrequency.Diaria, dailySummary.Frequency);
        Assert.Equal((dailyStart, dailyEnd), (dailySummary.PeriodStart, dailySummary.PeriodEnd));
        Assert.Equal([today], dailySummary.Items.Select(i => i.Id));
        Assert.Contains(daily.Communications, c => c.Id == yesterday);
        // El centro vecino no cambió su frecuencia: sigue en semanal.
        Assert.Equal(FamilyCommunicationSummaryFrequency.Semanal, Assert.IsType<FamilyPortalSummary>(otherHome.Summary).Frequency);

        await SetFrequencyAsync(setup.Admin, 1, FamilyCommunicationSummaryFrequency.Semanal);
        var weekly = Assert.IsType<FamilyPortalSummary>((await ReadAsync(setup)).Summary);

        // Con el corte semanal vuelve el periodo de la semana: lo del principio de la semana entra y nada queda fuera de sus límites.
        Assert.Equal(FamilyCommunicationSummaryFrequency.Semanal, weekly.Frequency);
        Assert.Contains(weekly.Items, i => i.Id == ofTheWeek);
        Assert.All(weekly.Items, i => Assert.InRange(i.PublishedAt, weekStart.AddTicks(1), weekEnd));
    }

    [Fact]
    public async Task LaFrecuenciaSeGuardaPorCentro_ConHistorialYVersion_YVolverALaDeCJBorraLaFila()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var service = Schedule(admin.ExternalSubject);

        var initial = (await service.ReadAsync(admin.ProfileScopeId, admin.CenterId)).Value!;
        await SetFrequencyAsync(admin, 0, FamilyCommunicationSummaryFrequency.Diaria);
        var afterSave = (await service.ReadAsync(admin.ProfileScopeId, admin.CenterId)).Value!;
        var row = await CountAsync(
            "SELECT COUNT(*) FROM dbo.comunicados_horario_centro WHERE centro_id = @c AND resumen_frecuencia_codigo = 'DIARIA'", new { c = admin.CenterId.Value });
        var byNurse = await Schedule(nurse.ExternalSubject).SaveAsync(new SaveFamilyCommunicationScheduleCommand(
            nurse.ProfileScopeId, nurse.CenterId, 1, new TimeOnly(19, 0), 60, FamilyCommunicationSummaryFrequency.Semanal));
        await SetFrequencyAsync(admin, 1, FamilyCommunicationSummaryFrequency.Semanal);
        var afterReset = (await service.ReadAsync(admin.ProfileScopeId, admin.CenterId)).Value!;

        Assert.Equal(FamilyCommunicationSummaryFrequency.Semanal, initial.Settings.SummaryFrequency);
        Assert.Equal(FamilyCommunicationSummaryFrequency.Diaria, afterSave.Settings.SummaryFrequency);
        Assert.Equal(1, row);
        var change = Assert.Single(afterSave.History);
        Assert.Equal((FamilyCommunicationSummaryFrequency.Semanal, FamilyCommunicationSummaryFrequency.Diaria), (change.Previous.SummaryFrequency, change.New.SummaryFrequency));
        Assert.False(byNurse.Ok);
        // Volver a la frecuencia de partida con la hora y el margen de CJ borra la fila: sin fila vale el horario de CJ.
        Assert.Equal(FamilyCommunicationScheduleSettings.Default, afterReset.Settings);
        Assert.Equal(2, afterReset.History.Count);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.comunicados_horario_centro WHERE centro_id = @c", new { c = admin.CenterId.Value }));
    }
}

file sealed class FixedResumenSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
