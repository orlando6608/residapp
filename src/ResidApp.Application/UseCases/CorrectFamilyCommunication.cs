using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record CorrectFamilyCommunicationCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid ComunicadoId, int VersionEsperada, FamilyCommunicationType? Tipo, string? Texto);

/// <summary>
/// Corregir el texto y el tipo de un comunicado a la familia hasta que se publique (CJ, 2026-10-07 y 2026-10-10; script 0049). Lo hace Enfermería
/// o Medicina del ámbito del evento, sea quien sea la persona que lo preparó (no hay propiedad permanente, como en los seguimientos). Llegada
/// su hora de publicación, o publicado antes por Administración o Dirección, ya no se puede corregir. Cada corrección queda en el historial
/// con su autoría real.
/// </summary>
public sealed class CorrectFamilyCommunication(
    IProfileScopeDirectoryProvider scopes, ISessionIdentityProvider session, IFamilyCommunicationCorrector corrector)
{
    public Task<ApplicationResult<bool>> ExecuteAsync(CorrectFamilyCommunicationCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            // Mismas reglas del texto que al prepararlo: tipo obligatorio y un texto comprensible de hasta 2000 caracteres.
            var content = new FamilyCommunicationChoice(FamilyCommunicationDecision.Preparar, command.Tipo, command.Texto);
            var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
            var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
            var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == command.AmbitoPerfilId && s.CenterId == command.CentroId);
            if (scope is null || scope.Profile is not (SystemProfile.Enfermeria or SystemProfile.Medicina))
            {
                throw new AccessDeniedException();
            }

            await corrector.CorrectAsync(new FamilyCommunicationCorrectionInput(
                scope.AccountId, scope.ProfileScopeId, scope.CenterId, scope.Profile, command.ComunicadoId, command.VersionEsperada,
                content.Type!.Value, content.Text!), ct);
            return true;
        });
}
