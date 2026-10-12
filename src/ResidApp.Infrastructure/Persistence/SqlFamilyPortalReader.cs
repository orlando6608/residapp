using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Families;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Lecturas del Portal Familiar (script 0052). El criterio de acceso es uno solo, <see cref="AccessibleLinks"/>: el vínculo es de un familiar
/// de la cuenta (cuya sesión ya exige cuenta y ámbito activos), no está desvinculado y su autorización más reciente es Activa y no ha caducado. Todas las lecturas empiezan por él, así que un
/// vínculo ajeno, suspendido, revocado o inexistente no da nada y no se distingue. La seguridad por filas solo aísla por centro; el aislamiento
/// entre familiares y entre residentes del centro está aquí. De un comunicado se lee solo su tipo, su texto vigente y cuándo se publicó: nada
/// del evento del que sale.
/// </summary>
public sealed class SqlFamilyPortalReader(SqlConnectionFactory connections) : IFamilyPortalReader
{
    /// <summary>Los vínculos a los que la cuenta (@AccountId, en el centro @CenterId) tiene acceso hoy (@Today), con los alias link, fam y
    /// resident. Para pedir uno: <c>AND link.id = @LinkId</c>.</summary>
    private const string AccessibleLinks = """
        FROM dbo.residentes_familiares link
        JOIN dbo.familiares fam ON fam.id = link.familiar_id AND fam.centro_id = link.centro_id
        JOIN dbo.residentes resident ON resident.id = link.residente_id AND resident.centro_id = link.centro_id
        WHERE fam.cuenta_id = @AccountId AND fam.centro_id = @CenterId
          AND link.desvinculado_en IS NULL
          AND EXISTS (
              SELECT 1 FROM dbo.familiares_autorizaciones_cambios auth
               WHERE auth.vinculo_id = link.id AND auth.centro_id = link.centro_id AND auth.estado_codigo = 'ACTIVA'
                 AND (auth.valida_hasta IS NULL OR auth.valida_hasta >= @Today)
                 AND auth.numero = (SELECT MAX(latest.numero) FROM dbo.familiares_autorizaciones_cambios latest WHERE latest.vinculo_id = link.id))
        """;

    private const string ResidentColumns =
        "link.id AS LinkId, link.residente_id AS ResidentId, resident.nombre_visible AS ResidentName, link.relacion AS Relationship";

    public async Task<IReadOnlyList<FamilyPortalResident>> ListResidentsAsync(FamilyPortalAccess access, DateOnly today, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<ResidentRow>(new CommandDefinition(
            $"SELECT {ResidentColumns} {AccessibleLinks} ORDER BY resident.nombre_visible", Parameters(access, today), cancellationToken: ct));
        return rows.Select(row => new FamilyPortalResident(row.LinkId, row.ResidentName, row.Relationship)).ToList();
    }

    public async Task<FamilyPortalResidentHome?> ReadResidentAsync(
        FamilyPortalAccess access, Guid linkId, DateOnly today, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var resident = await FindResidentAsync(connection, transaction, access, linkId, today, ct);
        if (resident is null)
        {
            return null;
        }

        // Los candidatos son los preparados desde hace menos que la ventana más larga (6 meses) y unos días más: lo publicado sale como mucho dos
        // días después de prepararse. Qué está publicado y dentro de su ventana lo decide el dominio.
        var since = now.AddMonths(-FamilyPortalVisibility.RelevantMonths).AddDays(-3).UtcDateTime;
        var rows = await connection.QueryAsync<CommunicationRow>(new CommandDefinition(
            $"{CommunicationSelect} AND family.preparado_en >= @Since",
            new { CenterId = access.CenterId.Value, resident.ResidentId, CommunicationId = (Guid?)null, Since = since }, transaction, cancellationToken: ct));
        var correctives = await connection.QueryAsync<CorrectiveRow>(new CommandDefinition(
            $"{CorrectiveSelect} AND corr.publicada_en >= @Since",
            new { CenterId = access.CenterId.Value, resident.ResidentId, CommunicationId = (Guid?)null, Since = since }, transaction, cancellationToken: ct));
        var communications = rows.Select(row => ToVisible(row, now)).Concat(correctives.Select(row => ToVisible(row, now)))
            .OfType<FamilyPortalCommunication>().OrderByDescending(c => c.PublishedAt).ToList();
        await AuditAsync(connection, transaction, access, resident.ResidentId, "RESIDENT", resident.ResidentId, now, ct);
        transaction.Commit();
        return new FamilyPortalResidentHome(new FamilyPortalResident(resident.LinkId, resident.ResidentName, resident.Relationship), communications);
    }

