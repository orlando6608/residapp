using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Appointments;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Los avisos por correo de las citas externas y lo que la familia ofrece, vistos por el personal (PR 6; script 0105). El interruptor de correos del centro y la baja de una persona de la familia son
/// tablas de solo añadir con la misma forma que los interruptores del PR 5: se inserta la fila siguiente bajo bloqueo, con el número visto, y se deja el evento en la auditoría con solo identificadores.
/// De lo que la familia ofrece se lee el vínculo y, solo para quien puede ver el nombre (Administración, y Enfermería con el permiso de gestionar citas), el nombre resuelto en la lectura; nunca quién dijo
/// «No puedo»: solo cuántas personas.
/// </summary>
public sealed partial class SqlExternalAppointmentRepository
{
    public const string MailNeedsFamilyCode = "EXTERNAL_APPOINTMENT_MAIL_NEEDS_FAMILY";

    public async Task<ExternalFamilyMailState> ReadMailAsync(ExternalAppointmentActor actor, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await RequireAdministrationAsync(connection, null, actor, ct);
        var latest = await LatestMailAsync(connection, null, actor, ct);
        var family = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            $"SELECT CASE WHEN {ExternalFamilyVisibilitySql.CenterEnabled} THEN 1 ELSE 0 END", Params(actor, new { }), cancellationToken: ct));
        return new ExternalFamilyMailState(latest?.Enabled ?? false, latest?.Number ?? 0, family);
    }

    public async Task<int> SetMailAsync(ExternalAppointmentActor actor, int expectedNumber, bool enable, bool informed, CancellationToken ct = default)
    {
        if (actor.Profile != SystemProfile.Administracion)
        {
            throw new AccessDeniedException();
        }

        if (enable && !informed)
        {
            throw new DomainValidationException(ExternalAppointment.InvalidCode);
        }

        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await RequireAdministrationAsync(connection, transaction, actor, ct);
        var latest = await connection.QuerySingleOrDefaultAsync<MailSwitchRow?>(new CommandDefinition("""
            SELECT TOP (1) numero AS Number, correo_activo AS Enabled FROM dbo.citas_externas_correo_centro WITH (UPDLOCK, HOLDLOCK) WHERE centro_id = @CenterId ORDER BY numero DESC
            """, Params(actor, new { }), transaction, cancellationToken: ct));
        if ((latest?.Number ?? 0) != expectedNumber || (latest?.Enabled ?? false) == enable)
        {
            throw new DomainValidationException(ExternalAppointment.RevisionConflictCode);
        }

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.citas_externas_correo_centro (id, centro_id, numero, correo_activo, confirma_informada, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
                VALUES (@Id, @CenterId, @Number, @Enable, @Informed, @AccountId, @Profile, @Now);
                """, Params(actor, new { Id = id, Number = expectedNumber + 1, Enable = enable, Informed = enable && informed, Now = now.UtcDateTime }), transaction, cancellationToken: ct));
        }
        catch (SqlException error) when (error.Number == 50947)
        {
            throw new DomainValidationException(MailNeedsFamilyCode);
        }
        catch (SqlException error) when (error.Number is 2601 or 2627 or 50946)
        {
            throw new DomainValidationException(ExternalAppointment.RevisionConflictCode);
        }

        await AuditFamilyAsync(connection, transaction, actor, null, null, id, enable ? "EXT_APPOINTMENT_MAIL_ON" : "EXT_APPOINTMENT_MAIL_OFF", now, ct);
        transaction.Commit();
        return expectedNumber + 1;
    }

    public async Task<IReadOnlyList<ExternalFamilyMember>> ListFamilyMembersAsync(ExternalAppointmentActor actor, ResidentId residentId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await RequireAdministrationAsync(connection, null, actor, ct);
        await RequireResidentAsync(connection, null, actor, residentId.Value, ct);
        var rows = await connection.QueryAsync<MemberRow>(new CommandDefinition("""
            SELECT fam.id AS FamilyId, fam.nombre_visible AS Name, link.relacion AS Relationship,
                   CAST(CASE WHEN fam.correo IS NOT NULL AND fam.correo_confirmado_en IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS HasConfirmedAddress,
                   CAST(ISNULL(latest.sin_avisos, 0) AS BIT) AS OptedOut, ISNULL(latest.numero, 0) AS Number
              FROM dbo.residentes_familiares link
              JOIN dbo.familiares fam ON fam.id = link.familiar_id AND fam.centro_id = link.centro_id
              OUTER APPLY (SELECT TOP (1) o.numero, o.sin_avisos FROM dbo.citas_externas_correo_familiar o WHERE o.centro_id = fam.centro_id AND o.familiar_id = fam.id ORDER BY o.numero DESC) latest
             WHERE link.residente_id = @ResidentId AND link.centro_id = @CenterId AND link.desvinculado_en IS NULL
             ORDER BY fam.nombre_visible
            """, Params(actor, new { ResidentId = residentId.Value }), cancellationToken: ct));
        return [.. rows.Select(r => new ExternalFamilyMember(r.FamilyId, r.Name, r.Relationship, r.HasConfirmedAddress, r.OptedOut, r.Number))];
    }

    public async Task<int> SetFamilyMemberMailAsync(
        ExternalAppointmentActor actor, ResidentId residentId, Guid familyId, int expectedNumber, bool optOut, CancellationToken ct = default)
    {
        if (actor.Profile != SystemProfile.Administracion)
        {
            throw new AccessDeniedException();
        }

        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await RequireAdministrationAsync(connection, transaction, actor, ct);
        await RequireResidentAsync(connection, transaction, actor, residentId.Value, ct);
        // La persona tiene que ser de la familia de este residente (vínculo vigente): no se da de baja a nadie por su identificador suelto.
        var linked = await connection.ExecuteScalarAsync<int?>(new CommandDefinition("""
            SELECT 1 FROM dbo.residentes_familiares link WHERE link.residente_id = @ResidentId AND link.familiar_id = @FamilyId AND link.centro_id = @CenterId AND link.desvinculado_en IS NULL
            """, Params(actor, new { ResidentId = residentId.Value, FamilyId = familyId }), transaction, cancellationToken: ct));
        if (linked is null)
        {
            throw new AccessDeniedException();
        }

        var latest = await connection.QuerySingleOrDefaultAsync<OptOutRow?>(new CommandDefinition("""
            SELECT TOP (1) numero AS Number, sin_avisos AS OptedOut FROM dbo.citas_externas_correo_familiar WITH (UPDLOCK, HOLDLOCK)
             WHERE centro_id = @CenterId AND familiar_id = @FamilyId ORDER BY numero DESC
            """, Params(actor, new { FamilyId = familyId }), transaction, cancellationToken: ct));
        if ((latest?.Number ?? 0) != expectedNumber || (latest?.OptedOut ?? false) == optOut)
        {
            throw new DomainValidationException(ExternalAppointment.RevisionConflictCode);
        }

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.citas_externas_correo_familiar (id, centro_id, familiar_id, numero, sin_avisos, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
                VALUES (@Id, @CenterId, @FamilyId, @Number, @OptOut, @AccountId, @Profile, @Now);
                """, Params(actor, new { Id = id, FamilyId = familyId, Number = expectedNumber + 1, OptOut = optOut, Now = now.UtcDateTime }), transaction, cancellationToken: ct));
        }
        catch (SqlException error) when (error.Number is 2601 or 2627 or 50946)
        {
            throw new DomainValidationException(ExternalAppointment.RevisionConflictCode);
        }

        await AuditFamilyAsync(connection, transaction, actor, residentId.Value, null, id, optOut ? "EXT_APPOINTMENT_MAIL_OPT_OUT" : "EXT_APPOINTMENT_MAIL_OPT_IN", now, ct);
        transaction.Commit();
        return expectedNumber + 1;
    }

    public async Task<ExternalFamilyOffers?> ReadOffersAsync(ExternalAppointmentActor actor, Guid appointmentId, DateOnly todayAtCenter, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var seen = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            $"SELECT 1 FROM dbo.citas_externas e WHERE e.id = @Id AND e.centro_id = @CenterId AND {AppointmentScopeSql}", Params(actor, new { Id = appointmentId }), cancellationToken: ct));
        if (seen is null)
        {
            return null;
        }

        var names = await CanSeeNamesAsync(connection, actor, ct);
        var offers = await connection.QueryAsync<OfferRow>(new CommandDefinition("""
            SELECT held.ambito_codigo AS Scope, fam.nombre_visible AS Name
              FROM dbo.fn_citas_externas_encargos_vigentes(@Today) held
              JOIN dbo.residentes_familiares link ON link.id = held.vinculo_id AND link.centro_id = held.centro_id
              JOIN dbo.familiares fam ON fam.id = link.familiar_id AND fam.centro_id = link.centro_id
             WHERE held.cita_id = @Id AND held.centro_id = @CenterId
             ORDER BY held.ambito_codigo
            """, Params(actor, new { Id = appointmentId, Today = todayAtCenter.ToDateTime(TimeOnly.MinValue) }), cancellationToken: ct));
        var refusals = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dbo.citas_externas_no_puedo refusal
              JOIN dbo.residentes_familiares link ON link.id = refusal.vinculo_id AND link.centro_id = refusal.centro_id AND link.desvinculado_en IS NULL
             WHERE refusal.cita_id = @Id AND refusal.centro_id = @CenterId AND refusal.no_puede = 1
               AND refusal.numero = (SELECT MAX(latest.numero) FROM dbo.citas_externas_no_puedo latest WHERE latest.cita_id = refusal.cita_id AND latest.vinculo_id = refusal.vinculo_id)
            """, Params(actor, new { Id = appointmentId }), cancellationToken: ct));
        return new ExternalFamilyOffers([.. offers.Select(o => new ExternalFamilyOffer(EnumCode.ParseCode<ExternalArrangementScope>(o.Scope), names ? o.Name : null))], refusals, names);
    }

    public async Task<IReadOnlySet<Guid>> ListOfferedAsync(
        ExternalAppointmentActor actor, IReadOnlyCollection<Guid> appointmentIds, DateOnly todayAtCenter, CancellationToken ct = default)
    {
        if (appointmentIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        using var connection = await connections.OpenAsync(ct);
        var ids = await connection.QueryAsync<Guid>(new CommandDefinition($"""
            SELECT DISTINCT e.id
              FROM dbo.citas_externas e
              JOIN dbo.fn_citas_externas_encargos_vigentes(@Today) held ON held.cita_id = e.id AND held.centro_id = e.centro_id
             WHERE e.centro_id = @CenterId AND e.id IN @Ids AND {AppointmentScopeSql}
               AND ((held.ambito_codigo = 'TRANSPORTE' AND e.transporte_estado_codigo = 'PENDIENTE') OR (held.ambito_codigo = 'ACOMPANANTE' AND e.acompanante_estado_codigo = 'PENDIENTE'))
            """, Params(actor, new { Ids = appointmentIds.ToArray(), Today = todayAtCenter.ToDateTime(TimeOnly.MinValue) }), cancellationToken: ct));
        return ids.ToHashSet();
    }

    /// <summary>Administración y Enfermería con el permiso de gestionar citas ven el nombre de quien se encarga; Medicina, no (decisión 7 del PR 6).</summary>
    private static async Task<bool> CanSeeNamesAsync(SqlConnection connection, ExternalAppointmentActor actor, CancellationToken ct) => actor.Profile switch
    {
        SystemProfile.Administracion => true,
        SystemProfile.Enfermeria => await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dbo.permisos_perfil permission
             WHERE permission.ambito_perfil_id = @ProfileScopeId AND permission.centro_id = @CenterId AND permission.permiso_codigo = @Permission AND permission.revocado_en IS NULL
            """, new { actor.ProfileScopeId, CenterId = actor.CenterId.Value, Permission = ProfilePermissions.ExternalAppointmentManage }, cancellationToken: ct)) > 0,
        _ => false,
    };

    private static async Task RequireResidentAsync(SqlConnection connection, SqlTransaction? transaction, ExternalAppointmentActor actor, Guid residentId, CancellationToken ct)
    {
        if (await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT 1 FROM dbo.residentes WHERE id = @ResidentId AND centro_id = @CenterId AND estado = 'ACTIVE'", Params(actor, new { ResidentId = residentId }), transaction, cancellationToken: ct)) is null)
        {
            throw new AccessDeniedException();
        }
    }

    private static Task<MailSwitchRow?> LatestMailAsync(SqlConnection connection, SqlTransaction? transaction, ExternalAppointmentActor actor, CancellationToken ct) =>
        connection.QuerySingleOrDefaultAsync<MailSwitchRow?>(new CommandDefinition("""
            SELECT TOP (1) numero AS Number, correo_activo AS Enabled FROM dbo.citas_externas_correo_centro WHERE centro_id = @CenterId ORDER BY numero DESC
            """, Params(actor, new { }), transaction, cancellationToken: ct));

    private sealed record MailSwitchRow(int Number, bool Enabled);

    private sealed record OptOutRow(int Number, bool OptedOut);

    private sealed record MemberRow(Guid FamilyId, string Name, string Relationship, bool HasConfirmedAddress, bool OptedOut, int Number);

    private sealed record OfferRow(string Scope, string Name);
}
