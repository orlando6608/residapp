using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Structure;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// ADM-05 (0025): escrituras sobre las unidades del centro. Cada una va en una transacción que:
///   1. vuelve a comprobar que quien gestiona tiene su ámbito de Administración vigente (la misma comprobación que
///      SqlProfessionalAccountRepository);
///   2. bloquea la fila del centro (UPDLOCK), lo que ordena los cambios de estructura simultáneos y hace fiable la comprobación
///      de que el código y el nombre no se repiten;
///   3. escribe y deja su evento en dbo.eventos_auditoria, sin datos.
/// Renombrar e inactivar solo valen sobre unidades concedidas al ámbito de quien gestiona; una unidad ajena o inexistente da
/// acceso denegado, sin distinguirlas. TR_units_guard (0025) impide cambiar el código y TR_units_no_delete borrar.
/// </summary>
public sealed class SqlCenterStructureRepository(SqlConnectionFactory connections) : ICenterStructureRepository
{
    public async Task<UnitId> CreateUnitAsync(
        AccountAdministrationAccess access, Guid operationId, CenterUnitData data, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        await LockCenterAsync(connection, transaction, access, ct);

        var previous = await connection.QuerySingleOrDefaultAsync<UnitRow>(new CommandDefinition("""
            SELECT centro_id AS CenterId, codigo AS Code, nombre_visible AS Name, estado AS StatusCode FROM dbo.unidades WHERE id = @Id
            """, new { Id = operationId }, transaction, cancellationToken: ct));
        if (previous is not null)
        {
            return previous.CenterId == access.CenterId.Value
                   && string.Equals(previous.Code, data.Code, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(previous.Name, data.Name, StringComparison.Ordinal)
                ? UnitId.From(operationId)
                : throw new DomainValidationException("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST");
        }

        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.unidades WHERE centro_id = @CenterId AND codigo = @Code",
                new { CenterId = access.CenterId.Value, data.Code }, transaction, cancellationToken: ct)) > 0)
        {
            throw new DomainValidationException("UNIT_CODE_TAKEN");
        }

        await EnsureNameFreeAsync(connection, transaction, access, data.Name, null, ct);

        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en)
            VALUES (@UnitId, @CenterId, @Code, @Name, 'ACTIVE', @OccurredAt);

            INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @ProfileScopeId, @CenterId, @UnitId, @OccurredAt, @ActorId);
            """, new
        {
            UnitId = operationId, CenterId = access.CenterId.Value, data.Code, data.Name, OccurredAt = occurredAt,
            access.ProfileScopeId, ActorId = access.AccountId.Value,
        }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "UNIT", operationId, "UNIT_CREATE", occurredAt, operationId, ct: ct);
        transaction.Commit();
        return UnitId.From(operationId);
    }

    public async Task RenameUnitAsync(AccountAdministrationAccess access, UnitId unitId, string name, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        await LockCenterAsync(connection, transaction, access, ct);
        var unit = await LockUnitAsync(connection, transaction, access, unitId, ct);
        if (string.Equals(unit.Name, name, StringComparison.Ordinal))
        {
            throw new DomainValidationException(CenterUnit.InvalidCode);
        }

        await EnsureNameFreeAsync(connection, transaction, access, name, unitId, ct);
        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.unidades SET nombre_visible = @Name WHERE id = @UnitId AND centro_id = @CenterId",
            new { Name = name, UnitId = unitId.Value, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "UNIT", unitId.Value, "UNIT_RENAME", occurredAt, unitId.Value, ct: ct);
        transaction.Commit();
    }

    public async Task ChangeUnitStatusAsync(AccountAdministrationAccess access, UnitId unitId, bool active, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        await LockCenterAsync(connection, transaction, access, ct);
        var unit = await LockUnitAsync(connection, transaction, access, unitId, ct);
        if ((unit.StatusCode == "ACTIVE") == active)
        {
            throw new DomainValidationException("UNIT_CHANGE_CONFLICT");
        }

        if (!active && await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COUNT(*) FROM dbo.intervalos_ubicacion_residente
                 WHERE centro_id = @CenterId AND unidad_id = @UnitId AND vigente_hasta IS NULL
                """, new { CenterId = access.CenterId.Value, UnitId = unitId.Value }, transaction, cancellationToken: ct)) > 0)
        {
            throw new DomainValidationException("UNIT_HAS_RESIDENTS");
        }

        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.unidades SET estado = @StatusCode WHERE id = @UnitId AND centro_id = @CenterId",
            new { StatusCode = active ? "ACTIVE" : "INACTIVE", UnitId = unitId.Value, CenterId = access.CenterId.Value },
            transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "UNIT", unitId.Value,
            active ? "UNIT_ACTIVATE" : "UNIT_DEACTIVATE", occurredAt, unitId.Value, ct: ct);
        transaction.Commit();
    }

    private static Task LockCenterAsync(SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, CancellationToken ct) =>
        connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            "SELECT id FROM dbo.centros WITH (UPDLOCK, ROWLOCK) WHERE id = @CenterId",
            new { CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));

    /// <summary>Bloquea la unidad si está concedida, sin revocar, al ámbito de quien gestiona; si no, acceso denegado.</summary>
    private static async Task<UnitRow> LockUnitAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, UnitId unitId, CancellationToken ct) =>
        await connection.QuerySingleOrDefaultAsync<UnitRow>(new CommandDefinition("""
            SELECT unit.centro_id AS CenterId, unit.codigo AS Code, unit.nombre_visible AS Name, unit.estado AS StatusCode
              FROM dbo.unidades unit WITH (UPDLOCK, ROWLOCK)
             WHERE unit.id = @UnitId AND unit.centro_id = @CenterId
               AND EXISTS (SELECT 1 FROM dbo.ambitos_perfil_unidad unit_scope
                            WHERE unit_scope.ambito_perfil_id = @ProfileScopeId AND unit_scope.centro_id = @CenterId
                              AND unit_scope.unidad_id = unit.id AND unit_scope.revocado_en IS NULL)
            """, new { UnitId = unitId.Value, CenterId = access.CenterId.Value, access.ProfileScopeId }, transaction, cancellationToken: ct))
        ?? throw new AccessDeniedException();

    /// <summary>Ninguna otra unidad del centro (Except es la propia, al renombrar) lleva ya ese nombre.</summary>
    private static async Task EnsureNameFreeAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, string name, UnitId? except, CancellationToken ct)
    {
        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COUNT(*) FROM dbo.unidades WHERE centro_id = @CenterId AND nombre_visible = @Name AND id <> @Except
                """, new { CenterId = access.CenterId.Value, Name = name, Except = except?.Value ?? Guid.Empty }, transaction, cancellationToken: ct)) > 0)
        {
            throw new DomainValidationException("UNIT_NAME_TAKEN");
        }
    }

    private sealed record UnitRow(Guid CenterId, string Code, string Name, string StatusCode);
}
