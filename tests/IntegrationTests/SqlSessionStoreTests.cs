using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Application.Sessions;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>dbo.sesiones (0061) contra SQL real: abrir, leer, anotar actividad y cerrar, y las guardas del script (una sesión cerrada no
/// se reabre ni cambia, la caducidad absoluta no se mueve y no se borra ninguna).</summary>
public class SqlSessionStoreTests
{
    private static async Task<(Guid AccountId, string Subject)> SeedAccountAsync()
    {
        var accountId = Guid.NewGuid();
        var subject = $"sesion-{Guid.NewGuid():N}";
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@accountId, @subject, 'ACTIVE', SYSUTCDATETIME())",
            new { accountId, subject });
        return (accountId, subject);
    }

    private static readonly DateTime T0 = new(2026, 10, 21, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AbrirLeerTocarYCerrar_ConLaCuentaYSuEstado()
    {
        var (accountId, subject) = await SeedAccountAsync();
        var store = new SqlSessionStore(TestDatabase.ConnectionFactory);
        var id = Guid.NewGuid();

        await store.OpenAsync(id, AccountId.From(accountId), T0, T0.AddHours(12));
        var opened = await store.FindAsync(id);
        await store.TouchAsync(id, T0.AddMinutes(5));
        await store.TouchAsync(id, T0.AddMinutes(2)); // más antigua: no retrocede
        var touched = await store.FindAsync(id);
        await store.CloseAsync(id, SessionCloseReason.Logout, T0.AddMinutes(6));
        await store.CloseAsync(id, SessionCloseReason.Inactividad, T0.AddMinutes(9)); // ya cerrada: no cambia
        await store.TouchAsync(id, T0.AddMinutes(10)); // ya cerrada: no cambia
        var closed = await store.FindAsync(id);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var reason = await connection.ExecuteScalarAsync<string>("SELECT motivo_cierre FROM dbo.sesiones WHERE id = @id", new { id });

        Assert.Null(await store.FindAsync(Guid.NewGuid()));
        Assert.Equal(subject, opened!.ExternalSubject);
        Assert.True(opened.AccountActive);
        Assert.Equal(T0.AddHours(12), opened.ExpiresAt);
        Assert.Equal(T0, opened.LastActivityAt);
        Assert.Null(opened.ClosedAt);
        Assert.Equal(T0.AddMinutes(5), touched!.LastActivityAt);
        Assert.Equal(T0.AddMinutes(6), closed!.ClosedAt);
        Assert.Equal(T0.AddMinutes(5), closed.LastActivityAt);
        Assert.Equal("LOGOUT", reason);
    }

    [Fact]
    public async Task CuentaSuspendida_LaSesionLoDice()
    {
        var (accountId, _) = await SeedAccountAsync();
        var store = new SqlSessionStore(TestDatabase.ConnectionFactory);
        var id = Guid.NewGuid();
        await store.OpenAsync(id, AccountId.From(accountId), T0, T0.AddHours(12));
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("UPDATE dbo.cuentas SET estado = 'SUSPENDED' WHERE id = @accountId", new { accountId });
        }

        Assert.False((await store.FindAsync(id))!.AccountActive);
    }

    [Fact]
    public async Task LasGuardasDelScript_ImpidenReabrirAmpliarYBorrar()
    {
        var (accountId, _) = await SeedAccountAsync();
        var store = new SqlSessionStore(TestDatabase.ConnectionFactory);
        var id = Guid.NewGuid();
        await store.OpenAsync(id, AccountId.From(accountId), T0, T0.AddHours(12));
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        var extend = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.sesiones SET caduca_en = DATEADD(HOUR, 1, caduca_en) WHERE id = @id", new { id }));
        await store.CloseAsync(id, SessionCloseReason.Logout, T0.AddMinutes(1));
        var reopen = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.sesiones SET cerrada_en = NULL, motivo_cierre = NULL WHERE id = @id", new { id }));
        var delete = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.sesiones WHERE id = @id", new { id }));

        Assert.Contains("SESSION_UPDATE_INVALID", extend.Message);
        Assert.Contains("SESSION_UPDATE_INVALID", reopen.Message);
        Assert.Contains("SESSION_DELETE_FORBIDDEN", delete.Message);
    }

    /// <summary>ADR 0007, I3 (script 0064): «Mi cuenta» lista las sesiones sin cerrar ni caducar con la IP de su entrada, y solo cierra las suyas.</summary>
    [Fact]
    public async Task MiCuenta_ListaLasAbiertas_ConLaIpDeLaEntrada_YSoloCierraLasPropias()
    {
        var (accountId, _) = await SeedAccountAsync();
        var (otherId, _) = await SeedAccountAsync();
        var store = new SqlSessionStore(TestDatabase.ConnectionFactory);
        var log = new SqlAccessEventLog(TestDatabase.ConnectionFactory);
        var account = AccountId.From(accountId);
        Guid first = Guid.NewGuid(), second = Guid.NewGuid(), closed = Guid.NewGuid(), expired = Guid.NewGuid(), foreign = Guid.NewGuid();
        await store.OpenAsync(first, account, T0, T0.AddHours(12));
        await store.OpenAsync(second, account, T0.AddMinutes(1), T0.AddHours(12));
        await store.OpenAsync(closed, account, T0, T0.AddHours(12));
        await store.OpenAsync(expired, account, T0, T0.AddHours(1));
        await store.OpenAsync(foreign, AccountId.From(otherId), T0, T0.AddHours(12));
        await store.CloseAsync(closed, SessionCloseReason.Logout, T0.AddMinutes(2));
        await log.RecordAsync(AccessEventType.Entrada, account, first, "203.0.113.7", T0);
        await store.TouchAsync(second, T0.AddMinutes(30));

        var open = await store.ListOpenAsync(account, T0.AddHours(2));
        var foreignClosedByOther = await store.CloseOwnedAsync(account, foreign, SessionCloseReason.CierrePropio, T0.AddHours(2));
        var ownClosed = await store.CloseOwnedAsync(account, first, SessionCloseReason.CierrePropio, T0.AddHours(2));
        var ownClosedAgain = await store.CloseOwnedAsync(account, first, SessionCloseReason.CierrePropio, T0.AddHours(2));
        await store.OpenAsync(Guid.NewGuid(), account, T0.AddHours(2), T0.AddHours(14));
        await store.CloseOthersAsync(account, second, SessionCloseReason.CierrePropio, T0.AddHours(3));
        var afterOthers = await store.ListOpenAsync(account, T0.AddHours(3));

        Assert.Equal([second, first], open.Select(s => s.Id));
        Assert.Equal("203.0.113.7", open.Single(s => s.Id == first).Ip);
        Assert.Null(open.Single(s => s.Id == second).Ip);
        Assert.Equal(T0.AddMinutes(30), open.First().LastActivityAt);
        Assert.False(foreignClosedByOther);
        Assert.True(ownClosed);
        Assert.False(ownClosedAgain);
        Assert.Null((await store.FindAsync(foreign))!.ClosedAt);
        Assert.Equal([second], afterOthers.Select(s => s.Id));
    }
}