    public async Task<FamilyPortalCommunicationDetail?> ReadCommunicationAsync(
        FamilyPortalAccess access, Guid linkId, Guid communicationId, DateOnly today, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var resident = await FindResidentAsync(connection, transaction, access, linkId, today, ct);
        if (resident is null)
        {
            return null;
        }

        var rows = await connection.QueryAsync<CommunicationRow>(new CommandDefinition(
            $"{CommunicationSelect} AND family.id = @CommunicationId",
            new { CenterId = access.CenterId.Value, resident.ResidentId, CommunicationId = communicationId, Since = (DateTime?)null }, transaction, cancellationToken: ct));
        var communication = rows.Select(row => ToVisible(row, now)).OfType<FamilyPortalCommunication>().FirstOrDefault();
        if (communication is null)
        {
            var correctives = await connection.QueryAsync<CorrectiveRow>(new CommandDefinition(
                $"{CorrectiveSelect} AND corr.id = @CommunicationId",
                new { CenterId = access.CenterId.Value, resident.ResidentId, CommunicationId = (Guid?)communicationId, Since = (DateTime?)null },
                transaction, cancellationToken: ct));
            communication = correctives.Select(row => ToVisible(row, now)).OfType<FamilyPortalCommunication>().FirstOrDefault();
        }

        if (communication is null)
        {
            return null;
        }

        // Un original retirado no enseña correctoras (retirar un comunicado las retira también) y CorrectiveSelect ya las excluye.
        IReadOnlyList<FamilyPortalCommunication> corrections = [];
        if (communication.Kind == FamilyPortalCommunicationKind.Original)
        {
            var later = await connection.QueryAsync<CorrectiveRow>(new CommandDefinition(
                $"{CorrectiveSelect} AND corr.comunicacion_id = @CommunicationId",
                new { CenterId = access.CenterId.Value, resident.ResidentId, CommunicationId = (Guid?)communicationId, Since = (DateTime?)null },
                transaction, cancellationToken: ct));
            corrections = later.Select(row => ToVisible(row, now)).OfType<FamilyPortalCommunication>().OrderBy(c => c.PublishedAt).ToList();
        }

        await AuditAsync(connection, transaction, access, resident.ResidentId, "FAMILY_COMMUNICATION", communicationId, now, ct);
        transaction.Commit();
        return new FamilyPortalCommunicationDetail(new FamilyPortalResident(resident.LinkId, resident.ResidentName, resident.Relationship), communication, corrections);
    }

    /// <summary>Los comunicados de un residente (el acceso ya se comprobó): tipo y texto vigentes y los datos para saber si están publicados.</summary>
    private static readonly string CommunicationSelect = $"""
        SELECT family.id AS Id, {FamilyCommunicationSql.CurrentType} AS TypeCode, {FamilyCommunicationSql.CurrentText} AS Text,
               family.preparado_en AS PreparedAt, family.publicacion_programada_en AS ScheduledAt,
               (SELECT early.publicada_en FROM dbo.comunicaciones_familiares_publicacion_anticipada early WHERE early.comunicacion_id = family.id) AS PublishedEarlyAt,
               CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.comunicaciones_familiares_retiradas withdrawal WHERE withdrawal.comunicacion_id = family.id)
                         THEN 1 ELSE 0 END AS BIT) AS Withdrawn
          FROM dbo.comunicaciones_familiares family
         WHERE family.centro_id = @CenterId AND family.residente_id = @ResidentId
        """;

    /// <summary>Las publicaciones correctoras de un residente (script 0053) con lo que hace falta del comunicado que corrigen para saber si la
    /// familia aún puede abrirlo. Las de un comunicado retirado no salen: retirar un comunicado retira también sus correctoras.</summary>
    private static readonly string CorrectiveSelect = $"""
        SELECT corr.id AS Id, corr.tipo_codigo AS TypeCode, corr.texto AS Text, corr.publicada_en AS PublishedAt, family.id AS OriginalId,
               {FamilyCommunicationSql.CurrentType} AS OriginalTypeCode, family.preparado_en AS OriginalPreparedAt,
               family.publicacion_programada_en AS OriginalScheduledAt,
               (SELECT early.publicada_en FROM dbo.comunicaciones_familiares_publicacion_anticipada early WHERE early.comunicacion_id = family.id) AS OriginalPublishedEarlyAt
          FROM dbo.comunicaciones_familiares_correctoras corr
          JOIN dbo.comunicaciones_familiares family ON family.id = corr.comunicacion_id AND family.centro_id = corr.centro_id
         WHERE corr.centro_id = @CenterId AND corr.residente_id = @ResidentId
           AND NOT EXISTS (SELECT 1 FROM dbo.comunicaciones_familiares_retiradas withdrawal WHERE withdrawal.comunicacion_id = family.id)
        """;

