using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Organization;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>ADM-07: alta de un cargo. OperacionId nace con el formulario y es el id del cargo. Unidades son las del ámbito de Administración
/// a las que se liga.</summary>
public sealed record CreatePositionCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid OperacionId, string? Nombre, bool UnaPersona, Guid? SuperiorId, string? Nota,
    IReadOnlyList<UnitId> Unidades);

/// <summary>ADM-07: cambiar un cargo. Las unidades que se mandan son las de su ámbito que quedan ligadas; las ligas a unidades que quien gestiona
/// no tiene en su ámbito se conservan.</summary>
public sealed record UpdatePositionCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid CargoId, string? Nombre, bool UnaPersona, Guid? SuperiorId, string? Nota, IReadOnlyList<UnitId> Unidades);

/// <summary>ADM-07: Activo es el estado que se quiere (false para inactivar, true para reactivar).</summary>
public sealed record ChangePositionStatusCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid CargoId, bool Activo);

/// <summary>ADM-07: poner a una persona en un cargo: una cuenta profesional del centro (CuentaId) o, sin cuenta, solo un nombre. Exactamente una.</summary>
public sealed record AddPositionOccupantCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid CargoId, AccountId? CuentaId, string? Nombre);

public sealed record RemovePositionOccupantCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid CargoId, Guid OcupanteId);

public sealed record LoadPositionTemplateCommand(Guid AmbitoPerfilId, CenterId CentroId);

/// <summary>
/// ADM-07 (script 0073), con las respuestas de CJ del 2026-10-25 (organigrama-y-cargos-cj): cualquier Administración del centro crea y cambia los
/// cargos, pone o quita a las personas que los ocupan y puede cargar la plantilla de CJ cuando el centro aún no tiene ninguno. Ocupar un cargo no
/// concede permisos: no se toca ningún permiso ni ámbito. Cada caso de uso exige un ámbito activo de Administración y el repositorio lo repite
/// dentro de la transacción.
/// </summary>
public sealed class AdministracionOrganigramaApplicationService(
    AdministrationAccessResolver access, IProfileScopeDirectoryProvider scopes, IOrganizationDirectory directory, IOrganizationRepository repository)
{
    /// <summary>Todos los cargos del centro, también los inactivos.</summary>
    public Task<ApplicationResult<IReadOnlyList<PositionInfo>>> ListAsync(AdministracionQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await directory.ListAsync(grant.CenterId, includeInactive: true, ct);
        });

    /// <summary>Las unidades del ámbito de quien gestiona: las únicas que puede ligar a un cargo.</summary>
    public Task<ApplicationResult<IReadOnlyList<ScopeUnit>>> ListScopeUnitsAsync(AdministracionQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, subject) = await access.ResolveAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await scopes.ListUnitsAsync(subject, grant.ProfileScopeId, grant.CenterId, ct);
        });

    public Task<ApplicationResult<IReadOnlyList<PositionOccupantCandidate>>> ListCandidatesAsync(AdministracionQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await directory.ListCandidatesAsync(grant, ct);
        });

    public Task<ApplicationResult<Guid>> CreateAsync(CreatePositionCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            var data = Position.Validate(command.Nombre, command.UnaPersona, command.SuperiorId, command.Nota);
            return await repository.CreateAsync(grant, command.OperacionId, data, command.Unidades.Distinct().ToList(), ct);
        });

    public Task<ApplicationResult<bool>> UpdateAsync(UpdatePositionCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            var data = Position.Validate(command.Nombre, command.UnaPersona, command.SuperiorId, command.Nota);
            await repository.UpdateAsync(grant, command.CargoId, data, command.Unidades.Distinct().ToList(), ct);
            return true;
        });

    public Task<ApplicationResult<bool>> ChangeStatusAsync(ChangePositionStatusCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await repository.ChangeStatusAsync(grant, command.CargoId, command.Activo, ct);
            return true;
        });

    public Task<ApplicationResult<bool>> AddOccupantAsync(AddPositionOccupantCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            var hasName = !string.IsNullOrWhiteSpace(command.Nombre);
            var hasAccount = command.CuentaId is not null;
            if (hasAccount == hasName)
            {
                // Exactamente una de las dos: una cuenta o un nombre.
                throw new DomainValidationException("POSITION_OCCUPANT_INVALID");
            }

            await repository.AddOccupantAsync(grant, command.CargoId, command.CuentaId, hasName ? Position.ValidatePersonName(command.Nombre) : null, ct);
            return true;
        });

    public Task<ApplicationResult<bool>> RemoveOccupantAsync(RemovePositionOccupantCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await repository.RemoveOccupantAsync(grant, command.CargoId, command.OcupanteId, ct);
            return true;
        });

    public Task<ApplicationResult<bool>> LoadTemplateAsync(LoadPositionTemplateCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await repository.LoadTemplateAsync(grant, ct);
            return true;
        });
}

/// <summary>
/// ADM-07: la consulta del organigrama (CJ 1.8): la hacen en solo lectura todos los profesionales del centro (Administración, Enfermería,
/// Auxiliar, Medicina y Dirección Clínica), nunca la familia ni Plataforma. El ámbito activo se revalida contra SQL, no se fía de la cookie. Solo
/// salen los cargos activos y consultarlo no se audita (no es una lectura clínica).
/// </summary>
public sealed class OrganigramaApplicationService(
    IProfileScopeDirectoryProvider scopes, ISessionIdentityProvider session, IOrganizationDirectory directory)
{
    private static readonly SystemProfile[] Viewers =
        [SystemProfile.Administracion, SystemProfile.Enfermeria, SystemProfile.Auxiliar, SystemProfile.Medicina, SystemProfile.DireccionClinica];

    public Task<ApplicationResult<IReadOnlyList<PositionInfo>>> ReadAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
            var active = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
            var scope = active.FirstOrDefault(s => s.ProfileScopeId == profileScopeId && s.CenterId == centerId);
            if (scope is null || !Viewers.Contains(scope.Profile))
            {
                throw new AccessDeniedException();
            }

            return await directory.ListAsync(scope.CenterId, includeInactive: false, ct);
        });
}
