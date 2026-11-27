using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>
/// El cuadro de mando de Administración. Cada bloque se lee por separado: si uno falla, los demás se siguen mostrando. Cada lectura vuelve a autorizar.
/// </summary>
public sealed class AdministrationDashboardApplicationService(AdministrationAccessResolver administrationAccess, IAdministrationDashboardReader reader)
{
    public Task<ApplicationResult<DashboardOccupancy>> ReadOccupancyAsync(
        Guid profileScopeId, CenterId centerId, DashboardPeriod period, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await administrationAccess.ResolveAsync(profileScopeId, centerId, ct);
            return await reader.ReadOccupancyAsync(access, CenterTime.StartOfDayUtc(period.From, period.Zone), ct);
        });

    public Task<ApplicationResult<DashboardMovements>> ReadMovementsAsync(
        Guid profileScopeId, CenterId centerId, DashboardPeriod period, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await administrationAccess.ResolveAsync(profileScopeId, centerId, ct);
            var facts = await reader.ReadMovementsAsync(access, period.PreviousFrom, CenterTime.StartOfDayUtc(period.PreviousFrom, period.Zone), ct);
            return DashboardRules.Movements(facts, period);
        });

    public Task<ApplicationResult<IReadOnlyList<DashboardAppointmentBucket>>> ReadAppointmentsAsync(
        Guid profileScopeId, CenterId centerId, DashboardPeriod period, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await administrationAccess.ResolveAsync(profileScopeId, centerId, ct);
            var (from, to) = SupervisionIndicatorRules.UtcBounds(period.From, period.Today, period.Zone);
            return DashboardRules.Appointments(await reader.ReadAppointmentsAsync(access, from, to, ct), period);
        });

    public Task<ApplicationResult<IReadOnlyList<DashboardAuthorizationBucket>>> ReadAuthorizationsAsync(
        Guid profileScopeId, CenterId centerId, DashboardPeriod period, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await administrationAccess.ResolveAsync(profileScopeId, centerId, ct);
            return DashboardRules.Authorizations(await reader.ReadAuthorizationsAsync(access, ct), period);
        });
}
