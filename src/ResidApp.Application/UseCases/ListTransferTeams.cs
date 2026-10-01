using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record ListTransferTeamsQuery(Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId);

/// <summary>Los equipos activos de la unidad del evento, para transferir un seguimiento (ENF-09 y su equivalente de Medicina). Lo usan
/// los dos verticales; cada uno exige un ámbito activo propio.</summary>
public sealed class ListTransferTeams(
    IProfileScopeDirectoryProvider scopes, ISessionIdentityProvider session, ITransferTeamDirectory teams)
{
    public Task<ApplicationResult<IReadOnlyList<TransferTeam>>> ExecuteAsync(
        ListTransferTeamsQuery query, SystemProfile profile, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
            var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
            var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == query.AmbitoPerfilId && s.CenterId == query.CentroId);
            if (scope is null || scope.Profile != profile)
            {
                throw new AccessDeniedException();
            }

            return await teams.ListAsync(query.AmbitoPerfilId, query.CentroId, query.EventoId, ct);
        });
}
