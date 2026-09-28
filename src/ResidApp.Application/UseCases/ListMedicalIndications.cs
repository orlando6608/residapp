using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record ListMedicalIndicationsCommand(Guid AmbitoPerfilId, CenterId CentroId);

/// <summary>MED-08/MED-09: indicaciones emitidas en los eventos que siguen con indicación pendiente, con su
/// lectura, realización e incidencias. Las no leídas, vencidas o con incidencia siguen visibles.</summary>
public sealed class ListMedicalIndications(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<IReadOnlyList<MedicalIndicationListItem>>> ExecuteAsync(
        ListMedicalIndicationsCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync<IReadOnlyList<MedicalIndicationListItem>>(async () =>
        {
            await MedicinaScope.RequireAsync(scopes, session, directory, command.AmbitoPerfilId, command.CentroId, null, ct);
            return (await directory.ListIndicationsAsync(command.AmbitoPerfilId, command.CentroId, ct))
                .Where(i => i.EventStatus == ClinicalEventStatus.ConIndicacionPendiente)
                .ToList();
        });
}
