using Dapper;
using ResidApp.Application.Ports;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// ADM-01: las cifras del inicio de Administración, en una sola consulta de solo lectura. Residentes y autorizaciones parten de los residentes del
/// ámbito (<see cref="SqlAdministracionResidentDirectory.ScopedResidentsSelect"/>, la misma regla que la lista); la autorización vigente de cada vínculo es
/// su último cambio (el de mayor número) y un vínculo sin ningún cambio cuenta como pendiente de activar. Las solicitudes y las citas son de todo el
/// centro, como la bandeja y la agenda. Las fechas se guardan en UTC; «caducada» es una Activa con «válida hasta» anterior a hoy
/// (<c>FamilyAuthorization.Effective</c>). No lee nada clínico ni nombres.
/// </summary>
public sealed class SqlAdministrationOverview(SqlConnectionFactory connections) : IAdministrationOverviewReader
{
    /// <summary>Los residentes del ámbito (<c>scoped</c>) y la autorización vigente de cada uno de sus vínculos (<c>latest</c>): el último cambio, o ninguno si
    /// no se ha abierto. La usan el inicio y los filtros «requiere atención» de la lista de residentes, para que cuenten lo mismo.</summary>
    internal const string ScopedAuthorizationsCtes = $"""
        WITH scoped AS ({SqlAdministracionResidentDirectory.ScopedResidentsSelect}),
        latest AS (
            SELECT scoped.ResidentId AS ResidentId, link.id AS LinkId, authorization_change.estado_codigo AS StatusCode, authorization_change.valida_hasta AS ValidUntil
              FROM scoped
              JOIN dbo.residentes_familiares link ON link.residente_id = scoped.ResidentId AND link.centro_id = @CenterId
                   AND link.desvinculado_en IS NULL
              LEFT JOIN dbo.familiares_autorizaciones_cambios authorization_change ON authorization_change.vinculo_id = link.id
                   AND authorization_change.centro_id = link.centro_id
                   AND authorization_change.numero = (SELECT MAX(previous.numero) FROM dbo.familiares_autorizaciones_cambios previous
                                                       WHERE previous.vinculo_id = link.id AND previous.centro_id = link.centro_id))
        """;

    public async Task<AdministrationOverview> ReadAsync(
        AccountAdministrationAccess access, AdministrationOverviewPeriod period, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, null, access, ct);
        var today = period.Today.ToDateTime(TimeOnly.MinValue);
        return await connection.QuerySingleAsync<AdministrationOverview>(new CommandDefinition($"""
            {ScopedAuthorizationsCtes}
            SELECT
                (SELECT COUNT(*) FROM scoped
                   JOIN dbo.intervalos_ubicacion_residente location ON location.residente_id = scoped.ResidentId AND location.centro_id = @CenterId
                        AND location.vigente_hasta IS NULL
                  WHERE location.habitacion_id IS NULL) AS ResidentsWithoutRoom,
                (SELECT COUNT(*) FROM latest WHERE StatusCode IS NULL OR StatusCode = 'PENDIENTE') AS AuthorizationsPending,
                (SELECT COUNT(*) FROM latest WHERE StatusCode = 'ACTIVA' AND ValidUntil >= @Today AND ValidUntil <= @SoonLimit) AS AuthorizationsExpiringSoon,
                (SELECT COUNT(*) FROM latest WHERE StatusCode = 'ACTIVA' AND ValidUntil < @Today) AS AuthorizationsExpired,
                (SELECT COUNT(*) FROM dbo.citas_solicitudes WHERE centro_id = @CenterId AND estado_codigo = 'PENDIENTE') AS AppointmentRequestsPending,
                (SELECT COUNT(*) FROM dbo.citas_solicitudes WHERE centro_id = @CenterId AND estado_codigo = 'PROPUESTA'
                    AND propuesta_caduca_en > @Now) AS AppointmentRequestsAwaitingFamily,
                (SELECT COUNT(*) FROM dbo.citas WHERE centro_id = @CenterId AND estado_codigo = 'CONFIRMADA'
                    AND inicio_en >= @TodayStart AND inicio_en < @TomorrowStart) AS AppointmentsToday,
                (SELECT COUNT(*) FROM dbo.citas WHERE centro_id = @CenterId AND estado_codigo = 'CONFIRMADA'
                    AND inicio_en >= @TomorrowStart AND inicio_en < @ComingEnd) AS AppointmentsComing,
                (SELECT COUNT(*) FROM dbo.citas WHERE centro_id = @CenterId AND estado_codigo = 'CONFIRMADA'
                    AND inicio_en <= @Now) AS AppointmentsPastUnclosed
            """,
            new
            {
                access.ProfileScopeId, CenterId = access.CenterId.Value, Today = today,
                SoonLimit = today.AddDays(AdministrationOverviewRules.ExpiringSoonDays), Now = period.Now.UtcDateTime,
                TodayStart = period.TodayStart.UtcDateTime, TomorrowStart = period.TomorrowStart.UtcDateTime, ComingEnd = period.ComingEnd.UtcDateTime,
            }, cancellationToken: ct));
    }
}
