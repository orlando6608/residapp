using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Families;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.AdministracionResidentesTests;

namespace ResidApp.IntegrationTests;

/// <summary>Portal Familiar, bloque 1 (script 0052): la cuenta del familiar, a qué residentes accede (vínculo vigente y autorización Activa y
/// vigente) y qué comunicados ve (publicados y dentro de su ventana). Cada prueba crea su propio centro.</summary>
public class PortalFamiliarTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    internal sealed record Setup(SeededProfile Admin, SeededProfile Nurse, ResidentId ResidentId, Guid LinkId, string Subject);

    internal static FamilyPortalApplicationService Portal(string subject) => new(
        new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedPortalSessionIdentityProvider(subject),
        new SqlFamilyPortalReader(TestDatabase.ConnectionFactory));

    private static async Task<(Guid ProfileScopeId, CenterId CenterId)> ScopeAsync(string subject)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var row = await connection.QuerySingleAsync<(Guid Id, Guid CenterId)>("""
            SELECT scope.id, scope.centro_id FROM dbo.ambitos_perfil scope JOIN dbo.cuentas account ON account.id = scope.cuenta_id
             WHERE account.sujeto_externo = @subject AND scope.perfil_codigo = 'FAMILIAR'
            """, new { subject });
        return (row.Id, CenterId.From(row.CenterId));
    }

    internal static async Task<FamilyPortalQuery> QueryAsync(string subject)
    {
        var (scopeId, centerId) = await ScopeAsync(subject);
        return new FamilyPortalQuery(scopeId, centerId);
    }

    internal static async Task<Guid> AddFamilyAsync(SeededProfile admin, ResidentId residentId, string name = "Lucía Pérez") =>
        (await Build(admin.ExternalSubject).AddFamilyMemberAsync(new AddFamilyMemberCommand(
            admin.ProfileScopeId, admin.CenterId, residentId, Guid.NewGuid(), name, "Hija", "600123456", null))).Value;

    internal static async Task ChangeAsync(
        SeededProfile admin, ResidentId residentId, Guid linkId, FamilyAuthorizationChange change, int expected, string? reason = null)
    {
        var result = await Build(admin.ExternalSubject).ChangeFamilyAuthorizationAsync(new ChangeFamilyAuthorizationCommand(
            admin.ProfileScopeId, admin.CenterId, residentId, linkId, change, null, reason, expected));
        Assert.True(result.Ok, result.Error?.Message);
    }

    /// <summary>Un residente con una familiar con acceso al portal y la autorización Activa.</summary>
    internal static async Task<Setup> SetupAsync(string residentName = "Residente Portal")
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var residentId = await CreateResidentAsync(admin, residentName);
        var linkId = await AddFamilyAsync(admin, residentId);
        var subject = $"portal-{Guid.NewGuid():N}"[..20];
        var grant = await Build(admin.ExternalSubject).GrantFamilyPortalAccessAsync(
            new GrantFamilyPortalAccessCommand(admin.ProfileScopeId, admin.CenterId, residentId, linkId, subject));
        Assert.True(grant.Ok, grant.Error?.Message);
        await ChangeAsync(admin, residentId, linkId, FamilyAuthorizationChange.Abrir, 0);
        await ChangeAsync(admin, residentId, linkId, FamilyAuthorizationChange.Activar, 1);
        return new Setup(admin, nurse, residentId, linkId, subject);
    }

    /// <summary>Un comunicado de un evento nuevo del residente, insertado con las horas que se quieran (los guardados antes de 0051 no tienen
    /// hora programada: aquí se fija siempre). preparedAt y scheduledAt son momentos UTC.</summary>
    internal static async Task<Guid> AddCommunicationAsync(
        Setup setup, string text, FamilyCommunicationType type, DateTime preparedAt, DateTime scheduledAt, DateTime? publishedEarlyAt = null,
        string? correctedText = null, ResidentId? residentId = null)
    {
        var resident = residentId ?? setup.ResidentId;
        var registered = await EnfermeriaApplicationServiceTests.BuildService(setup.Nurse.ExternalSubject).RegisterClinicalEventAsync(
            new RegisterClinicalEventCommand(setup.Nurse.ProfileScopeId, setup.Nurse.CenterId, resident, "Evento de prueba del portal.",
                ResidApp.Domain.Auxiliar.DailyChangeClassification.Ordinario, null, Guid.NewGuid()));
        var id = Guid.NewGuid();
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.comunicaciones_familiares
                (id, evento_id, residente_id, centro_id, tipo_codigo, texto, preparado_por_cuenta_id, preparado_en, publicacion_programada_en)
            VALUES (@id, @eventId, @residentId, @centerId, @type, @text, @accountId, @preparedAt, @scheduledAt)
            """, new
        {
            id, eventId = registered.Value!.EventId, residentId = resident.Value, centerId = setup.Admin.CenterId.Value, type = type.ToCode(), text,
            accountId = setup.Nurse.AccountId.Value, preparedAt, scheduledAt,
        });
        if (correctedText is not null)
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.comunicaciones_familiares_correcciones
                    (id, comunicacion_id, centro_id, residente_id, numero, tipo_codigo, texto, corregida_por_cuenta_id, corregida_en)
                VALUES (NEWID(), @id, @centerId, @residentId, 1, @type, @correctedText, @accountId, SYSUTCDATETIME())
                """, new
            {
                id, centerId = setup.Admin.CenterId.Value, residentId = resident.Value, type = type.ToCode(), correctedText, accountId = setup.Nurse.AccountId.Value,
            });
        }

        if (publishedEarlyAt is { } early)
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.comunicaciones_familiares_publicacion_anticipada (id, comunicacion_id, centro_id, publicada_por_cuenta_id, publicada_en)
                VALUES (NEWID(), @id, @centerId, @accountId, @early)
                """, new { id, centerId = setup.Admin.CenterId.Value, accountId = setup.Admin.AccountId.Value, early });
        }

        return id;
    }

    internal static async Task<IReadOnlyList<FamilyPortalCommunication>> ListAsync(Setup setup)
    {
        var result = await Portal(setup.Subject).ReadResidentAsync(await QueryAsync(setup.Subject), setup.LinkId);
        Assert.True(result.Ok, result.Error?.Message);
        return result.Value!.Communications;
    }

    internal static async Task<int> CountAsync(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }

    [Fact]
    public async Task Familiar_VeSoloLosResidentesConAutorizacionActivaYVigente()
    {
        var setup = await SetupAsync();
        var portal = Portal(setup.Subject);
        var query = await QueryAsync(setup.Subject);

        var active = await portal.ListResidentsAsync(query);
        await ChangeAsync(setup.Admin, setup.ResidentId, setup.LinkId, FamilyAuthorizationChange.Suspender, 2, "Petición del familiar.");
        var suspended = await portal.ListResidentsAsync(query);
        var readWhileSuspended = await portal.ReadResidentAsync(query, setup.LinkId);
        await ChangeAsync(setup.Admin, setup.ResidentId, setup.LinkId, FamilyAuthorizationChange.Activar, 3);
        var reactivated = await portal.ListResidentsAsync(query);
        // Una Activa cuya fecha «válida hasta» ya pasó está Caducada (se renueva con otra fila Activa).
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.familiares_autorizaciones_cambios
                    (id, centro_id, vinculo_id, numero, estado_codigo, valida_hasta, motivo, registrado_por_cuenta_id, registrado_por_perfil, registrado_en)
                VALUES (NEWID(), @centerId, @linkId, 5, 'ACTIVA', @yesterday, NULL, @accountId, 'ADMINISTRACION', SYSUTCDATETIME())
                """, new { centerId = setup.Admin.CenterId.Value, linkId = setup.LinkId, yesterday = Today.AddDays(-1).ToDateTime(TimeOnly.MinValue), accountId = setup.Admin.AccountId.Value });
        }

        var expired = await portal.ListResidentsAsync(query);
        await ChangeAsync(setup.Admin, setup.ResidentId, setup.LinkId, FamilyAuthorizationChange.Revocar, 5, "Deja de ser familiar autorizado.");
        var revoked = await portal.ListResidentsAsync(query);

        Assert.Equal(("Residente Portal", "Hija"), (Assert.Single(active.Value!).ResidentName, active.Value![0].Relationship));
        Assert.Empty(suspended.Value!);
        Assert.Equal(ApplicationFailureCode.AccessDenied, readWhileSuspended.Error!.Code);
        Assert.Single(reactivated.Value!);
        Assert.Empty(expired.Value!);
        Assert.Empty(revoked.Value!);
    }

    [Fact]
    public async Task Familiar_SinAutorizacionAbierta_NoVeNada()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await CreateResidentAsync(admin, "Residente Sin Autorización");
        var linkId = await AddFamilyAsync(admin, residentId);
        var subject = $"portal-{Guid.NewGuid():N}"[..20];
        await Build(admin.ExternalSubject).GrantFamilyPortalAccessAsync(
            new GrantFamilyPortalAccessCommand(admin.ProfileScopeId, admin.CenterId, residentId, linkId, subject));

        var withoutAuthorization = await Portal(subject).ListResidentsAsync(await QueryAsync(subject));
        await ChangeAsync(admin, residentId, linkId, FamilyAuthorizationChange.Abrir, 0);
        var pending = await Portal(subject).ListResidentsAsync(await QueryAsync(subject));

        Assert.Empty(withoutAuthorization.Value!);
        Assert.Empty(pending.Value!);
    }

    [Fact]
    public async Task Familiar_NoAccedeAlVinculoDeOtroFamiliar_NiAOtroResidente_NiConUnVinculoDesvinculado()
    {
        var setup = await SetupAsync("Residente Uno Portal");
        var otherResident = await CreateResidentAsync(setup.Admin, "Residente Dos Portal");
        var otherLink = await AddFamilyAsync(setup.Admin, otherResident, "Otra Persona");
        await ChangeAsync(setup.Admin, otherResident, otherLink, FamilyAuthorizationChange.Abrir, 0);
        await ChangeAsync(setup.Admin, otherResident, otherLink, FamilyAuthorizationChange.Activar, 1);
        var otherSubject = $"portal-{Guid.NewGuid():N}"[..20];
        await Build(setup.Admin.ExternalSubject).GrantFamilyPortalAccessAsync(
            new GrantFamilyPortalAccessCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, otherResident, otherLink, otherSubject));
        var portal = Portal(setup.Subject);
        var query = await QueryAsync(setup.Subject);

        // El vínculo activo de OTRA persona (con su propia cuenta) con OTRO residente no sirve de id: ni el vínculo ni un id inventado dan acceso.
        var foreignLink = await portal.ReadResidentAsync(query, otherLink);
        var otherListing = await Portal(otherSubject).ListResidentsAsync(await QueryAsync(otherSubject));
        var invented = await portal.ReadResidentAsync(query, Guid.NewGuid());
        var unlink = await Build(setup.Admin.ExternalSubject).UnlinkFamilyMemberAsync(
            new UnlinkFamilyMemberCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, setup.LinkId, "Ya no es familiar."));
        var afterUnlink = await portal.ListResidentsAsync(query);
        var readAfterUnlink = await portal.ReadResidentAsync(query, setup.LinkId);

        Assert.Equal(ApplicationFailureCode.AccessDenied, foreignLink.Error!.Code);
        Assert.Equal("Residente Dos Portal", Assert.Single(otherListing.Value!).ResidentName);
        Assert.Equal(ApplicationFailureCode.AccessDenied, invented.Error!.Code);
        Assert.True(unlink.Ok, unlink.Error?.Message);
        Assert.Empty(afterUnlink.Value!);
        Assert.Equal(ApplicationFailureCode.AccessDenied, readAfterUnlink.Error!.Code);
    }

    [Fact]
    public async Task VinculoDesvinculado_NoDaAcceso_AunqueLaAutorizacionSigaActiva()
    {
        // Desvincular desde Administración revoca antes la autorización; aquí se marca el vínculo sin tocarla para probar la condición por sí sola.
        var setup = await SetupAsync();
        var portal = Portal(setup.Subject);
        var query = await QueryAsync(setup.Subject);
        var before = await portal.ListResidentsAsync(query);
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                UPDATE dbo.residentes_familiares SET desvinculado_en = SYSUTCDATETIME(), desvinculado_por_cuenta_id = @accountId, desvinculado_motivo = 'Prueba.'
                 WHERE id = @linkId
                """, new { accountId = setup.Admin.AccountId.Value, linkId = setup.LinkId });
        }

        var after = await portal.ListResidentsAsync(query);
        var read = await portal.ReadResidentAsync(query, setup.LinkId);

        Assert.Single(before.Value!);
        Assert.Empty(after.Value!);
        Assert.Equal(ApplicationFailureCode.AccessDenied, read.Error!.Code);
    }

    [Fact]
    public async Task Portal_SoloConElPerfilFamiliar_YConUnaCuentaActiva()
    {
        var setup = await SetupAsync();
        var asNurse = Portal(setup.Nurse.ExternalSubject);
        var forged = await asNurse.ListResidentsAsync(new FamilyPortalQuery(setup.Nurse.ProfileScopeId, setup.Nurse.CenterId));
        var foreignScope = await asNurse.ListResidentsAsync(await QueryAsync(setup.Subject));
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("UPDATE dbo.cuentas SET estado = 'SUSPENDED' WHERE sujeto_externo = @subject", new { subject = setup.Subject });
        }

        var suspendedAccount = await Portal(setup.Subject).ListResidentsAsync(await QueryAsync(setup.Subject));

        Assert.Equal(ApplicationFailureCode.AccessDenied, forged.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, foreignScope.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, suspendedAccount.Error!.Code);
    }

    [Fact]
    public async Task Comunicados_SoloLosPublicados_ConSuTextoVigente_YDentroDeSuVentana()
    {
        var setup = await SetupAsync();
        var now = DateTime.UtcNow;
        var published = await AddCommunicationAsync(setup, "Publicado a su hora.", FamilyCommunicationType.Ordinaria, now.AddHours(-30), now.AddHours(-5));
        await AddCommunicationAsync(setup, "Aún sin publicar.", FamilyCommunicationType.Relevante, now.AddHours(-1), now.AddHours(20));
        await AddCommunicationAsync(setup, "Publicado antes de su hora.", FamilyCommunicationType.Relevante, now.AddHours(-3), now.AddHours(20),
            publishedEarlyAt: now.AddHours(-2));
        await AddCommunicationAsync(setup, "Texto original.", FamilyCommunicationType.Ordinaria, now.AddHours(-8), now.AddHours(-7), correctedText: "Texto corregido.");
        await AddCommunicationAsync(setup, "Ordinaria antigua.", FamilyCommunicationType.Ordinaria, now.AddDays(-32), now.AddDays(-31));
        await AddCommunicationAsync(setup, "Relevante de hace 2 meses.", FamilyCommunicationType.Relevante, now.AddDays(-61), now.AddDays(-60));
        await AddCommunicationAsync(setup, "Relevante de hace 7 meses.", FamilyCommunicationType.Relevante, now.AddMonths(-7), now.AddMonths(-7).AddHours(1));

        var communications = await ListAsync(setup);

        Assert.Equal(
            ["Publicado antes de su hora.", "Publicado a su hora.", "Texto corregido.", "Relevante de hace 2 meses."],
            communications.Select(c => c.Text));
        var detail = await Portal(setup.Subject).ReadCommunicationAsync(await QueryAsync(setup.Subject), setup.LinkId, published);
        Assert.Equal(("Publicado a su hora.", FamilyCommunicationType.Ordinaria), (detail.Value!.Communication.Text, detail.Value.Communication.Type));
        Assert.Equal("Residente Portal", detail.Value.Resident.ResidentName);
    }

    [Fact]
    public async Task Comunicado_NoSeLeeSiNoEsDelResidente_NoEstaPublicado_OYaSalioDeSuVentana()
    {
        var setup = await SetupAsync("Residente Con Acceso");
        var other = await CreateResidentAsync(setup.Admin, "Residente Sin Acceso");
        var now = DateTime.UtcNow;
        var ofOther = await AddCommunicationAsync(setup, "Es de otro residente.", FamilyCommunicationType.Ordinaria, now.AddHours(-30), now.AddHours(-5), residentId: other);
        var unpublished = await AddCommunicationAsync(setup, "Sin publicar.", FamilyCommunicationType.Ordinaria, now.AddHours(-1), now.AddHours(20));
        var tooOld = await AddCommunicationAsync(setup, "Demasiado antigua.", FamilyCommunicationType.Ordinaria, now.AddDays(-40), now.AddDays(-39));
        var portal = Portal(setup.Subject);
        var query = await QueryAsync(setup.Subject);

        foreach (var id in new[] { ofOther, unpublished, tooOld, Guid.NewGuid() })
        {
            var result = await portal.ReadCommunicationAsync(query, setup.LinkId, id);
            Assert.Equal(ApplicationFailureCode.AccessDenied, result.Error!.Code);
        }
    }

    [Fact]
    public async Task Lectura_QuedaAuditadaConElPerfilFamiliar_ConLaMismaTransaccion()
    {
        var setup = await SetupAsync();
        var now = DateTime.UtcNow;
        var communicationId = await AddCommunicationAsync(setup, "Para auditar.", FamilyCommunicationType.Ordinaria, now.AddHours(-30), now.AddHours(-5));

        await ListAsync(setup);
        await Portal(setup.Subject).ReadCommunicationAsync(await QueryAsync(setup.Subject), setup.LinkId, communicationId);
        var denied = await Portal(setup.Subject).ReadResidentAsync(await QueryAsync(setup.Subject), Guid.NewGuid());

        Assert.False(denied.Ok);
        var parameters = new { residentId = setup.ResidentId.Value, communicationId };
        Assert.Equal(1, await CountAsync("""
            SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE residente_id = @residentId AND perfil_activo = 'FAMILIAR'
               AND accion_codigo = 'FAMILY_PORTAL_READ' AND tipo_recurso = 'RESIDENT' AND recurso_id = @residentId
            """, parameters));
        Assert.Equal(1, await CountAsync("""
            SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE residente_id = @residentId AND perfil_activo = 'FAMILIAR'
               AND accion_codigo = 'FAMILY_PORTAL_READ' AND tipo_recurso = 'FAMILY_COMMUNICATION' AND recurso_id = @communicationId
            """, parameters));
        // Una lectura denegada no deja rastro de lectura (no se entregó nada).
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE residente_id = @residentId AND accion_codigo = 'FAMILY_PORTAL_READ'", parameters));
    }

    [Fact]
    public async Task DarAcceso_CreaLaCuentaFamiliar_ConAuditoria_YNoApareceEnUsuarios()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await CreateResidentAsync(admin, "Residente Acceso");
        var linkId = await AddFamilyAsync(admin, residentId);
        var service = Build(admin.ExternalSubject);
        var subject = $"portal-{Guid.NewGuid():N}"[..20];

        var invalid = await service.GrantFamilyPortalAccessAsync(new GrantFamilyPortalAccessCommand(admin.ProfileScopeId, admin.CenterId, residentId, linkId, "a b"));
        var granted = await service.GrantFamilyPortalAccessAsync(new GrantFamilyPortalAccessCommand(admin.ProfileScopeId, admin.CenterId, residentId, linkId, $"  {subject}  "));
        var again = await service.GrantFamilyPortalAccessAsync(new GrantFamilyPortalAccessCommand(admin.ProfileScopeId, admin.CenterId, residentId, linkId, subject + "x"));
        var otherLink = await AddFamilyAsync(admin, residentId, "Otro Familiar");
        var taken = await service.GrantFamilyPortalAccessAsync(new GrantFamilyPortalAccessCommand(admin.ProfileScopeId, admin.CenterId, residentId, otherLink, subject));
        var detail = (await service.FindResidentAsync(new FindAdministrativeResidentQuery(admin.ProfileScopeId, admin.CenterId, residentId))).Value!;
        var users = (await service.ListAccountsAsync(new AdministracionQuery(admin.ProfileScopeId, admin.CenterId))).Value!;

        Assert.Equal(ApplicationFailureCode.InvalidInput, invalid.Error!.Code);
        Assert.True(granted.Ok, granted.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, again.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, taken.Error!.Code);
        Assert.Equal(subject, detail.Family.Single(f => f.LinkId == linkId).PortalAccount);
        Assert.Null(detail.Family.Single(f => f.LinkId == otherLink).PortalAccount);
        Assert.DoesNotContain(users, u => u.Subject == subject);
        Assert.Contains(users, u => u.Subject == admin.ExternalSubject);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE residente_id = @id AND accion_codigo = 'FAMILY_PORTAL_ACCESS_GRANT' AND perfil_activo = 'ADMINISTRACION'",
            new { id = residentId.Value }));
        // Sin unidades: el acceso a cada residente sale del vínculo y la autorización, no de las unidades del ámbito.
        Assert.Equal(0, await CountAsync("""
            SELECT COUNT(*) FROM dbo.ambitos_perfil_unidad unit_scope JOIN dbo.ambitos_perfil scope ON scope.id = unit_scope.ambito_perfil_id
              JOIN dbo.cuentas account ON account.id = scope.cuenta_id WHERE account.sujeto_externo = @subject
            """, new { subject }));
    }

    [Fact]
    public async Task DarAcceso_SoloLoHaceLaAdministracionDelResidente_YUnVinculoAjenoEsAccesoDenegado()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await CreateResidentAsync(admin, "Residente Acceso Ajeno");
        var linkId = await AddFamilyAsync(admin, residentId);
        var otherCenterAdmin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var subject = $"portal-{Guid.NewGuid():N}"[..20];

        var byNurse = await Build(nurse.ExternalSubject).GrantFamilyPortalAccessAsync(
            new GrantFamilyPortalAccessCommand(nurse.ProfileScopeId, nurse.CenterId, residentId, linkId, subject));
        var byOtherCenter = await Build(otherCenterAdmin.ExternalSubject).GrantFamilyPortalAccessAsync(
            new GrantFamilyPortalAccessCommand(otherCenterAdmin.ProfileScopeId, otherCenterAdmin.CenterId, residentId, linkId, subject));
        var wrongLink = await Build(admin.ExternalSubject).GrantFamilyPortalAccessAsync(
            new GrantFamilyPortalAccessCommand(admin.ProfileScopeId, admin.CenterId, residentId, Guid.NewGuid(), subject));

        Assert.Equal(ApplicationFailureCode.AccessDenied, byNurse.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byOtherCenter.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, wrongLink.Error!.Code);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.cuentas WHERE sujeto_externo = @subject", new { subject }));
    }

    [Fact]
    public async Task BaseDeDatos_LaCuentaDelFamiliarNoSeCambiaNiSeQuita_YNoHayDosCuentasIgualesEnUnCentro()
    {
        var setup = await SetupAsync();
        var secondLink = await AddFamilyAsync(setup.Admin, setup.ResidentId, "Segundo Familiar");
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var familyId = await connection.ExecuteScalarAsync<Guid>("SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @linkId", new { linkId = setup.LinkId });
        var secondFamilyId = await connection.ExecuteScalarAsync<Guid>("SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @secondLink", new { secondLink });
        var accountId = await connection.ExecuteScalarAsync<Guid>("SELECT cuenta_id FROM dbo.familiares WHERE id = @familyId", new { familyId });

        var remove = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("UPDATE dbo.familiares SET cuenta_id = NULL WHERE id = @familyId", new { familyId }));
        var change = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("UPDATE dbo.familiares SET cuenta_id = @otherAccount WHERE id = @familyId", new { familyId, otherAccount = setup.Admin.AccountId.Value }));
        var duplicate = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.familiares SET cuenta_id = @accountId WHERE id = @secondFamilyId", new { accountId, secondFamilyId }));

        Assert.Contains("FAMILY_PORTAL_ACCOUNT_IMMUTABLE", remove.Message);
        Assert.Contains("FAMILY_PORTAL_ACCOUNT_IMMUTABLE", change.Message);
        Assert.Equal(2601, duplicate.Number);
    }
}

file sealed class FixedPortalSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
