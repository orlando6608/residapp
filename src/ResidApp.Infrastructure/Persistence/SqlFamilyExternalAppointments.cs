using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Domain.Appointments;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Las citas externas que ve la familia (PR 5, solo ver; script 0104). El acceso a cada residente es el criterio único del portal (<see cref="SqlFamilyPortalReader.AccessibleLinks"/>) y, encima, la
/// regla única de visibilidad (<see cref="ExternalFamilyVisibilitySql"/>): centro encendido, residente no restringido y cita no oculta, evaluada en cada lectura. Cualquier fallo devuelve null, sin
/// distinguir un vínculo ajeno, un interruptor apagado o una cita inexistente. La redacción por nivel está en la propia consulta: de una cita «sin especialidad» no se lee la especialidad, y de
/// ninguna se leen la nota «al terminar», los motivos, el historial ni quién la registró o se encarga. Las lecturas de la familia no se auditan (decisión general del portal).
/// </summary>
public sealed partial class SqlFamilyExternalAppointments(SqlConnectionFactory connections) : IFamilyExternalAppointments
{
    private static readonly string Gate = $"{ExternalFamilyVisibilitySql.CenterEnabled} AND NOT {ExternalFamilyVisibilitySql.ResidentRestricted("@ResidentId")}";

    private static readonly string SelectSql = $"""
        SELECT e.id AS Id, e.fecha AS Date, e.hora AS Time, e.tipo_codigo AS Type,
               CASE WHEN e.visibilidad_familia_codigo = 'VISIBLE' THEN e.especialidad_codigo END AS Specialty,
               e.lugar AS Place, e.transporte_modo_codigo AS TransportMode, e.transporte_estado_codigo AS TransportState,
               e.acompanante_modo_codigo AS CompanionMode, e.acompanante_estado_codigo AS CompanionState,
               e.preparar_ayuno AS Fasting, e.preparar_informe AS LastReport, e.preparar_tarjeta AS HealthCard, e.preparar_medicacion AS Medication,
               e.preparar_silla_ruedas AS Wheelchair, e.preparar_otra AS PrepOther, e.preparar_texto AS PrepText, e.estado_codigo AS Status,
               {HolderSql("TRANSPORTE")} AS TransportHolder, {HolderSql("ACOMPANANTE")} AS CompanionHolder,
               CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.citas_externas_no_puedo refusal WHERE refusal.cita_id = e.id AND refusal.vinculo_id = @LinkId AND refusal.no_puede = 1
                                    AND refusal.numero = (SELECT MAX(latest.numero) FROM dbo.citas_externas_no_puedo latest WHERE latest.cita_id = refusal.cita_id AND latest.vinculo_id = refusal.vinculo_id)) THEN 1 ELSE 0 END AS BIT) AS Refused
          FROM dbo.citas_externas e
         WHERE e.centro_id = @CenterId AND e.residente_id = @ResidentId AND e.visibilidad_familia_codigo <> 'OCULTA'
           AND e.estado_codigo IN ('PROGRAMADA', 'CONFIRMADA_HOSPITAL', 'CANCELADA') AND e.fecha BETWEEN @From AND @To
           AND {Gate}
        """;

    /// <summary>Quién tiene hoy una parte de la cita visto por la persona que mira (@LinkId): ella misma (YOU), otra persona de la familia sin decir quién (ANOTHER) o nadie (NOBODY).</summary>
    private static string HolderSql(string scope) => $"""
        CASE WHEN EXISTS (SELECT 1 FROM dbo.fn_citas_externas_encargos_vigentes(@Today) held WHERE held.cita_id = e.id AND held.ambito_codigo = '{scope}' AND held.vinculo_id = @LinkId) THEN 'YOU'
             WHEN EXISTS (SELECT 1 FROM dbo.fn_citas_externas_encargos_vigentes(@Today) held WHERE held.cita_id = e.id AND held.ambito_codigo = '{scope}') THEN 'ANOTHER'
             WHEN EXISTS (SELECT 1 FROM dbo.citas_externas_encargos lapsed WHERE lapsed.cita_id = e.id AND lapsed.ambito_codigo = '{scope}' AND lapsed.accion_codigo = 'ME_ENCARGO' AND lapsed.vinculo_id = @LinkId
                             AND lapsed.numero = (SELECT MAX(x.numero) FROM dbo.citas_externas_encargos x WHERE x.cita_id = lapsed.cita_id AND x.ambito_codigo = lapsed.ambito_codigo)) THEN 'LAPSED'
             ELSE 'NOBODY' END
        """;

    public async Task<bool> IsAvailableAsync(FamilyPortalAccess access, Guid linkId, DateOnly todayAtCenter, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await OpenAsync(connection, access, linkId, todayAtCenter, ct) is not null;
    }

