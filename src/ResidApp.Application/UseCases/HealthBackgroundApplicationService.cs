using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record SaveAllergiesCommand(
    Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, int VersionVista, HealthBackgroundReason? Motivo, AllergyRecordContent Contenido);

public sealed record SaveConditionsCommand(
    Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, int VersionVista, HealthBackgroundReason? Motivo, ConditionRecordContent Contenido);

/// <summary>Lo que necesita el bloque de antecedentes de una ficha: cada apartado vigente (null si está pendiente) y si quien mira puede cambiarlos. UnreliableVersion es el
/// número de la versión vigente de las alergias cuando no se puede dar por fiable (Allergies es entonces null): la pantalla lo avisa y el informe lo dice, y se puede guardar otra
/// que la corrija. UnreliableConditionsVersion es lo mismo para enfermedades e intervenciones; un apartado ilegible no oculta al otro.</summary>
public sealed record HealthBackgroundView(
    AllergyRecord? Allergies, bool CanEdit, int? UnreliableVersion = null, ConditionRecord? Conditions = null, int? UnreliableConditionsVersion = null);

/// <summary>
/// Ficha de antecedentes de salud, fase 1 (docs/producto/ficha-antecedentes-salud-diseno.md, decisiones 9 y 10): la ven Enfermería y Medicina en su ámbito y
/// la cambian con el permiso HEALTH_BACKGROUND_EDIT que da Administración a su perfil. No la ven Auxiliar, Administración, Dirección Clínica ni Plataforma, ni la
/// familia. Como la foto, comprueba el ámbito con la evidencia de autorización (AuthorizationTarget.Read), la misma regla de las listas de cada perfil.
/// </summary>
public sealed class HealthBackgroundApplicationService(
    IAuthorizationEvidenceProvider evidenceProvider, ISessionIdentityProvider session, IHealthBackgroundRepository repository)
{
    private static readonly SystemProfile[] Profiles = [SystemProfile.Enfermeria, SystemProfile.Medicina];

    public Task<ApplicationResult<HealthBackgroundView>> ReadAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (target, canEdit) = await RequireAsync(profileScopeId, centerId, residentId, ct);
            AllergyRecord? allergies = null;
            int? unreliableAllergies = null;
            try
            {
                allergies = await repository.ReadAllergiesAsync(target.CenterId, target.ResidentId, ct);
            }
            catch (HealthBackgroundInconsistentException inconsistent)
            {
                unreliableAllergies = inconsistent.Version;
            }

            ConditionRecord? conditions = null;
            int? unreliableConditions = null;
            try
            {
                conditions = await repository.ReadConditionsAsync(target.CenterId, target.ResidentId, ct);
            }
            catch (HealthBackgroundInconsistentException inconsistent)
            {
                unreliableConditions = inconsistent.Version;
            }
            return new HealthBackgroundView(allergies, canEdit, unreliableAllergies, conditions, unreliableConditions);
        });

    /// <summary>Guarda una versión nueva y devuelve su número. Sin versión vista (0) es el registro inicial; con ella, el motivo es obligatorio y distinto.</summary>
    public Task<ApplicationResult<int>> SaveAllergiesAsync(SaveAllergiesCommand command, CancellationToken ct = default) =>
        SaveAsync(command.AmbitoPerfilId, command.CentroId, command.ResidenteId, command.VersionVista, command.Motivo,
            (target, reason, token) => repository.SaveAllergiesAsync(target, command.VersionVista, reason, command.Contenido, token), ct);

    /// <summary>Lo mismo para el apartado de enfermedades e intervenciones.</summary>
    public Task<ApplicationResult<int>> SaveConditionsAsync(SaveConditionsCommand command, CancellationToken ct = default) =>
        SaveAsync(command.AmbitoPerfilId, command.CentroId, command.ResidenteId, command.VersionVista, command.Motivo,
            (target, reason, token) => repository.SaveConditionsAsync(target, command.VersionVista, reason, command.Contenido, token), ct);

    private Task<ApplicationResult<int>> SaveAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, int seenVersion, HealthBackgroundReason? chosenReason,
        Func<HealthBackgroundTarget, HealthBackgroundReason, CancellationToken, Task<int>> save, CancellationToken ct) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var reason = seenVersion == 0
                ? HealthBackgroundReason.RegistroInicial
                : chosenReason is { } chosen && chosen != HealthBackgroundReason.RegistroInicial && Enum.IsDefined(chosen)
                    ? chosen
                    : throw new DomainValidationException(HealthBackground.InvalidCode);
            if (seenVersion < 0)
            {
                throw new DomainValidationException(HealthBackground.InvalidCode);
            }

            var (target, canEdit) = await RequireAsync(profileScopeId, centerId, residentId, ct);
            if (!canEdit)
            {
                throw new AccessDeniedException();
            }
            return await save(target, reason, ct);
        });

    private async Task<(HealthBackgroundTarget Target, bool CanEdit)> RequireAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct);
        if (identity is null || string.IsNullOrWhiteSpace(identity.ExternalSubject))
        {
            throw new AccessDeniedException();
        }
        var evidence = await evidenceProvider.LoadEvidenceAsync(
            identity.ExternalSubject, new AuthorizationSelection(profileScopeId, centerId), new AuthorizationTarget.Read(residentId), ct);
        if (evidence?.ResidentId is null || !Profiles.Contains(evidence.Profile))
        {
            throw new AccessDeniedException();
        }
        var canEdit = evidence.Permissions.Any(p => p.Code == ProfilePermissions.HealthBackgroundEdit);
        return (new HealthBackgroundTarget(
            evidence.AccountId, profileScopeId, evidence.Profile, evidence.CenterId, evidence.UnitId, evidence.ResidentId.Value), canEdit);
    }
}
