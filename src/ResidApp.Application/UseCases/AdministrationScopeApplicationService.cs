using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Las unidades del centro vistas por una Administración y si esta es la principal.</summary>
public sealed record AdministrationScopeView(bool IsPrincipal, IReadOnlyList<CenterUnitScope> Units);

public sealed record AddUnitToOwnScopeCommand(Guid AmbitoPerfilId, CenterId CentroId, UnitId UnidadId);

public sealed record SetAdministrationPrincipalCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid AmbitoDestinoId, bool Principal);

/// <summary>
/// Administración «principal» del centro (CJ, 2026-10-07; script 0047). Solo la principal ve todas las unidades del centro y se añade las que
/// no tiene, y marca o desmarca a otras Administraciones como principales. Una Administración que no es principal solo ve que no lo es, sin
/// las unidades de las que no es responsable. Cada ampliación y cada cambio de marca quedan en la auditoría con quién y cuándo.
/// </summary>
public sealed class AdministrationScopeApplicationService(
    AdministrationAccessResolver administrationAccess, IAdministrationScopeDirectory directory, IAdministrationScopeRepository repository)
{
    public Task<ApplicationResult<AdministrationScopeView>> ReadAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await administrationAccess.ResolveAsync(profileScopeId, centerId, ct);
            return await ReadViewAsync(access, ct);
        });

    public Task<ApplicationResult<bool>> AddUnitAsync(AddUnitToOwnScopeCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await administrationAccess.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await repository.AddUnitToOwnScopeAsync(access, command.UnidadId, ct);
            return true;
        });

    public Task<ApplicationResult<bool>> SetPrincipalAsync(SetAdministrationPrincipalCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await administrationAccess.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await repository.SetPrincipalAsync(access, command.AmbitoDestinoId, command.Principal, ct);
            return true;
        });

    private async Task<AdministrationScopeView> ReadViewAsync(AccountAdministrationAccess access, CancellationToken ct)
    {
        var principal = await directory.IsPrincipalAsync(access, ct);
        return new AdministrationScopeView(principal, principal ? await directory.ListCenterUnitsAsync(access, ct) : []);
    }
}
