using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Ficha de antecedentes de salud, fase 1: alergias (script 0094). Cada guardado va en una transacción que bloquea al residente (UPDLOCK), como la foto y la
/// corrección de identidad: comprueba que la versión vigente es la que vio quien edita (si no, HEALTH_BACKGROUND_REVISION_CONFLICT), vuelve a comprobar el permiso
/// HEALTH_BACKGROUND_EDIT del ámbito, inserta la versión y sus elementos y deja su evento en dbo.eventos_auditoria, sin el texto de la ficha.
/// </summary>
public sealed class SqlHealthBackgroundRepository(SqlConnectionFactory connections) : IHealthBackgroundRepository
{
    private const string Section = HealthBackground.AllergiesSection;

    public async Task<AllergyRecord?> ReadAllergiesAsync(CenterId centerId, ResidentId residentId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var version = await connection.QuerySingleOrDefaultAsync<VersionRow>(new CommandDefinition("""
            SELECT TOP (1) id AS Id, numero_version AS Version, estado_codigo AS State, numero_elementos AS Count, fuente_codigo AS Source, creado_por_perfil AS Profile, creado_en AS CreatedAt
              FROM dbo.fichas_antecedentes_versiones
             WHERE residente_id = @ResidentId AND centro_id = @CenterId AND apartado_codigo = @Section
             ORDER BY numero_version DESC
            """, new { ResidentId = residentId.Value, CenterId = centerId.Value, Section }, cancellationToken: ct));
        if (version is null)
        {
            return null;
        }

        var rows = (await connection.QueryAsync<ItemRow>(new CommandDefinition("""
            SELECT tipo_codigo AS Type, nombre AS Name, reaccion AS Reaction
              FROM dbo.fichas_antecedentes_elementos
             WHERE version_id = @VersionId AND centro_id = @CenterId
             ORDER BY posicion
            """, new { VersionId = version.Id, CenterId = centerId.Value }, cancellationToken: ct))).ToList();
        // Una versión sin todos sus elementos o que no cumple las reglas del apartado (la BD no puede garantizarlo, 0094) no es fiable: no se enseña como válida
        // ni se imprime, y quien edita puede guardar otra que la corrija.
        try
        {
            if (rows.Count != version.Count)
            {
                throw new HealthBackgroundInconsistentException(version.Version);
            }
            var content = new AllergyRecordContent(
                EnumCode.ParseCode<HealthBackgroundState>(version.State),
                rows.Select(row => new AllergyItem(EnumCode.ParseCode<AllergyItemType>(row.Type), row.Name, row.Reaction)).ToList(),
                version.Source is { } source ? EnumCode.ParseCode<HealthBackgroundSource>(source) : null);
            return new AllergyRecord(
                version.Version, content, EnumCode.ParseCode<SystemProfile>(version.Profile),
                new DateTimeOffset(DateTime.SpecifyKind(version.CreatedAt, DateTimeKind.Utc)));
        }
        catch (DomainValidationException)
        {
            throw new HealthBackgroundInconsistentException(version.Version);
        }
    }
    public async Task<int> SaveAllergiesAsync(
        HealthBackgroundTarget target, int expectedVersion, HealthBackgroundReason reason, AllergyRecordContent content, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await LockResidentAsync(connection, transaction, target, ct);

        var current = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT ISNULL(MAX(numero_version), 0) FROM dbo.fichas_antecedentes_versiones
             WHERE residente_id = @ResidentId AND centro_id = @CenterId AND apartado_codigo = @Section
            """, new { ResidentId = target.ResidentId.Value, CenterId = target.CenterId.Value, Section }, transaction, cancellationToken: ct));
        if (current != expectedVersion)
        {
            throw new DomainValidationException(HealthBackground.RevisionConflictCode);
        }

        var versionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.fichas_antecedentes_versiones
                (id, centro_id, residente_id, apartado_codigo, numero_version, estado_codigo, numero_elementos, fuente_codigo, motivo_codigo,
                 creado_por_cuenta_id, creado_por_perfil, creado_en)
            VALUES (@Id, @CenterId, @ResidentId, @Section, @Version, @State, @Count, @Source, @Reason, @AccountId, @Profile, @OccurredAt);
            """, new
        {
            Id = versionId, CenterId = target.CenterId.Value, ResidentId = target.ResidentId.Value, Section, Version = current + 1,
            State = content.State.ToCode(), Count = content.Items.Count, Source = content.Source?.ToCode(), Reason = reason.ToCode(),
            AccountId = target.AccountId.Value, Profile = target.Profile.ToCode(), OccurredAt = now,
        }, transaction, cancellationToken: ct));

        for (var index = 0; index < content.Items.Count; index++)
        {
            var item = content.Items[index];
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.fichas_antecedentes_elementos (id, version_id, residente_id, centro_id, posicion, tipo_codigo, nombre, reaccion)
                VALUES (NEWID(), @VersionId, @ResidentId, @CenterId, @Position, @Type, @Name, @Reaction);
                """, new
            {
                VersionId = versionId, ResidentId = target.ResidentId.Value, CenterId = target.CenterId.Value, Position = index + 1,
                Type = item.Type.ToCode(), item.Name, item.Reaction,
            }, transaction, cancellationToken: ct));
        }

        // La auditoría lleva solo identificadores: ni el estado ni el texto de la ficha.
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.eventos_auditoria
                (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
            VALUES (NEWID(), @AccountId, @Profile, @CenterId, @UnitId, @ResidentId, 'HEALTH_BACKGROUND', @VersionId, @Action, @OccurredAt);
            """, new
        {
            AccountId = target.AccountId.Value, Profile = target.Profile.ToCode(), CenterId = target.CenterId.Value, UnitId = target.UnitId.Value,
            ResidentId = target.ResidentId.Value, VersionId = versionId, OccurredAt = now,
            Action = reason == HealthBackgroundReason.Correccion ? "HEALTH_BACKGROUND_CORRECT" : "HEALTH_BACKGROUND_SAVE",
        }, transaction, cancellationToken: ct));
        transaction.Commit();
        return current + 1;
    }

    /// <summary>Bloquea al residente activo y vuelve a comprobar el permiso HEALTH_BACKGROUND_EDIT de su ámbito: si Administración se lo retiró después de la
    /// comprobación del caso de uso, no pasa.</summary>
    private static async Task LockResidentAsync(SqlConnection connection, SqlTransaction transaction, HealthBackgroundTarget target, CancellationToken ct)
    {
        var active = await connection.ExecuteScalarAsync<int?>(new CommandDefinition("""
            SELECT 1 FROM dbo.residentes WITH (UPDLOCK, ROWLOCK)
             WHERE id = @ResidentId AND centro_id = @CenterId AND estado = 'ACTIVE'
            """, new { ResidentId = target.ResidentId.Value, CenterId = target.CenterId.Value }, transaction, cancellationToken: ct));
        if (active is null)
        {
            throw new AccessDeniedException();
        }

        var allowed = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*)
              FROM dbo.permisos_perfil permission
             WHERE permission.ambito_perfil_id = @ProfileScopeId AND permission.centro_id = @CenterId
               AND permission.permiso_codigo = @Permission AND permission.revocado_en IS NULL
            """, new { target.ProfileScopeId, CenterId = target.CenterId.Value, Permission = ProfilePermissions.HealthBackgroundEdit },
            transaction, cancellationToken: ct));
        if (allowed == 0)
        {
            throw new AccessDeniedException();
        }
    }

    private sealed record VersionRow(Guid Id, int Version, string State, short Count, string? Source, string Profile, DateTime CreatedAt);

    private sealed record ItemRow(string Type, string Name, string? Reaction);
}
