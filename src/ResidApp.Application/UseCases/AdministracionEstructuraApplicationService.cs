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

/// <summary>Historia 2 (0029): alta de un edificio. OperacionId nace con el formulario y es el id del edificio.</summary>
public sealed record CreateBuildingCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid OperacionId, string? Nombre);

public sealed record RenameBuildingCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid EdificioId, string? Nombre);

/// <summary>Activo es el estado que se quiere (false para inactivar, true para reactivar).</summary>
public sealed record ChangeBuildingStatusCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid EdificioId, bool Activo);

/// <summary>Historia 2 (0029): alta de una planta en un edificio. OperacionId es el id de la planta.</summary>
public sealed record CreateFloorCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid OperacionId, Guid EdificioId, string? Nombre);

public sealed record RenameFloorCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid PlantaId, string? Nombre);

public sealed record ChangeFloorStatusCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid PlantaId, bool Activa);

/// <summary>Historia 2: colocar una unidad en un edificio y una planta (EdificioId null la quita del edificio; PlantaId exige edificio).</summary>
public sealed record SetUnitLocationCommand(Guid AmbitoPerfilId, CenterId CentroId, UnitId UnidadId, Guid? EdificioId, Guid? PlantaId);

/// <summary>Historia 2 (0029): las habitaciones de una unidad del ámbito con sus plazas.</summary>
public sealed record ListRoomsQuery(Guid AmbitoPerfilId, CenterId CentroId, UnitId UnidadId);

/// <summary>Historia 2: alta de una habitación en una unidad del ámbito. OperacionId es el id de la habitación.</summary>
public sealed record CreateRoomCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid OperacionId, UnitId UnidadId, string? Nombre);

public sealed record RenameRoomCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid HabitacionId, string? Nombre);

public sealed record ChangeRoomStatusCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid HabitacionId, bool Activa);

/// <summary>Historia 2: alta de una plaza en una habitación. OperacionId es el id de la plaza.</summary>
public sealed record CreatePlaceCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid OperacionId, Guid HabitacionId, string? Nombre);

public sealed record RenamePlaceCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid PlazaId, string? Nombre);

public sealed record ChangePlaceStatusCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid PlazaId, bool Activa);

/// <summary>Administración, estructura del centro (ADM-05, script 0025) y auditoría administrativa (ADM-28). Como el resto de
/// Administración, cada caso de uso exige un ámbito activo de Administración y el repositorio lo repite dentro de la transacción.</summary>
public sealed class AdministracionEstructuraApplicationService(
    AdministrationAccessResolver access, ICenterStructureDirectory structure, ICenterStructureRepository structureWriter,
    ICenterLayoutDirectory layout, ICenterLayoutRepository layoutWriter,
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

    /// <summary>Historia 2 (0029): los edificios del centro con sus plantas, activos e inactivos.</summary>
    public Task<ApplicationResult<IReadOnlyList<LayoutBuilding>>> ListBuildingsAsync(AdministracionQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await layout.ListBuildingsAsync(grant, ct);
        });

    public Task<ApplicationResult<Guid>> CreateBuildingAsync(CreateBuildingCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            return await layoutWriter.CreateBuildingAsync(grant, command.OperacionId, CenterLayout.ValidateBuildingName(command.Nombre), ct);
        });

    public Task<ApplicationResult<bool>> RenameBuildingAsync(RenameBuildingCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await layoutWriter.RenameBuildingAsync(grant, command.EdificioId, CenterLayout.ValidateBuildingName(command.Nombre), ct);
            return true;
        });

    public Task<ApplicationResult<bool>> ChangeBuildingStatusAsync(ChangeBuildingStatusCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await layoutWriter.ChangeBuildingStatusAsync(grant, command.EdificioId, command.Activo, ct);
            return true;
        });

    public Task<ApplicationResult<Guid>> CreateFloorAsync(CreateFloorCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            return await layoutWriter.CreateFloorAsync(
                grant, command.OperacionId, command.EdificioId, CenterLayout.ValidateFloorName(command.Nombre), ct);
        });

    public Task<ApplicationResult<bool>> RenameFloorAsync(RenameFloorCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await layoutWriter.RenameFloorAsync(grant, command.PlantaId, CenterLayout.ValidateFloorName(command.Nombre), ct);
            return true;
        });

    public Task<ApplicationResult<bool>> ChangeFloorStatusAsync(ChangeFloorStatusCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await layoutWriter.ChangeFloorStatusAsync(grant, command.PlantaId, command.Activa, ct);
            return true;
        });

    public Task<ApplicationResult<bool>> SetUnitLocationAsync(SetUnitLocationCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await layoutWriter.SetUnitLocationAsync(grant, command.UnidadId, command.EdificioId, command.PlantaId, ct);
            return true;
        });

    public Task<ApplicationResult<IReadOnlyList<LayoutRoom>>> ListRoomsAsync(ListRoomsQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await layout.ListRoomsAsync(grant, query.UnidadId, ct);
        });

    public Task<ApplicationResult<Guid>> CreateRoomAsync(CreateRoomCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            return await layoutWriter.CreateRoomAsync(grant, command.OperacionId, command.UnidadId, CenterLayout.ValidateRoomName(command.Nombre), ct);
        });

    public Task<ApplicationResult<bool>> RenameRoomAsync(RenameRoomCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await layoutWriter.RenameRoomAsync(grant, command.HabitacionId, CenterLayout.ValidateRoomName(command.Nombre), ct);
            return true;
        });

    public Task<ApplicationResult<bool>> ChangeRoomStatusAsync(ChangeRoomStatusCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await layoutWriter.ChangeRoomStatusAsync(grant, command.HabitacionId, command.Activa, ct);
            return true;
        });

    public Task<ApplicationResult<Guid>> CreatePlaceAsync(CreatePlaceCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            return await layoutWriter.CreatePlaceAsync(grant, command.OperacionId, command.HabitacionId, CenterLayout.ValidatePlaceName(command.Nombre), ct);
        });

    public Task<ApplicationResult<bool>> RenamePlaceAsync(RenamePlaceCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await layoutWriter.RenamePlaceAsync(grant, command.PlazaId, CenterLayout.ValidatePlaceName(command.Nombre), ct);
            return true;
        });

    public Task<ApplicationResult<bool>> ChangePlaceStatusAsync(ChangePlaceStatusCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await layoutWriter.ChangePlaceStatusAsync(grant, command.PlazaId, command.Activa, ct);
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
