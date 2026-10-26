using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Organization;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// ADM-07 (0073): escrituras sobre los cargos del organigrama. Cada una va en una transacción que:
///   1. vuelve a comprobar que quien gestiona tiene su ámbito de Administración vigente (EnsureAdministratorAsync);
///   2. bloquea la fila del centro (UPDLOCK), lo que ordena los cambios simultáneos y hace fiables las comprobaciones (nombre, ciclo, ocupante único);
///   3. escribe y deja su evento en dbo.eventos_auditoria, sin datos: el recurso es el cargo, que no se borra (se inactiva), así que su
///      nombre sigue resolviéndose. Las personas que ocupan un cargo se añaden y se borran: no se conserva quién lo ocupó antes (CJ 1.7).
/// Cualquier Administración del centro puede gestionar. Solo se ligan unidades del ámbito de quien gestiona: al guardar se reemplazan sus
/// ligas y se conservan las de unidades que no tiene. Ocupar un cargo no toca ningún permiso ni ámbito.
/// </summary>
public sealed class SqlOrganizationRepository(SqlConnectionFactory connections) : IOrganizationRepository
{
    public async Task<Guid> CreateAsync(
        AccountAdministrationAccess access, Guid operationId, PositionData data, IReadOnlyList<UnitId> units, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await BeginAsync(connection, transaction, access, ct);
        var previous = await connection.QuerySingleOrDefaultAsync<PositionRow>(new CommandDefinition(PositionSelect + " WHERE id = @Id",
            new { Id = operationId }, transaction, cancellationToken: ct));
        if (previous is not null)
        {
            return previous.CenterId == access.CenterId.Value && string.Equals(previous.Name, data.Name, StringComparison.Ordinal)
                   && previous.OnePerson == data.OnePerson && previous.SuperiorId == data.SuperiorId && previous.Note == data.Note
                ? operationId
                : throw new DomainValidationException("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST");
        }

        await EnsureNameFreeAsync(connection, transaction, access, data.Name, null, ct);
        await EnsureSuperiorAsync(connection, transaction, access, data.SuperiorId, mustBeActive: true, ct);
        await EnsureUnitsInScopeAsync(connection, transaction, access, units, ct);
        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.cargos (id, centro_id, nombre, una_persona, superior_id, nota_coordinacion, estado, creado_en, creado_por_cuenta_id)
            VALUES (@Id, @CenterId, @Name, @OnePerson, @SuperiorId, @Note, 'ACTIVE', @OccurredAt, @ActorId)
            """, new
        {
            Id = operationId, CenterId = access.CenterId.Value, data.Name, data.OnePerson, data.SuperiorId, data.Note, OccurredAt = occurredAt,
            ActorId = access.AccountId.Value,
        }, transaction, cancellationToken: ct));
        await ReplaceUnitsAsync(connection, transaction, access, operationId, units, occurredAt, ct);
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "POSITION", operationId, "POSITION_CREATE", occurredAt, ct: ct);
        transaction.Commit();
        return operationId;
    }

    public async Task UpdateAsync(
        AccountAdministrationAccess access, Guid positionId, PositionData data, IReadOnlyList<UnitId> units, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await BeginAsync(connection, transaction, access, ct);
        var position = await LockPositionAsync(connection, transaction, access, positionId, ct);
        await EnsureUnitsInScopeAsync(connection, transaction, access, units, ct);
        var currentUnits = await ScopedLinksAsync(connection, transaction, access, positionId, ct);
        var sameUnits = currentUnits.ToHashSet().SetEquals(units.Select(u => u.Value));
        if (string.Equals(position.Name, data.Name, StringComparison.Ordinal) && position.OnePerson == data.OnePerson
            && position.SuperiorId == data.SuperiorId && position.Note == data.Note && sameUnits)
        {
            return;
        }

        if (!string.Equals(position.Name, data.Name, StringComparison.Ordinal))
        {
            await EnsureNameFreeAsync(connection, transaction, access, data.Name, positionId, ct);
        }

        if (position.SuperiorId != data.SuperiorId)
        {
            await EnsureSuperiorAsync(connection, transaction, access, data.SuperiorId, mustBeActive: position.StatusCode == "ACTIVE", ct);
            var superiors = (await connection.QueryAsync<SuperiorRow>(new CommandDefinition(
                "SELECT id AS Id, superior_id AS SuperiorId FROM dbo.cargos WHERE centro_id = @CenterId",
                new { CenterId = access.CenterId.Value }, transaction, cancellationToken: ct))).ToDictionary(p => p.Id, p => p.SuperiorId);
            if (PositionTree.WouldCreateCycle(superiors, positionId, data.SuperiorId))
            {
                throw new DomainValidationException("POSITION_CYCLE");
            }
        }

        if (data.OnePerson && !position.OnePerson && await OccupantCountAsync(connection, transaction, positionId, ct) > 1)
        {
            throw new DomainValidationException("POSITION_SINGLE_OCCUPANT");
        }

        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.cargos
               SET nombre = @Name, una_persona = @OnePerson, superior_id = @SuperiorId, nota_coordinacion = @Note,
                   modificado_en = @OccurredAt, modificado_por_cuenta_id = @ActorId
             WHERE id = @Id AND centro_id = @CenterId
            """, new
        {
            Id = positionId, CenterId = access.CenterId.Value, data.Name, data.OnePerson, data.SuperiorId, data.Note, OccurredAt = occurredAt,
            ActorId = access.AccountId.Value,
        }, transaction, cancellationToken: ct));
        await ReplaceUnitsAsync(connection, transaction, access, positionId, units, occurredAt, ct);
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "POSITION", positionId, "POSITION_UPDATE", occurredAt, ct: ct);
        transaction.Commit();
    }

    public async Task ChangeStatusAsync(AccountAdministrationAccess access, Guid positionId, bool active, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await BeginAsync(connection, transaction, access, ct);
        var position = await LockPositionAsync(connection, transaction, access, positionId, ct);
        if ((position.StatusCode == "ACTIVE") == active)
        {
            throw new DomainValidationException("POSITION_CHANGE_CONFLICT");
        }

        if (active)
        {
            // Reactivar: su superior, si tiene, tiene que estar activo.
            await EnsureSuperiorAsync(connection, transaction, access, position.SuperiorId, mustBeActive: true, ct, "POSITION_SUPERIOR_INACTIVE");
        }
        else
        {
            if (await OccupantCountAsync(connection, transaction, positionId, ct) > 0)
            {
                throw new DomainValidationException("POSITION_HAS_OCCUPANTS");
            }

            if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COUNT(*) FROM dbo.cargos WHERE superior_id = @Id AND centro_id = @CenterId AND estado = 'ACTIVE'",
                    new { Id = positionId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct)) > 0)
            {
                throw new DomainValidationException("POSITION_HAS_SUBORDINATES");
            }
        }

        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.cargos SET estado = @StatusCode, modificado_en = @OccurredAt, modificado_por_cuenta_id = @ActorId
             WHERE id = @Id AND centro_id = @CenterId
            """, new
        {
            StatusCode = active ? "ACTIVE" : "INACTIVE", Id = positionId, CenterId = access.CenterId.Value, OccurredAt = occurredAt,
            ActorId = access.AccountId.Value,
        }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "POSITION", positionId,
            active ? "POSITION_REACTIVATE" : "POSITION_DEACTIVATE", occurredAt, ct: ct);
        transaction.Commit();
    }

    public async Task AddOccupantAsync(
        AccountAdministrationAccess access, Guid positionId, AccountId? accountId, string? personName, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await BeginAsync(connection, transaction, access, ct);
        var position = await LockPositionAsync(connection, transaction, access, positionId, ct);
        if (position.StatusCode != "ACTIVE")
        {
            throw new DomainValidationException("POSITION_INACTIVE");
        }

        if (accountId is { } account && await connection.ExecuteScalarAsync<int>(new CommandDefinition($"""
                SELECT COUNT(*)
                  FROM dbo.cuentas account
                 WHERE account.id = @AccountId AND account.estado = 'ACTIVE'
                   AND EXISTS (SELECT 1 FROM dbo.ambitos_perfil profile
                                WHERE profile.cuenta_id = account.id AND profile.centro_id = @CenterId AND profile.estado = 'ACTIVE'
                                  AND profile.revocado_en IS NULL AND profile.perfil_codigo IN ({SqlOrganizationDirectory.ProfessionalProfileCodes}))
                """, new { AccountId = account.Value, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct)) == 0)
        {
            throw new DomainValidationException("POSITION_OCCUPANT_INVALID");
        }

        if (position.OnePerson && await OccupantCountAsync(connection, transaction, positionId, ct) > 0)
        {
            throw new DomainValidationException("POSITION_SINGLE_OCCUPANT");
        }

        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COUNT(*) FROM dbo.cargos_ocupantes
                 WHERE cargo_id = @PositionId AND ((@AccountId IS NOT NULL AND cuenta_id = @AccountId) OR (@PersonName IS NOT NULL AND nombre_texto = @PersonName))
                """, new { PositionId = positionId, AccountId = accountId?.Value, PersonName = personName }, transaction, cancellationToken: ct)) > 0)
        {
            throw new DomainValidationException("POSITION_CHANGE_CONFLICT");
        }

        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.cargos_ocupantes (id, centro_id, cargo_id, cuenta_id, nombre_texto, puesto_en, puesto_por_cuenta_id)
            VALUES (NEWID(), @CenterId, @PositionId, @AccountId, @PersonName, @OccurredAt, @ActorId)
            """, new
        {
            CenterId = access.CenterId.Value, PositionId = positionId, AccountId = accountId?.Value, PersonName = personName, OccurredAt = occurredAt,
            ActorId = access.AccountId.Value,
        }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "POSITION", positionId, "POSITION_OCCUPANT_ADD", occurredAt, ct: ct);
        transaction.Commit();
    }

    public async Task RemoveOccupantAsync(AccountAdministrationAccess access, Guid positionId, Guid occupantId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await BeginAsync(connection, transaction, access, ct);
        await LockPositionAsync(connection, transaction, access, positionId, ct);
        var removed = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.cargos_ocupantes WHERE id = @Id AND cargo_id = @PositionId AND centro_id = @CenterId",
            new { Id = occupantId, PositionId = positionId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        if (removed == 0)
        {
            throw new DomainValidationException("POSITION_CHANGE_CONFLICT");
        }

        await SqlProfessionalAccountRepository.AuditAsync(
            connection, transaction, access, "POSITION", positionId, "POSITION_OCCUPANT_REMOVE", DateTimeOffset.UtcNow, ct: ct);
        transaction.Commit();
    }

    public async Task LoadTemplateAsync(AccountAdministrationAccess access, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await BeginAsync(connection, transaction, access, ct);
        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.cargos WHERE centro_id = @CenterId", new { CenterId = access.CenterId.Value }, transaction, cancellationToken: ct)) > 0)
        {
            throw new DomainValidationException("POSITION_TEMPLATE_NOT_EMPTY");
        }

        var occurredAt = DateTimeOffset.UtcNow;
        var ids = PositionTemplate.Entries.ToDictionary(e => e.Key, _ => Guid.NewGuid());
        foreach (var entry in PositionTemplate.Entries)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.cargos (id, centro_id, nombre, una_persona, superior_id, nota_coordinacion, estado, creado_en, creado_por_cuenta_id)
                VALUES (@Id, @CenterId, @Name, @OnePerson, @SuperiorId, @Note, 'ACTIVE', @OccurredAt, @ActorId)
                """, new
            {
                Id = ids[entry.Key], CenterId = access.CenterId.Value, entry.Name, entry.OnePerson,
                SuperiorId = entry.SuperiorKey is null ? (Guid?)null : ids[entry.SuperiorKey], entry.Note, OccurredAt = occurredAt,
                ActorId = access.AccountId.Value,
            }, transaction, cancellationToken: ct));
        }

        // Un solo evento: el recurso es el primer cargo de la plantilla.
        await SqlProfessionalAccountRepository.AuditAsync(
            connection, transaction, access, "POSITION", ids[PositionTemplate.Entries[0].Key], "POSITION_TEMPLATE_LOAD", occurredAt, ct: ct);
        transaction.Commit();
    }

    private const string PositionSelect = """
        SELECT centro_id AS CenterId, nombre AS Name, una_persona AS OnePerson, superior_id AS SuperiorId, nota_coordinacion AS Note, estado AS StatusCode
          FROM dbo.cargos
        """;

    /// <summary>Quien gestiona sigue con su ámbito vigente, y se bloquea la fila del centro.</summary>
    private static async Task BeginAsync(SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, CancellationToken ct)
    {
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            "SELECT id FROM dbo.centros WITH (UPDLOCK, ROWLOCK) WHERE id = @CenterId",
            new { CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
    }

    private static async Task<PositionRow> LockPositionAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, Guid positionId, CancellationToken ct) =>
        await connection.QuerySingleOrDefaultAsync<PositionRow>(new CommandDefinition(
            PositionSelect.Replace("FROM dbo.cargos", "FROM dbo.cargos WITH (UPDLOCK, ROWLOCK)") + " WHERE id = @Id AND centro_id = @CenterId",
            new { Id = positionId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct))
        ?? throw new AccessDeniedException();

    private static async Task EnsureNameFreeAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, string name, Guid? except, CancellationToken ct)
    {
        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.cargos WHERE centro_id = @CenterId AND nombre = @Name AND id <> @Except",
                new { CenterId = access.CenterId.Value, Name = name, Except = except ?? Guid.Empty }, transaction, cancellationToken: ct)) > 0)
        {
            throw new DomainValidationException("POSITION_NAME_TAKEN");
        }
    }

    /// <summary>El superior, si lo hay, es un cargo del centro (y, al crear o con el cargo activo, activo).</summary>
    private static async Task EnsureSuperiorAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, Guid? superiorId, bool mustBeActive, CancellationToken ct,
        string failureCode = Position.InvalidCode)
    {
        if (superiorId is null)
        {
            return;
        }

        var status = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT estado FROM dbo.cargos WHERE id = @Id AND centro_id = @CenterId",
            new { Id = superiorId.Value, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        if (status is null || (mustBeActive && status != "ACTIVE"))
        {
            throw new DomainValidationException(failureCode);
        }
    }

    private static Task<int> OccupantCountAsync(SqlConnection connection, SqlTransaction transaction, Guid positionId, CancellationToken ct) =>
        connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.cargos_ocupantes WHERE cargo_id = @PositionId", new { PositionId = positionId }, transaction, cancellationToken: ct));

    /// <summary>Las unidades que se quieren ligar tienen que estar activas y concedidas al ámbito de quien gestiona.</summary>
    private static async Task EnsureUnitsInScopeAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, IReadOnlyList<UnitId> units, CancellationToken ct)
    {
        if (units.Count == 0)
        {
            return;
        }

        var valid = await connection.ExecuteScalarAsync<int>(new CommandDefinition($"""
            SELECT COUNT(*) FROM dbo.unidades unit
             WHERE unit.centro_id = @CenterId AND unit.id IN @Units AND unit.estado = 'ACTIVE' AND {SqlSchedulingDirectory.AdministratorUnitGrant("unit.id")}
            """, new { CenterId = access.CenterId.Value, access.ProfileScopeId, Units = units.Select(u => u.Value).ToList() }, transaction, cancellationToken: ct));
        if (valid != units.Count)
        {
            throw new DomainValidationException(Position.InvalidCode);
        }
    }

    /// <summary>Las unidades ligadas al cargo que están en el ámbito de quien gestiona (las demás no son suyas y no se tocan).</summary>
    private static async Task<IReadOnlyList<Guid>> ScopedLinksAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, Guid positionId, CancellationToken ct) =>
        (await connection.QueryAsync<Guid>(new CommandDefinition($"""
            SELECT link.unidad_id FROM dbo.cargos_unidades link
             WHERE link.cargo_id = @PositionId AND link.centro_id = @CenterId AND {SqlSchedulingDirectory.AdministratorUnitGrant("link.unidad_id")}
            """, new { PositionId = positionId, CenterId = access.CenterId.Value, access.ProfileScopeId }, transaction, cancellationToken: ct))).ToList();

    /// <summary>Deja ligadas, de las unidades del ámbito de quien gestiona, exactamente las pedidas; las ligas a otras unidades se conservan.</summary>
    private static async Task ReplaceUnitsAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, Guid positionId, IReadOnlyList<UnitId> units,
        DateTimeOffset occurredAt, CancellationToken ct)
    {
        var desired = units.Select(u => u.Value).ToHashSet();
        var current = await ScopedLinksAsync(connection, transaction, access, positionId, ct);
        var toRemove = current.Where(u => !desired.Contains(u)).ToList();
        if (toRemove.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM dbo.cargos_unidades WHERE cargo_id = @PositionId AND centro_id = @CenterId AND unidad_id IN @Units",
                new { PositionId = positionId, CenterId = access.CenterId.Value, Units = toRemove }, transaction, cancellationToken: ct));
        }

        foreach (var unit in desired.Where(u => !current.Contains(u)))
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.cargos_unidades (cargo_id, unidad_id, centro_id, ligada_en, ligada_por_cuenta_id)
                VALUES (@PositionId, @UnitId, @CenterId, @OccurredAt, @ActorId)
                """, new
            {
                PositionId = positionId, UnitId = unit, CenterId = access.CenterId.Value, OccurredAt = occurredAt, ActorId = access.AccountId.Value,
            }, transaction, cancellationToken: ct));
        }
    }

    private sealed record PositionRow(Guid CenterId, string Name, bool OnePerson, Guid? SuperiorId, string? Note, string StatusCode);

    private sealed record SuperiorRow(Guid Id, Guid? SuperiorId);
}
