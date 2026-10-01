using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Platform;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Alta de un centro por el operador de plataforma (script 0026). Una sola transacción que:
///   1. vuelve a comprobar que quien opera tiene su ámbito de Plataforma vigente en el centro reservado, con la cuenta activa;
///   2. bloquea la fila del centro reservado (UPDLOCK), lo que ordena las altas simultáneas y hace fiable la comprobación de que el
///      código del centro y el identificador del administrador no se repiten;
///   3. escribe el centro, su primera unidad, la cuenta del primer administrador, su ámbito de Administración y la concesión de la
///      unidad, y deja su auditoría en dbo.eventos_auditoria, sin datos. Si algo falla, no queda nada.
/// </summary>
public sealed class SqlPlatformCenterRepository(SqlConnectionFactory connections) : IPlatformCenterRepository
{
    private const string InsertAudit = """
        INSERT INTO dbo.eventos_auditoria
            (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
        VALUES (NEWID(), @ActorId, 'PLATAFORMA', @CenterId, @UnitId, NULL, @ResourceType, @ResourceId, @Action, @OccurredAt);
        """;

    public async Task<CenterId> CreateCenterAsync(PlatformAccess access, Guid operationId, NewCenterData data, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await EnsureOperatorAsync(connection, transaction, access, ct);
        await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            "SELECT id FROM dbo.centros WITH (UPDLOCK, ROWLOCK) WHERE id = @PlatformCenterId",
            new { PlatformCenterId = PlatformCenter.Id }, transaction, cancellationToken: ct));

        var previous = await connection.QuerySingleOrDefaultAsync<PreviousRow>(new CommandDefinition("""
            SELECT center.codigo AS Code, center.nombre_visible AS Name,
                   (SELECT TOP (1) unit.codigo FROM dbo.unidades unit WHERE unit.centro_id = center.id ORDER BY unit.creado_en) AS UnitCode,
                   (SELECT TOP (1) account.sujeto_externo
                      FROM dbo.ambitos_perfil profile JOIN dbo.cuentas account ON account.id = profile.cuenta_id
                     WHERE profile.centro_id = center.id AND profile.perfil_codigo = 'ADMINISTRACION' ORDER BY profile.concedido_en) AS AdminSubject
              FROM dbo.centros center WHERE center.id = @Id
            """, new { Id = operationId }, transaction, cancellationToken: ct));
        if (previous is not null)
        {
            return string.Equals(previous.Code, data.CenterCode, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(previous.Name, data.CenterName, StringComparison.Ordinal)
                   && string.Equals(previous.UnitCode, data.Unit.Code, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(previous.AdminSubject, data.Administrator.Subject, StringComparison.OrdinalIgnoreCase)
                ? CenterId.From(operationId)
                : throw new DomainValidationException("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST");
        }

        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.centros WHERE codigo = @Code", new { Code = data.CenterCode }, transaction, cancellationToken: ct)) > 0)
        {
            throw new DomainValidationException("CENTER_CODE_TAKEN");
        }

        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.cuentas WHERE sujeto_externo = @Subject", new { data.Administrator.Subject }, transaction, cancellationToken: ct)) > 0)
        {
            throw new DomainValidationException("ACCOUNT_SUBJECT_TAKEN");
        }

        var occurredAt = DateTimeOffset.UtcNow;
        var unitId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var profileScopeId = Guid.NewGuid();
        var parameters = new
        {
            CenterId = operationId, UnitId = unitId, AdminId = adminId, ProfileScopeId = profileScopeId, ActorId = access.AccountId.Value,
            CenterCode = data.CenterCode, CenterName = data.CenterName, UnitCode = data.Unit.Code, UnitName = data.Unit.Name,
            data.Administrator.Subject, AdminName = data.Administrator.DisplayName, OccurredAt = occurredAt,
        };
        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en) VALUES (@CenterId, @CenterCode, @CenterName, 'ACTIVE', @OccurredAt);
                INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en) VALUES (@UnitId, @CenterId, @UnitCode, @UnitName, 'ACTIVE', @OccurredAt);
                INSERT INTO dbo.cuentas (id, sujeto_externo, nombre_visible, estado, creado_en) VALUES (@AdminId, @Subject, @AdminName, 'ACTIVE', @OccurredAt);
                INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
                VALUES (@ProfileScopeId, @AdminId, @CenterId, 'ADMINISTRACION', 'ACTIVE', @OccurredAt, @ActorId);
                INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @ProfileScopeId, @CenterId, @UnitId, @OccurredAt, @ActorId);
                """, parameters, transaction, cancellationToken: ct));
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            throw new DomainValidationException(error.Message.Contains("UX_centers_code", StringComparison.Ordinal)
                ? "CENTER_CODE_TAKEN"
                : "ACCOUNT_SUBJECT_TAKEN");
        }

        await AuditAsync(connection, transaction, access, operationId, null, "CENTER", operationId, "CENTER_CREATE", occurredAt, ct);
        await AuditAsync(connection, transaction, access, operationId, unitId, "UNIT", unitId, "UNIT_CREATE", occurredAt, ct);
        await AuditAsync(connection, transaction, access, operationId, null, "ACCOUNT", adminId, "ACCOUNT_CREATE", occurredAt, ct);
        await AuditAsync(connection, transaction, access, operationId, null, "PROFILE_SCOPE", profileScopeId, "PROFILE_SCOPE_GRANT", occurredAt, ct);
        await AuditAsync(connection, transaction, access, operationId, unitId, "PROFILE_SCOPE", profileScopeId, "PROFILE_UNIT_GRANT", occurredAt, ct);
        transaction.Commit();
        return CenterId.From(operationId);
    }

    /// <summary>Quien opera sigue teniendo su ámbito de Plataforma vigente en el centro reservado, con la cuenta y el centro
    /// activos; si no, acceso denegado.</summary>
    internal static async Task EnsureOperatorAsync(SqlConnection connection, SqlTransaction? transaction, PlatformAccess access, CancellationToken ct)
    {
        var allowed = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*)
              FROM dbo.cuentas account
              JOIN dbo.ambitos_perfil profile ON profile.cuenta_id = account.id AND profile.id = @ProfileScopeId
                   AND profile.centro_id = @PlatformCenterId AND profile.perfil_codigo = 'PLATAFORMA'
                   AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
              JOIN dbo.centros center ON center.id = profile.centro_id AND center.estado = 'ACTIVE'
             WHERE account.id = @AccountId AND account.estado = 'ACTIVE'
            """, new { access.ProfileScopeId, PlatformCenterId = PlatformCenter.Id, AccountId = access.AccountId.Value },
            transaction, cancellationToken: ct));
        if (allowed == 0)
        {
            throw new AccessDeniedException();
        }
    }

    private static Task AuditAsync(
        SqlConnection connection, SqlTransaction transaction, PlatformAccess access, Guid centerId, Guid? unitId, string resourceType,
        Guid resourceId, string action, DateTimeOffset occurredAt, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition(InsertAudit, new
        {
            ActorId = access.AccountId.Value, CenterId = centerId, UnitId = unitId, ResourceType = resourceType, ResourceId = resourceId,
            Action = action, OccurredAt = occurredAt,
        }, transaction, cancellationToken: ct));

    private sealed record PreviousRow(string Code, string Name, string? UnitCode, string? AdminSubject);
}
