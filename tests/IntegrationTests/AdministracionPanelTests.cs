using Dapper;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Families;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.AdministracionResidentesTests;
using static ResidApp.IntegrationTests.PortalFamiliarTests;

namespace ResidApp.IntegrationTests;

/// <summary>El cuadro de mando de Administración contra la base de datos: cada cifra, solo de las unidades del ámbito (las citas, del centro), cuadra con la
/// lista o el inicio a los que corresponde. Cada prueba crea su propio centro.</summary>
public class AdministracionPanelTests
{
    private static readonly DateOnly Today = CenterTime.Today;

    private static AccountAdministrationAccess AccessOf(SeededProfile profile) => new(profile.ProfileScopeId, profile.AccountId, profile.CenterId);

    private static SqlAdministrationDashboard Reader() => new(TestDatabase.ConnectionFactory);

    private static AdministrationDashboardApplicationService Service(string subject) => new(
        new AdministrationAccessResolver(new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedPanelSessionIdentityProvider(subject)),
        Reader());

    private static DashboardPeriod Period(DashboardRange range = DashboardRange.TreintaDias) =>
        DashboardPeriod.At(range, DateTimeOffset.UtcNow, CenterTime.Zone);

    private static async Task<ResidentId> CreateAsync(SeededProfile admin, string name, UnitId? unit = null, Guid? room = null, Guid? place = null,
        DateOnly? admissionDate = null) =>
        (await new SqlResidentRepository(TestDatabase.ConnectionFactory).CreateWithInitialLocationAsync(new CreateResidentInput(
            admin.AccountId, SystemProfile.Administracion, admin.CenterId, unit ?? admin.UnitId, name, new DateOnly(1940, 5, 20), DocumentedSexCode.NoConsta,
            null, null, null, room, place, Guid.NewGuid(), LocationPending: true, AdmissionDate: admissionDate))).ResidentId;

    private static async Task ExecuteAsync(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync(sql, parameters);
    }

