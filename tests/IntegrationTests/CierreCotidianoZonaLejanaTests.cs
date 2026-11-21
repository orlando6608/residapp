using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>
/// «Cerrado hoy» de la lista del Auxiliar es el día del centro, no el día UTC. Con una zona a cinco horas de UTC (Nueva York en invierno), el día UTC empieza a las 19:00 del día anterior en
/// el centro: antes, un cierre de las 20:00 contaba como «de mañana» y los residentes volvían a salir sin cerrar cada tarde-noche. Se prueba en los límites del día del centro, sea la hora que sea
/// cuando corre la prueba.
/// </summary>
public class CierreCotidianoZonaLejanaTests
{
    private static readonly TimeZoneInfo NuevaYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    [Fact]
    public async Task ElCierreCuentaEnElDiaDelCentro_SinMirarElDiaUtc()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var auxiliar = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, admin.CenterId, admin.UnitId);
        var residents = new SqlResidentRepository(TestDatabase.ConnectionFactory);
        var created = new List<ResidentId>();
        foreach (var name in new[] { "Residente Cierre A", "Residente Cierre B", "Residente Cierre C" })
        {
            var resident = await residents.CreateWithInitialLocationAsync(new CreateResidentInput(
                admin.AccountId, SystemProfile.Administracion, admin.CenterId, admin.UnitId, name, new DateOnly(1944, 4, 4), DocumentedSexCode.Hombre, null, null, null, null, null, Guid.NewGuid()));
            created.Add(resident.ResidentId);
            using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
            await connection.ExecuteAsync("""
                INSERT INTO dbo.ambitos_perfil_residente (id, ambito_perfil_id, centro_id, residente_id, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @ProfileScopeId, @CenterId, @ResidentId, SYSUTCDATETIME(), @AccountId)
                """, new { ProfileScopeId = auxiliar.ProfileScopeId, CenterId = admin.CenterId.Value, ResidentId = resident.ResidentId.Value, AccountId = admin.AccountId.Value });
        }

        using var zone = CenterTime.Use(NuevaYork);
        var todayStart = CenterTime.StartOfDayUtc(CenterTime.Today, NuevaYork);
        var tomorrowStart = CenterTime.StartOfDayUtc(CenterTime.Today.AddDays(1), NuevaYork);
        // A: justo después de la medianoche del centro (hoy). B: justo antes (ayer, aunque su día UTC puede ser el mismo que el de hoy). C: justo antes de la medianoche siguiente (hoy).
        var closedAt = new[] { todayStart.AddMinutes(30), todayStart.AddMinutes(-30), tomorrowStart.AddMinutes(-30) };
        for (var i = 0; i < created.Count; i++)
        {
            using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
            await connection.ExecuteAsync("""
                INSERT INTO dbo.cierres_cotidianos_residente (id, residente_id, centro_id, unidad_id, tipo_codigo, registrado_por_cuenta_id, registrado_por_perfil, ocurrido_en)
                VALUES (NEWID(), @ResidentId, @CenterId, @UnitId, 'SIN_CAMBIOS', @AccountId, 'AUXILIAR', @At)
                """, new { ResidentId = created[i].Value, CenterId = admin.CenterId.Value, UnitId = admin.UnitId.Value, AccountId = auxiliar.AccountId.Value, At = closedAt[i] });
        }

        var listed = await new SqlAssignedResidentDirectory(TestDatabase.ConnectionFactory).ListAsync(auxiliar.ProfileScopeId, admin.CenterId);

        Assert.True(listed.Single(r => r.ResidentId == created[0]).CerradoHoy);
        Assert.False(listed.Single(r => r.ResidentId == created[1]).CerradoHoy);
        Assert.True(listed.Single(r => r.ResidentId == created[2]).CerradoHoy);
    }
}
