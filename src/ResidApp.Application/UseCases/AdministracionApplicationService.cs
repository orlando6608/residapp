using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Families;
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

/// <summary>ADM-09 (0022): añadir un familiar al residente. OperacionId nace con el formulario y es el identificador del
/// familiar, así que un reenvío no lo duplica.</summary>
public sealed record AddFamilyMemberCommand(
    Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, Guid OperacionId, string? NombreVisible, string? Relacion,
    string? Telefono, string? Correo);

public sealed record UpdateFamilyMemberCommand(
    Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, Guid VinculoId, string? NombreVisible, string? Relacion,
    string? Telefono, string? Correo);

/// <summary>ADM-10/ADM-11 (0022): CambiosEsperados es cuántos cambios tenía la autorización al abrir la pantalla.</summary>
public sealed record ChangeFamilyAuthorizationCommand(
    Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, Guid VinculoId, FamilyAuthorizationChange Cambio,
    DateOnly? ValidaHasta, string? Motivo, int CambiosEsperados);

/// <summary>ADM-08 (0022): VinculoId null quita el contacto urgente; DesignacionesEsperadas es cuántas designaciones
/// tenía el residente al abrir la pantalla.</summary>
public sealed record DesignateEmergencyContactCommand(
    Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, Guid? VinculoId, int DesignacionesEsperadas);

/// <summary>
/// Fachada del vertical Administración. Bloque 1 (historia 1): lista de residentes (ADM-02), ficha administrativa con
/// historial de ubicación (ADM-03, RES-04) y corrección de identidad (script 0021). Bloque 2 (historia 3, script 0022):
/// familiares, autorizaciones y contacto urgente, que se autorizan igual que la corrección. Nunca entrega basal, Barthel ni
/// contenido clínico. La lectura exige un ámbito activo de Administración de la cuenta, y el directorio aplica la regla
/// de ámbito en la consulta. La corrección pasa por RequestAuthorizationContextResolver
/// (ResidentIdentityUpdate, que la política reserva a Administración).
/// </summary>
public sealed class AdministracionApplicationService(
    IProfileScopeDirectoryProvider scopes, IAdministracionResidentDirectory directory, ISessionIdentityProvider session,
    IAuthorizationEvidenceProvider evidenceProvider, IResidentIdentityRepository identities, IResidentFamilyRepository families)
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

    /// <summary>Devuelve el vínculo creado.</summary>
    public Task<ApplicationResult<Guid>> AddFamilyMemberAsync(AddFamilyMemberCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var target = await ResolveResidentAsync(command.AmbitoPerfilId, command.CentroId, command.ResidenteId, ct);
            var data = FamilyMember.Validate(command.NombreVisible, command.Relacion, command.Telefono, command.Correo);
            return await families.AddAsync(target, command.OperacionId, data, ct);
        });

    public Task<ApplicationResult<bool>> UpdateFamilyMemberAsync(UpdateFamilyMemberCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var target = await ResolveResidentAsync(command.AmbitoPerfilId, command.CentroId, command.ResidenteId, ct);
            var data = FamilyMember.Validate(command.NombreVisible, command.Relacion, command.Telefono, command.Correo);
            await families.UpdateAsync(target, command.VinculoId, data, ct);
            return true;
        });

    /// <summary>Devuelve cuántos cambios tiene ya la autorización.</summary>
    public Task<ApplicationResult<int>> ChangeFamilyAuthorizationAsync(
        ChangeFamilyAuthorizationCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var target = await ResolveResidentAsync(command.AmbitoPerfilId, command.CentroId, command.ResidenteId, ct);
            return await families.ChangeAuthorizationAsync(
                target, command.VinculoId, command.Cambio, command.ValidaHasta, command.Motivo, command.CambiosEsperados,
                DateOnly.FromDateTime(DateTime.Today), ct);
        });

    /// <summary>Devuelve cuántas designaciones tiene ya el residente.</summary>
    public Task<ApplicationResult<int>> DesignateEmergencyContactAsync(
        DesignateEmergencyContactCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var target = await ResolveResidentAsync(command.AmbitoPerfilId, command.CentroId, command.ResidenteId, ct);
            return await families.DesignateEmergencyContactAsync(target, command.VinculoId, command.DesignacionesEsperadas, ct);
        });

    private async Task<AdministrativeResidentTarget> ResolveResidentAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct)
    {
        var context = await RequestAuthorizationContextResolver.ResolveAsync(
            evidenceProvider, session, new AuthorizationSelection(profileScopeId, centerId),
            new AuthorizationTarget.IdentityUpdate(residentId), ct: ct);
        return RequestAuthorizationContextResolver.RequireResidentAdministration(context);
    }

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
