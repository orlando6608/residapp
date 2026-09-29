using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record ListMedicalFollowUpsCommand(Guid AmbitoPerfilId, CenterId CentroId);

/// <summary>MED-11: bandeja compartida de seguimientos médicos abiertos del ámbito, vencidos incluidos (nunca
/// se ocultan ni se cierran solos).</summary>
public sealed class ListMedicalFollowUps(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<IReadOnlyList<MedicalFollowUpSummary>>> ExecuteAsync(
        ListMedicalFollowUpsCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            await MedicinaScope.RequireAsync(scopes, session, directory, command.AmbitoPerfilId, command.CentroId, null, ct);
            return await directory.ListMedicalFollowUpsAsync(command.AmbitoPerfilId, command.CentroId, ct);
        });
}
