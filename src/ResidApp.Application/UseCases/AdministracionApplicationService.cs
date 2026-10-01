using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Families;
using ResidApp.Domain.Residents;
using ResidApp.Domain.Structure;
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

/// <summary>ADM-13 (0023): una cuenta del centro, vista por quien la gestiona.</summary>
public sealed record FindProfessionalAccountQuery(Guid AmbitoPerfilId, CenterId CentroId, AccountId CuentaId);

/// <summary>ADM-13 (0023): alta de una cuenta con su primer perfil. OperacionId nace con el formulario y es el id de la
/// cuenta, así que un reenvío no la duplica.</summary>
public sealed record CreateProfessionalAccountCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid OperacionId, string? Identificador, string? NombreVisible, SystemProfile Perfil,
    IReadOnlyList<Guid> Unidades);

public sealed record RenameProfessionalAccountCommand(Guid AmbitoPerfilId, CenterId CentroId, AccountId CuentaId, string? NombreVisible);

/// <summary>ADM-12: Estado es el que se quiere (Suspended para suspender, Active para reactivar).</summary>
public sealed record ChangeAccountStatusCommand(Guid AmbitoPerfilId, CenterId CentroId, AccountId CuentaId, AccountStatus Estado);

/// <summary>ADM-13: conceder un perfil más a la cuenta. OperacionId es el id del ámbito nuevo.</summary>
public sealed record GrantAccountProfileCommand(
    Guid AmbitoPerfilId, CenterId CentroId, AccountId CuentaId, Guid OperacionId, SystemProfile Perfil, IReadOnlyList<Guid> Unidades);

/// <summary>ADM-13: PerfilCuentaId es el ámbito de la cuenta gestionada (no el de quien gestiona).</summary>
public sealed record RevokeAccountProfileCommand(Guid AmbitoPerfilId, CenterId CentroId, AccountId CuentaId, Guid PerfilCuentaId);

public sealed record ChangeAccountProfileUnitCommand(
    Guid AmbitoPerfilId, CenterId CentroId, AccountId CuentaId, Guid PerfilCuentaId, UnitId UnidadId, bool Conceder);

public sealed record ChangeAccountProfileResidentCommand(
    Guid AmbitoPerfilId, CenterId CentroId, AccountId CuentaId, Guid PerfilCuentaId, ResidentId ResidenteId, bool Asignar);

/// <summary>ADM-05 (0025): alta de una unidad. OperacionId nace con el formulario y es el id de la unidad, así que un reenvío
/// no la duplica.</summary>
public sealed record CreateUnitCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid OperacionId, string? Codigo, string? Nombre);

public sealed record RenameUnitCommand(Guid AmbitoPerfilId, CenterId CentroId, UnitId UnidadId, string? Nombre);

/// <summary>ADM-05: Activa es el estado que se quiere (false para inactivar, true para reactivar).</summary>
public sealed record ChangeUnitStatusCommand(Guid AmbitoPerfilId, CenterId CentroId, UnitId UnidadId, bool Activa);

/// <summary>ADM-13 (0024): conceder (Conceder = true) o revocar un permiso del catálogo del perfil.</summary>
public sealed record ChangeAccountProfilePermissionCommand(
    Guid AmbitoPerfilId, CenterId CentroId, AccountId CuentaId, Guid PerfilCuentaId, string? Permiso, bool Conceder);

