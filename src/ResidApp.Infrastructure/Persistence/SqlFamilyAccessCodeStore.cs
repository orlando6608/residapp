using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// dbo.codigos_acceso_correo (script 0121), sin seguridad por filas, como dbo.credenciales. Pedir y comprobar un código bloquean antes la fila de credenciales de la cuenta, así
/// que dos peticiones simultáneas de la misma cuenta se ordenan y no se saltan un límite ni usan dos veces un código. La dirección es lo único que sí está bajo la seguridad por
/// filas: se lee abriendo la conexión con el ámbito familiar que la contraseña acaba de acreditar.
/// </summary>
public sealed class SqlFamilyAccessCodeStore(SqlConnectionFactory connections, string connectionString) : IFamilyAccessCodeStore
{
    private const string LockAccount = """
        SELECT codigo_correo_bloqueado_en FROM dbo.credenciales WITH (UPDLOCK, ROWLOCK, FORCESEEK) WHERE cuenta_id = @AccountId
        """;

    // FORCESEEK: buscar por la cuenta y no recorrer la tabla, para no bloquearse con las peticiones de otras cuentas (como en 0056).
    private const string CloseLive = """
        UPDATE code SET resumen = NULL, cerrado_en = @Now, motivo_cierre = N'ANULADO'
          FROM dbo.codigos_acceso_correo code WITH (FORCESEEK)
         WHERE code.cuenta_id = @AccountId AND code.cerrado_en IS NULL
        """;

