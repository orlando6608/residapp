using Dapper;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests.TestSupport;

/// <summary>Equipos para las pruebas de transferencia de seguimientos (script 0028), insertados directamente: aquí no se prueba Administración.</summary>
internal static class TransferTeamData
{
    internal static async Task<Guid> CreateAsync(SeededProfile seed, string name, bool active = true, UnitId? unit = null)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.equipos (id, centro_id, unidad_id, nombre_visible, estado, creado_en, creado_por_cuenta_id)
            VALUES (@Id, @CenterId, @UnitId, @Name, @Status, SYSUTCDATETIME(), @AccountId)
            """,
            new
            {
                Id = id, CenterId = seed.CenterId.Value, UnitId = (unit ?? seed.UnitId).Value, Name = name,
                Status = active ? "ACTIVE" : "INACTIVE", AccountId = seed.AccountId.Value,
            });
        return id;
    }
}
