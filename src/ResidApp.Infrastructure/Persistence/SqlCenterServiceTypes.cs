using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Domain.Platform;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// El tipo de servicio de un centro (ADR 0017, script 0113). La fuente de verdad es la columna <c>centros.tipo_de_servicio</c>, que la aplicación no puede escribir (DENY de columna): el único
/// cambio es el procedimiento <c>corregir_tipo_servicio_centro</c>, que comprueba el ámbito de Plataforma y que el centro no tiene personas. <c>tipos_servicio_centro</c> es la constancia de solo añadir.
/// «Tiene personas» lo contesta <c>centro_tiene_personas</c> (sí o no, nunca cuántas).
/// </summary>
public sealed class SqlCenterServiceTypes(SqlConnectionFactory connections) : ICenterServiceTypeRepository, ICenterServiceTypeDirectory
{
    public async Task<CenterServiceType?> FindForScopeAsync(string externalSubject, Guid profileScopeId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var code = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT center.tipo_de_servicio
              FROM dbo.cuentas account
              JOIN dbo.ambitos_perfil profile ON profile.cuenta_id = account.id AND profile.id = @ProfileScopeId AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
              JOIN dbo.centros center ON center.id = profile.centro_id AND center.codigo <> @PlatformCode
             WHERE account.sujeto_externo = @ExternalSubject AND account.estado = 'ACTIVE'
            """, new { ExternalSubject = externalSubject, ProfileScopeId = profileScopeId, PlatformCode = PlatformCenter.Code }, cancellationToken: ct));
        return code is null ? null : EnumCode.ParseCode<CenterServiceType>(code);
    }

    public async Task<CenterServiceTypeState> GetAsync(PlatformAccess access, CenterId centerId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlPlatformCenterRepository.EnsureOperatorAsync(connection, null, access, ct);
        var current = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT tipo_de_servicio FROM dbo.centros WHERE id = @CenterId AND codigo <> @PlatformCode",
            new { CenterId = centerId.Value, PlatformCode = PlatformCenter.Code }, cancellationToken: ct))
            ?? throw new DomainValidationException("CENTER_SERVICE_TYPE_CENTER_INVALID");
        var rows = (await connection.QueryAsync<HistoryRow>(new CommandDefinition(
            """
            SELECT numero AS Number, tipo_codigo AS TypeCode, motivo_codigo AS Reason, cambiado_en AS ChangedAt
              FROM dbo.tipos_servicio_centro WHERE centro_id = @CenterId ORDER BY numero DESC
            """, new { CenterId = centerId.Value }, cancellationToken: ct))).ToList();
        var hasPeople = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "EXEC dbo.centro_tiene_personas @CenterId", new { CenterId = centerId.Value }, cancellationToken: ct));
        var history = rows.Select(row => new CenterServiceTypeChange(
            row.Number, EnumCode.ParseCode<CenterServiceType>(row.TypeCode), row.Reason, new DateTimeOffset(DateTime.SpecifyKind(row.ChangedAt, DateTimeKind.Utc)))).ToList();
        return new CenterServiceTypeState(EnumCode.ParseCode<CenterServiceType>(current), history, hasPeople);
    }

    public async Task ChangeAsync(PlatformAccess access, CenterId centerId, CenterServiceType type, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlPlatformCenterRepository.EnsureOperatorAsync(connection, transaction, access, ct);
        // Todo lo importante lo hace el procedimiento, con permisos del dueño: comprueba el ámbito de Plataforma, que el centro está vacío, deja la constancia y la auditoría. Si falla, se deshace todo.
        await connection.ExecuteAsync(new CommandDefinition(
            "EXEC dbo.corregir_tipo_servicio_centro @CenterId, @Type, @ActorId",
            new { CenterId = centerId.Value, Type = type.ToCode(), ActorId = access.AccountId.Value }, transaction, cancellationToken: ct));
        transaction.Commit();
    }

    /// <summary>La fila de alta de un centro nuevo (fila 1, motivo ALTA), dentro de la transacción de su creación y después de insertar el centro con ese mismo tipo.</summary>
    internal static Task InsertCreationAsync(
        SqlConnection connection, SqlTransaction transaction, CenterId centerId, CenterServiceType type, PlatformAccess access, DateTimeOffset occurredAt, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.tipos_servicio_centro (id, centro_id, numero, tipo_codigo, motivo_codigo, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (NEWID(), @CenterId, 1, @Type, 'ALTA', @ActorId, 'PLATAFORMA', @OccurredAt)
            """, new { CenterId = centerId.Value, Type = type.ToCode(), ActorId = access.AccountId.Value, OccurredAt = occurredAt.UtcDateTime }, transaction, cancellationToken: ct));

    private sealed record HistoryRow(int Number, string TypeCode, string Reason, DateTime ChangedAt);
}
