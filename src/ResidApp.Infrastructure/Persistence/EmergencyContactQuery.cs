using System.Data;
using Dapper;
using ResidApp.Application.Ports;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// ADM-08 (0022): el contacto urgente vigente de un residente, para Enfermería y Medicina (ficha y derivación). Es la
/// designación con el número más alto; si esa designación lo quitó (vinculo_id NULL), no hay contacto. No comprueba el
/// ámbito: quien llama ya ha autorizado al residente.
/// </summary>
internal static class EmergencyContactQuery
{
    public static Task<EmergencyContactSummary?> FindAsync(
        IDbConnection connection, Guid centerId, Guid residentId, CancellationToken ct) =>
        connection.QuerySingleOrDefaultAsync<EmergencyContactSummary?>(new CommandDefinition("""
            SELECT f.nombre_visible AS DisplayName, link.relacion AS Relationship, f.telefono AS Phone
              FROM (SELECT TOP 1 d.vinculo_id, d.centro_id
                      FROM dbo.residentes_contacto_urgente d
                     WHERE d.residente_id = @ResidentId AND d.centro_id = @CenterId
                     ORDER BY d.numero DESC) latest
              JOIN dbo.residentes_familiares link ON link.id = latest.vinculo_id AND link.centro_id = latest.centro_id
              JOIN dbo.familiares f ON f.id = link.familiar_id AND f.centro_id = link.centro_id
            """, new { CenterId = centerId, ResidentId = residentId }, cancellationToken: ct));
}
