using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record RegisterClinicalEventCommand(
    Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId,
    string Observacion, DailyChangeClassification Clasificacion, string? DatosClinicosPertinentes, Guid OperacionId,
    SystemProfile Perfil = SystemProfile.Enfermeria);

/// <summary>
/// Traduce ENF-16 "Registrar un evento observado por Enfermería", en su alcance mínimo: solo registrar y
/// guardar. No pasa por RequestAuthorizationContext (pensado para las 7 acciones de ResidentBaselinePolicy,
/// ninguna de las cuales es esta) ni exige un permiso propio: mismo criterio que RegisterDailyClosure
/// (Auxiliar) — "puedo registrar sobre este residente" es exactamente "aparece en mi lista de residentes
/// del ámbito" (IEnfermeriaResidentDirectory), sin permiso adicional. Medicina registra el suyo (MED-18)
/// pidiendo su perfil, con el mismo criterio; su evento nace ya en valoración médica.
/// </summary>
public sealed class RegisterClinicalEvent(
    IProfileScopeDirectoryProvider scopes, IEnfermeriaResidentDirectory directory,
    ISessionIdentityProvider session, IClinicalEventRepository repository)
{
    public Task<ApplicationResult<ClinicalEventResult>> ExecuteAsync(
        RegisterClinicalEventCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(command.Observacion))
            {
                throw new DomainValidationException("CLINICAL_EVENT_OBSERVATION_REQUIRED");
            }

            var identity = await session.GetVerifiedIdentityAsync(ct);
            if (identity is null)
            {
                throw new AccessDeniedException();
            }

            var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
            var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == command.AmbitoPerfilId && s.CenterId == command.CentroId);
            if (scope is null || scope.Profile != command.Perfil
                || command.Perfil is not (SystemProfile.Enfermeria or SystemProfile.Medicina))
            {
                throw new AccessDeniedException();
            }

            var resident = (await directory.ListAsync(command.AmbitoPerfilId, command.CentroId, ct))
                .FirstOrDefault(r => r.ResidentId == command.ResidenteId);
            if (resident is null)
            {
                throw new AccessDeniedException();
            }

            var input = new RegisterClinicalEventInput(
                scope.AccountId, command.CentroId, resident.UnitId, command.ResidenteId,
                command.Observacion, command.Clasificacion, command.DatosClinicosPertinentes, command.OperacionId, command.Perfil);
            return await repository.RegisterAsync(input, ct);
        });
}
