using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Platform;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Zona horaria por centro (script 0109). <c>zonas_centro</c> es de solo añadir: la zona vigente es la fila de mayor número y un centro sin fila usa Europe/Madrid. Lo que se vería
/// afectado al cambiar la zona (franjas de citas y citas futuras) lo cuenta el procedimiento <c>recuento_citas_centro</c>, porque esas tablas solo las ve el centro propio.
/// </summary>
public sealed class SqlCenterTimeZones(SqlConnectionFactory connections) : ICenterTimeZoneDirectory, ICenterTimeZoneRepository
{
    public async Task<IReadOnlyList<CenterTimeZone>> ListCatalogAsync(CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return (await connection.QueryAsync<CenterTimeZone>(new CommandDefinition(
            "SELECT codigo AS Code, etiqueta AS Label FROM dbo.zonas_horarias WHERE activa = 1 ORDER BY orden", cancellationToken: ct))).ToList();
    }

    public async Task<string?> FindZoneCodeForScopeAsync(string externalSubject, Guid profileScopeId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT ISNULL((SELECT TOP (1) zone.zona_codigo FROM dbo.zonas_centro zone WHERE zone.centro_id = profile.centro_id ORDER BY zone.numero DESC), @DefaultZone)
              FROM dbo.cuentas account
              JOIN dbo.ambitos_perfil profile ON profile.cuenta_id = account.id AND profile.id = @ProfileScopeId AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
             WHERE account.sujeto_externo = @ExternalSubject AND account.estado = 'ACTIVE'
            """, new { ExternalSubject = externalSubject, ProfileScopeId = profileScopeId, DefaultZone = CenterTimeZoneRules.DefaultZoneCode }, cancellationToken: ct));
    }

    public async Task<CenterTimeZoneState> GetAsync(PlatformAccess access, CenterId centerId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlPlatformCenterRepository.EnsureOperatorAsync(connection, null, access, ct);
        await EnsureCenterAsync(connection, null, centerId, ct);
        var rows = (await connection.QueryAsync<HistoryRow>(new CommandDefinition(
            """
            SELECT change.numero AS Number, change.zona_codigo AS ZoneCode, zone.etiqueta AS ZoneLabel, change.motivo_codigo AS Reason,
                   change.franjas_afectadas AS Slots, change.citas_afectadas AS Appointments, change.cambiado_en AS ChangedAt
              FROM dbo.zonas_centro change JOIN dbo.zonas_horarias zone ON zone.codigo = change.zona_codigo
             WHERE change.centro_id = @CenterId
             ORDER BY change.numero DESC
            """, new { CenterId = centerId.Value }, cancellationToken: ct))).ToList();
        var catalog = await ListCatalogAsync(ct);
        var currentCode = rows.Count > 0 ? rows[0].ZoneCode : CenterTimeZoneRules.DefaultZoneCode;
        var current = catalog.FirstOrDefault(zone => zone.Code == currentCode)
            ?? new CenterTimeZone(currentCode, rows.Count > 0 ? rows[0].ZoneLabel : currentCode);
        var history = rows.Select(row => new CenterTimeZoneChange(
            row.Number, row.ZoneCode, row.ZoneLabel, row.Reason, new CenterTimeZoneImpact(row.Slots, row.Appointments),
            new DateTimeOffset(DateTime.SpecifyKind(row.ChangedAt, DateTimeKind.Utc)))).ToList();
        return new CenterTimeZoneState(current, history, await CountAffectedAsync(connection, null, centerId, ct));
    }

    public async Task ChangeAsync(
        PlatformAccess access, CenterId centerId, string zoneCode, CenterTimeZoneChangeReason reason, CenterTimeZoneImpact confirmed, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlPlatformCenterRepository.EnsureOperatorAsync(connection, transaction, access, ct);
        // Bloquea el centro: ordena los cambios simultáneos y hace fiable la numeración.
        await EnsureCenterAsync(connection, transaction, centerId, ct, lockRow: true);

        var last = await connection.QuerySingleOrDefaultAsync<LastRow>(new CommandDefinition(
            "SELECT TOP (1) numero AS Number, zona_codigo AS ZoneCode FROM dbo.zonas_centro WHERE centro_id = @CenterId ORDER BY numero DESC",
            new { CenterId = centerId.Value }, transaction, cancellationToken: ct));
        var currentCode = last?.ZoneCode ?? CenterTimeZoneRules.DefaultZoneCode;
        if (zoneCode == currentCode)
        {
            throw new DomainStateException("CENTER_TIMEZONE_CONFLICT");
        }

        var affected = await CountAffectedAsync(connection, transaction, centerId, ct);
        if (affected != confirmed)
        {
            throw new DomainStateException("CENTER_TIMEZONE_IMPACT_CHANGED");
        }

        var occurredAt = DateTime.UtcNow;
        var number = (last?.Number ?? 0) + 1;
        if (last is null)
        {
            // Un centro que existía antes de las zonas no tiene fila: su zona era la de la península. Se anota como alta para que la historia empiece donde debe.
            await InsertAsync(connection, transaction, centerId, 1, CenterTimeZoneRules.DefaultZoneCode, "ALTA", new CenterTimeZoneImpact(0, 0), access, occurredAt, ct);
            number = 2;
        }

        var id = await InsertAsync(connection, transaction, centerId, number, zoneCode, reason.ToCode(), affected, access, occurredAt, ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.eventos_auditoria (id, cuenta_id, perfil_activo, centro_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
            VALUES (NEWID(), @ActorId, 'PLATAFORMA', @CenterId, 'CENTER_TIMEZONE', @Id, 'CENTER_TIMEZONE_CHANGE', @OccurredAt)
            """, new { ActorId = access.AccountId.Value, CenterId = centerId.Value, Id = id, OccurredAt = occurredAt }, transaction, cancellationToken: ct));
        transaction.Commit();
    }

    /// <summary>La fila de alta de un centro nuevo, dentro de la transacción de su creación.</summary>
    internal static Task<Guid> InsertCreationAsync(
        SqlConnection connection, SqlTransaction transaction, CenterId centerId, string zoneCode, PlatformAccess access, DateTimeOffset occurredAt, CancellationToken ct) =>
        InsertAsync(connection, transaction, centerId, 1, zoneCode, "ALTA", new CenterTimeZoneImpact(0, 0), access, occurredAt.UtcDateTime, ct);

    private static async Task<Guid> InsertAsync(
        SqlConnection connection, SqlTransaction transaction, CenterId centerId, int number, string zoneCode, string reasonCode, CenterTimeZoneImpact impact,
        PlatformAccess access, DateTime occurredAt, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.zonas_centro (id, centro_id, numero, zona_codigo, motivo_codigo, franjas_afectadas, citas_afectadas, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (@Id, @CenterId, @Number, @ZoneCode, @Reason, @Slots, @Appointments, @ActorId, 'PLATAFORMA', @OccurredAt)
            """, new
        {
            Id = id, CenterId = centerId.Value, Number = number, ZoneCode = zoneCode, Reason = reasonCode, impact.Slots, impact.Appointments,
            ActorId = access.AccountId.Value, OccurredAt = occurredAt,
        }, transaction, cancellationToken: ct));
        return id;
    }

    private static async Task EnsureCenterAsync(SqlConnection connection, SqlTransaction? transaction, CenterId centerId, CancellationToken ct, bool lockRow = false)
    {
        var found = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(*) FROM dbo.centros {(lockRow ? "WITH (UPDLOCK, ROWLOCK)" : "")} WHERE id = @CenterId AND codigo <> @PlatformCode",
            new { CenterId = centerId.Value, PlatformCode = PlatformCenter.Code }, transaction, cancellationToken: ct));
        if (found == 0)
        {
            throw new DomainValidationException("CENTER_TIMEZONE_CENTER_INVALID");
        }
    }

    private static async Task<CenterTimeZoneImpact> CountAffectedAsync(SqlConnection connection, SqlTransaction? transaction, CenterId centerId, CancellationToken ct)
    {
        var row = await connection.QuerySingleAsync<CountRow>(new CommandDefinition(
            "EXEC dbo.recuento_citas_centro @CenterId", new { CenterId = centerId.Value }, transaction, cancellationToken: ct));
        return new CenterTimeZoneImpact(row.Franjas, row.Citas);
    }

    private sealed record LastRow(int Number, string ZoneCode);

    private sealed record CountRow(int Franjas, int Citas);

    private sealed record HistoryRow(int Number, string ZoneCode, string ZoneLabel, string Reason, int Slots, int Appointments, DateTime ChangedAt);
}