    private static Task<ResidentRow?> FindResidentAsync(
        SqlConnection connection, SqlTransaction transaction, FamilyPortalAccess access, Guid linkId, DateOnly today, CancellationToken ct) =>
        connection.QuerySingleOrDefaultAsync<ResidentRow>(new CommandDefinition(
            $"SELECT {ResidentColumns} {AccessibleLinks} AND link.id = @LinkId", Parameters(access, today, linkId), transaction, cancellationToken: ct));

    private static FamilyPortalCommunication? ToVisible(CommunicationRow row, DateTimeOffset now)
    {
        var scheduledAt = FamilyCommunicationSql.ScheduledAt(row.PreparedAt, row.ScheduledAt);
        DateTimeOffset? earlyAt = row.PublishedEarlyAt is { } early ? Utc(early) : null;
        var type = EnumCode.ParseCode<FamilyCommunicationType>(row.TypeCode);
        if (!FamilyPortalVisibility.IsVisible(type, scheduledAt, earlyAt, now))
        {
            return null;
        }

        var publishedAt = FamilyPortalVisibility.PublishedAt(scheduledAt, earlyAt);
        return row.Withdrawn
            ? new FamilyPortalCommunication(row.Id, type, string.Empty, publishedAt, FamilyPortalCommunicationKind.Withdrawn)
            : new FamilyPortalCommunication(row.Id, type, row.Text, publishedAt);
    }

    /// <summary>Una publicación correctora es una novedad más, con su propio tipo y su propia ventana desde que se publicó; guarda a qué comunicado
    /// corrige y si la familia todavía puede abrirlo.</summary>
    private static FamilyPortalCommunication? ToVisible(CorrectiveRow row, DateTimeOffset now)
    {
        var type = EnumCode.ParseCode<FamilyCommunicationType>(row.TypeCode);
        var publishedAt = Utc(row.PublishedAt);
        if (!FamilyPortalVisibility.IsVisible(type, publishedAt, now))
        {
            return null;
        }

        var originalScheduledAt = FamilyCommunicationSql.ScheduledAt(row.OriginalPreparedAt, row.OriginalScheduledAt);
        DateTimeOffset? originalEarlyAt = row.OriginalPublishedEarlyAt is { } early ? Utc(early) : null;
        var originalType = EnumCode.ParseCode<FamilyCommunicationType>(row.OriginalTypeCode);
        return new FamilyPortalCommunication(
            row.Id, type, row.Text, publishedAt, FamilyPortalCommunicationKind.Corrective,
            new FamilyPortalCorrection(
                row.OriginalId, FamilyPortalVisibility.PublishedAt(originalScheduledAt, originalEarlyAt),
                FamilyPortalVisibility.IsVisible(originalType, originalScheduledAt, originalEarlyAt, now)));
    }

    /// <summary>Quién leyó qué, antes de entregarlo (misma transacción). Aparte de la auditoría administrativa: el que lee es el familiar.</summary>
    private static Task AuditAsync(
        SqlConnection connection, SqlTransaction transaction, FamilyPortalAccess access, Guid residentId, string resourceType, Guid resourceId,
        DateTimeOffset now, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.eventos_auditoria
                (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
            VALUES (NEWID(), @AccountId, 'FAMILIAR', @CenterId, NULL, @ResidentId, @ResourceType, @ResourceId, 'FAMILY_PORTAL_READ', @OccurredAt)
            """, new
        {
            AccountId = access.AccountId.Value, CenterId = access.CenterId.Value, ResidentId = residentId, ResourceType = resourceType,
            ResourceId = resourceId, OccurredAt = now.UtcDateTime,
        }, transaction, cancellationToken: ct));

    private static object Parameters(FamilyPortalAccess access, DateOnly today, Guid? linkId = null) => new
    {
        AccountId = access.AccountId.Value, CenterId = access.CenterId.Value, Today = today.ToDateTime(TimeOnly.MinValue), LinkId = linkId,
    };

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed record ResidentRow(Guid LinkId, Guid ResidentId, string ResidentName, string Relationship);

    private sealed record CommunicationRow(
        Guid Id, string TypeCode, string Text, DateTime PreparedAt, DateTime? ScheduledAt, DateTime? PublishedEarlyAt, bool Withdrawn);

    private sealed record CorrectiveRow(
        Guid Id, string TypeCode, string Text, DateTime PublishedAt, Guid OriginalId, string OriginalTypeCode, DateTime OriginalPreparedAt,
        DateTime? OriginalScheduledAt, DateTime? OriginalPublishedEarlyAt);
}
