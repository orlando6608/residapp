using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Structure;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>Historia 2 (0029), fase 2: habitaciones y plazas. A diferencia de edificios y plantas, son de una unidad: exigen que la unidad esté
/// concedida al ámbito de quien gestiona (una unidad ajena o inexistente da acceso denegado, sin distinguirlas). Mismo patrón: ámbito
/// repetido, bloqueo del centro, idempotencia por OperacionId (= id de la entidad) y auditoría sin datos con la unidad.</summary>
public sealed partial class SqlCenterLayoutRepository
{
    public async Task<Guid> CreateRoomAsync(
        AccountAdministrationAccess access, Guid operationId, UnitId unitId, string name, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = await BeginAsync(connection, access, ct);
        var unitStatus = await connection.ExecuteScalarAsync<string?>(new CommandDefinition($"""
            SELECT unit.estado FROM dbo.unidades unit WITH (UPDLOCK, ROWLOCK)
             WHERE unit.id = @UnitId AND unit.centro_id = @CenterId AND {SqlSchedulingDirectory.AdministratorUnitGrant("unit.id")}
            """, new { UnitId = unitId.Value, CenterId = access.CenterId.Value, access.ProfileScopeId }, transaction, cancellationToken: ct))
            ?? throw new AccessDeniedException();
        var previous = await connection.QuerySingleOrDefaultAsync<RoomRow>(new CommandDefinition(
            "SELECT centro_id AS CenterId, unidad_id AS UnitId, nombre_visible AS Name, estado AS StatusCode FROM dbo.habitaciones WHERE id = @Id",
            new { Id = operationId }, transaction, cancellationToken: ct));
        if (previous is not null)
        {
            return previous.CenterId == access.CenterId.Value && previous.UnitId == unitId.Value && string.Equals(previous.Name, name, StringComparison.Ordinal)
                ? operationId
                : throw new DomainValidationException("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST");
        }

        if (unitStatus != "ACTIVE")
        {
            throw new DomainValidationException(CenterLayout.RoomInvalidCode);
        }

        await EnsureRoomNameFreeAsync(connection, transaction, access, unitId.Value, name, null, ct);
        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.habitaciones (id, centro_id, unidad_id, nombre_visible, estado, creado_en, creado_por_cuenta_id)
            VALUES (@Id, @CenterId, @UnitId, @Name, 'ACTIVE', @OccurredAt, @ActorId)
            """, new
        {
            Id = operationId, CenterId = access.CenterId.Value, UnitId = unitId.Value, Name = name, OccurredAt = occurredAt,
            ActorId = access.AccountId.Value,
        }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "ROOM", operationId, "ROOM_CREATE", occurredAt, unitId.Value, ct: ct);
        transaction.Commit();
        return operationId;
    }

    public async Task RenameRoomAsync(AccountAdministrationAccess access, Guid roomId, string name, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = await BeginAsync(connection, access, ct);
        var room = await LockRoomAsync(connection, transaction, access, roomId, ct);
        if (string.Equals(room.Name, name, StringComparison.Ordinal))
        {
            throw new DomainValidationException(CenterLayout.RoomInvalidCode);
        }

        await EnsureRoomNameFreeAsync(connection, transaction, access, room.UnitId, name, roomId, ct);
        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.habitaciones SET nombre_visible = @Name WHERE id = @Id AND centro_id = @CenterId",
            new { Name = name, Id = roomId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "ROOM", roomId, "ROOM_RENAME", occurredAt, room.UnitId, ct: ct);
        transaction.Commit();
    }

    public async Task ChangeRoomStatusAsync(AccountAdministrationAccess access, Guid roomId, bool active, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = await BeginAsync(connection, access, ct);
        var room = await LockRoomAsync(connection, transaction, access, roomId, ct);
        if ((room.StatusCode == "ACTIVE") == active)
        {
            throw new DomainValidationException("STRUCTURE_CHANGE_CONFLICT");
        }

        if (!active && await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COUNT(*) FROM dbo.intervalos_ubicacion_residente
                 WHERE centro_id = @CenterId AND habitacion_id = @Id AND vigente_hasta IS NULL
                """, new { Id = roomId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct)) > 0)
        {
            throw new DomainValidationException("LAYOUT_HAS_RESIDENTS");
        }

        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.habitaciones SET estado = @StatusCode WHERE id = @Id AND centro_id = @CenterId",
            new { StatusCode = active ? "ACTIVE" : "INACTIVE", Id = roomId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "ROOM", roomId,
            active ? "ROOM_ACTIVATE" : "ROOM_DEACTIVATE", occurredAt, room.UnitId, ct: ct);
        transaction.Commit();
    }

    public async Task<Guid> CreatePlaceAsync(
        AccountAdministrationAccess access, Guid operationId, Guid roomId, string name, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = await BeginAsync(connection, access, ct);
        var room = await LockRoomAsync(connection, transaction, access, roomId, ct);
        var previous = await connection.QuerySingleOrDefaultAsync<PlaceRow>(new CommandDefinition("""
            SELECT centro_id AS CenterId, unidad_id AS UnitId, habitacion_id AS RoomId, nombre_visible AS Name, estado AS StatusCode
              FROM dbo.plazas WHERE id = @Id
            """, new { Id = operationId }, transaction, cancellationToken: ct));
        if (previous is not null)
        {
            return previous.CenterId == access.CenterId.Value && previous.RoomId == roomId && string.Equals(previous.Name, name, StringComparison.Ordinal)
                ? operationId
                : throw new DomainValidationException("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST");
        }

        if (room.StatusCode != "ACTIVE")
        {
            throw new DomainValidationException(CenterLayout.PlaceInvalidCode);
        }

        await EnsurePlaceNameFreeAsync(connection, transaction, roomId, name, null, ct);
        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.plazas (id, centro_id, unidad_id, habitacion_id, nombre_visible, estado, creado_en, creado_por_cuenta_id)
            VALUES (@Id, @CenterId, @UnitId, @RoomId, @Name, 'ACTIVE', @OccurredAt, @ActorId)
            """, new
        {
            Id = operationId, CenterId = access.CenterId.Value, room.UnitId, RoomId = roomId, Name = name, OccurredAt = occurredAt,
            ActorId = access.AccountId.Value,
        }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "PLACE", operationId, "PLACE_CREATE", occurredAt, room.UnitId, ct: ct);
        transaction.Commit();
        return operationId;
    }

    public async Task RenamePlaceAsync(AccountAdministrationAccess access, Guid placeId, string name, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = await BeginAsync(connection, access, ct);
        var place = await LockPlaceAsync(connection, transaction, access, placeId, ct);
        if (string.Equals(place.Name, name, StringComparison.Ordinal))
        {
            throw new DomainValidationException(CenterLayout.PlaceInvalidCode);
        }

        await EnsurePlaceNameFreeAsync(connection, transaction, place.RoomId, name, placeId, ct);
        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.plazas SET nombre_visible = @Name WHERE id = @Id AND centro_id = @CenterId",
            new { Name = name, Id = placeId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "PLACE", placeId, "PLACE_RENAME", occurredAt, place.UnitId, ct: ct);
        transaction.Commit();
    }

    public async Task ChangePlaceStatusAsync(AccountAdministrationAccess access, Guid placeId, bool active, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = await BeginAsync(connection, access, ct);
        var place = await LockPlaceAsync(connection, transaction, access, placeId, ct);
        if ((place.StatusCode == "ACTIVE") == active)
        {
            throw new DomainValidationException("STRUCTURE_CHANGE_CONFLICT");
        }

        if (active && await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.habitaciones WHERE id = @RoomId AND centro_id = @CenterId AND estado = 'ACTIVE'",
                new { place.RoomId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct)) == 0)
        {
            throw new DomainValidationException(CenterLayout.PlaceInvalidCode);
        }

        if (!active && await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COUNT(*) FROM dbo.intervalos_ubicacion_residente
                 WHERE centro_id = @CenterId AND plaza_id = @Id AND vigente_hasta IS NULL
                """, new { Id = placeId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct)) > 0)
        {
            throw new DomainValidationException("LAYOUT_HAS_RESIDENTS");
        }

        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.plazas SET estado = @StatusCode WHERE id = @Id AND centro_id = @CenterId",
            new { StatusCode = active ? "ACTIVE" : "INACTIVE", Id = placeId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "PLACE", placeId,
            active ? "PLACE_ACTIVATE" : "PLACE_DEACTIVATE", occurredAt, place.UnitId, ct: ct);
        transaction.Commit();
    }

    /// <summary>Bloquea la habitación si es de una unidad concedida al ámbito de quien gestiona; si no, acceso denegado.</summary>
    private static async Task<RoomRow> LockRoomAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, Guid roomId, CancellationToken ct) =>
        await connection.QuerySingleOrDefaultAsync<RoomRow>(new CommandDefinition($"""
            SELECT room.centro_id AS CenterId, room.unidad_id AS UnitId, room.nombre_visible AS Name, room.estado AS StatusCode
              FROM dbo.habitaciones room WITH (UPDLOCK, ROWLOCK)
             WHERE room.id = @Id AND room.centro_id = @CenterId AND {SqlSchedulingDirectory.AdministratorUnitGrant("room.unidad_id")}
            """, new { Id = roomId, CenterId = access.CenterId.Value, access.ProfileScopeId }, transaction, cancellationToken: ct))
        ?? throw new AccessDeniedException();

    private static async Task<PlaceRow> LockPlaceAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, Guid placeId, CancellationToken ct) =>
        await connection.QuerySingleOrDefaultAsync<PlaceRow>(new CommandDefinition($"""
            SELECT place.centro_id AS CenterId, place.unidad_id AS UnitId, place.habitacion_id AS RoomId, place.nombre_visible AS Name,
                   place.estado AS StatusCode
              FROM dbo.plazas place WITH (UPDLOCK, ROWLOCK)
             WHERE place.id = @Id AND place.centro_id = @CenterId AND {SqlSchedulingDirectory.AdministratorUnitGrant("place.unidad_id")}
            """, new { Id = placeId, CenterId = access.CenterId.Value, access.ProfileScopeId }, transaction, cancellationToken: ct))
        ?? throw new AccessDeniedException();

    private static async Task EnsureRoomNameFreeAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, Guid unitId, string name, Guid? except,
        CancellationToken ct)
    {
        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.habitaciones WHERE centro_id = @CenterId AND unidad_id = @UnitId AND nombre_visible = @Name AND id <> @Except",
                new { CenterId = access.CenterId.Value, UnitId = unitId, Name = name, Except = except ?? Guid.Empty }, transaction, cancellationToken: ct)) > 0)
        {
            throw new DomainValidationException("ROOM_NAME_TAKEN");
        }
    }

    private static async Task EnsurePlaceNameFreeAsync(
        SqlConnection connection, SqlTransaction transaction, Guid roomId, string name, Guid? except, CancellationToken ct)
    {
        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.plazas WHERE habitacion_id = @RoomId AND nombre_visible = @Name AND id <> @Except",
                new { RoomId = roomId, Name = name, Except = except ?? Guid.Empty }, transaction, cancellationToken: ct)) > 0)
        {
            throw new DomainValidationException("PLACE_NAME_TAKEN");
        }
    }

    private sealed record RoomRow(Guid CenterId, Guid UnitId, string Name, string StatusCode);

    private sealed record PlaceRow(Guid CenterId, Guid UnitId, Guid RoomId, string Name, string StatusCode);
}