    public async Task<string?> FindAddressAsync(
        string externalSubject, AccountId accountId, IReadOnlyList<ActiveProfileScope> scopes, CancellationToken ct = default)
    {
        var addresses = new List<string?>();
        foreach (var scope in scopes.Where(scope => scope.Profile == SystemProfile.Familiar))
        {
            var tenant = new FixedTenant(new TenantScope(externalSubject, scope.ProfileScopeId));
            using var connection = await new SqlConnectionFactory(connectionString, tenant).OpenAsync(ct);
            // La seguridad por filas filtra por centro, no por persona: el aislamiento entre familias lo pone esta consulta, que además solo trae el correo.
            addresses.AddRange(await connection.QueryAsync<string?>(new CommandDefinition(
                "SELECT correo FROM dbo.familiares WHERE cuenta_id = @AccountId AND centro_id = @CenterId",
                new { AccountId = accountId.Value, CenterId = scope.CenterId.Value }, cancellationToken: ct)));
        }

        var distinct = addresses.Select(address => address?.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return distinct is [{ Length: > 0 } only] ? only : null;
    }

    public async Task<AccessCodeReservation> ReserveAsync(NewAccessCode code, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var parameters = new
        {
            code.Id, AccountId = code.AccountId.Value, code.Digest, Now = code.CreatedAt, code.ExpiresAt, code.Language, code.TemplateVersion,
            HourAgo = code.CreatedAt.AddHours(-1), DayAgo = code.CreatedAt.AddDays(-1),
        };

        if (await connection.ExecuteScalarAsync<DateTime?>(new CommandDefinition(LockAccount, parameters, transaction, cancellationToken: ct)) is not null)
        {
            return AccessCodeReservation.Blocked;
        }

        var (lastSent, lastHour, lastDay) = await connection.QuerySingleAsync<(DateTime? LastSent, int LastHour, int LastDay)>(new CommandDefinition("""
            SELECT MAX(creado_en) AS LastSent, ISNULL(SUM(CASE WHEN creado_en > @HourAgo THEN 1 ELSE 0 END), 0) AS LastHour, COUNT(*) AS LastDay
              FROM dbo.codigos_acceso_correo WITH (FORCESEEK)
             WHERE cuenta_id = @AccountId AND creado_en > @DayAgo AND cuenta_para_limites = 1
            """, parameters, transaction, cancellationToken: ct));
        if (lastSent is { } previous && code.CreatedAt - previous < FamilyAccessCodeRules.WaitBetweenSends)
        {
            return AccessCodeReservation.TooSoon;
        }

        if (lastHour >= FamilyAccessCodeRules.SendsPerHour)
        {
            return AccessCodeReservation.HourLimit;
        }

        if (lastDay >= FamilyAccessCodeRules.SendsPerDay)
        {
            return AccessCodeReservation.DayLimit;
        }

        await connection.ExecuteAsync(new CommandDefinition(CloseLive + """
            ;
            INSERT INTO dbo.codigos_acceso_correo (id, cuenta_id, resumen, creado_en, caduca_en, plantilla_version, idioma)
            VALUES (@Id, @AccountId, @Digest, @Now, @ExpiresAt, @TemplateVersion, @Language)
            """, parameters, transaction, cancellationToken: ct));
        transaction.Commit();
        return AccessCodeReservation.Reserved;
    }

    public async Task RecordSendAsync(Guid codeId, string? operationId, string? failureCode, DateTime now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.codigos_acceso_correo
               SET envio_estado_codigo = CASE WHEN @FailureCode IS NULL THEN N'ENVIADO' ELSE N'FALLIDO' END,
                   envio_motivo_codigo = @FailureCode, acs_operacion_id = @OperationId
             WHERE id = @CodeId;
            IF @FailureCode IS NOT NULL
                UPDATE dbo.codigos_acceso_correo SET resumen = NULL, cerrado_en = @Now, motivo_cierre = N'NO_ENVIADO'
                 WHERE id = @CodeId AND cerrado_en IS NULL;
            """, new { CodeId = codeId, OperationId = operationId, FailureCode = failureCode, Now = now }, cancellationToken: ct));
    }

    public async Task<AccessCodeCheck> RedeemAsync(Guid codeId, AccountId accountId, byte[] digest, DateTime now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var parameters = new
        {
            CodeId = codeId, AccountId = accountId.Value, Digest = digest, Now = now, DayAgo = now.AddDays(-1),
        };

        if (await connection.ExecuteScalarAsync<DateTime?>(new CommandDefinition(LockAccount, parameters, transaction, cancellationToken: ct)) is not null)
        {
            return AccessCodeCheck.Blocked;
        }

        const string Live = " WHERE id = @CodeId AND cuenta_id = @AccountId AND cerrado_en IS NULL AND caduca_en > @Now";
        if (await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.codigos_acceso_correo SET resumen = NULL, cerrado_en = @Now, motivo_cierre = N'USADO'" + Live + " AND resumen = @Digest",
                parameters, transaction, cancellationToken: ct)) == 1)
        {
            transaction.Commit();
            return AccessCodeCheck.Valid;
        }

        if (await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.codigos_acceso_correo SET intentos_fallidos = intentos_fallidos + 1" + Live,
                parameters, transaction, cancellationToken: ct)) == 0)
        {
            return AccessCodeCheck.NotLive;
        }

        var (attempts, failuresToday) = await connection.QuerySingleAsync<(int Attempts, int FailuresToday)>(new CommandDefinition("""
            SELECT (SELECT CAST(intentos_fallidos AS INT) FROM dbo.codigos_acceso_correo WHERE id = @CodeId) AS Attempts,
                   (SELECT ISNULL(SUM(CAST(intentos_fallidos AS INT)), 0) FROM dbo.codigos_acceso_correo WITH (FORCESEEK)
                     WHERE cuenta_id = @AccountId AND creado_en > @DayAgo AND cuenta_para_limites = 1) AS FailuresToday
            """, parameters, transaction, cancellationToken: ct));
        var result = AccessCodeCheck.Wrong;
        if (attempts >= FamilyAccessCodeRules.AttemptsPerCode)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.codigos_acceso_correo SET resumen = NULL, cerrado_en = @Now, motivo_cierre = N'AGOTADO'" + Live,
                parameters, transaction, cancellationToken: ct));
            result = AccessCodeCheck.Exhausted;
        }

        if (failuresToday >= FamilyAccessCodeRules.FailuresPerDayBeforeBlock)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.credenciales SET codigo_correo_bloqueado_en = @Now WHERE cuenta_id = @AccountId;" + CloseLive,
                parameters, transaction, cancellationToken: ct));
            result = AccessCodeCheck.Blocked;
        }

        transaction.Commit();
        return result;
    }

    public async Task ResetAsync(AccountId accountId, DateTime now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await connection.ExecuteAsync(new CommandDefinition(
            LockAccount + ";" + CloseLive + ";" + """

            UPDATE code SET cuenta_para_limites = 0
              FROM dbo.codigos_acceso_correo code WITH (FORCESEEK)
             WHERE code.cuenta_id = @AccountId AND code.cuenta_para_limites = 1;
            UPDATE dbo.credenciales SET codigo_correo_bloqueado_en = NULL WHERE cuenta_id = @AccountId AND codigo_correo_bloqueado_en IS NOT NULL;
            """, new { AccountId = accountId.Value, Now = now }, transaction, cancellationToken: ct));
        transaction.Commit();
    }

    private sealed class FixedTenant(TenantScope scope) : ITenantContext
    {
        public Task<TenantScope?> GetAsync(CancellationToken ct = default) => Task.FromResult<TenantScope?>(scope);
    }
}

/// <summary>La purga de los códigos de acceso (script 0121): llama al procedimiento, que fija el plazo. La conexión es la de la tarea programada (rol residapp_tareas).</summary>
public sealed class SqlAccessCodePurge(SqlConnectionFactory connections) : IAccessCodePurge
{
    public async Task<bool> HasTaskIdentityAsync(CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(SqlChangeNoticeStore.IdentitySql, cancellationToken: ct)) == 1;
    }

    public async Task<int> PurgeAsync(CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition("EXEC dbo.purgar_codigos_acceso", commandTimeout: 120, cancellationToken: ct));
    }
}
