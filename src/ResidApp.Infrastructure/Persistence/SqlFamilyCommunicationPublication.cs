using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Families;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Publicación de los comunicados a la familia por Administración y por Dirección Clínica (script 0048; CJ, 2026-10-07 y 2026-10-10). Lee los
/// comunicados del ámbito y publica uno antes de su hora dentro de una transacción que bloquea el comunicado, comprueba el ámbito y que no esté
/// ya publicado, y lo audita sin datos (FAMILY_COMMUNICATION_PUBLISH_NOW, con el perfil que lo hizo). El ámbito de Administración son las
/// unidades de su ámbito; el de Dirección, el mismo de sus lecturas (unidades y, si los restringe, residentes).
/// </summary>
public sealed class SqlFamilyCommunicationPublication(SqlConnectionFactory connections)
    : IFamilyCommunicationDirectory, IFamilyCommunicationPublisher, IFamilyCommunicationAmender
{
    private const string Columns = """
        family.id AS Id, family.evento_id AS EventId, family.residente_id AS ResidentId, resident.nombre_visible AS ResidentName,
        unit.nombre_visible AS UnitName, {0} AS TypeCode, {1} AS Text, family.preparado_en AS PreparedAt, family.publicacion_programada_en AS ScheduledAt,
        (SELECT early.publicada_en FROM dbo.comunicaciones_familiares_publicacion_anticipada early WHERE early.comunicacion_id = family.id) AS PublishedEarlyAt
        """;

    /// <summary>Los comunicados del ámbito, con los alias family, ea, unit y resident. Administración: los de las unidades de su ámbito;
    /// Dirección: los eventos de sus unidades y residentes (la misma regla que sus lecturas), y que el ámbito sea de la cuenta.</summary>
    private const string AdministrationCommunications = """
        FROM dbo.comunicaciones_familiares family
        JOIN dbo.eventos_asistenciales ea ON ea.id = family.evento_id AND ea.centro_id = family.centro_id
        JOIN dbo.ambitos_perfil_unidad unit_scope ON unit_scope.ambito_perfil_id = @ProfileScopeId AND unit_scope.centro_id = family.centro_id
             AND unit_scope.unidad_id = ea.unidad_id AND unit_scope.revocado_en IS NULL
        JOIN dbo.unidades unit ON unit.id = ea.unidad_id AND unit.centro_id = family.centro_id
        JOIN dbo.residentes resident ON resident.id = family.residente_id AND resident.centro_id = family.centro_id
         WHERE family.centro_id = @CenterId
        """;

    private static string ScopedCommunications(FamilyCommunicationAccess access) => access.Profile == SystemProfile.Administracion
        ? AdministrationCommunications
        : SqlChangeInboxDirectory.DirectionScopedEventsFrom + " AND profile.cuenta_id = @AccountId AND family.id IS NOT NULL";

    public async Task<IReadOnlyList<AdministrationFamilyCommunication>> ListAsync(
        FamilyCommunicationAccess access, DateTimeOffset since, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<CommunicationRow>(new CommandDefinition($"""
            SELECT {string.Format(Columns, FamilyCommunicationSql.CurrentType, FamilyCommunicationSql.CurrentText)}
            {ScopedCommunications(access)}
               AND family.preparado_en >= @Since
             ORDER BY family.preparado_en DESC
            """, new { access.ProfileScopeId, CenterId = access.CenterId.Value, AccountId = access.AccountId.Value, Since = since.UtcDateTime },
            cancellationToken: ct));
        var ids = rows.Select(row => row.Id).ToList();
        var correctives = (await connection.QueryAsync<CorrectiveRow>(new CommandDefinition("""
            SELECT comunicacion_id AS CommunicationId, id AS Id, tipo_codigo AS TypeCode, texto AS Text, publicada_en AS PublishedAt
              FROM dbo.comunicaciones_familiares_correctoras WHERE centro_id = @CenterId AND comunicacion_id IN @Ids ORDER BY publicada_en
            """, new { CenterId = access.CenterId.Value, Ids = ids }, cancellationToken: ct))).ToLookup(row => row.CommunicationId);
        var withdrawals = (await connection.QueryAsync<WithdrawalRow>(new CommandDefinition("""
            SELECT comunicacion_id AS CommunicationId, motivo AS Reason, retirada_en AS WithdrawnAt
              FROM dbo.comunicaciones_familiares_retiradas WHERE centro_id = @CenterId AND comunicacion_id IN @Ids
            """, new { CenterId = access.CenterId.Value, Ids = ids }, cancellationToken: ct))).ToDictionary(row => row.CommunicationId);
        return rows.Select(row => new AdministrationFamilyCommunication(
            row.Id, row.EventId, ResidentId.From(row.ResidentId), row.ResidentName, row.UnitName,
            EnumCode.ParseCode<FamilyCommunicationType>(row.TypeCode), row.Text, Utc(row.PreparedAt),
            row.PublishedEarlyAt is { } early ? Utc(early) : null, FamilyCommunicationSql.ScheduledAt(row.PreparedAt, row.ScheduledAt),
            correctives[row.Id].Select(c => new FamilyCommunicationCorrective(c.Id, EnumCode.ParseCode<FamilyCommunicationType>(c.TypeCode), c.Text, Utc(c.PublishedAt))).ToList(),
            withdrawals.TryGetValue(row.Id, out var withdrawal) ? new FamilyCommunicationWithdrawalNote(withdrawal.Reason, Utc(withdrawal.WithdrawnAt)) : null)).ToList();
    }

    public async Task PublishNowAsync(FamilyCommunicationAccess access, Guid communicationId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var communication = await LockAndReadAsync(connection, transaction, access, communicationId, ct);

        var occurredAt = DateTimeOffset.UtcNow;
        if (IsPublished(communication, occurredAt))
        {
            throw new DomainValidationException("FAMILY_COMMUNICATION_ALREADY_PUBLISHED");
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.comunicaciones_familiares_publicacion_anticipada (id, comunicacion_id, centro_id, publicada_por_cuenta_id, publicada_en)
            VALUES (NEWID(), @CommunicationId, @CenterId, @AccountId, @OccurredAt);
            """, new
        {
            CommunicationId = communicationId, CenterId = access.CenterId.Value, AccountId = access.AccountId.Value, OccurredAt = occurredAt,
        }, transaction, cancellationToken: ct));
        await AuditAsync(connection, transaction, access, communicationId, communication, "FAMILY_COMMUNICATION_PUBLISH_NOW", occurredAt, ct);
        transaction.Commit();
    }

    public async Task PublishCorrectiveAsync(
        FamilyCommunicationAccess access, Guid communicationId, FamilyCommunicationType type, string text, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var communication = await LockAndReadAsync(connection, transaction, access, communicationId, ct);
        var occurredAt = DateTimeOffset.UtcNow;
        EnsureCanAmend(communication, occurredAt);
        if (!FamilyPortalVisibility.IsVisible(
                EnumCode.ParseCode<FamilyCommunicationType>(communication.TypeCode), FamilyCommunicationSql.ScheduledAt(communication.PreparedAt, communication.ScheduledAt),
                communication.PublishedEarlyAt is { } early ? Utc(early) : null, occurredAt))
        {
            // Una correctora deja de verse con su original (CJ, portal 4.2): sobre uno que la familia ya no ve, no la vería nunca.
            throw new DomainValidationException("FAMILY_COMMUNICATION_NOT_VISIBLE");
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.comunicaciones_familiares_correctoras
                (id, comunicacion_id, centro_id, residente_id, tipo_codigo, texto, publicada_por_cuenta_id, publicada_en)
            VALUES (NEWID(), @CommunicationId, @CenterId, @ResidentId, @Type, @Text, @AccountId, @OccurredAt);
            """, new
        {
            CommunicationId = communicationId, CenterId = access.CenterId.Value, communication.ResidentId, Type = type.ToCode(), Text = text,
            AccountId = access.AccountId.Value, OccurredAt = occurredAt,
        }, transaction, cancellationToken: ct));
        await AuditAsync(connection, transaction, access, communicationId, communication, "FAMILY_COMMUNICATION_CORRECTIVE", occurredAt, ct);
        transaction.Commit();
    }

    public async Task WithdrawAsync(FamilyCommunicationAccess access, Guid communicationId, string reason, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var communication = await LockAndReadAsync(connection, transaction, access, communicationId, ct);
        var occurredAt = DateTimeOffset.UtcNow;
        EnsureCanAmend(communication, occurredAt);

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.comunicaciones_familiares_retiradas
                (id, comunicacion_id, centro_id, residente_id, motivo, retirada_por_cuenta_id, retirada_en)
            VALUES (NEWID(), @CommunicationId, @CenterId, @ResidentId, @Reason, @AccountId, @OccurredAt);
            """, new
        {
            CommunicationId = communicationId, CenterId = access.CenterId.Value, communication.ResidentId, Reason = reason,
            AccountId = access.AccountId.Value, OccurredAt = occurredAt,
        }, transaction, cancellationToken: ct));
        await AuditAsync(connection, transaction, access, communicationId, communication, "FAMILY_COMMUNICATION_WITHDRAW", occurredAt, ct);
        transaction.Commit();
    }

    /// <summary>Comprueba que quien actúa es el administrador (si es Administración), bloquea el comunicado para ordenar dos operaciones
    /// simultáneas sobre él (la segunda ya ve el resultado de la primera) y lo lee dentro del ámbito; si no es de ese ámbito, AccessDenied.</summary>
    private static async Task<PublishRow> LockAndReadAsync(
        SqlConnection connection, SqlTransaction transaction, FamilyCommunicationAccess access, Guid communicationId, CancellationToken ct)
    {
        if (access.Profile == SystemProfile.Administracion)
        {
            await SqlProfessionalAccountRepository.EnsureAdministratorAsync(
                connection, transaction, new AccountAdministrationAccess(access.ProfileScopeId, access.AccountId, access.CenterId), ct);
        }

        var parameters = new
        {
            access.ProfileScopeId, CenterId = access.CenterId.Value, AccountId = access.AccountId.Value, CommunicationId = communicationId,
        };
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT id FROM dbo.comunicaciones_familiares WITH (UPDLOCK, ROWLOCK) WHERE id = @CommunicationId AND centro_id = @CenterId",
            parameters, transaction, cancellationToken: ct));
        return await connection.QuerySingleOrDefaultAsync<PublishRow>(new CommandDefinition($"""
            SELECT family.preparado_en AS PreparedAt, family.publicacion_programada_en AS ScheduledAt, ea.unidad_id AS UnitId,
                   family.residente_id AS ResidentId, {FamilyCommunicationSql.CurrentType} AS TypeCode,
                   (SELECT early.publicada_en FROM dbo.comunicaciones_familiares_publicacion_anticipada early
                     WHERE early.comunicacion_id = family.id) AS PublishedEarlyAt,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.comunicaciones_familiares_retiradas withdrawal
                                           WHERE withdrawal.comunicacion_id = family.id) THEN 1 ELSE 0 END AS BIT) AS Withdrawn
            {ScopedCommunications(access)}
               AND family.id = @CommunicationId
            """, parameters, transaction, cancellationToken: ct)) ?? throw new AccessDeniedException();
    }

    private static bool IsPublished(PublishRow communication, DateTimeOffset now) => FamilyCommunicationSchedule.IsPublished(
        FamilyCommunicationSql.ScheduledAt(communication.PreparedAt, communication.ScheduledAt), communication.PublishedEarlyAt is null ? null : now, now);

    /// <summary>Una correctora o una retirada solo caben sobre un comunicado ya publicado (antes se corrige) y todavía no retirado.</summary>
    private static void EnsureCanAmend(PublishRow communication, DateTimeOffset now)
    {
        if (communication.Withdrawn)
        {
            throw new DomainValidationException("FAMILY_COMMUNICATION_WITHDRAWN");
        }

        if (!IsPublished(communication, now))
        {
            throw new DomainValidationException("FAMILY_COMMUNICATION_NOT_PUBLISHED");
        }
    }

    private static Task AuditAsync(
        SqlConnection connection, SqlTransaction transaction, FamilyCommunicationAccess access, Guid communicationId, PublishRow communication,
        string action, DateTimeOffset occurredAt, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.eventos_auditoria
                (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
            VALUES (NEWID(), @AccountId, @Profile, @CenterId, @UnitId, @ResidentId, 'FAMILY_COMMUNICATION', @CommunicationId, @Action, @OccurredAt)
            """, new
        {
            CommunicationId = communicationId, CenterId = access.CenterId.Value, AccountId = access.AccountId.Value, Profile = access.Profile.ToCode(),
            communication.UnitId, communication.ResidentId, Action = action, OccurredAt = occurredAt,
        }, transaction, cancellationToken: ct));

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed record CommunicationRow(
        Guid Id, Guid EventId, Guid ResidentId, string ResidentName, string? UnitName, string TypeCode, string Text, DateTime PreparedAt,
        DateTime? ScheduledAt, DateTime? PublishedEarlyAt);

    private sealed record PublishRow(
        DateTime PreparedAt, DateTime? ScheduledAt, Guid UnitId, Guid ResidentId, string TypeCode, DateTime? PublishedEarlyAt, bool Withdrawn);

    private sealed record CorrectiveRow(Guid CommunicationId, Guid Id, string TypeCode, string Text, DateTime PublishedAt);

    private sealed record WithdrawalRow(Guid CommunicationId, string Reason, DateTime WithdrawnAt);
}
