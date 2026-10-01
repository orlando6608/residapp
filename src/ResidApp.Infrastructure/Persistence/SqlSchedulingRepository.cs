using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Scheduling;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// ADM-14 (0027): escrituras sobre turnos, equipos y miembros. Cada una va en una transacción que:
///   1. vuelve a comprobar que quien gestiona tiene su ámbito de Administración vigente (EnsureAdministratorAsync);
///   2. bloquea la fila del centro (UPDLOCK), lo que ordena los cambios simultáneos y hace fiable la comprobación de nombres;
///   3. escribe y deja su evento en dbo.eventos_auditoria, sin datos.
/// Los turnos son del centro; un equipo solo se toca si su unidad está concedida al ámbito (si no, acceso denegado, sin distinguir
/// si existe). Las horas de un turno no cambian (TR_shifts_guard) y nada se borra. Los miembros solo se revocan.
/// </summary>
public sealed class SqlSchedulingRepository(SqlConnectionFactory connections) : ISchedulingRepository
{
    public async Task<Guid> CreateShiftAsync(AccountAdministrationAccess access, Guid operationId, ShiftData data, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await BeginAsync(connection, transaction, access, ct);
        var previous = await connection.QuerySingleOrDefaultAsync<ShiftRow>(new CommandDefinition("""
            SELECT centro_id AS CenterId, nombre_visible AS Name, hora_inicio AS Start, hora_fin AS [End], estado AS StatusCode
              FROM dbo.turnos_catalogo WHERE id = @Id
            """, new { Id = operationId }, transaction, cancellationToken: ct));
        if (previous is not null)
        {
            return previous.CenterId == access.CenterId.Value && string.Equals(previous.Name, data.Name, StringComparison.Ordinal)
                   && TimeOnly.FromTimeSpan(previous.Start) == data.Start && TimeOnly.FromTimeSpan(previous.End) == data.End
                ? operationId
                : throw new DomainValidationException("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST");
        }

        await EnsureShiftNameFreeAsync(connection, transaction, access, data.Name, null, ct);
        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.turnos_catalogo (id, centro_id, nombre_visible, hora_inicio, hora_fin, estado, creado_en, creado_por_cuenta_id)
            VALUES (@Id, @CenterId, @Name, @Start, @End, 'ACTIVE', @OccurredAt, @ActorId)
            """, new
        {
            Id = operationId, CenterId = access.CenterId.Value, data.Name, Start = data.Start.ToTimeSpan(), End = data.End.ToTimeSpan(),
            OccurredAt = occurredAt, ActorId = access.AccountId.Value,
        }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "SHIFT", operationId, "SHIFT_CREATE", occurredAt, ct: ct);
        transaction.Commit();
        return operationId;
    }

    public async Task RenameShiftAsync(AccountAdministrationAccess access, Guid shiftId, string name, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await BeginAsync(connection, transaction, access, ct);
        var shift = await LockShiftAsync(connection, transaction, access, shiftId, ct);
        if (string.Equals(shift.Name, name, StringComparison.Ordinal))
        {
            throw new DomainValidationException(Shift.InvalidCode);
        }

        await EnsureShiftNameFreeAsync(connection, transaction, access, name, shiftId, ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.turnos_catalogo SET nombre_visible = @Name WHERE id = @Id AND centro_id = @CenterId",
            new { Name = name, Id = shiftId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "SHIFT", shiftId, "SHIFT_RENAME", DateTimeOffset.UtcNow, ct: ct);
        transaction.Commit();
    }

    public async Task ChangeShiftStatusAsync(AccountAdministrationAccess access, Guid shiftId, bool active, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await BeginAsync(connection, transaction, access, ct);
        var shift = await LockShiftAsync(connection, transaction, access, shiftId, ct);
        if ((shift.StatusCode == "ACTIVE") == active)
        {
            throw new DomainValidationException("SCHEDULING_CHANGE_CONFLICT");
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.turnos_catalogo SET estado = @StatusCode WHERE id = @Id AND centro_id = @CenterId",
            new { StatusCode = active ? "ACTIVE" : "INACTIVE", Id = shiftId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "SHIFT", shiftId,
            active ? "SHIFT_ACTIVATE" : "SHIFT_DEACTIVATE", DateTimeOffset.UtcNow, ct: ct);
        transaction.Commit();
    }

    public async Task<Guid> CreateTeamAsync(
        AccountAdministrationAccess access, Guid operationId, UnitId unitId, string name, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await BeginAsync(connection, transaction, access, ct);
        var previous = await connection.QuerySingleOrDefaultAsync<TeamRow>(new CommandDefinition("""
            SELECT centro_id AS CenterId, unidad_id AS UnitId, nombre_visible AS Name, estado AS StatusCode FROM dbo.equipos WHERE id = @Id
            """, new { Id = operationId }, transaction, cancellationToken: ct));
        if (previous is not null)
        {
            return previous.CenterId == access.CenterId.Value && previous.UnitId == unitId.Value
                   && string.Equals(previous.Name, name, StringComparison.Ordinal)
                ? operationId
                : throw new DomainValidationException("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST");
        }

        // La unidad tiene que estar concedida al ámbito y activa.
        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition($"""
                SELECT COUNT(*) FROM dbo.unidades unit
                 WHERE unit.id = @UnitId AND unit.centro_id = @CenterId AND unit.estado = 'ACTIVE' AND {SqlSchedulingDirectory.AdministratorUnitGrant("unit.id")}
                """, new { UnitId = unitId.Value, CenterId = access.CenterId.Value, access.ProfileScopeId }, transaction, cancellationToken: ct)) == 0)
        {
            throw new DomainValidationException(Team.InvalidCode);
        }

        await EnsureTeamNameFreeAsync(connection, transaction, access, unitId.Value, name, null, ct);
        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.equipos (id, centro_id, unidad_id, nombre_visible, estado, creado_en, creado_por_cuenta_id)
            VALUES (@Id, @CenterId, @UnitId, @Name, 'ACTIVE', @OccurredAt, @ActorId)
            """, new
        {
            Id = operationId, CenterId = access.CenterId.Value, UnitId = unitId.Value, Name = name, OccurredAt = occurredAt,
            ActorId = access.AccountId.Value,
        }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "TEAM", operationId, "TEAM_CREATE", occurredAt, unitId.Value, ct: ct);
        transaction.Commit();
        return operationId;
    }

    public async Task RenameTeamAsync(AccountAdministrationAccess access, Guid teamId, string name, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await BeginAsync(connection, transaction, access, ct);
        var team = await LockTeamAsync(connection, transaction, access, teamId, ct);
        if (string.Equals(team.Name, name, StringComparison.Ordinal))
        {
            throw new DomainValidationException(Team.InvalidCode);
        }

        await EnsureTeamNameFreeAsync(connection, transaction, access, team.UnitId, name, teamId, ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.equipos SET nombre_visible = @Name WHERE id = @Id AND centro_id = @CenterId",
            new { Name = name, Id = teamId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "TEAM", teamId, "TEAM_RENAME", DateTimeOffset.UtcNow, team.UnitId, ct: ct);
        transaction.Commit();
    }

    public async Task ChangeTeamStatusAsync(AccountAdministrationAccess access, Guid teamId, bool active, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await BeginAsync(connection, transaction, access, ct);
        var team = await LockTeamAsync(connection, transaction, access, teamId, ct);
        if ((team.StatusCode == "ACTIVE") == active)
        {
            throw new DomainValidationException("SCHEDULING_CHANGE_CONFLICT");
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.equipos SET estado = @StatusCode WHERE id = @Id AND centro_id = @CenterId",
            new { StatusCode = active ? "ACTIVE" : "INACTIVE", Id = teamId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "TEAM", teamId,
            active ? "TEAM_ACTIVATE" : "TEAM_DEACTIVATE", DateTimeOffset.UtcNow, team.UnitId, ct: ct);
        transaction.Commit();
    }

    public async Task AddTeamMemberAsync(AccountAdministrationAccess access, Guid teamId, AccountId accountId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await BeginAsync(connection, transaction, access, ct);
        var team = await LockTeamAsync(connection, transaction, access, teamId, ct);
        if (team.StatusCode != "ACTIVE")
        {
            throw new DomainValidationException(Team.InvalidCode);
        }

        // La cuenta tiene que ser elegible ahora: activa y con un perfil de equipo vigente con la unidad del equipo concedida.
        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition($"""
                SELECT COUNT(*)
                  FROM dbo.cuentas account
                  JOIN dbo.ambitos_perfil profile ON profile.cuenta_id = account.id AND profile.centro_id = @CenterId AND profile.estado = 'ACTIVE'
                       AND profile.revocado_en IS NULL AND profile.perfil_codigo IN ({SqlSchedulingDirectory.TeamProfileCodes})
                  JOIN dbo.ambitos_perfil_unidad unit_scope ON unit_scope.ambito_perfil_id = profile.id AND unit_scope.centro_id = profile.centro_id
                       AND unit_scope.unidad_id = @UnitId AND unit_scope.revocado_en IS NULL
                 WHERE account.id = @AccountId AND account.estado = 'ACTIVE'
                """, new { AccountId = accountId.Value, CenterId = access.CenterId.Value, team.UnitId }, transaction, cancellationToken: ct)) == 0)
        {
            throw new DomainValidationException(Team.InvalidCode);
        }

        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.equipos_miembros WHERE equipo_id = @TeamId AND cuenta_id = @AccountId AND revocado_en IS NULL",
                new { TeamId = teamId, AccountId = accountId.Value }, transaction, cancellationToken: ct)) > 0)
        {
            throw new DomainValidationException("SCHEDULING_CHANGE_CONFLICT");
        }

        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.equipos_miembros (id, centro_id, equipo_id, cuenta_id, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @CenterId, @TeamId, @AccountId, @OccurredAt, @ActorId)
            """, new
        {
            CenterId = access.CenterId.Value, TeamId = teamId, AccountId = accountId.Value, OccurredAt = occurredAt, ActorId = access.AccountId.Value,
        }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "ACCOUNT", accountId.Value, "TEAM_MEMBER_ADD", occurredAt, team.UnitId, ct: ct);
        transaction.Commit();
    }

    public async Task RemoveTeamMemberAsync(AccountAdministrationAccess access, Guid teamId, AccountId accountId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await BeginAsync(connection, transaction, access, ct);
        var team = await LockTeamAsync(connection, transaction, access, teamId, ct);
        var occurredAt = DateTimeOffset.UtcNow;
        var removed = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.equipos_miembros SET revocado_en = @OccurredAt, revocado_por_cuenta_id = @ActorId
             WHERE equipo_id = @TeamId AND cuenta_id = @AccountId AND centro_id = @CenterId AND revocado_en IS NULL
            """, new
        {
            TeamId = teamId, AccountId = accountId.Value, CenterId = access.CenterId.Value, OccurredAt = occurredAt, ActorId = access.AccountId.Value,
        }, transaction, cancellationToken: ct));
        if (removed == 0)
        {
            throw new DomainValidationException("SCHEDULING_CHANGE_CONFLICT");
        }

        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "ACCOUNT", accountId.Value, "TEAM_MEMBER_REMOVE", occurredAt, team.UnitId, ct: ct);
        transaction.Commit();
    }

    /// <summary>Quien gestiona sigue con su ámbito vigente, y se bloquea la fila del centro.</summary>
    private static async Task BeginAsync(SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, CancellationToken ct)
    {
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            "SELECT id FROM dbo.centros WITH (UPDLOCK, ROWLOCK) WHERE id = @CenterId",
            new { CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
    }

    private static async Task<ShiftRow> LockShiftAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, Guid shiftId, CancellationToken ct) =>
        await connection.QuerySingleOrDefaultAsync<ShiftRow>(new CommandDefinition("""
            SELECT centro_id AS CenterId, nombre_visible AS Name, hora_inicio AS Start, hora_fin AS [End], estado AS StatusCode
              FROM dbo.turnos_catalogo WITH (UPDLOCK, ROWLOCK) WHERE id = @Id AND centro_id = @CenterId
            """, new { Id = shiftId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct))
        ?? throw new AccessDeniedException();

    /// <summary>Bloquea el equipo si su unidad está concedida, sin revocar, al ámbito de quien gestiona; si no, acceso denegado.</summary>
    private static async Task<TeamRow> LockTeamAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, Guid teamId, CancellationToken ct) =>
        await connection.QuerySingleOrDefaultAsync<TeamRow>(new CommandDefinition($"""
            SELECT team.centro_id AS CenterId, team.unidad_id AS UnitId, team.nombre_visible AS Name, team.estado AS StatusCode
              FROM dbo.equipos team WITH (UPDLOCK, ROWLOCK)
             WHERE team.id = @Id AND team.centro_id = @CenterId AND {SqlSchedulingDirectory.AdministratorUnitGrant("team.unidad_id")}
            """, new { Id = teamId, CenterId = access.CenterId.Value, access.ProfileScopeId }, transaction, cancellationToken: ct))
        ?? throw new AccessDeniedException();

    private static async Task EnsureShiftNameFreeAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, string name, Guid? except, CancellationToken ct)
    {
        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.turnos_catalogo WHERE centro_id = @CenterId AND nombre_visible = @Name AND id <> @Except",
                new { CenterId = access.CenterId.Value, Name = name, Except = except ?? Guid.Empty }, transaction, cancellationToken: ct)) > 0)
        {
            throw new DomainValidationException("SHIFT_NAME_TAKEN");
        }
    }

    private static async Task EnsureTeamNameFreeAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, Guid unitId, string name, Guid? except,
        CancellationToken ct)
    {
        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.equipos WHERE centro_id = @CenterId AND unidad_id = @UnitId AND nombre_visible = @Name AND id <> @Except",
                new { CenterId = access.CenterId.Value, UnitId = unitId, Name = name, Except = except ?? Guid.Empty }, transaction, cancellationToken: ct)) > 0)
        {
            throw new DomainValidationException("TEAM_NAME_TAKEN");
        }
    }

    private sealed record ShiftRow(Guid CenterId, string Name, TimeSpan Start, TimeSpan End, string StatusCode);

    private sealed record TeamRow(Guid CenterId, Guid UnitId, string Name, string StatusCode);
}
