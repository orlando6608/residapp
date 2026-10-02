using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Families;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// ADM-08 a ADM-11 (script 0022): familiares, autorizaciones y contacto urgente de un residente ya autorizado. Cada
/// escritura va en una transacción con su evento en dbo.eventos_auditoria, sin los datos. Las autorizaciones y las
/// designaciones de contacto se cuentan por número (UX_fac_numero, UX_rcu_numero): si el número no es el que tenía la
/// pantalla, otro cambio se ha adelantado (conflicto).
/// </summary>
public sealed class SqlResidentFamilyRepository(SqlConnectionFactory connections) : IResidentFamilyRepository
{
    private const string InsertAudit = """
        INSERT INTO dbo.eventos_auditoria
            (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
        VALUES (NEWID(), @AccountId, 'ADMINISTRACION', @CenterId, @UnitId, @ResidentId, @ResourceType, @ResourceId, @Action, @OccurredAt);
        """;

    public async Task<Guid> AddAsync(
        AdministrativeResidentTarget target, Guid operationId, FamilyMemberData data, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var previous = await FindAddedLinkAsync(connection, null, target, operationId, ct);
        if (previous is not null)
        {
            return previous.Value;
        }

        var linkId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition($"""
                INSERT INTO dbo.familiares (id, centro_id, nombre_visible, telefono, correo, creado_por_cuenta_id, creado_en)
                VALUES (@FamilyId, @CenterId, @DisplayName, @Phone, @Email, @AccountId, @OccurredAt);

                INSERT INTO dbo.residentes_familiares
                    (id, centro_id, residente_id, familiar_id, relacion, vinculado_por_cuenta_id, vinculado_en)
                VALUES (@LinkId, @CenterId, @ResidentId, @FamilyId, @Relationship, @AccountId, @OccurredAt);

                {InsertAudit}
                """, new
            {
                FamilyId = operationId, LinkId = linkId, data.DisplayName, data.Phone, data.Email, data.Relationship,
                AccountId = target.AccountId.Value, CenterId = target.CenterId.Value, UnitId = target.UnitId.Value,
                ResidentId = target.ResidentId.Value, ResourceType = "FAMILY_MEMBER", ResourceId = operationId,
                Action = "FAMILY_MEMBER_CREATE", OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));
            transaction.Commit();
            return linkId;
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            // PK_familiares: un reenvío simultáneo del mismo formulario ya lo creó.
            transaction.Rollback();
            return await FindAddedLinkAsync(connection, null, target, operationId, ct)
                ?? throw new DomainValidationException("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST");
        }
    }

    public async Task UpdateAsync(
        AdministrativeResidentTarget target, Guid linkId, FamilyMemberData data, string expectedVersion, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var current = await connection.QuerySingleOrDefaultAsync<MemberRow>(new CommandDefinition("""
            SELECT f.id AS FamilyId, f.nombre_visible AS DisplayName, link.relacion AS Relationship, f.telefono AS Phone, f.correo AS Email
              FROM dbo.residentes_familiares link WITH (UPDLOCK, ROWLOCK)
              JOIN dbo.familiares f WITH (UPDLOCK, ROWLOCK) ON f.id = link.familiar_id AND f.centro_id = link.centro_id
             WHERE link.id = @LinkId AND link.residente_id = @ResidentId AND link.centro_id = @CenterId
            """, new { LinkId = linkId, ResidentId = target.ResidentId.Value, CenterId = target.CenterId.Value },
            transaction, cancellationToken: ct))
            ?? throw new AccessDeniedException();
        var currentData = new FamilyMemberData(current.DisplayName, current.Relationship, current.Phone, current.Email);
        if (!string.Equals(currentData.Version, expectedVersion, StringComparison.Ordinal))
        {
            throw new DomainValidationException("FAMILY_MEMBER_CONFLICT");
        }

        if (currentData.SameAs(data))
        {
            throw new DomainValidationException(FamilyMember.InvalidCode);
        }

        await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE dbo.familiares SET nombre_visible = @DisplayName, telefono = @Phone, correo = @Email
             WHERE id = @FamilyId AND centro_id = @CenterId;

            UPDATE dbo.residentes_familiares SET relacion = @Relationship WHERE id = @LinkId AND centro_id = @CenterId;

            {InsertAudit}
            """, new
        {
            current.FamilyId, LinkId = linkId, data.DisplayName, data.Phone, data.Email, data.Relationship,
            AccountId = target.AccountId.Value, CenterId = target.CenterId.Value, UnitId = target.UnitId.Value,
            ResidentId = target.ResidentId.Value, ResourceType = "FAMILY_MEMBER", ResourceId = current.FamilyId,
            Action = "FAMILY_MEMBER_UPDATE", OccurredAt = DateTimeOffset.UtcNow,
        }, transaction, cancellationToken: ct));
        transaction.Commit();
    }

    public async Task<int> ChangeAuthorizationAsync(
        AdministrativeResidentTarget target, Guid linkId, FamilyAuthorizationChange change, DateOnly? validUntil, string? reason,
        int expectedChanges, DateOnly today, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var parameters = new { LinkId = linkId, ResidentId = target.ResidentId.Value, CenterId = target.CenterId.Value };
        var linked = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dbo.residentes_familiares WITH (UPDLOCK, ROWLOCK)
             WHERE id = @LinkId AND residente_id = @ResidentId AND centro_id = @CenterId
            """, parameters, transaction, cancellationToken: ct));
        if (linked == 0)
        {
            throw new AccessDeniedException();
        }

        var latest = await connection.QuerySingleOrDefaultAsync<AuthorizationRow>(new CommandDefinition("""
            SELECT TOP 1 numero AS Number, estado_codigo AS StatusCode, valida_hasta AS ValidUntil
              FROM dbo.familiares_autorizaciones_cambios
             WHERE vinculo_id = @LinkId AND centro_id = @CenterId
             ORDER BY numero DESC
            """, parameters, transaction, cancellationToken: ct));
        var changes = latest?.Number ?? 0;
        if (changes != expectedChanges)
        {
            throw new DomainValidationException("FAMILY_AUTHORIZATION_CONFLICT");
        }

        FamilyAuthorizationStatus? effective = latest is null
            ? null
            : FamilyAuthorizationRules.Effective(
                EnumCode.ParseCode<FamilyAuthorizationStatus>(latest.StatusCode),
                latest.ValidUntil is { } until ? DateOnly.FromDateTime(until) : null, today);
        var (status, newValidUntil, newReason) = FamilyAuthorizationRules.Validate(effective, change, validUntil, reason, today);
        var number = changes + 1;
        try
        {
            await connection.ExecuteAsync(new CommandDefinition($"""
                INSERT INTO dbo.familiares_autorizaciones_cambios
                    (id, centro_id, vinculo_id, numero, estado_codigo, valida_hasta, motivo, registrado_por_cuenta_id,
                     registrado_por_perfil, registrado_en)
                VALUES (NEWID(), @CenterId, @LinkId, @Number, @StatusCode, @ValidUntil, @Reason, @AccountId, 'ADMINISTRACION', @OccurredAt);

                {InsertAudit}
                """, new
            {
                LinkId = linkId, Number = number, StatusCode = status.ToCode(), ValidUntil = newValidUntil, Reason = newReason,
                AccountId = target.AccountId.Value, CenterId = target.CenterId.Value, UnitId = target.UnitId.Value,
                ResidentId = target.ResidentId.Value, ResourceType = "FAMILY_AUTHORIZATION", ResourceId = linkId,
                Action = "FAMILY_AUTHORIZATION_CHANGE", OccurredAt = DateTimeOffset.UtcNow,
            }, transaction, cancellationToken: ct));
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            throw new DomainValidationException("FAMILY_AUTHORIZATION_CONFLICT");
        }

        transaction.Commit();
        return number;
    }

    public async Task<int> DesignateEmergencyContactAsync(
        AdministrativeResidentTarget target, Guid? linkId, int expectedDesignations, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var parameters = new { LinkId = linkId, ResidentId = target.ResidentId.Value, CenterId = target.CenterId.Value };
        // El bloqueo del residente ordena las designaciones simultáneas.
        await connection.ExecuteAsync(new CommandDefinition("""
            SELECT id FROM dbo.residentes WITH (UPDLOCK, ROWLOCK) WHERE id = @ResidentId AND centro_id = @CenterId
            """, parameters, transaction, cancellationToken: ct));
        var latest = await connection.QuerySingleOrDefaultAsync<DesignationRow>(new CommandDefinition("""
            SELECT TOP 1 numero AS Number, vinculo_id AS LinkId
              FROM dbo.residentes_contacto_urgente
             WHERE residente_id = @ResidentId AND centro_id = @CenterId
             ORDER BY numero DESC
            """, parameters, transaction, cancellationToken: ct));
        var designations = latest?.Number ?? 0;
        if (designations != expectedDesignations)
        {
            throw new DomainValidationException("EMERGENCY_CONTACT_CONFLICT");
        }

        if (linkId is not null && await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COUNT(*) FROM dbo.residentes_familiares
                 WHERE id = @LinkId AND residente_id = @ResidentId AND centro_id = @CenterId
                """, parameters, transaction, cancellationToken: ct)) == 0)
        {
            throw new AccessDeniedException();
        }

        if (linkId == latest?.LinkId)
        {
            // Designar el que ya está, o quitarlo cuando no hay ninguno.
            throw new DomainValidationException("EMERGENCY_CONTACT_INVALID");
        }

        var number = designations + 1;
        try
        {
            await connection.ExecuteAsync(new CommandDefinition($"""
                INSERT INTO dbo.residentes_contacto_urgente
                    (id, centro_id, residente_id, numero, vinculo_id, designado_por_cuenta_id, designado_por_perfil, designado_en)
                VALUES (NEWID(), @CenterId, @ResidentId, @Number, @LinkId, @AccountId, 'ADMINISTRACION', @OccurredAt);

                {InsertAudit}
                """, new
            {
                LinkId = linkId, Number = number, AccountId = target.AccountId.Value, CenterId = target.CenterId.Value,
                UnitId = target.UnitId.Value, ResidentId = target.ResidentId.Value, ResourceType = "RESIDENT",
                ResourceId = target.ResidentId.Value, Action = "EMERGENCY_CONTACT_DESIGNATE", OccurredAt = DateTimeOffset.UtcNow,
            }, transaction, cancellationToken: ct));
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            throw new DomainValidationException("EMERGENCY_CONTACT_CONFLICT");
        }

        transaction.Commit();
        return number;
    }

    /// <summary>El vínculo de un familiar ya creado con este identificador de operación; null si no existe. Si existe pero
    /// es de otro residente u otro centro, el identificador se ha reutilizado para otra petición.</summary>
    private static async Task<Guid?> FindAddedLinkAsync(
        SqlConnection connection, SqlTransaction? transaction, AdministrativeResidentTarget target, Guid operationId, CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<AddedRow>(new CommandDefinition("""
            SELECT f.centro_id AS CenterId, link.id AS LinkId
              FROM dbo.familiares f
              LEFT JOIN dbo.residentes_familiares link
                     ON link.familiar_id = f.id AND link.centro_id = f.centro_id AND link.residente_id = @ResidentId
             WHERE f.id = @FamilyId
            """, new { FamilyId = operationId, ResidentId = target.ResidentId.Value }, transaction, cancellationToken: ct));
        if (row is null)
        {
            return null;
        }

        return row.CenterId == target.CenterId.Value && row.LinkId is { } link
            ? link
            : throw new DomainValidationException("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST");
    }

    private sealed record MemberRow(Guid FamilyId, string DisplayName, string Relationship, string Phone, string? Email);

    private sealed record AuthorizationRow(int Number, string StatusCode, DateTime? ValidUntil);

    private sealed record DesignationRow(int Number, Guid? LinkId);

    private sealed record AddedRow(Guid CenterId, Guid? LinkId);
}
