using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Medicina;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record ListMedicalIndicationsCommand(Guid AmbitoPerfilId, CenterId CentroId);

/// <summary>MED-08/MED-09: indicaciones emitidas en los eventos que siguen con indicación pendiente (o que
/// después pasaron a seguimiento médico o a protocolo urgente), con su
/// lectura, realización e incidencias. Las no leídas, vencidas o con incidencia siguen visibles. De un evento
/// ya cerrado por Medicina siguen las que Enfermería aún no ha resuelto y las no realizadas después del
/// cierre (una incidencia que Medicina todavía no ha visto nunca desaparece en silencio).</summary>
public sealed class ListMedicalIndications(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<IReadOnlyList<MedicalIndicationListItem>>> ExecuteAsync(
        ListMedicalIndicationsCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync<IReadOnlyList<MedicalIndicationListItem>>(async () =>
        {
            await MedicinaScope.RequireAsync(scopes, session, directory, command.AmbitoPerfilId, command.CentroId, null, ct);
            return (await directory.ListIndicationsAsync(command.AmbitoPerfilId, command.CentroId, ct))
                .Where(i => i.EventStatus is ClinicalEventStatus.ConIndicacionPendiente or ClinicalEventStatus.EnSeguimientoMedico
                        or ClinicalEventStatus.ProtocoloUrgenteMedico
                    || (i.EventStatus == ClinicalEventStatus.Cerrado
                        && (i.Indication.Status is MedicalIndicationStatus.PendienteLectura or MedicalIndicationStatus.Leida
                            || (i.Indication.Status == MedicalIndicationStatus.NoRealizada && i.Indication.ResolvedAt > i.EventClosedAt))))
                .ToList();
        });
}
