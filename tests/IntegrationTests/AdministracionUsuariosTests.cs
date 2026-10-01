using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Accounts;
using ResidApp.Infrastructure.Authorization;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.AdministracionResidentesTests;

namespace ResidApp.IntegrationTests;

/// <summary>Administración, bloque 3 (historia 4 sin turnos ni permisos, ADM-12/ADM-13, script 0023): cuentas
/// profesionales, sus perfiles, unidades y residentes de Auxiliar. El efecto se comprueba con la evidencia de autorización
/// real (SqlAuthorizationEvidenceProvider), la misma que usa cada petición. Cada prueba crea su propio centro.</summary>
public class AdministracionUsuariosTests
{
    private static Task<AuthorizationEvidence?> EvidenceAsync(string subject, Guid profileScopeId, CenterId centerId, AuthorizationTarget target) =>
        new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory).LoadEvidenceAsync(
            subject, new AuthorizationSelection(profileScopeId, centerId), target);

    private static Task<IReadOnlyList<ActiveProfileScope>> ActiveScopesAsync(string subject) =>
        new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory).ListActiveAsync(subject);

    private static CreateProfessionalAccountCommand Create(
        SeededProfile admin, string subject, Guid? operationId = null, SystemProfile profile = SystemProfile.Enfermeria,
        IReadOnlyList<Guid>? units = null) =>
        new(admin.ProfileScopeId, admin.CenterId, operationId ?? Guid.NewGuid(), subject, "Ana Ruiz", profile,
            units ?? [admin.UnitId.Value]);

    private static ChangeAccountStatusCommand Status(SeededProfile admin, AccountId accountId, AccountStatus status) =>
        new(admin.ProfileScopeId, admin.CenterId, accountId, status);

    private static ChangeAccountProfileUnitCommand Unit(SeededProfile admin, SeededProfile target, UnitId unitId, bool grant) =>
        new(admin.ProfileScopeId, admin.CenterId, target.AccountId, target.ProfileScopeId, unitId, grant);

    private static ChangeAccountProfileResidentCommand Resident(SeededProfile admin, SeededProfile target, ResidentId residentId, bool assign) =>
        new(admin.ProfileScopeId, admin.CenterId, target.AccountId, target.ProfileScopeId, residentId, assign);

    private static async Task<ProfessionalAccountDetail> DetailAsync(SeededProfile admin, AccountId accountId) =>
        (await Build(admin.ExternalSubject).FindAccountAsync(new FindProfessionalAccountQuery(admin.ProfileScopeId, admin.CenterId, accountId))).Value!;

    private static string NewSubject() => $"test-alta-{Guid.NewGuid():N}"[..30];

    private static async Task<int> CountAuditAsync(Guid resourceId, string action)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE recurso_id = @Id AND accion_codigo = @Action AND perfil_activo = 'ADMINISTRACION'",
            new { Id = resourceId, Action = action });
    }

    /// <summary>Concede una unidad más al ámbito de un perfil por SQL, como hoy el seed.</summary>
    private static async Task GrantUnitBySqlAsync(SeededProfile profile, UnitId unitId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @ProfileScopeId, @CenterId, @UnitId, SYSUTCDATETIME(), @AccountId)
            """, new { profile.ProfileScopeId, CenterId = profile.CenterId.Value, UnitId = unitId.Value, AccountId = profile.AccountId.Value });
    }

    [Fact]
    public async Task Alta_CreaLaCuentaConSuPerfil_QueAutorizaEnSuUnidad_YUnReenvioNoLaDuplica()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);
        var subject = NewSubject();
        var operationId = Guid.NewGuid();
        var foreignUnit = await AddUnitAsync(admin.CenterId);

        var first = await service.CreateAccountAsync(Create(admin, subject, operationId));
        var resent = await service.CreateAccountAsync(Create(admin, subject, operationId));
        var taken = await service.CreateAccountAsync(Create(admin, subject.ToUpperInvariant()));
        var outsideUnit = await service.CreateAccountAsync(Create(admin, NewSubject(), units: [foreignUnit.Value]));
        var family = await service.CreateAccountAsync(Create(admin, NewSubject(), profile: SystemProfile.Familiar));
        var noUnits = await service.CreateAccountAsync(Create(admin, NewSubject(), units: []));
        var list = (await service.ListAccountsAsync(new AdministracionQuery(admin.ProfileScopeId, admin.CenterId))).Value!;

        Assert.True(first.Ok, first.Error?.Message);
        Assert.Equal(operationId, first.Value.Value);
        Assert.Equal(first.Value, resent.Value);
        Assert.Equal(ApplicationFailureCode.Conflict, taken.Error!.Code);
        Assert.All(new[] { outsideUnit, family, noUnits }, r => Assert.Equal(ApplicationFailureCode.InvalidInput, r.Error!.Code));
        var created = Assert.Single(list, a => a.AccountId == first.Value);
        Assert.Equal((subject, "Ana Ruiz", AccountStatus.Active), (created.Subject, created.DisplayName, created.Status));
        var profile = Assert.Single(created.Profiles);
        Assert.Equal(SystemProfile.Enfermeria, profile.Profile);
        Assert.Equal(admin.UnitId.Value, Assert.Single(profile.Units).TargetId);
        var scope = Assert.Single(await ActiveScopesAsync(subject));
        Assert.Equal((profile.ProfileScopeId, SystemProfile.Enfermeria), (scope.ProfileScopeId, scope.Profile));
        Assert.NotNull(await EvidenceAsync(subject, profile.ProfileScopeId, admin.CenterId, new AuthorizationTarget.Create(admin.UnitId)));
        Assert.Equal(1, await CountAuditAsync(operationId, "ACCOUNT_CREATE"));
        Assert.Equal(1, await CountAuditAsync(profile.ProfileScopeId, "PROFILE_SCOPE_GRANT"));
        Assert.Equal(1, await CountAuditAsync(profile.ProfileScopeId, "PROFILE_UNIT_GRANT"));
    }

    [Fact]
    public async Task Suspender_DejaSinAccesoEnLaSiguientePeticion_YReactivarLoDevuelve()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var residentId = await CreateResidentAsync(admin, "Residente Suspensión");
        var service = Build(admin.ExternalSubject);
        var read = new AuthorizationTarget.Read(residentId);
        Assert.NotNull(await EvidenceAsync(nurse.ExternalSubject, nurse.ProfileScopeId, nurse.CenterId, read));

        var suspended = await service.ChangeAccountStatusAsync(Status(admin, nurse.AccountId, AccountStatus.Suspended));
        var whileSuspended = await EvidenceAsync(nurse.ExternalSubject, nurse.ProfileScopeId, nurse.CenterId, read);
        var scopesWhileSuspended = await ActiveScopesAsync(nurse.ExternalSubject);
        var again = await service.ChangeAccountStatusAsync(Status(admin, nurse.AccountId, AccountStatus.Suspended));
        var detail = await DetailAsync(admin, nurse.AccountId);
        var reactivated = await service.ChangeAccountStatusAsync(Status(admin, nurse.AccountId, AccountStatus.Active));

        Assert.True(suspended.Ok, suspended.Error?.Message);
        Assert.Null(whileSuspended);
        Assert.Empty(scopesWhileSuspended);
        Assert.Equal(ApplicationFailureCode.Conflict, again.Error!.Code);
        Assert.Equal(AccountStatus.Suspended, detail.Account.Status);
        Assert.True(reactivated.Ok, reactivated.Error?.Message);
        Assert.NotNull(await EvidenceAsync(nurse.ExternalSubject, nurse.ProfileScopeId, nurse.CenterId, read));
        Assert.Equal(1, await CountAuditAsync(nurse.AccountId.Value, "ACCOUNT_SUSPEND"));
        Assert.Equal(1, await CountAuditAsync(nurse.AccountId.Value, "ACCOUNT_ACTIVATE"));
    }

    [Fact]
    public async Task ConPerfilesEnOtroCentro_NoSeSuspende_PeroSeRevocanLosDeEsteCentro()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var elsewhere = await SeedFixture.CreateProfileAsync(SystemProfile.Medicina);
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @AccountId, @CenterId, 'MEDICINA', 'ACTIVE', SYSUTCDATETIME(), @GrantedBy)
                """, new { AccountId = nurse.AccountId.Value, CenterId = elsewhere.CenterId.Value, GrantedBy = elsewhere.AccountId.Value });
        }
        var service = Build(admin.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, "Residente Otro Centro");

        var suspend = await service.ChangeAccountStatusAsync(Status(admin, nurse.AccountId, AccountStatus.Suspended));
        var before = await DetailAsync(admin, nurse.AccountId);
        var revoke = await service.RevokeProfileAsync(new RevokeAccountProfileCommand(
            admin.ProfileScopeId, admin.CenterId, nurse.AccountId, nurse.ProfileScopeId));
        var revokeAgain = await service.RevokeProfileAsync(new RevokeAccountProfileCommand(
            admin.ProfileScopeId, admin.CenterId, nurse.AccountId, nurse.ProfileScopeId));
        var after = await DetailAsync(admin, nurse.AccountId);

        Assert.Equal(ApplicationFailureCode.InvalidInput, suspend.Error!.Code);
        Assert.True(before.HasActiveProfilesElsewhere);
        Assert.True(revoke.Ok, revoke.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, revokeAgain.Error!.Code);
        Assert.Equal(AccountStatus.Active, after.Account.Status);
        Assert.False(Assert.Single(after.Account.Profiles).Active);
        Assert.Null(await EvidenceAsync(nurse.ExternalSubject, nurse.ProfileScopeId, nurse.CenterId, new AuthorizationTarget.Read(residentId)));
        Assert.Equal(elsewhere.CenterId, Assert.Single(await ActiveScopesAsync(nurse.ExternalSubject)).CenterId);
        Assert.Equal(1, await CountAuditAsync(nurse.ProfileScopeId, "PROFILE_SCOPE_REVOKE"));
    }

    [Fact]
    public async Task PropiaCuenta_NoSePuedeCambiar()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);

        var results = new List<ApplicationFailure?>
        {
            (await service.RenameAccountAsync(new RenameProfessionalAccountCommand(admin.ProfileScopeId, admin.CenterId, admin.AccountId, "Yo"))).Error,
            (await service.ChangeAccountStatusAsync(Status(admin, admin.AccountId, AccountStatus.Suspended))).Error,
            (await service.GrantProfileAsync(new GrantAccountProfileCommand(
                admin.ProfileScopeId, admin.CenterId, admin.AccountId, Guid.NewGuid(), SystemProfile.Medicina, [admin.UnitId.Value]))).Error,
            (await service.RevokeProfileAsync(new RevokeAccountProfileCommand(admin.ProfileScopeId, admin.CenterId, admin.AccountId, admin.ProfileScopeId))).Error,
            (await service.ChangeProfileUnitAsync(Unit(admin, admin, admin.UnitId, grant: false))).Error,
        };
        var detail = await DetailAsync(admin, admin.AccountId);

        Assert.All(results, e => Assert.Equal(ApplicationFailureCode.InvalidInput, e!.Code));
        Assert.True(detail.IsOwnAccount);
        Assert.Equal(AccountStatus.Active, detail.Account.Status);
        Assert.Single(detail.Account.Profiles);
    }

    [Fact]
    public async Task Perfiles_NoSeDuplican_LasUnidadesSonDelAmbito_YLaUltimaNoSeRevoca()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var secondUnit = await AddUnitAsync(admin.CenterId);
        await GrantUnitBySqlAsync(admin, secondUnit);
        var foreignUnit = await AddUnitAsync(admin.CenterId);
        var service = Build(admin.ExternalSubject);
        GrantAccountProfileCommand Grant(SystemProfile profile, Guid? operationId = null) => new(
            admin.ProfileScopeId, admin.CenterId, nurse.AccountId, operationId ?? Guid.NewGuid(), profile, [admin.UnitId.Value]);

        var duplicate = await service.GrantProfileAsync(Grant(SystemProfile.Enfermeria));
        var medicineOperation = Guid.NewGuid();
        var medicine = await service.GrantProfileAsync(Grant(SystemProfile.Medicina, medicineOperation));
        var medicineResent = await service.GrantProfileAsync(Grant(SystemProfile.Medicina, medicineOperation));
        var addSecond = await service.ChangeProfileUnitAsync(Unit(admin, nurse, secondUnit, grant: true));
        var addSecondAgain = await service.ChangeProfileUnitAsync(Unit(admin, nurse, secondUnit, grant: true));
        var addForeign = await service.ChangeProfileUnitAsync(Unit(admin, nurse, foreignUnit, grant: true));
        var revokeFirst = await service.ChangeProfileUnitAsync(Unit(admin, nurse, admin.UnitId, grant: false));
        var revokeLast = await service.ChangeProfileUnitAsync(Unit(admin, nurse, secondUnit, grant: false));
        var detail = await DetailAsync(admin, nurse.AccountId);

        Assert.Equal(ApplicationFailureCode.Conflict, duplicate.Error!.Code);
        Assert.True(medicine.Ok, medicine.Error?.Message);
        Assert.Equal(medicineOperation, medicineResent.Value);
        Assert.True(addSecond.Ok, addSecond.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, addSecondAgain.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, addForeign.Error!.Code);
        Assert.True(revokeFirst.Ok, revokeFirst.Error?.Message);
        Assert.Equal(ApplicationFailureCode.InvalidInput, revokeLast.Error!.Code);
        Assert.Equal(2, detail.Account.Profiles.Count(p => p.Active));
        var nursing = Assert.Single(detail.Account.Profiles, p => p.Profile == SystemProfile.Enfermeria);
        Assert.Equal(secondUnit.Value, Assert.Single(nursing.Units, u => u.Active).TargetId);
        Assert.Null(await EvidenceAsync(nurse.ExternalSubject, nurse.ProfileScopeId, nurse.CenterId, new AuthorizationTarget.Create(admin.UnitId)));
        Assert.NotNull(await EvidenceAsync(nurse.ExternalSubject, nurse.ProfileScopeId, nurse.CenterId, new AuthorizationTarget.Create(secondUnit)));
        Assert.Equal(1, await CountAuditAsync(nurse.ProfileScopeId, "PROFILE_UNIT_REVOKE"));
    }

    [Fact]
    public async Task Auxiliar_SoloVeLosResidentesAsignados_YRetirarElUltimoNoAmpliaSuAcceso()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var assistant = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, admin.CenterId, admin.UnitId);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var first = await CreateResidentAsync(admin, "Residente Asignado");
        var second = await CreateResidentAsync(admin, "Residente Sin Asignar");
        var service = Build(admin.ExternalSubject);
        var query = new FindProfessionalAccountQuery(admin.ProfileScopeId, admin.CenterId, assistant.AccountId);
        Task<AuthorizationEvidence?> AssistantReads(ResidentId residentId) =>
            EvidenceAsync(assistant.ExternalSubject, assistant.ProfileScopeId, assistant.CenterId, new AuthorizationTarget.Read(residentId));

        var assignableBefore = (await service.ListAssignableResidentsAsync(query, assistant.ProfileScopeId)).Value!;
        var assign = await service.ChangeProfileResidentAsync(Resident(admin, assistant, first, assign: true));
        var assignAgain = await service.ChangeProfileResidentAsync(Resident(admin, assistant, first, assign: true));
        var assignToNurse = await service.ChangeProfileResidentAsync(Resident(admin, nurse, first, assign: true));
        var assignableAfter = (await service.ListAssignableResidentsAsync(query, assistant.ProfileScopeId)).Value!;
        var seesFirst = await AssistantReads(first);
        var seesSecond = await AssistantReads(second);
        var retire = await service.ChangeProfileResidentAsync(Resident(admin, assistant, first, assign: false));
        var retireAgain = await service.ChangeProfileResidentAsync(Resident(admin, assistant, first, assign: false));

        Assert.Equal(new[] { first, second }.OrderBy(r => r.Value), assignableBefore.Select(r => r.ResidentId).OrderBy(r => r.Value));
        Assert.True(assign.Ok, assign.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, assignAgain.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, assignToNurse.Error!.Code);
        Assert.Equal(second, Assert.Single(assignableAfter).ResidentId);
        Assert.NotNull(seesFirst);
        Assert.Null(seesSecond);
        Assert.True(retire.Ok, retire.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, retireAgain.Error!.Code);
        Assert.Null(await AssistantReads(first));
        Assert.Null(await AssistantReads(second));
        Assert.Equal(1, await CountAuditAsync(assistant.ProfileScopeId, "PROFILE_RESIDENT_GRANT"));
        Assert.Equal(1, await CountAuditAsync(assistant.ProfileScopeId, "PROFILE_RESIDENT_REVOKE"));
    }

    [Fact]
    public async Task SoloAdministracionDelCentro_GestionaSusCuentas()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var otherAdmin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var failures = new List<ApplicationFailure?>();
        foreach (var profile in new[] { SystemProfile.Enfermeria, SystemProfile.Medicina, SystemProfile.Auxiliar, SystemProfile.DireccionClinica })
        {
            var other = await SeedFixture.AddProfileToCenterAsync(profile, admin.CenterId, admin.UnitId);
            var service = Build(other.ExternalSubject);
            failures.Add((await service.ListAccountsAsync(new AdministracionQuery(other.ProfileScopeId, other.CenterId))).Error);
            failures.Add((await service.CreateAccountAsync(Create(other, NewSubject()))).Error);
            failures.Add((await service.ChangeAccountStatusAsync(Status(other, nurse.AccountId, AccountStatus.Suspended))).Error);
        }
        var foreignService = Build(otherAdmin.ExternalSubject);
        failures.Add((await foreignService.FindAccountAsync(new FindProfessionalAccountQuery(
            otherAdmin.ProfileScopeId, otherAdmin.CenterId, nurse.AccountId))).Error);
        failures.Add((await foreignService.ChangeAccountStatusAsync(Status(otherAdmin, nurse.AccountId, AccountStatus.Suspended))).Error);
        var foreignList = (await foreignService.ListAccountsAsync(new AdministracionQuery(otherAdmin.ProfileScopeId, otherAdmin.CenterId))).Value!;

        Assert.All(failures, e => Assert.Equal(ApplicationFailureCode.AccessDenied, e!.Code));
        Assert.DoesNotContain(foreignList, a => a.AccountId == nurse.AccountId);
        Assert.Equal(AccountStatus.Active, (await DetailAsync(admin, nurse.AccountId)).Account.Status);
        Assert.Equal(0, await CountAuditAsync(nurse.AccountId.Value, "ACCOUNT_SUSPEND"));
    }

    private static ChangeAccountProfilePermissionCommand Permission(SeededProfile admin, SeededProfile target, string code, bool grant) =>
        new(admin.ProfileScopeId, admin.CenterId, target.AccountId, target.ProfileScopeId, code, grant);

    [Fact]
    public async Task Permisos_SeConcedenYRevocan_SoloLosDelCatalogoDelPerfil()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var doctor = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Medicina, admin.CenterId, admin.UnitId);
        var assistant = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, admin.CenterId, admin.UnitId);
        var service = Build(admin.ExternalSubject);
        var create = new AuthorizationTarget.Create(admin.UnitId);
        async Task<IReadOnlyList<string>> NursePermissionsAsync() =>
            (await EvidenceAsync(nurse.ExternalSubject, nurse.ProfileScopeId, nurse.CenterId, create))!.Permissions.Select(p => p.Code).ToList();

        var grant = await service.ChangeProfilePermissionAsync(Permission(admin, nurse, "BASELINE_INITIAL_COMPLETE", grant: true));
        var afterGrant = await NursePermissionsAsync();
        var grantAgain = await service.ChangeProfilePermissionAsync(Permission(admin, nurse, "BASELINE_INITIAL_COMPLETE", grant: true));
        var outOfCatalog = new[]
        {
            await service.ChangeProfilePermissionAsync(Permission(admin, doctor, "RESIDENT_IDENTITY_CREATE", grant: true)),
            await service.ChangeProfilePermissionAsync(Permission(admin, nurse, "CLINICAL_DETAIL_READ", grant: true)),
            await service.ChangeProfilePermissionAsync(Permission(admin, nurse, "BASELINE_DRAFT_CONTRIBUTE", grant: true)),
            await service.ChangeProfilePermissionAsync(Permission(admin, assistant, "BASELINE_REEVALUATE", grant: true)),
            await service.ChangeProfilePermissionAsync(Permission(admin, admin, "RESIDENT_IDENTITY_CREATE", grant: true)),
        };
        var detail = await DetailAsync(admin, nurse.AccountId);
        var revoke = await service.ChangeProfilePermissionAsync(Permission(admin, nurse, "BASELINE_INITIAL_COMPLETE", grant: false));
        var revokeAgain = await service.ChangeProfilePermissionAsync(Permission(admin, nurse, "BASELINE_INITIAL_COMPLETE", grant: false));
        var afterRevoke = await NursePermissionsAsync();

        Assert.True(grant.Ok, grant.Error?.Message);
        Assert.Equal(["BASELINE_INITIAL_COMPLETE"], afterGrant);
        Assert.Equal(ApplicationFailureCode.Conflict, grantAgain.Error!.Code);
        Assert.All(outOfCatalog, r => Assert.Equal(ApplicationFailureCode.InvalidInput, r.Error!.Code));
        Assert.Equal("BASELINE_INITIAL_COMPLETE", Assert.Single(Assert.Single(detail.Account.Profiles).Permissions).Name);
        Assert.True(revoke.Ok, revoke.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, revokeAgain.Error!.Code);
        Assert.Empty(afterRevoke);
        Assert.Equal(1, await CountAuditAsync(nurse.ProfileScopeId, "PROFILE_PERMISSION_GRANT"));
        Assert.Equal(1, await CountAuditAsync(nurse.ProfileScopeId, "PROFILE_PERMISSION_REVOKE"));
    }

    [Fact]
    public async Task Permisos_LosDelAmbitoActivo_YLaBaseDeDatosNoAdmiteOtroPerfil()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(
            SystemProfile.Enfermeria, admin.CenterId, admin.UnitId, ["RESIDENT_IDENTITY_CREATE", "BASELINE_REEVALUATE"]);
        Assert.True((await Build(admin.ExternalSubject).ChangeProfilePermissionAsync(
            Permission(admin, nurse, "BASELINE_REEVALUATE", grant: false))).Ok);
        var scopes = new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory);

        var own = await scopes.ListPermissionsAsync(nurse.ExternalSubject, nurse.ProfileScopeId, nurse.CenterId);
        var foreign = await scopes.ListPermissionsAsync(admin.ExternalSubject, nurse.ProfileScopeId, nurse.CenterId);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var direct = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("""
            INSERT INTO dbo.permisos_perfil (id, ambito_perfil_id, centro_id, permiso_codigo, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @ProfileScopeId, @CenterId, 'CLINICAL_DETAIL_READ', SYSUTCDATETIME(), @AccountId)
            """, new { nurse.ProfileScopeId, CenterId = nurse.CenterId.Value, AccountId = admin.AccountId.Value }));

        Assert.Equal(["RESIDENT_IDENTITY_CREATE"], own);
        Assert.Empty(foreign);
        Assert.Contains("PROFILE_PERMISSION_NOT_ALLOWED", direct.Message);
    }

    [Fact]
    public async Task Nombre_SePoneYSeCambiaConAuditoria()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var service = Build(admin.ExternalSubject);
        RenameProfessionalAccountCommand Rename(string? name) => new(admin.ProfileScopeId, admin.CenterId, nurse.AccountId, name);

        var withoutName = await DetailAsync(admin, nurse.AccountId);
        var renamed = await service.RenameAccountAsync(Rename(" Ana Ruiz "));
        var same = await service.RenameAccountAsync(Rename("Ana Ruiz"));
        var blank = await service.RenameAccountAsync(Rename("  "));

        Assert.Null(withoutName.Account.DisplayName);
        Assert.True(renamed.Ok, renamed.Error?.Message);
        Assert.Equal(ApplicationFailureCode.InvalidInput, same.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, blank.Error!.Code);
        Assert.Equal("Ana Ruiz", (await DetailAsync(admin, nurse.AccountId)).Account.DisplayName);
        Assert.Equal(1, await CountAuditAsync(nurse.AccountId.Value, "ACCOUNT_RENAME"));
    }

    [Fact]
    public async Task BaseDeDatos_NoDejaCambiarElIdentificadorNiBorrarCuentas()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var parameters = new { Id = admin.AccountId.Value };

        var subject = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.cuentas SET sujeto_externo = UPPER(sujeto_externo) WHERE id = @Id", parameters));
        var delete = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.cuentas WHERE id = @Id", parameters));
        var blankName = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.cuentas SET nombre_visible = N' ' WHERE id = @Id", parameters));

        Assert.Contains("ACCOUNT_UPDATE_INVALID", subject.Message);
        Assert.Contains("ACCOUNT_DELETE_FORBIDDEN", delete.Message);
        Assert.Contains("CK_accounts_display_name", blankName.Message);
    }
}
