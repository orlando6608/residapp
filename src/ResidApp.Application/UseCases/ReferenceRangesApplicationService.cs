using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record ReadReferenceRangesCommand(Guid AmbitoPerfilId, CenterId CentroId);

public sealed record ReferenceRangeCommandItem(VitalSignCode Constante, decimal? Minimo, decimal? Maximo);

/// <summary>Una constante con Minimo y Maximo a null se queda sin rango.</summary>
public sealed record SaveReferenceRangesCommand(
    Guid AmbitoPerfilId, CenterId CentroId, int Version, IReadOnlyList<ReferenceRangeCommandItem> Rangos);

/// <summary>
/// Pantalla de rangos de referencia de constantes del centro. Solo Dirección / Coordinación
/// Clínica (no Medicina, CJ 2026-10-07) con el permiso REFERENCE_RANGES_MANAGE, que comprueba el repositorio en la misma transacción;
/// nunca Administración (wireframe ADM-29). Los rangos solo alimentan un aviso visual en la valoración de
/// Enfermería: no deciden nada clínicamente.
/// </summary>
public sealed class ReferenceRangesApplicationService(
    IProfileScopeDirectoryProvider scopes, ISessionIdentityProvider session, IReferenceRangeRepository repository)
{
    public Task<ApplicationResult<ReferenceRangesView>> ReadAsync(ReadReferenceRangesCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await repository.ReadAsync(await ResolveAccessAsync(command.AmbitoPerfilId, command.CentroId, ct), ct));

    public Task<ApplicationResult<int>> SaveAsync(SaveReferenceRangesCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            if (command.Rangos.Select(r => r.Constante).Distinct().Count() != command.Rangos.Count)
            {
                throw new DomainValidationException("REFERENCE_RANGE_INVALID");
            }
            var ranges = command.Rangos
                .Where(r => r.Minimo is not null || r.Maximo is not null)
                .Select(r => new VitalSignRange(r.Constante, r.Minimo, r.Maximo))
                .ToList();
            ranges.ForEach(VitalSignReferenceRanges.Validate);

            var access = await ResolveAccessAsync(command.AmbitoPerfilId, command.CentroId, ct);
            return await repository.SaveAsync(new SaveReferenceRangesInput(access, command.Version, ranges), ct);
        });

    private async Task<ReferenceRangesAccess> ResolveAccessAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
        var scope = (await scopes.ListActiveAsync(identity.ExternalSubject, ct))
            .FirstOrDefault(s => s.ProfileScopeId == profileScopeId && s.CenterId == centerId);
        if (scope is null || scope.Profile is not SystemProfile.DireccionClinica)
        {
            throw new AccessDeniedException();
        }
        return new ReferenceRangesAccess(scope.ProfileScopeId, scope.AccountId, scope.Profile, centerId);
    }
}