/// <summary>
/// Fachada del vertical Administración. Bloque 1 (historia 1): lista de residentes (ADM-02), ficha administrativa con
/// historial de ubicación (ADM-03, RES-04) y corrección de identidad (script 0021). Bloque 2 (historia 3, script 0022):
/// familiares, autorizaciones y contacto urgente, que se autorizan igual que la corrección. Nunca entrega basal, Barthel ni
/// contenido clínico. La lectura exige un ámbito activo de Administración de la cuenta, y el directorio aplica la regla
/// de ámbito en la consulta. La corrección pasa por RequestAuthorizationContextResolver
/// (ResidentIdentityUpdate, que la política reserva a Administración). Bloque 3 (historia 4, script 0023): cuentas
/// profesionales, sus perfiles, unidades y residentes de Auxiliar; bloque 4 (0024), sus permisos configurables. Son
/// operaciones de centro, sin residente: como los
/// rangos de referencia, se comprueba aquí el ámbito activo de Administración y el repositorio lo repite dentro de la
/// transacción.
/// </summary>
public sealed class AdministracionApplicationService(
    IProfileScopeDirectoryProvider scopes, IAdministracionResidentDirectory directory, ISessionIdentityProvider session,
    IAuthorizationEvidenceProvider evidenceProvider, IResidentIdentityRepository identities, IResidentFamilyRepository families,
    IProfessionalAccountDirectory accountDirectory, IProfessionalAccountRepository accounts, ICenterStructureDirectory structure,
    ICenterStructureRepository structureWriter)
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

    public Task<ApplicationResult<IReadOnlyList<ProfessionalAccountSummary>>> ListAccountsAsync(
        AdministracionQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await AdministrationAccessAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await accountDirectory.ListAsync(access, ct);
        });

    public Task<ApplicationResult<ProfessionalAccountDetail>> FindAccountAsync(
        FindProfessionalAccountQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await AdministrationAccessAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await accountDirectory.FindAsync(access, query.CuentaId, ct) ?? throw new AccessDeniedException();
        });

    /// <summary>Las unidades del ámbito de quien gestiona: las únicas que puede conceder o revocar.</summary>
    public Task<ApplicationResult<IReadOnlyList<ScopeUnit>>> ListAdministrationUnitsAsync(
        AdministracionQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, subject) = await AdministrationAccessAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await scopes.ListUnitsAsync(subject, access.ProfileScopeId, access.CenterId, ct);
        });

    public Task<ApplicationResult<IReadOnlyList<AssignableResident>>> ListAssignableResidentsAsync(
        FindProfessionalAccountQuery query, Guid profileScopeId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await AdministrationAccessAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await accountDirectory.ListAssignableResidentsAsync(access, profileScopeId, ct);
        });

    public Task<ApplicationResult<AccountId>> CreateAccountAsync(CreateProfessionalAccountCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await AdministrationAccessAsync(command.AmbitoPerfilId, command.CentroId, ct);
            var data = ProfessionalAccount.Validate(command.Identificador, command.NombreVisible);
            return await accounts.CreateAsync(access, command.OperacionId, data, command.Perfil, ProfileUnits(command.Perfil, command.Unidades), ct);
        });

    public Task<ApplicationResult<bool>> RenameAccountAsync(RenameProfessionalAccountCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await AdministrationAccessAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await accounts.RenameAsync(access, command.CuentaId, ProfessionalAccount.ValidateDisplayName(command.NombreVisible), ct);
            return true;
        });

    public Task<ApplicationResult<bool>> ChangeAccountStatusAsync(ChangeAccountStatusCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await AdministrationAccessAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await accounts.ChangeStatusAsync(access, command.CuentaId, command.Estado, ct);
            return true;
        });

    /// <summary>Devuelve el ámbito concedido.</summary>
    public Task<ApplicationResult<Guid>> GrantProfileAsync(GrantAccountProfileCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await AdministrationAccessAsync(command.AmbitoPerfilId, command.CentroId, ct);
            return await accounts.GrantProfileAsync(
                access, command.CuentaId, command.OperacionId, command.Perfil, ProfileUnits(command.Perfil, command.Unidades), ct);
        });

    public Task<ApplicationResult<bool>> RevokeProfileAsync(RevokeAccountProfileCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await AdministrationAccessAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await accounts.RevokeProfileAsync(access, command.CuentaId, command.PerfilCuentaId, ct);
            return true;
        });

    public Task<ApplicationResult<bool>> ChangeProfileUnitAsync(ChangeAccountProfileUnitCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await AdministrationAccessAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await (command.Conceder
                ? accounts.GrantUnitAsync(access, command.CuentaId, command.PerfilCuentaId, command.UnidadId, ct)
                : accounts.RevokeUnitAsync(access, command.CuentaId, command.PerfilCuentaId, command.UnidadId, ct));
            return true;
        });

    public Task<ApplicationResult<bool>> ChangeProfileResidentAsync(
        ChangeAccountProfileResidentCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await AdministrationAccessAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await (command.Asignar
                ? accounts.GrantResidentAsync(access, command.CuentaId, command.PerfilCuentaId, command.ResidenteId, ct)
                : accounts.RevokeResidentAsync(access, command.CuentaId, command.PerfilCuentaId, command.ResidenteId, ct));
            return true;
        });

    public Task<ApplicationResult<bool>> ChangeProfilePermissionAsync(
        ChangeAccountProfilePermissionCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await AdministrationAccessAsync(command.AmbitoPerfilId, command.CentroId, ct);
            var code = string.IsNullOrWhiteSpace(command.Permiso) ? throw new DomainValidationException("PROFILE_SCOPE_INVALID") : command.Permiso;
            await (command.Conceder
                ? accounts.GrantPermissionAsync(access, command.CuentaId, command.PerfilCuentaId, code, ct)
                : accounts.RevokePermissionAsync(access, command.CuentaId, command.PerfilCuentaId, code, ct));
            return true;
        });

    /// <summary>ADM-05: las unidades concedidas al ámbito de quien gestiona, activas e inactivas.</summary>
    public Task<ApplicationResult<IReadOnlyList<StructureUnit>>> ListStructureUnitsAsync(
        AdministracionQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await AdministrationAccessAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await structure.ListUnitsAsync(access, ct);
        });

    public Task<ApplicationResult<UnitId>> CreateUnitAsync(CreateUnitCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await AdministrationAccessAsync(command.AmbitoPerfilId, command.CentroId, ct);
            var data = CenterUnit.Validate(command.Codigo, command.Nombre);
            return await structureWriter.CreateUnitAsync(access, command.OperacionId, data, ct);
        });

    public Task<ApplicationResult<bool>> RenameUnitAsync(RenameUnitCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await AdministrationAccessAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await structureWriter.RenameUnitAsync(access, command.UnidadId, CenterUnit.ValidateName(command.Nombre), ct);
            return true;
        });

    public Task<ApplicationResult<bool>> ChangeUnitStatusAsync(ChangeUnitStatusCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await AdministrationAccessAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await structureWriter.ChangeUnitStatusAsync(access, command.UnidadId, command.Activa, ct);
            return true;
        });

    /// <summary>Un perfil concedible con al menos una unidad, sin repetir.</summary>
    private static IReadOnlyList<UnitId> ProfileUnits(SystemProfile profile, IReadOnlyList<Guid> units) =>
        !ProfessionalAccount.IsGrantable(profile) || units.Count == 0 || units.Distinct().Count() != units.Count
            ? throw new DomainValidationException("PROFILE_SCOPE_INVALID")
            : units.Select(UnitId.From).ToList();

    private async Task<AdministrativeResidentTarget> ResolveResidentAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct)
    {
        var context = await RequestAuthorizationContextResolver.ResolveAsync(
            evidenceProvider, session, new AuthorizationSelection(profileScopeId, centerId),
            new AuthorizationTarget.IdentityUpdate(residentId), ct: ct);
        return RequestAuthorizationContextResolver.RequireResidentAdministration(context);
    }

    private Task EnsureAdministrationScopeAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct) =>
        AdministrationAccessAsync(profileScopeId, centerId, ct);

    /// <summary>El ámbito activo de Administración de la cuenta de la sesión y su sujeto externo; si no lo es, acceso
    /// denegado.</summary>
    private async Task<(AccountAdministrationAccess Access, string Subject)> AdministrationAccessAsync(
        Guid profileScopeId, CenterId centerId, CancellationToken ct)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
        var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
        var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == profileScopeId && s.CenterId == centerId);
        if (scope is null || scope.Profile != SystemProfile.Administracion)
        {
            throw new AccessDeniedException();
        }

        return (new AccountAdministrationAccess(scope.ProfileScopeId, scope.AccountId, scope.CenterId), identity.ExternalSubject);
    }
}
