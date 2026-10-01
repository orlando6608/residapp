using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Structure;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Historia 2 (0029): escrituras sobre edificios, plantas y la colocación de las unidades. Cada una va en una transacción que repite el
/// ámbito de Administración, bloquea la fila del centro (UPDLOCK, como SqlCenterStructureRepository) y deja su evento en
/// dbo.eventos_auditoria, sin datos. Edificios y plantas son del centro; un edificio o planta de otro centro, o inexistente, da acceso
/// denegado sin distinguirlos. Colocar una unidad exige que esté concedida al ámbito. Los triggers de 0029 impiden cambiar el padre y borrar.
/// </summary>
public sealed class SqlCenterLayoutRepository(SqlConnectionFactory connections) : ICenterLayoutRepository
{
    public async Task<Guid> CreateBuildingAsync(AccountAdministrationAccess access, Guid operationId, string name, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = await BeginAsync(connection, access, ct);
        var previous = await connection.QuerySingleOrDefaultAsync<BuildingRow>(new CommandDefinition(
            "SELECT centro_id AS CenterId, nombre_visible AS Name, estado AS StatusCode FROM dbo.edificios WHERE id = @Id",
            new { Id = operationId }, transaction, cancellationToken: ct));
        if (previous is not null)
        {
            return previous.CenterId == access.CenterId.Value && string.Equals(previous.Name, name, StringComparison.Ordinal)
                ? operationId
                : throw new DomainValidationException("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST");
        }

        await EnsureBuildingNameFreeAsync(connection, transaction, access, name, null, ct);
        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.edificios (id, centro_id, nombre_visible, estado, creado_en, creado_por_cuenta_id)
            VALUES (@Id, @CenterId, @Name, 'ACTIVE', @OccurredAt, @ActorId)
            """, new { Id = operationId, CenterId = access.CenterId.Value, Name = name, OccurredAt = occurredAt, ActorId = access.AccountId.Value },
            transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "BUILDING", operationId, "BUILDING_CREATE", occurredAt, null, ct: ct);
        transaction.Commit();
        return operationId;
    }

    public async Task RenameBuildingAsync(AccountAdministrationAccess access, Guid buildingId, string name, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = await BeginAsync(connection, access, ct);
        var building = await LockBuildingAsync(connection, transaction, access, buildingId, ct);
        if (string.Equals(building.Name, name, StringComparison.Ordinal))
        {
            throw new DomainValidationException(CenterLayout.BuildingInvalidCode);
        }

        await EnsureBuildingNameFreeAsync(connection, transaction, access, name, buildingId, ct);
        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.edificios SET nombre_visible = @Name WHERE id = @Id AND centro_id = @CenterId",
            new { Name = name, Id = buildingId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "BUILDING", buildingId, "BUILDING_RENAME", occurredAt, null, ct: ct);
        transaction.Commit();
    }

    public async Task ChangeBuildingStatusAsync(AccountAdministrationAccess access, Guid buildingId, bool active, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = await BeginAsync(connection, access, ct);
        var building = await LockBuildingAsync(connection, transaction, access, buildingId, ct);
        if ((building.StatusCode == "ACTIVE") == active)
        {
            throw new DomainValidationException("STRUCTURE_CHANGE_CONFLICT");
        }

        if (!active && await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT (SELECT COUNT(*) FROM dbo.plantas WHERE centro_id = @CenterId AND edificio_id = @Id AND estado = 'ACTIVE')
                     + (SELECT COUNT(*) FROM dbo.unidades WHERE centro_id = @CenterId AND edificio_id = @Id AND estado = 'ACTIVE')
                """, new { Id = buildingId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct)) > 0)
        {
            throw new DomainValidationException("STRUCTURE_HAS_ACTIVE_CHILDREN");
        }

        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.edificios SET estado = @StatusCode WHERE id = @Id AND centro_id = @CenterId",
            new { StatusCode = active ? "ACTIVE" : "INACTIVE", Id = buildingId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "BUILDING", buildingId,
            active ? "BUILDING_ACTIVATE" : "BUILDING_DEACTIVATE", occurredAt, null, ct: ct);
        transaction.Commit();
    }

    public async Task<Guid> CreateFloorAsync(
        AccountAdministrationAccess access, Guid operationId, Guid buildingId, string name, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = await BeginAsync(connection, access, ct);
        var building = await LockBuildingAsync(connection, transaction, access, buildingId, ct);
        var previous = await connection.QuerySingleOrDefaultAsync<FloorRow>(new CommandDefinition(
            "SELECT centro_id AS CenterId, edificio_id AS BuildingId, nombre_visible AS Name, estado AS StatusCode FROM dbo.plantas WHERE id = @Id",
            new { Id = operationId }, transaction, cancellationToken: ct));
        if (previous is not null)
        {
            return previous.CenterId == access.CenterId.Value && previous.BuildingId == buildingId
                   && string.Equals(previous.Name, name, StringComparison.Ordinal)
                ? operationId
                : throw new DomainValidationException("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST");
        }

        if (building.StatusCode != "ACTIVE")
        {
            throw new DomainValidationException(CenterLayout.FloorInvalidCode);
        }

        await EnsureFloorNameFreeAsync(connection, transaction, access, buildingId, name, null, ct);
        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.plantas (id, centro_id, edificio_id, nombre_visible, estado, creado_en, creado_por_cuenta_id)
            VALUES (@Id, @CenterId, @BuildingId, @Name, 'ACTIVE', @OccurredAt, @ActorId)
            """, new
        {
            Id = operationId, CenterId = access.CenterId.Value, BuildingId = buildingId, Name = name, OccurredAt = occurredAt,
            ActorId = access.AccountId.Value,
        }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "FLOOR", operationId, "FLOOR_CREATE", occurredAt, null, ct: ct);
        transaction.Commit();
        return operationId;
    }

    public async Task RenameFloorAsync(AccountAdministrationAccess access, Guid floorId, string name, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = await BeginAsync(connection, access, ct);
        var floor = await LockFloorAsync(connection, transaction, access, floorId, ct);
        if (string.Equals(floor.Name, name, StringComparison.Ordinal))
        {
            throw new DomainValidationException(CenterLayout.FloorInvalidCode);
        }

        await EnsureFloorNameFreeAsync(connection, transaction, access, floor.BuildingId, name, floorId, ct);
        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.plantas SET nombre_visible = @Name WHERE id = @Id AND centro_id = @CenterId",
            new { Name = name, Id = floorId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "FLOOR", floorId, "FLOOR_RENAME", occurredAt, null, ct: ct);
        transaction.Commit();
    }

    public async Task ChangeFloorStatusAsync(AccountAdministrationAccess access, Guid floorId, bool active, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = await BeginAsync(connection, access, ct);
        var floor = await LockFloorAsync(connection, transaction, access, floorId, ct);
        if ((floor.StatusCode == "ACTIVE") == active)
        {
            throw new DomainValidationException("STRUCTURE_CHANGE_CONFLICT");
        }

        if (active && await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.edificios WHERE id = @BuildingId AND centro_id = @CenterId AND estado = 'ACTIVE'",
                new { floor.BuildingId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct)) == 0)
        {
            throw new DomainValidationException(CenterLayout.FloorInvalidCode);
        }

        if (!active && await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.unidades WHERE centro_id = @CenterId AND planta_id = @Id AND estado = 'ACTIVE'",
                new { Id = floorId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct)) > 0)
        {
            throw new DomainValidationException("STRUCTURE_HAS_ACTIVE_CHILDREN");
        }

        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.plantas SET estado = @StatusCode WHERE id = @Id AND centro_id = @CenterId",
            new { StatusCode = active ? "ACTIVE" : "INACTIVE", Id = floorId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "FLOOR", floorId,
            active ? "FLOOR_ACTIVATE" : "FLOOR_DEACTIVATE", occurredAt, null, ct: ct);
        transaction.Commit();
    }

    public async Task SetUnitLocationAsync(
        AccountAdministrationAccess access, UnitId unitId, Guid? buildingId, Guid? floorId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = await BeginAsync(connection, access, ct);
        var unit = await connection.QuerySingleOrDefaultAsync<UnitLocationRow>(new CommandDefinition("""
            SELECT unit.edificio_id AS BuildingId, unit.planta_id AS FloorId
              FROM dbo.unidades unit WITH (UPDLOCK, ROWLOCK)
             WHERE unit.id = @UnitId AND unit.centro_id = @CenterId
               AND EXISTS (SELECT 1 FROM dbo.ambitos_perfil_unidad unit_scope
                            WHERE unit_scope.ambito_perfil_id = @ProfileScopeId AND unit_scope.centro_id = @CenterId
                              AND unit_scope.unidad_id = unit.id AND unit_scope.revocado_en IS NULL)
            """, new { UnitId = unitId.Value, CenterId = access.CenterId.Value, access.ProfileScopeId }, transaction, cancellationToken: ct))
            ?? throw new AccessDeniedException();

        if (floorId is not null && buildingId is null)
        {
            throw new DomainValidationException(CenterLayout.FloorInvalidCode);
        }

        if (buildingId is { } building)
        {
            var status = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT estado FROM dbo.edificios WHERE id = @Id AND centro_id = @CenterId",
                new { Id = building, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
            if (status != "ACTIVE")
            {
                throw new DomainValidationException(CenterLayout.BuildingInvalidCode);
            }
        }

        if (floorId is { } floor)
        {
            var status = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT estado FROM dbo.plantas WHERE id = @Id AND centro_id = @CenterId AND edificio_id = @BuildingId",
                new { Id = floor, CenterId = access.CenterId.Value, BuildingId = buildingId }, transaction, cancellationToken: ct));
            if (status != "ACTIVE")
            {
                throw new DomainValidationException(CenterLayout.FloorInvalidCode);
            }
        }

        if (unit.BuildingId == buildingId && unit.FloorId == floorId)
        {
            throw new DomainValidationException("STRUCTURE_CHANGE_CONFLICT");
        }

        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.unidades SET edificio_id = @BuildingId, planta_id = @FloorId WHERE id = @UnitId AND centro_id = @CenterId",
            new { BuildingId = buildingId, FloorId = floorId, UnitId = unitId.Value, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "UNIT", unitId.Value, "UNIT_LOCATE", occurredAt, unitId.Value, ct: ct);
        transaction.Commit();
    }

    private static async Task<SqlTransaction> BeginAsync(SqlConnection connection, AccountAdministrationAccess access, CancellationToken ct)
    {
        var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            "SELECT id FROM dbo.centros WITH (UPDLOCK, ROWLOCK) WHERE id = @CenterId",
            new { CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        return transaction;
    }

    private static async Task<BuildingRow> LockBuildingAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, Guid buildingId, CancellationToken ct) =>
        await connection.QuerySingleOrDefaultAsync<BuildingRow>(new CommandDefinition("""
            SELECT centro_id AS CenterId, nombre_visible AS Name, estado AS StatusCode
              FROM dbo.edificios WITH (UPDLOCK, ROWLOCK) WHERE id = @Id AND centro_id = @CenterId
            """, new { Id = buildingId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct))
        ?? throw new AccessDeniedException();

    private static async Task<FloorRow> LockFloorAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, Guid floorId, CancellationToken ct) =>
        await connection.QuerySingleOrDefaultAsync<FloorRow>(new CommandDefinition("""
            SELECT centro_id AS CenterId, edificio_id AS BuildingId, nombre_visible AS Name, estado AS StatusCode
              FROM dbo.plantas WITH (UPDLOCK, ROWLOCK) WHERE id = @Id AND centro_id = @CenterId
            """, new { Id = floorId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct))
        ?? throw new AccessDeniedException();

    private static async Task EnsureBuildingNameFreeAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, string name, Guid? except, CancellationToken ct)
    {
        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.edificios WHERE centro_id = @CenterId AND nombre_visible = @Name AND id <> @Except",
                new { CenterId = access.CenterId.Value, Name = name, Except = except ?? Guid.Empty }, transaction, cancellationToken: ct)) > 0)
        {
            throw new DomainValidationException("BUILDING_NAME_TAKEN");
        }
    }

    private static async Task EnsureFloorNameFreeAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, Guid buildingId, string name, Guid? except,
        CancellationToken ct)
    {
        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.plantas WHERE centro_id = @CenterId AND edificio_id = @BuildingId AND nombre_visible = @Name AND id <> @Except",
                new { CenterId = access.CenterId.Value, BuildingId = buildingId, Name = name, Except = except ?? Guid.Empty }, transaction, cancellationToken: ct)) > 0)
        {
            throw new DomainValidationException("FLOOR_NAME_TAKEN");
        }
    }

    private sealed record BuildingRow(Guid CenterId, string Name, string StatusCode);

    private sealed record FloorRow(Guid CenterId, Guid BuildingId, string Name, string StatusCode);

    private sealed record UnitLocationRow(Guid? BuildingId, Guid? FloorId);
}
