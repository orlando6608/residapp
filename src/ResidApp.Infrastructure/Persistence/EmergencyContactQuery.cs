using System.Data;
using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Domain.Families;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// ADM-08 (0022, 0044): los contactos urgentes vigentes de un residente, para Enfermería y Medicina (ficha y derivación), en el orden en que
/// se designaron. El conjunto vigente sale de recorrer las designaciones por número (EmergencyContactSet). No comprueba el ámbito: quien
/// llama ya ha autorizado al residente.
/// </summary>
internal static class EmergencyContactQuery
{
    public static async Task<IReadOnlyList<EmergencyContactSummary>> FindAsync(
        IDbConnection connection, Guid centerId, Guid residentId, CancellationToken ct)
    {
        var rows = (await connection.QueryAsync<Row>(new CommandDefinition("""
            SELECT d.accion_codigo AS Action, d.vinculo_id AS LinkId, f.nombre_visible AS DisplayName, link.relacion AS Relationship,
                   f.telefono AS Phone
              FROM dbo.residentes_contacto_urgente d
              LEFT JOIN dbo.residentes_familiares link ON link.id = d.vinculo_id AND link.centro_id = d.centro_id
              LEFT JOIN dbo.familiares f ON f.id = link.familiar_id AND f.centro_id = link.centro_id
             WHERE d.residente_id = @ResidentId AND d.centro_id = @CenterId
             ORDER BY d.numero
            """, new { CenterId = centerId, ResidentId = residentId }, cancellationToken: ct))).ToList();
        return EmergencyContactSet.Current(rows.Select(r => (r.Action, r.LinkId)))
            .Select(linkId => rows.Last(r => r.LinkId == linkId))
            .Select(r => new EmergencyContactSummary(r.DisplayName!, r.Relationship!, r.Phone!))
            .ToList();
    }

    private sealed record Row(string Action, Guid? LinkId, string? DisplayName, string? Relationship, string? Phone);
}
