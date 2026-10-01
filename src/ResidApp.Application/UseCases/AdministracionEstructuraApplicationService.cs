using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Audit;
using ResidApp.Domain.Structure;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>ADM-28: la auditoría administrativa de un periodo (días de la hora local del servidor, ambos incluidos). Accion, si
/// se da, es una de AdministrativeAudit.Actions; Cuenta filtra por la cuenta afectada, nunca por quien actuó (AUD-02).</summary>
public sealed record ListAdministrativeAuditQuery(
    Guid AmbitoPerfilId, CenterId CentroId, DateOnly From, DateOnly To, string? Accion = null, AccountId? Cuenta = null);

/// <summary>ADM-05 (0025): alta de una unidad. OperacionId nace con el formulario y es el id de la unidad, así que un reenvío
/// no la duplica.</summary>
public sealed record CreateUnitCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid OperacionId, string? Codigo, string? Nombre);

public sealed record RenameUnitCommand(Guid AmbitoPerfilId, CenterId CentroId, UnitId UnidadId, string? Nombre);

/// <summary>ADM-05: Activa es el estado que se quiere (false para inactivar, true para reactivar).</summary>
public sealed record ChangeUnitStatusCommand(Guid AmbitoPerfilId, CenterId CentroId, UnitId UnidadId, bool Activa);

/// <summary>Administración, estructura del centro (ADM-05, script 0025) y auditoría administrativa (ADM-28). Como el resto de
/// Administración, cada caso de uso exige un ámbito activo de Administración y el repositorio lo repite dentro de la transacción.</summary>
public sealed class AdministracionEstructuraApplicationService(
    AdministrationAccessResolver access, ICenterStructureDirectory structure, ICenterStructureRepository structureWriter,
    IAdministrativeAuditDirectory audit)
{
    /// <summary>ADM-05: las unidades concedidas al ámbito de quien gestiona, activas e inactivas.</summary>
    public Task<ApplicationResult<IReadOnlyList<StructureUnit>>> ListStructureUnitsAsync(
        AdministracionQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await structure.ListUnitsAsync(grant, ct);
        });

    public Task<ApplicationResult<UnitId>> CreateUnitAsync(CreateUnitCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            var data = CenterUnit.Validate(command.Codigo, command.Nombre);
            return await structureWriter.CreateUnitAsync(grant, command.OperacionId, data, ct);
        });

    public Task<ApplicationResult<bool>> RenameUnitAsync(RenameUnitCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await structureWriter.RenameUnitAsync(grant, command.UnidadId, CenterUnit.ValidateName(command.Nombre), ct);
            return true;
        });

    public Task<ApplicationResult<bool>> ChangeUnitStatusAsync(ChangeUnitStatusCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await structureWriter.ChangeUnitStatusAsync(grant, command.UnidadId, command.Activa, ct);
            return true;
        });

    /// <summary>ADM-28: los eventos administrativos del ámbito, del más reciente al más antiguo. Un periodo imposible o una acción
    /// que no es administrativa es entrada inválida.</summary>
    public Task<ApplicationResult<AuditPage>> ListAuditAsync(ListAdministrativeAuditQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            if (query.From > query.To || query.To.DayNumber - query.From.DayNumber + 1 > SupervisionIndicatorRules.MaxPeriodDays
                || query.Accion is not null && !AdministrativeAudit.IsAdministrative(query.Accion))
            {
                throw new DomainValidationException("APPLICATION_INPUT_INVALID");
            }

            var (grant, _) = await access.ResolveAsync(query.AmbitoPerfilId, query.CentroId, ct);
            var (fromUtc, toExclusiveUtc) = SupervisionIndicatorRules.UtcBounds(query.From, query.To, TimeZoneInfo.Local);
            return await audit.ListAsync(grant, new AdministrativeAuditQuery(fromUtc, toExclusiveUtc, query.Accion, query.Cuenta), ct);
        });
}
