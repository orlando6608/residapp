using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.IntegrationTests.TestSupport;
using static ResidApp.IntegrationTests.PortalFamiliarTests;

namespace ResidApp.IntegrationTests;

/// <summary>La fecha de consentimiento de la foto es un día del centro; actualizada_en está en UTC (script 0110, que corrige el CHECK de 0071). Entre la medianoche del centro y la UTC, el día del centro
/// ya es el siguiente al día UTC: guardar el consentimiento de «hoy» no debe fallar.</summary>
public class FotoConsentimientoFechaTests
{
    [Fact]
    public async Task LaFechaDeConsentimientoPuedeSerElDiaSiguienteAlDiaUtc_PeroNoDosDiasDespues()
    {
        var setup = await SetupAsync("Residente Foto Fecha Consentimiento");
        var parameters = new
        {
            ResidentId = setup.ResidentId.Value, CenterId = setup.Admin.CenterId.Value, AccountId = setup.Admin.AccountId.Value,
            UpdatedAt = new DateTime(2026, 1, 15, 23, 30, 0, DateTimeKind.Utc), NextDay = new DateTime(2026, 1, 16), TwoDaysLater = new DateTime(2026, 1, 17),
        };
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        // 23:30 UTC del 15 es ya el día 16 en la península: el consentimiento de «hoy» lleva la fecha del 16.
        await connection.ExecuteAsync("""
            INSERT INTO dbo.fotos_residente
                (residente_id, centro_id, contenido, actualizada_por_cuenta_id, actualizada_por_perfil, actualizada_en, consentimiento_firmante_codigo, consentimiento_fecha)
            VALUES (@ResidentId, @CenterId, 0xFFD8FFD9, @AccountId, 'ADMINISTRACION', @UpdatedAt, 'RESIDENTE', @NextDay)
            """, parameters);

        var tooLate = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.fotos_residente SET consentimiento_fecha = @TwoDaysLater WHERE residente_id = @ResidentId", parameters));
        Assert.Equal(547, tooLate.Number);
    }
}