    public async Task<FamilyExternalAppointments?> ListAsync(FamilyPortalAccess access, Guid linkId, DateOnly todayAtCenter, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        if (await OpenAsync(connection, access, linkId, todayAtCenter, ct) is not { } link)
        {
            return null;
        }

        var rows = await connection.QueryAsync<Row>(new CommandDefinition($"""
            {SelectSql}
             ORDER BY e.fecha, CASE WHEN e.hora IS NULL THEN 1 ELSE 0 END, e.hora, e.creada_en
            """, Parameters(access, link.ResidentId, todayAtCenter, link.LinkId), cancellationToken: ct));
        return new FamilyExternalAppointments(
            new FamilyPortalResident(link.LinkId, link.ResidentName, link.Relationship), todayAtCenter, rows.Select(ToAppointment).ToList());
    }

    public async Task<(FamilyPortalResident Resident, ExternalAppointmentForFamily Appointment)?> FindAsync(
        FamilyPortalAccess access, Guid linkId, Guid appointmentId, DateOnly todayAtCenter, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        if (await OpenAsync(connection, access, linkId, todayAtCenter, ct) is not { } link)
        {
            return null;
        }

        var parameters = new DynamicParameters(Parameters(access, link.ResidentId, todayAtCenter, link.LinkId));
        parameters.Add("AppointmentId", appointmentId);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition($"{SelectSql} AND e.id = @AppointmentId", parameters, cancellationToken: ct));
        return row is null ? null : (new FamilyPortalResident(link.LinkId, link.ResidentName, link.Relationship), ToAppointment(row));
    }

    /// <summary>El vínculo, si es accesible y las familias pueden ver las citas externas de este residente (centro encendido y residente no restringido); si no, null.</summary>
    private static async Task<SqlFamilyAppointments.LinkRow?> OpenAsync(
        System.Data.IDbConnection connection, FamilyPortalAccess access, Guid linkId, DateOnly todayAtCenter, CancellationToken ct)
    {
        var link = await SqlFamilyAppointments.FindLinkAsync(connection, null, access, linkId, todayAtCenter, ct);
        if (link is null)
        {
            return null;
        }

        var open = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            $"SELECT CASE WHEN {Gate} THEN 1 ELSE 0 END", new { CenterId = access.CenterId.Value, link.ResidentId }, cancellationToken: ct));
        return open ? link : null;
    }

    private static object Parameters(FamilyPortalAccess access, Guid residentId, DateOnly todayAtCenter, Guid? linkId = null) => new
    {
        CenterId = access.CenterId.Value, ResidentId = residentId, From = todayAtCenter, To = todayAtCenter.AddDays(ExternalFamilyRules.WindowDays), Today = todayAtCenter.ToDateTime(TimeOnly.MinValue), LinkId = linkId ?? Guid.Empty,
    };

    private static ExternalAppointmentForFamily ToAppointment(Row row) => new(
        row.Id, row.Date, row.Time is { } time ? TimeOnly.FromTimeSpan(time) : null, EnumCode.ParseCode<ExternalAppointmentType>(row.Type),
        row.Specialty is { } specialty ? EnumCode.ParseCode<ExternalAppointmentSpecialty>(specialty) : null, row.Place,
        new ExternalPreparation(row.Fasting, row.LastReport, row.HealthCard, row.Medication, row.Wheelchair, row.PrepOther, row.PrepText),
        EnumCode.ParseCode<ExternalTransportMode>(row.TransportMode), EnumCode.ParseCode<ExternalArrangementState>(row.TransportState),
        EnumCode.ParseCode<ExternalCompanionMode>(row.CompanionMode), EnumCode.ParseCode<ExternalArrangementState>(row.CompanionState),
        EnumCode.ParseCode<ExternalAppointmentStatus>(row.Status) == ExternalAppointmentStatus.Cancelada,
        ToHolder(row.TransportHolder), ToHolder(row.CompanionHolder), row.Refused, row.TransportHolder == "LAPSED", row.CompanionHolder == "LAPSED");

    private static ExternalFamilyHolder ToHolder(string code) => code switch
    {
        "YOU" => ExternalFamilyHolder.You,
        "ANOTHER" => ExternalFamilyHolder.Another,
        _ => ExternalFamilyHolder.Nobody,
    };

    private sealed record Row(
        Guid Id, DateOnly Date, TimeSpan? Time, string Type, string? Specialty, string? Place, string TransportMode, string TransportState, string CompanionMode, string CompanionState,
        bool Fasting, bool LastReport, bool HealthCard, bool Medication, bool Wheelchair, bool PrepOther, string? PrepText, string Status,
        string TransportHolder, string CompanionHolder, bool Refused);
}
