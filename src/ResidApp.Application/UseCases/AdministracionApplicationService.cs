using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record AdministracionQuery(Guid AmbitoPerfilId, CenterId CentroId);

public sealed record FindAdministrativeResidentQuery(Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId);

/// <summary>ADM-03: la identidad tal como llega del formulario; ExpectedCorrections es cuántas correcciones tenía el
/// residente al abrirlo.</summary>
public sealed record CorrectResidentIdentityCommand(
    Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, string? NombreVisible, DateOnly FechaNacimiento,
    DocumentedSexCode SexoDocumentado, string? Motivo, int ExpectedCorrections);

/// <summary>
/// Fachada del vertical Administración, bloque 1 (historia 1): lista de residentes (ADM-02), ficha administrativa con
/// historial de ubicación (ADM-03, RES-04) y corrección de identidad (script 0021). Nunca entrega basal, Barthel ni
/// contenido clínico. La lectura exige un ámbito activo de Administración de la cuenta, y el directorio aplica la regla
/// de ámbito en la consulta. La corrección pasa por RequestAuthorizationContextResolver
/// (ResidentIdentityUpdate, que la política reserva a Administración).
/// </summary>
public sealed class AdministracionApplicationService(
    IProfileScopeDirectoryProvider scopes, IAdministracionResidentDirectory directory, ISessionIdentityProvider session,
    IAuthorizationEvidenceProvider evidenceProvider, IResidentIdentityRepository identities)
{
    public Task<ApplicationResult<IReadOnlyList<AdministrativeResidentSummary>>> ListResidentsAsync(
        AdministracionQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            await EnsureAdministrationScopeAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await directory.ListAsync(query.AmbitoPerfilId, query.CentroId, ct);
        });

    public Task<ApplicationResult<AdministrativeResidentDetail>> FindResidentAsync(
        FindAdministrativeResidentQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            await EnsureAdministrationScopeAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await directory.FindAsync(query.AmbitoPerfilId, query.CentroId, query.ResidenteId, ct)
                ?? throw new AccessDeniedException();
        });

    /// <summary>Devuelve cuántas correcciones tiene ya el residente.</summary>
    public Task<ApplicationResult<int>> CorrectIdentityAsync(CorrectResidentIdentityCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var context = await RequestAuthorizationContextResolver.ResolveAsync(
                evidenceProvider, session, new AuthorizationSelection(command.AmbitoPerfilId, command.CentroId),
                new AuthorizationTarget.IdentityUpdate(command.ResidenteId), ct: ct);
            var (identity, reason) = ResidentIdentityCorrection.Validate(
                command.NombreVisible, command.FechaNacimiento, command.SexoDocumentado, command.Motivo,
                DateOnly.FromDateTime(DateTime.Today));
            return await RequestAuthorizationContextResolver.ExecuteResidentIdentityUpdateAsync(
                context, identities, new ResidentIdentityCorrectionPayload(identity, reason, command.ExpectedCorrections), ct);
        });

    private async Task EnsureAdministrationScopeAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
        var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
        var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == profileScopeId && s.CenterId == centerId);
        if (scope is null || scope.Profile != SystemProfile.Administracion)
        {
            throw new AccessDeniedException();
        }
    }
}