    [Fact]
    public async Task Ocupacion_SoloLasUnidadesDelAmbito_AunqueCompartanEdificio_ConLasPlazasActivas()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var otherUnit = await AddUnitAsync(admin.CenterId);
        var otherUnitAdmin = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, admin.CenterId, otherUnit);
        var otherCenter = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var building = Guid.NewGuid();
        await ExecuteAsync("""
            INSERT INTO dbo.edificios (id, centro_id, nombre_visible, creado_en, creado_por_cuenta_id)
            VALUES (@building, @center, N'Edificio compartido', SYSUTCDATETIME(), @account);
            UPDATE dbo.unidades SET edificio_id = @building WHERE centro_id = @center AND id IN (@mine, @other);
            """, new { building, center = admin.CenterId.Value, account = admin.AccountId.Value, mine = admin.UnitId.Value, other = otherUnit.Value });
        var structure = BuildEstructura(admin.ExternalSubject);
        var room = (await structure.CreateRoomAsync(new CreateRoomCommand(admin.ProfileScopeId, admin.CenterId, Guid.NewGuid(), admin.UnitId, "Habitación 1"))).Value;
        var beds = new List<Guid>();
        foreach (var name in new[] { "Cama A", "Cama B", "Cama C" })
        {
            beds.Add((await structure.CreatePlaceAsync(new CreatePlaceCommand(admin.ProfileScopeId, admin.CenterId, Guid.NewGuid(), room, name))).Value);
        }

        // La tercera cama está inactiva: no es una plaza.
        await ExecuteAsync("UPDATE dbo.plazas SET estado = 'INACTIVE' WHERE id = @id", new { id = beds[2] });
        await CreateAsync(admin, "En la cama A", room: room, place: beds[0]);
        await CreateAsync(admin, "Sin cama");
        await CreateAsync(admin, "De la otra unidad", otherUnit);

        var mine = await Reader().ReadOccupancyAsync(AccessOf(admin), DateTime.UtcNow.AddDays(-1));
        var now = await Reader().ReadOccupancyAsync(AccessOf(admin), DateTime.UtcNow.AddMinutes(1));
        var theirs = await Reader().ReadOccupancyAsync(AccessOf(otherUnitAdmin), DateTime.UtcNow.AddMinutes(1));
        var empty = await Reader().ReadOccupancyAsync(AccessOf(otherCenter), DateTime.UtcNow.AddMinutes(1));

        var unit = Assert.Single(mine.Units);
        Assert.Equal(("Edificio compartido", 2, 2, 1, 0, 0),
            (unit.BuildingName, unit.Residents, unit.Places, unit.Occupied, unit.ResidentsBefore, unit.OccupiedBefore));
        Assert.Equal((2, 1), (now.Units[0].ResidentsBefore, now.Units[0].OccupiedBefore));
        var other = Assert.Single(theirs.Units);
        Assert.Equal((1, 0, 0, 1), (other.Residents, other.Places, other.Occupied, other.ResidentsBefore));
        Assert.Equal((0, 0), (empty.Residents, empty.Places));
        // La cifra del panel es la de la lista de residentes del ámbito.
        var list = await Build(admin.ExternalSubject).ListResidentsAsync(new AdministracionQuery(admin.ProfileScopeId, admin.CenterId));
        Assert.Equal(list.Value!.Count, mine.Residents);
    }

    [Fact]
    public async Task LoPasado_CuentaAQuienEstabaEntonces_TambienEnUnaUnidadHoyInactiva_YUnTrasladoNoEsAltaNiBaja()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var closing = await AddUnitAsync(admin.CenterId);
        await GrantUnitAsync(admin, closing);
        var moves = await CreateAsync(admin, "Cambia de unidad", closing);
        var leaves = await CreateAsync(admin, "Se va después");
        await Task.Delay(20);
        var before = DateTime.UtcNow;
        await Task.Delay(20);
        var discharged = await EstadoResidenteTests.Build(admin.ExternalSubject).DischargeAsync(new DischargeResidentCommand(
            admin.ProfileScopeId, admin.CenterId, leaves, Guid.NewGuid(), ResidentDischargeReason.AltaVoluntaria, null));
        Assert.True(discharged.Ok, discharged.Error?.Message);
        // El traslado: se cierra la ubicación de la unidad que se va a inactivar y se abre otra en la misma estancia.
        await ExecuteAsync("""
            DECLARE @now DATETIME2(3) = SYSUTCDATETIME(), @episode UNIQUEIDENTIFIER;
            SELECT @episode = episodio_id FROM dbo.intervalos_ubicacion_residente WHERE residente_id = @resident AND vigente_hasta IS NULL;
            UPDATE dbo.intervalos_ubicacion_residente SET vigente_hasta = @now WHERE residente_id = @resident AND vigente_hasta IS NULL;
            INSERT INTO dbo.intervalos_ubicacion_residente (id, residente_id, centro_id, episodio_id, unidad_id, vigente_desde, modificado_en,
                                                            modificado_por_cuenta_id, modificado_por_perfil)
            VALUES (NEWID(), @resident, @center, @episode, @target, @now, @now, @account, 'ADMINISTRACION');
            UPDATE dbo.unidades SET estado = 'INACTIVE' WHERE id = @closing;
            """, new
        {
            resident = moves.Value, center = admin.CenterId.Value, target = admin.UnitId.Value, account = admin.AccountId.Value, closing = closing.Value,
        });

        var occupancy = await Reader().ReadOccupancyAsync(AccessOf(admin), before);
        var movements = await Service(admin.ExternalSubject).ReadMovementsAsync(admin.ProfileScopeId, admin.CenterId, Period());

        // Antes había dos (uno en la unidad hoy inactiva y quien se fue después); ahora, uno.
        Assert.Equal((2, 1), (occupancy.ResidentsBefore, occupancy.Residents));
        Assert.Equal(admin.UnitId.Value, Assert.Single(occupancy.ActiveUnits).UnitId);
        Assert.Equal((2, 1), (movements.Value!.Admissions, movements.Value.Discharges));
    }

    [Fact]
    public async Task LaFechaDeIngresoSoloValeParaLaPrimeraEstancia()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var statuses = EstadoResidenteTests.Build(admin.ExternalSubject);
        var resident = await CreateAsync(admin, "Importado que se va y vuelve", admissionDate: Today.AddYears(-3));
        await Task.Delay(20);
        Assert.True((await statuses.DischargeAsync(new DischargeResidentCommand(
            admin.ProfileScopeId, admin.CenterId, resident, Guid.NewGuid(), ResidentDischargeReason.AltaVoluntaria, null))).Ok);
        await Task.Delay(20);
        Assert.True((await statuses.ReactivateAsync(new ReactivateResidentCommand(
            admin.ProfileScopeId, admin.CenterId, resident, Guid.NewGuid(), admin.UnitId, null, null))).Ok);

        var movements = await Service(admin.ExternalSubject).ReadMovementsAsync(admin.ProfileScopeId, admin.CenterId, Period());

        // La primera estancia cuenta hace tres años; la segunda, hoy.
        Assert.Equal((1, 1), (movements.Value!.Admissions, movements.Value.AdmissionsToday));
    }

    private static Task GrantUnitAsync(SeededProfile profile, UnitId unit) => ExecuteAsync("""
        INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
        VALUES (NEWID(), @scope, @center, @unit, SYSUTCDATETIME(), @account)
        """, new { scope = profile.ProfileScopeId, center = profile.CenterId.Value, unit = unit.Value, account = profile.AccountId.Value });

    [Fact]
    public async Task AltasYBajas_DeLasUnidadesDelAmbito_LaReactivacionEsOtraAlta_YLaFechaDeIngresoImportadaMandaSobreElRegistro()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var otherUnit = await AddUnitAsync(admin.CenterId);
        var otherUnitAdmin = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, admin.CenterId, otherUnit);
        var statuses = EstadoResidenteTests.Build(admin.ExternalSubject);
        var leaves = await CreateAsync(admin, "Se va");
        var returns = await CreateAsync(admin, "Se va y vuelve");
        await CreateAsync(admin, "Ingresó hace años", admissionDate: Today.AddYears(-3));
        await CreateAsync(admin, "Ingresó hace tres días", admissionDate: Today.AddDays(-3));
        await CreateAsync(admin, "De la otra unidad", otherUnit);
        // La estancia de un alta y su baja no pueden compartir el mismo milisegundo.
        await Task.Delay(20);
        foreach (var (resident, reason, text) in new[]
                 {
                     (leaves, ResidentDischargeReason.Fallecimiento, (string?)null), (returns, ResidentDischargeReason.Otro, "Texto que el panel no lee"),
                 })
        {
            var discharged = await statuses.DischargeAsync(new DischargeResidentCommand(
                admin.ProfileScopeId, admin.CenterId, resident, Guid.NewGuid(), reason, text));
            Assert.True(discharged.Ok, discharged.Error?.Message);
        }

        await Task.Delay(20);
        var reactivated = await statuses.ReactivateAsync(new ReactivateResidentCommand(
            admin.ProfileScopeId, admin.CenterId, returns, Guid.NewGuid(), admin.UnitId, null, null));
        Assert.True(reactivated.Ok, reactivated.Error?.Message);

        var mine = await Service(admin.ExternalSubject).ReadMovementsAsync(admin.ProfileScopeId, admin.CenterId, Period());
        var theirs = await Service(otherUnitAdmin.ExternalSubject).ReadMovementsAsync(otherUnitAdmin.ProfileScopeId, admin.CenterId, Period());

        Assert.True(mine.Ok, mine.Error?.Message);
        // Cuatro estancias de mi unidad en el periodo (dos altas, la importada de hace tres días y la reactivación); la de hace tres años no.
        Assert.Equal(4, mine.Value!.Admissions);
        Assert.Equal(3, mine.Value.AdmissionsToday);
        Assert.Equal(new DashboardReasonCounts(1, 0, 0, 1), mine.Value.DischargesToday);
        Assert.Equal(2, mine.Value.Discharges);
        Assert.Equal((1, 0), (theirs.Value!.Admissions, theirs.Value.Discharges));
    }

    [Fact]
    public async Task Autorizaciones_ElUltimoTramoCuentaLoMismoQueElInicio()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var otherUnit = await AddUnitAsync(admin.CenterId);
        var otherUnitAdmin = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, admin.CenterId, otherUnit);
        var resident = await CreateResidentAsync(admin, "Con familia");
        var outsider = await CreateResidentAsync(admin, "De otra unidad", otherUnit);
        await AddFamilyAsync(otherUnitAdmin, outsider, "Familiar ajeno");
        await AddFamilyAsync(admin, resident, "Sin cambios");
        var active = await AddFamilyAsync(admin, resident, "Activa");
        var suspended = await AddFamilyAsync(admin, resident, "Suspendida");
        var gone = await AddFamilyAsync(admin, resident, "Desvinculado");
        var expired = await AddFamilyAsync(admin, resident, "Caducada");
        var revoked = await AddFamilyAsync(admin, resident, "Revocada");
        await ChangeAsync(admin, resident, revoked, FamilyAuthorizationChange.Abrir, 0);
        await ChangeAsync(admin, resident, revoked, FamilyAuthorizationChange.Revocar, 1, "Ya no procede.");
        await ChangeAsync(admin, resident, expired, FamilyAuthorizationChange.Abrir, 0);
        // Una Activa que ya ha vencido solo puede venir del paso del tiempo: se inserta directamente.
        await ExecuteAsync("""
            INSERT INTO dbo.familiares_autorizaciones_cambios
                (id, centro_id, vinculo_id, numero, estado_codigo, valida_hasta, registrado_por_cuenta_id, registrado_por_perfil, registrado_en)
            VALUES (NEWID(), @center, @expired, 2, 'ACTIVA', @yesterday, @account, 'ADMINISTRACION', SYSUTCDATETIME())
            """, new { center = admin.CenterId.Value, expired, yesterday = Today.AddDays(-1).ToDateTime(TimeOnly.MinValue), account = admin.AccountId.Value });
        foreach (var link in new[] { active, suspended })
        {
            await ChangeAsync(admin, resident, link, FamilyAuthorizationChange.Abrir, 0);
            await ChangeAsync(admin, resident, link, FamilyAuthorizationChange.Activar, 1);
        }

        await ChangeAsync(admin, resident, suspended, FamilyAuthorizationChange.Suspender, 2, "Petición del familiar.");
        await Task.Delay(20);
        var unlinked = await Build(admin.ExternalSubject).UnlinkFamilyMemberAsync(
            new UnlinkFamilyMemberCommand(admin.ProfileScopeId, admin.CenterId, resident, gone, "Ya no es familiar del residente."));
        Assert.True(unlinked.Ok, unlinked.Error?.Message);

        var buckets = await Service(admin.ExternalSubject).ReadAuthorizationsAsync(admin.ProfileScopeId, admin.CenterId, Period());
        var overview = await new SqlAdministrationOverview(TestDatabase.ConnectionFactory)
            .ReadAsync(AccessOf(admin), AdministrationOverviewApplicationService.PeriodAt(DateTimeOffset.UtcNow, CenterTime.Zone));

        Assert.True(buckets.Ok, buckets.Error?.Message);
        var today = buckets.Value![^1];
        Assert.Equal((1, 1, 1, 1), (today.Active, today.Pending, today.Expired, today.Suspended));
        Assert.Equal((overview.AuthorizationsPending, overview.AuthorizationsExpired), (today.Pending, today.Expired));
        // Antes de que existieran los vínculos no había nada.
        Assert.Equal(0, buckets.Value[0].Total);
    }

    [Fact]
    public async Task Citas_DeTodoElCentro_DentroDelPeriodo_YNuncaDeOtroCentro()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var otherCenter = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        // El residente es de una unidad fuera del ámbito: las citas se cuentan de todo el centro, como en la agenda.
        var otherUnit = await AddUnitAsync(admin.CenterId);
        var otherUnitAdmin = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, admin.CenterId, otherUnit);
        var resident = await CreateResidentAsync(admin, "Con citas", otherUnit);
        var link = await AddFamilyAsync(otherUnitAdmin, resident, "Familiar con citas");
        var now = DateTime.UtcNow;
        foreach (var (startsAt, status) in new[]
                 {
                     (now.AddDays(-2), "FINALIZADA"), (now.AddDays(-1), "CANCELADA"), (now.AddMinutes(-5), "CONFIRMADA"),
                     (now.AddDays(-60), "FINALIZADA"), (now.AddDays(3), "CONFIRMADA"),
                 })
        {
            await ExecuteAsync("""
                INSERT INTO dbo.citas (id, centro_id, residente_id, vinculo_id, cuenta_id, equipo_codigo, modalidad_codigo, inicio_en, fin_en,
                                       estado_codigo, operacion_id, creada_en)
                VALUES (NEWID(), @center, @resident, @link, @account, 'MEDICO', 'PRESENCIAL', @startsAt, DATEADD(MINUTE, 30, @startsAt), @status, NEWID(), SYSUTCDATETIME())
                """, new { center = admin.CenterId.Value, resident = resident.Value, link, account = admin.AccountId.Value, startsAt, status });
        }

        var mine = await Service(admin.ExternalSubject).ReadAppointmentsAsync(admin.ProfileScopeId, admin.CenterId, Period());
        var theirs = await Service(otherCenter.ExternalSubject).ReadAppointmentsAsync(otherCenter.ProfileScopeId, otherCenter.CenterId, Period());

        Assert.True(mine.Ok, mine.Error?.Message);
        // La de hace 60 días queda fuera y la de dentro de tres días también: el gráfico llega hasta hoy.
        var counted = mine.Value!;
        Assert.Equal((1, 1, 1), (counted.Sum(b => b.Finished), counted.Sum(b => b.Confirmed), counted.Sum(b => b.Cancelled)));
        Assert.Equal(0, theirs.Value!.Sum(b => b.Total));
    }

    [Fact]
    public async Task SoloAdministracionDelCentro_LeeElPanel()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var service = Service(nurse.ExternalSubject);

        Assert.Equal(ApplicationFailureCode.AccessDenied, (await service.ReadOccupancyAsync(nurse.ProfileScopeId, admin.CenterId, Period())).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await service.ReadMovementsAsync(nurse.ProfileScopeId, admin.CenterId, Period())).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await service.ReadAppointmentsAsync(nurse.ProfileScopeId, admin.CenterId, Period())).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await service.ReadAuthorizationsAsync(admin.ProfileScopeId, admin.CenterId, Period())).Error!.Code);
        // Aunque la capa de aplicación se saltara, cada lectura exige ser Administración del centro.
        await Assert.ThrowsAsync<AccessDeniedException>(() => Reader().ReadOccupancyAsync(AccessOf(nurse), DateTime.UtcNow));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Reader().ReadMovementsAsync(AccessOf(nurse), Today, DateTime.UtcNow));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Reader().ReadAppointmentsAsync(AccessOf(nurse), DateTime.UtcNow, DateTime.UtcNow));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Reader().ReadAuthorizationsAsync(AccessOf(nurse)));
    }

    [Fact]
    public async Task LaListaDeCuentas_TraeLaInvitacionSinUsarDelCentro_YNoLaUsadaNiLaAnulada()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var live = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var expired = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Medicina, admin.CenterId, admin.UnitId);
        var used = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, admin.CenterId, admin.UnitId);
        var elsewhere = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var otherCenter = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var now = DateTime.UtcNow;
        // La invitación sin usar de otro centro no es cosa de este.
        await ExecuteAsync("""
            INSERT INTO dbo.invitaciones_acceso (id, cuenta_id, centro_id, tipo, resumen_token, emitida_en, emitida_por_cuenta_id, perfil_emisor, caduca_en)
            VALUES (NEWID(), @account, @center, 'ALTA', @token, SYSUTCDATETIME(), @issuer, 'ADMINISTRACION', DATEADD(HOUR, 72, SYSUTCDATETIME()))
            """, new
        {
            account = elsewhere.AccountId.Value, center = otherCenter.CenterId.Value, token = $"{Guid.NewGuid():N}{Guid.NewGuid():N}",
            issuer = otherCenter.AccountId.Value,
        });
        foreach (var (account, issuedAt, usedAt, cancelledAt) in new[]
                 {
                     (live.AccountId, now, (DateTime?)null, (DateTime?)null), (expired.AccountId, now.AddDays(-5), null, null),
                     (used.AccountId, now.AddDays(-1), now, null), (used.AccountId, now.AddDays(-2), null, now),
                 })
        {
            await ExecuteAsync("""
                INSERT INTO dbo.invitaciones_acceso (id, cuenta_id, centro_id, tipo, resumen_token, emitida_en, emitida_por_cuenta_id, perfil_emisor,
                                                     caduca_en, usada_en, anulada_en)
                VALUES (NEWID(), @account, @center, 'ALTA', @token, @issuedAt, @issuer, 'ADMINISTRACION', DATEADD(HOUR, 72, @issuedAt), @usedAt, @cancelledAt)
                """, new
            {
                account = account.Value, center = admin.CenterId.Value, token = $"{Guid.NewGuid():N}{Guid.NewGuid():N}", issuedAt,
                issuer = admin.AccountId.Value, usedAt, cancelledAt,
            });
        }

        var accounts = await new SqlProfessionalAccountDirectory(TestDatabase.ConnectionFactory).ListAsync(AccessOf(admin));

        var open = accounts.Where(ProfessionalAccountAttention.HasOpenInvitation).Select(a => a.AccountId).ToHashSet();
        Assert.Equal([live.AccountId, expired.AccountId], open.OrderBy(id => id == expired.AccountId));
        Assert.Equal(expired.AccountId,
            Assert.Single(accounts, a => ProfessionalAccountAttention.HasExpiredInvitation(a, DateTimeOffset.UtcNow)).AccountId);
    }
}

file sealed class FixedPanelSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
