using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Común a Enfermería (RecordUrgentProtocolEntry) y Medicina (RecordMedicalUrgentProtocolEntry).
/// Texto es la actuación, la evolución o la nota del contacto; Servicio y ContactadoEn solo del contacto.</summary>
public sealed record RecordUrgentProtocolEntryCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId, int Revision, UrgentProtocolEntryType Tipo,
    string? Texto = null, string? Servicio = null, DateTimeOffset? ContactadoEn = null)
{
    internal UrgentProtocolEntry ToEntry() => Tipo switch
    {
        UrgentProtocolEntryType.Actuacion => UrgentProtocolEntry.Action(Texto),
        UrgentProtocolEntryType.Evolucion => UrgentProtocolEntry.Evolution(Texto),
        UrgentProtocolEntryType.Contacto => UrgentProtocolEntry.Contact(Servicio, ContactadoEn, Texto, DateTimeOffset.UtcNow),
        _ => throw new DomainValidationException("URGENT_PROTOCOL_ENTRY_INVALID"),
    };
}

/// <summary>ENF-11: registrar una actuación, la evolución o un contacto con un servicio (DER-04) en el
/// protocolo urgente activo de Enfermería. Cada registro conserva su autoría y avanza la revisión del evento.</summary>
public sealed class RecordUrgentProtocolEntry(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory,
    ISessionIdentityProvider session, INursingAssessmentRepository repository)
{
    public Task<ApplicationResult<int>> ExecuteAsync(RecordUrgentProtocolEntryCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var entry = command.ToEntry();

            var identity = await session.GetVerifiedIdentityAsync(ct);
            if (identity is null)
            {
                throw new AccessDeniedException();
            }

            var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
            var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == command.AmbitoPerfilId && s.CenterId == command.CentroId);
            if (scope is null || scope.Profile != SystemProfile.Enfermeria)
            {
                throw new AccessDeniedException();
            }
            if (await directory.FindAsync(command.AmbitoPerfilId, command.CentroId, command.EventoId, ct) is null)
            {
                throw new AccessDeniedException();
            }

            return await repository.RecordUrgentProtocolEntryAsync(new RecordUrgentProtocolEntryInput(
                scope.AccountId, command.CentroId, command.EventoId, command.Revision, entry), ct);
        });
}
