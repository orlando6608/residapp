using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Authorization;
using ResidApp.Application.Ports;
using ResidApp.Domain.Baseline;
using ResidApp.Domain.Baseline.Catalogs;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>
/// Verificación uno por uno de los 27 triggers de 0001_init_sqlserver.sql (recreados con nombres en español
/// por 0002) contra la instancia real de SQL Server: cada escritura prohibida se rechaza con su código, y las
/// transiciones permitidas (revocar, cerrar, avanzar revisión) siguen funcionando. Los datos se generan con
/// los repositorios reales (alta, dos firmas de basal, lectura de Dirección, borrador cancelado), así que cada
/// tabla protegida tiene filas reales sobre las que intentar la escritura.
/// </summary>
public class DatabaseTriggerTests
{
    private sealed record Scenario(
        Guid AccountId, Guid ProfileScopeId, Guid CenterId, Guid ResidentId, Guid ResidentScopeId, Guid SignedDraftId, Guid CancelledDraftId);

    private static readonly Lazy<Task<Scenario>> SharedScenario = new(BuildScenarioAsync);

    [Theory]
    // Ámbitos y permisos: solo se revocan, nunca se editan ni se borran.
    [InlineData("UPDATE dbo.ambitos_perfil SET concedido_en = DATEADD(day, -1, concedido_en) WHERE id = @ProfileScopeId", "PROFILE_SCOPE_REVOKE_ONLY")]
    [InlineData("DELETE FROM dbo.ambitos_perfil WHERE id = @ProfileScopeId", "PROFILE_SCOPE_DELETE_FORBIDDEN")]
    [InlineData("UPDATE dbo.ambitos_perfil_unidad SET concedido_en = DATEADD(day, -1, concedido_en) WHERE ambito_perfil_id = @ProfileScopeId", "PROFILE_UNIT_SCOPE_REVOKE_ONLY")]
    [InlineData("DELETE FROM dbo.ambitos_perfil_unidad WHERE ambito_perfil_id = @ProfileScopeId", "PROFILE_UNIT_SCOPE_DELETE_FORBIDDEN")]
    [InlineData("UPDATE dbo.ambitos_perfil_residente SET concedido_en = DATEADD(day, -1, concedido_en) WHERE id = @ResidentScopeId", "PROFILE_RESIDENT_SCOPE_REVOKE_ONLY")]
    [InlineData("DELETE FROM dbo.ambitos_perfil_residente WHERE id = @ResidentScopeId", "PROFILE_RESIDENT_SCOPE_DELETE_FORBIDDEN")]
    [InlineData("UPDATE dbo.permisos_perfil SET concedido_en = DATEADD(day, -1, concedido_en) WHERE ambito_perfil_id = @ProfileScopeId", "PROFILE_PERMISSION_REVOKE_ONLY")]
    [InlineData("DELETE FROM dbo.permisos_perfil WHERE ambito_perfil_id = @ProfileScopeId", "PROFILE_PERMISSION_DELETE_FORBIDDEN")]
    // Episodio y ubicación del residente: solo se cierran.
    [InlineData("UPDATE dbo.episodios_residente_centro SET referencia_interna = N'cambio' WHERE residente_id = @ResidentId", "RESIDENT_CENTER_EPISODE_CLOSE_ONLY")]
    [InlineData("DELETE FROM dbo.episodios_residente_centro WHERE residente_id = @ResidentId", "RESIDENT_CENTER_EPISODE_DELETE_FORBIDDEN")]
    [InlineData("UPDATE dbo.intervalos_ubicacion_residente SET vigente_desde = DATEADD(day, -1, vigente_desde) WHERE residente_id = @ResidentId", "RESIDENT_LOCATION_INTERVAL_CLOSE_ONLY")]
    [InlineData("DELETE FROM dbo.intervalos_ubicacion_residente WHERE residente_id = @ResidentId", "RESIDENT_LOCATION_INTERVAL_DELETE_FORBIDDEN")]
    // Borrador ya firmado: ni él ni su contenido admiten cambios.
    [InlineData("UPDATE dbo.basales_borrador SET revision_borrador = revision_borrador + 1 WHERE id = @SignedDraftId", "BASELINE_DRAFT_TRANSITION_INVALID")]
    [InlineData("DELETE FROM dbo.basales_borrador WHERE id = @SignedDraftId", "BASELINE_DRAFT_DELETE_FORBIDDEN")]
    [InlineData("UPDATE dbo.basales_borrador_areas SET observacion = N'cambio' WHERE borrador_id = @SignedDraftId", "BASELINE_DRAFT_AREA_PARENT_NOT_ACTIVE")]
    [InlineData("DELETE FROM dbo.basales_borrador_areas WHERE borrador_id = @SignedDraftId", "BASELINE_DRAFT_AREA_IMMUTABLE")]
    [InlineData("UPDATE dbo.basales_borrador_barthel SET puntuacion_total = 0 WHERE borrador_id = @SignedDraftId", "BASELINE_DRAFT_BARTHEL_PARENT_NOT_ACTIVE")]
    // Sobre el borrador cancelado, cuyo Barthel no tiene ítems: con ítems, la FK de estos rechaza el borrado antes que el trigger.
    [InlineData("DELETE FROM dbo.basales_borrador_barthel WHERE borrador_id = @CancelledDraftId", "BASELINE_DRAFT_BARTHEL_IMMUTABLE")]
    [InlineData("UPDATE dbo.basales_borrador_barthel_items SET puntuacion_otorgada = 0 WHERE borrador_id = @SignedDraftId", "BASELINE_DRAFT_BARTHEL_ITEM_PARENT_NOT_ACTIVE")]
    [InlineData("DELETE FROM dbo.basales_borrador_barthel_items WHERE borrador_id = @SignedDraftId", "BASELINE_DRAFT_BARTHEL_ITEM_IMMUTABLE")]
    // Versiones firmadas: append-only estricto.
    [InlineData("UPDATE dbo.basales_version SET motivo_codigo = motivo_codigo WHERE residente_id = @ResidentId", "BASELINE_VERSION_IMMUTABLE")]
    [InlineData("DELETE FROM dbo.basales_version WHERE residente_id = @ResidentId", "BASELINE_VERSION_IMMUTABLE")]
    [InlineData("UPDATE dbo.basales_version_areas SET observacion = N'cambio' WHERE residente_id = @ResidentId", "BASELINE_VERSION_AREA_IMMUTABLE")]
    [InlineData("DELETE FROM dbo.basales_version_areas WHERE residente_id = @ResidentId", "BASELINE_VERSION_AREA_IMMUTABLE")]
    [InlineData("UPDATE dbo.basales_version_barthel SET puntuacion_total = 0 WHERE residente_id = @ResidentId", "BASELINE_VERSION_BARTHEL_IMMUTABLE")]
    [InlineData("DELETE FROM dbo.basales_version_barthel WHERE residente_id = @ResidentId", "BASELINE_VERSION_BARTHEL_IMMUTABLE")]
    [InlineData("UPDATE dbo.basales_version_barthel_items SET puntuacion_otorgada = 0 WHERE residente_id = @ResidentId", "BASELINE_VERSION_BARTHEL_ITEM_IMMUTABLE")]
    [InlineData("DELETE FROM dbo.basales_version_barthel_items WHERE residente_id = @ResidentId", "BASELINE_VERSION_BARTHEL_ITEM_IMMUTABLE")]
    [InlineData("DELETE FROM dbo.basales_vigentes_residente WHERE residente_id = @ResidentId", "BASELINE_CURRENT_DELETE_FORBIDDEN")]
    [InlineData("UPDATE dbo.basales_sustituciones SET sustituido_en = sustituido_en WHERE residente_id = @ResidentId", "BASELINE_SUPERSESSION_IMMUTABLE")]
    [InlineData("DELETE FROM dbo.basales_sustituciones WHERE residente_id = @ResidentId", "BASELINE_SUPERSESSION_IMMUTABLE")]
    // Auditoría e idempotencia.
    [InlineData("UPDATE dbo.eventos_auditoria SET ocurrido_en = ocurrido_en WHERE residente_id = @ResidentId", "AUDIT_EVENT_IMMUTABLE")]
    [InlineData("DELETE FROM dbo.eventos_auditoria WHERE residente_id = @ResidentId", "AUDIT_EVENT_IMMUTABLE")]
    [InlineData("UPDATE dbo.operaciones_idempotencia SET completado_en = completado_en WHERE cuenta_id = @AccountId", "IDEMPOTENCY_OPERATION_IMMUTABLE")]
    [InlineData("DELETE FROM dbo.operaciones_idempotencia WHERE cuenta_id = @AccountId", "IDEMPOTENCY_OPERATION_IMMUTABLE")]
    public async Task ForbiddenWrite_IsRejectedByTrigger(string sql, string expectedCode)
    {
        var scenario = await SharedScenario.Value;
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        var targetRows = await connection.ExecuteScalarAsync<int>(ToCountQuery(sql), scenario);
        Assert.True(targetRows > 0, "La escritura debe apuntar a filas reales para que el trigger AFTER llegue a evaluarlas.");

        var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql, scenario));
        Assert.Contains(expectedCode, ex.Message);
    }

    [Fact]
    public async Task CurrentBaseline_CenterChange_IsRejectedByForeignKeyBeforeTheUpdateGuard()
    {
        // TR_rcb_update_guard (BASELINE_CURRENT_UPDATE_INVALID) es inalcanzable: cambiar centro_id rompe
        // antes la FK compuesta a residentes(centro_id, id), y los triggers AFTER se disparan después de
        // comprobar las restricciones. Se verifica que el cambio queda rechazado igualmente.
        var scenario = await SharedScenario.Value;
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.basales_vigentes_residente SET centro_id = NEWID() WHERE residente_id = @ResidentId", scenario));
        Assert.Equal(547, ex.Number);
    }

    [Fact]
    public async Task ProfileScopeUnitScopeAndPermission_CanBeRevokedOnce_ButNotTwice()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria, [ResidentBaselinePermission.BaselineReevaluate.ToCode()]);
        var parameters = new { seed.ProfileScopeId, AccountId = seed.AccountId.Value };
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        await AssertRevokeOnceAsync(connection, parameters, """
            UPDATE dbo.permisos_perfil SET revocado_en = SYSUTCDATETIME(), revocado_por_cuenta_id = @AccountId
             WHERE ambito_perfil_id = @ProfileScopeId
            """, "PROFILE_PERMISSION_REVOKE_ONLY");
        await AssertRevokeOnceAsync(connection, parameters, """
            UPDATE dbo.ambitos_perfil_unidad SET revocado_en = SYSUTCDATETIME(), revocado_por_cuenta_id = @AccountId
             WHERE ambito_perfil_id = @ProfileScopeId
            """, "PROFILE_UNIT_SCOPE_REVOKE_ONLY");
        await AssertRevokeOnceAsync(connection, parameters, """
            UPDATE dbo.ambitos_perfil SET estado = 'REVOKED', revocado_en = SYSUTCDATETIME(), revocado_por_cuenta_id = @AccountId
             WHERE id = @ProfileScopeId
            """, "PROFILE_SCOPE_REVOKE_ONLY");
    }

    [Fact]
    public async Task ResidentEpisodeAndLocation_CanBeClosedOnce_ButNotTwice()
    {
        var (_, residentId) = await CreateResidentAsync();
        var parameters = new { ResidentId = residentId.Value };
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        await AssertRevokeOnceAsync(connection, parameters,
            "UPDATE dbo.intervalos_ubicacion_residente SET vigente_hasta = DATEADD(minute, 1, vigente_desde) WHERE residente_id = @ResidentId",
            "RESIDENT_LOCATION_INTERVAL_CLOSE_ONLY");
        await AssertRevokeOnceAsync(connection, parameters,
            "UPDATE dbo.episodios_residente_centro SET vigente_hasta = DATEADD(minute, 1, vigente_desde) WHERE residente_id = @ResidentId",
            "RESIDENT_CENTER_EPISODE_CLOSE_ONLY");
    }

    [Fact]
    public async Task ActiveDraft_OnlyAdvancesRevisionByExactlyOne_AndCannotBeSignedWithoutAVersion()
    {
        var (enfermeria, residentId) = await CreateResidentAsync();
        var repository = new SqlBaselineRepository(TestDatabase.ConnectionFactory);
        var draft = await repository.CreateDraftAsync(DraftInput(enfermeria, residentId, BaselineReason.Alta));
        var parameters = new { DraftId = draft.DraftId.Value };
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        var skipped = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.basales_borrador SET revision_borrador = revision_borrador + 2 WHERE id = @DraftId", parameters));
        Assert.Contains("BASELINE_DRAFT_TRANSITION_INVALID", skipped.Message);

        Assert.Equal(1, await connection.ExecuteAsync(
            "UPDATE dbo.basales_borrador SET revision_borrador = revision_borrador + 1 WHERE id = @DraftId", parameters));

        var signedWithoutVersion = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.basales_borrador SET estado = 'SIGNED' WHERE id = @DraftId", parameters));
        Assert.Contains("BASELINE_DRAFT_TRANSITION_INVALID", signedWithoutVersion.Message);
    }

    private static async Task AssertRevokeOnceAsync(SqlConnection connection, object parameters, string sql, string expectedCode)
    {
        Assert.True(await connection.ExecuteAsync(sql, parameters) > 0);
        var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql, parameters));
        Assert.Contains(expectedCode, ex.Message);
    }

    /// <summary>"UPDATE t SET ... WHERE x" / "DELETE FROM t WHERE x" → "SELECT COUNT(*) FROM t WHERE x".</summary>
    private static string ToCountQuery(string sql)
    {
        var where = sql[sql.IndexOf(" WHERE ", StringComparison.Ordinal)..];
        var table = sql.StartsWith("DELETE FROM ", StringComparison.Ordinal)
            ? sql["DELETE FROM ".Length..].Split(' ')[0]
            : sql["UPDATE ".Length..].Split(' ')[0];
        return $"SELECT COUNT(*) FROM {table}{where}";
    }

    private static async Task<Scenario> BuildScenarioAsync()
    {
        var repository = new SqlBaselineRepository(TestDatabase.ConnectionFactory);
        var (enfermeria, residentId) = await CreateResidentAsync();

        // Dos firmas: la segunda sustituye a la primera (basales_sustituciones) y actualiza basales_vigentes_residente.
        var signedDraftId = await SignFullDraftAsync(repository, enfermeria, residentId, BaselineReason.Alta);
        await SignFullDraftAsync(repository, enfermeria, residentId, BaselineReason.RevisionProgramada);

        // Lectura de Dirección Clínica: deja una fila CLINICAL_DETAIL_READ en eventos_auditoria.
        var direction = await SeedFixture.AddProfileToCenterAsync(
            SystemProfile.DireccionClinica, enfermeria.CenterId, enfermeria.UnitId, [ResidentBaselinePermission.ClinicalDetailRead.ToCode()]);
        await repository.ReadAsClinicalDirectionAsync(new ClinicalDirectionReadInput(
            direction.AccountId, enfermeria.CenterId, enfermeria.UnitId, residentId,
            ClinicalResourceType.BaselineCurrent, ClinicalDetailAccessPurpose.SupervisionClinica, Guid.NewGuid()));

        // Borrador cancelado con un Barthel sin ítems (insertado a mano mientras estaba activo).
        var cancelled = await repository.CreateDraftAsync(DraftInput(enfermeria, residentId, BaselineReason.RevisionProgramada));
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.basales_borrador_barthel
                (id, borrador_id, residente_id, centro_id, fecha_valoracion, puntuacion_total, registrado_por_cuenta_id, registrado_por_perfil, registrado_en)
            VALUES (@Id, @DraftId, @ResidentId, @CenterId, '2026-09-28', NULL, @AccountId, 'ENFERMERIA', SYSUTCDATETIME())
            """, new
        {
            Id = Guid.NewGuid(), DraftId = cancelled.DraftId.Value, ResidentId = residentId.Value,
            CenterId = enfermeria.CenterId.Value, AccountId = enfermeria.AccountId.Value,
        });
        await repository.CancelDraftAsync(new CancelBaselineDraftInput(Owner(enfermeria, residentId), "Verificación de triggers"));

        var residentScopeId = Guid.NewGuid();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.ambitos_perfil_residente (id, ambito_perfil_id, centro_id, residente_id, concedido_en, concedido_por_cuenta_id)
            VALUES (@Id, @ProfileScopeId, @CenterId, @ResidentId, SYSUTCDATETIME(), @AccountId)
            """, new
        {
            Id = residentScopeId, enfermeria.ProfileScopeId, CenterId = enfermeria.CenterId.Value,
            ResidentId = residentId.Value, AccountId = enfermeria.AccountId.Value,
        });

        return new Scenario(enfermeria.AccountId.Value, enfermeria.ProfileScopeId, enfermeria.CenterId.Value, residentId.Value,
            residentScopeId, signedDraftId.Value, cancelled.DraftId.Value);
    }

    private static async Task<BaselineDraftId> SignFullDraftAsync(
        SqlBaselineRepository repository, SeededProfile enfermeria, ResidentId residentId, BaselineReason reason)
    {
        var created = await repository.CreateDraftAsync(DraftInput(enfermeria, residentId, reason));
        var owner = Owner(enfermeria, residentId);
        foreach (var (area, answer) in BaselineTestData.NineAreas())
        {
            await repository.SaveAreaAsync(new SaveBaselineDraftAreaInput(owner, area, answer, null));
        }
        await repository.SaveBarthelAsync(new SaveBaselineDraftBarthelInput(owner, new DateOnly(2026, 9, 28), BaselineTestData.FullBarthelItems()));
        await repository.SignDraftAsync(new SignBaselineDraftInput(
            enfermeria.AccountId, SystemProfile.Enfermeria, enfermeria.CenterId, enfermeria.UnitId, residentId,
            created.DraftId, created.DraftRevision, Guid.NewGuid()));
        return created.DraftId;
    }

    private static async Task<(SeededProfile Enfermeria, ResidentId ResidentId)> CreateResidentAsync()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var enfermeria = await SeedFixture.AddProfileToCenterAsync(
            SystemProfile.Enfermeria, admin.CenterId, admin.UnitId,
            [ResidentBaselinePermission.BaselineInitialComplete.ToCode(), ResidentBaselinePermission.BaselineReevaluate.ToCode()]);
        var resident = await new SqlResidentRepository(TestDatabase.ConnectionFactory).CreateWithInitialLocationAsync(new CreateResidentInput(
            admin.AccountId, SystemProfile.Administracion, admin.CenterId, admin.UnitId,
            "Residente Verificación Triggers", new DateOnly(1941, 3, 3), DocumentedSexCode.Mujer, null, null, null, null, null, Guid.NewGuid()));
        return (enfermeria, resident.ResidentId);
    }

    private static CreateBaselineDraftInput DraftInput(SeededProfile enfermeria, ResidentId residentId, BaselineReason reason) =>
        new(enfermeria.AccountId, SystemProfile.Enfermeria, enfermeria.CenterId, enfermeria.UnitId, residentId, reason,
            InformationSourceCode.ValoracionDirecta, null, new DateOnly(2026, 9, 28), Guid.NewGuid());

    private static OwnedActiveDraftInput Owner(SeededProfile enfermeria, ResidentId residentId) =>
        new(enfermeria.AccountId, SystemProfile.Enfermeria, enfermeria.CenterId, residentId);
}
