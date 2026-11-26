using Dapper;
using ResidApp.Application.Ports;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>El aviso de cambio de correo en dbo.envios_correo (scripts 0081 y 0122), bajo la seguridad por filas del centro de quien hace el cambio: una fila ya resuelta, con el
/// familiar y sin ninguna dirección.</summary>
public sealed class SqlFamilyEmailChangeLog(SqlConnectionFactory connections) : IFamilyEmailChangeLog
{
    public async Task RecordAsync(FamilyEmailChangeEntry entry, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.envios_correo
                (id, centro_id, plantilla_codigo, plantilla_version, idioma, familiar_id, referencia_id, referencia_clave, estado_codigo, motivo_codigo, acs_operacion_id,
                 intentos, creado_en, resuelto_en)
            VALUES (NEWID(), @CenterId, @Template, @Version, @Language, @FamilyId, @FamilyId, @Key, @State, @Reason, @OperationId, @Attempts, @At, @At)
            """, new
        {
            CenterId = entry.CenterId.Value, Template = EmailLog.EmailChangeTemplate, Version = entry.TemplateVersion, entry.Language, entry.FamilyId,
            Key = Guid.NewGuid().ToString("N"), entry.State, entry.Reason, entry.OperationId, entry.Attempts, entry.At,
        }, cancellationToken: ct));
    }
}
