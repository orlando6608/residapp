using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>
/// El inicio de Administración (ADM-01, historia 10): lo que hay que atender, solo con cifras. Hoy es el día natural de la zona del centro
/// (<see cref="TimeZoneInfo.Local"/>, verificada al arrancar); los límites del día y de los próximos días salen de ahí.
/// </summary>
public sealed class AdministrationOverviewApplicationService(AdministrationAccessResolver administrationAccess, IAdministrationOverviewReader reader)
{
    public Task<ApplicationResult<AdministrationOverview>> ReadAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await administrationAccess.ResolveAsync(profileScopeId, centerId, ct);
            return await reader.ReadAsync(access, PeriodAt(DateTimeOffset.UtcNow, TimeZoneInfo.Local), ct);
        });

    /// <summary>El día natural de <paramref name="now"/> en <paramref name="zone"/> y los límites UTC de hoy y de los próximos días.</summary>
    public static AdministrationOverviewPeriod PeriodAt(DateTimeOffset now, TimeZoneInfo zone)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var (todayStart, tomorrowStart) = SupervisionIndicatorRules.UtcBounds(today, today, zone);
        var (_, comingEnd) = SupervisionIndicatorRules.UtcBounds(today.AddDays(1), today.AddDays(AdministrationOverviewRules.ComingAppointmentDays), zone);
        return new AdministrationOverviewPeriod(
            today, now, new DateTimeOffset(todayStart, TimeSpan.Zero), new DateTimeOffset(tomorrowStart, TimeSpan.Zero),
            new DateTimeOffset(comingEnd, TimeSpan.Zero));
    }
}

/// <summary>De los comunicados de la ventana de <see cref="FamilyCommunicationPublicationApplicationService.WindowDays"/> días: cuántos están programados
/// y todavía no han salido, y cuántos salieron en los últimos <see cref="RecentDays"/> días. Un comunicado retirado no cuenta como publicado.</summary>
public sealed record AdministrationCommunicationsSummary(int Scheduled, int PublishedRecently)
{
    public const int RecentDays = 7;

    public static AdministrationCommunicationsSummary From(IEnumerable<FamilyCommunicationStatusView> communications, DateTimeOffset now)
    {
        var list = communications.ToList();
        var recentFrom = now.AddDays(-RecentDays);
        return new AdministrationCommunicationsSummary(
            list.Count(c => !c.Published),
            list.Count(c => c.Published && c.Communication.Withdrawal is null
                && (c.Communication.PublishedEarlyAt is { } early && early < c.ScheduledAt ? early : c.ScheduledAt) >= recentFrom));
    }
}
