using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Appointments;
using ResidApp.Domain.Platform;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Citas externas de los residentes, PR 1 (script 0101; docs/producto/citas-externas-diseno.md). Administración registra y cambia siempre; Enfermería solo con el permiso
/// EXTERNAL_APPOINTMENT_MANAGE; Medicina las ve; Auxiliar y Dirección Clínica no. Enfermería y Medicina ven las citas de los residentes de sus unidades; ningún perfil ve las de otro
/// centro, ni con el ámbito de otro centro puesto a mano. La cita se cierra al realizarse, no acudirse o cancelarse; cada cambio deja su fila de historial (solo añadir) y una fila de
/// auditoría con solo identificadores; la baja del residente cancela las abiertas de hoy en adelante con causa y texto fijos. Los nombres de las pruebas son inventados.</summary>
public class CitasExternasTests
{
    private const string DischargeText = "Cancelada por el sistema al dar de baja al residente.";

    private static ExternalAppointmentsApplicationService Service(SeededProfile profile)
    {
        var scopes = new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory);
        return new(scopes, new FixedExternalSessionIdentityProvider(profile.ExternalSubject), new SqlExternalAppointmentRepository(TestDatabase.ConnectionFactory), TimeProvider.System);
    }

    // El día del centro (hora de Madrid), el mismo que usa el servicio: con la fecha UTC la prueba fallaba unas horas cada noche.
    private static DateOnly InDays(int days) => ResidApp.Domain.Modules.CenterModuleRules.TodayInMadrid(DateTimeOffset.UtcNow).AddDays(days);

    private static ExternalAppointment Cita(
        int days = 5, TimeOnly? time = null, bool withoutTime = false, ExternalAppointmentSpecialty? specialty = ExternalAppointmentSpecialty.Cardiologia, string? place = "Hospital Ficticio Norte",
        ExternalTransportMode transport = ExternalTransportMode.SinDeterminar, ExternalArrangementState transportState = ExternalArrangementState.Pendiente,
        ExternalCompanionMode companion = ExternalCompanionMode.SinDeterminar, ExternalArrangementState companionState = ExternalArrangementState.Pendiente,
        ExternalPreparation? preparation = null, ExternalFamilyVisibility visibility = ExternalFamilyVisibility.Visible) =>
        new(InDays(days), withoutTime ? null : time ?? new TimeOnly(10, 30), ExternalAppointmentType.Consulta, specialty, place, transport, transportState, companion, companionState, preparation, visibility);

    private sealed record Scene(SeededProfile Admin, SeededProfile Nurse, SeededProfile Doctor, ResidentId ResidentId);

    private static async Task<Scene> SceneAsync(string residentName)
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId, [ProfilePermissions.ExternalAppointmentManage]);
        var doctor = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Medicina, admin.CenterId, admin.UnitId);
        return new Scene(admin, nurse, doctor, await AdministracionResidentesTests.CreateResidentAsync(admin, residentName));
    }

    private static async Task<Guid> RegisterAsync(SeededProfile by, ResidentId residentId, ExternalAppointment? cita = null)
    {
        var registered = await Service(by).RegisterAsync(new RegisterExternalAppointmentCommand(by.ProfileScopeId, by.CenterId, residentId, cita ?? Cita()));
        Assert.True(registered.Ok, registered.Error?.Message);
        return registered.Value;
    }

    private static Task<ApplicationResult<ExternalAppointmentList>> ListAsync(SeededProfile by, int fromDays = -30, int toDays = 60) =>
        Service(by).ListAsync(by.ProfileScopeId, by.CenterId, InDays(fromDays), InDays(toDays));

    /// <summary>Un operador de Plataforma: su ámbito es el del centro de Plataforma, que no es el de ningún centro con residentes.</summary>
    private static async Task<SeededProfile> CreatePlatformOperatorAsync()
    {
        var accountId = Guid.NewGuid();
        var scopeId = Guid.NewGuid();
        var subject = $"test-plat-{Guid.NewGuid():N}"[..28];
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.cuentas (id, sujeto_externo, nombre_visible, estado, creado_en) VALUES (@accountId, @subject, N'Operador de pruebas', 'ACTIVE', @now);
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (@scopeId, @accountId, @centerId, 'PLATAFORMA', 'ACTIVE', @now, @accountId);
            """, new { accountId, subject, now, scopeId, centerId = PlatformCenter.Id });
        return new SeededProfile(subject, AccountId.From(accountId), CenterId.From(PlatformCenter.Id), UnitId.From(Guid.NewGuid()), scopeId);
    }

    private static async Task<int> CountAsync(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }

    private static Task<int> ChangesAsync(Guid id) => CountAsync("SELECT COUNT(*) FROM dbo.citas_externas_cambios WHERE cita_id = @id", new { id });

    private static Task<int> AuditAsync(Guid id, string action) =>
        CountAsync("SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE recurso_id = @id AND accion_codigo = @action AND tipo_recurso = 'EXTERNAL_APPOINTMENT'", new { id, action });

    private static async Task<SqlException> SqlFailureAsync(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql, parameters));
    }

    private static Task<ApplicationResult<int>> UpdateAsync(
        SeededProfile by, Guid id, int seenVersion, ExternalAppointment cita, ExternalAppointmentReasonCode? reason = null, string? line = null) =>
        Service(by).UpdateAsync(new UpdateExternalAppointmentCommand(by.ProfileScopeId, by.CenterId, id, seenVersion, cita, reason, line));

    private static Task<ApplicationResult<int>> StatusAsync(SeededProfile by, Guid id, int seenVersion, ExternalAppointmentStatus status) =>
        Service(by).ChangeStatusAsync(new ChangeExternalAppointmentStatusCommand(by.ProfileScopeId, by.CenterId, id, seenVersion, status));

    private static Task<ApplicationResult<int>> CancelAsync(SeededProfile by, Guid id, int seenVersion, ExternalAppointmentReasonCode? reason = ExternalAppointmentReasonCode.CambioDelHospital) =>
        Service(by).CancelAsync(new CancelExternalAppointmentCommand(by.ProfileScopeId, by.CenterId, id, seenVersion, reason, null));

    [Fact]
    public async Task Administracion_RegistraLaCita_ConSuFichaCompleta_SuHistorialYSuAuditoriaSinContenido()
    {
        var scene = await SceneAsync("Residente Cita Externa Uno");
        var cita = Cita(withoutTime: true, preparation: new ExternalPreparation(true, false, true, false, true, false, "Llevar gafas"),
            transport: ExternalTransportMode.Ambulancia, transportState: ExternalArrangementState.Confirmado, companion: ExternalCompanionMode.NoNecesita,
            companionState: ExternalArrangementState.Confirmado, visibility: ExternalFamilyVisibility.SinEspecialidad);

        var id = await RegisterAsync(scene.Admin, scene.ResidentId, cita);

        var found = (await Service(scene.Admin).FindAsync(scene.Admin.ProfileScopeId, scene.Admin.CenterId, id)).Value!;
        Assert.Equal(cita, found.Appointment);
        Assert.True(found.Appointment.TimeToConfirm);
        Assert.True(found.Appointment.Covered);
        Assert.Equal(ExternalAppointmentStatus.Programada, found.Status);
        Assert.Equal(1, found.Version);
        Assert.False(found.ResidentSuspended);
        Assert.Equal(scene.Admin.UnitId, found.UnitId);
        Assert.Equal(1, await ChangesAsync(id));
        Assert.Equal(1, await CountAsync("""
            SELECT COUNT(*) FROM dbo.citas_externas_cambios WHERE cita_id = @id AND numero = 1 AND accion_codigo = 'CREADA' AND actor_perfil_codigo = 'ADMINISTRACION'
               AND estado_nuevo_codigo = 'PROGRAMADA' AND motivo_codigo IS NULL AND motivo IS NULL AND causa_codigo IS NULL
            """, new { id }));
        Assert.Equal(1, await AuditAsync(id, "EXT_APPOINTMENT_CREATE"));
        var listed = (await ListAsync(scene.Admin)).Value!;
        Assert.Equal(id, Assert.Single(listed.Appointments).Id);
        Assert.True(listed.CanManage);
    }

    [Fact]
    public async Task Enfermeria_SinElPermiso_VeLasCitasPeroNoLasRegistra_ConElPermisoSi_YRevocadoOtraVezNo()
    {
        var scene = await SceneAsync("Residente Cita Externa Permiso");
        var plainNurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, scene.Admin.CenterId, scene.Admin.UnitId);
        var id = await RegisterAsync(scene.Admin, scene.ResidentId);

        var plainList = await ListAsync(plainNurse);
        var plainRegister = await Service(plainNurse).RegisterAsync(new RegisterExternalAppointmentCommand(plainNurse.ProfileScopeId, plainNurse.CenterId, scene.ResidentId, Cita()));
        var plainCancel = await CancelAsync(plainNurse, id, 1);
        var withPermission = await Service(scene.Nurse).RegisterAsync(new RegisterExternalAppointmentCommand(scene.Nurse.ProfileScopeId, scene.Nurse.CenterId, scene.ResidentId, Cita(7)));

        Assert.True(plainList.Ok);
        Assert.False(plainList.Value!.CanManage);
        Assert.Equal(id, Assert.Single(plainList.Value.Appointments).Id);
        Assert.Equal(ApplicationFailureCode.AccessDenied, plainRegister.Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, plainCancel.Error?.Code);
        Assert.True(withPermission.Ok, withPermission.Error?.Message);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas_cambios WHERE cita_id = @id AND actor_perfil_codigo = 'ENFERMERIA'", new { id = withPermission.Value }));

        // Administración se lo retira: la siguiente petición ya no pasa, ni por el caso de uso ni por el repositorio (que lo vuelve a mirar dentro de su transacción).
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                UPDATE dbo.permisos_perfil SET revocado_en = SYSUTCDATETIME(), revocado_por_cuenta_id = @admin WHERE ambito_perfil_id = @scope AND permiso_codigo = 'EXTERNAL_APPOINTMENT_MANAGE'
                """, new { admin = scene.Admin.AccountId.Value, scope = scene.Nurse.ProfileScopeId });
        }
        var revoked = await Service(scene.Nurse).RegisterAsync(new RegisterExternalAppointmentCommand(scene.Nurse.ProfileScopeId, scene.Nurse.CenterId, scene.ResidentId, Cita(8)));
        var direct = new ExternalAppointmentActor(scene.Nurse.AccountId, scene.Nurse.ProfileScopeId, SystemProfile.Enfermeria, scene.Nurse.CenterId);
        var viaRepository = await Record.ExceptionAsync(() =>
            new SqlExternalAppointmentRepository(TestDatabase.ConnectionFactory).CreateAsync(direct, scene.ResidentId, Cita(9)));

        Assert.Equal(ApplicationFailureCode.AccessDenied, revoked.Error?.Code);
        Assert.IsType<AccessDeniedException>(viaRepository);
        Assert.True((await ListAsync(scene.Nurse)).Ok);
    }

    [Fact]
    public async Task Medicina_LasVe_PeroNoLasCambia_YAuxiliarYDireccionClinicaNoTienenAcceso()
    {
        var scene = await SceneAsync("Residente Cita Externa Perfiles");
        var auxiliar = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, scene.Admin.CenterId, scene.Admin.UnitId);
        var direction = await SeedFixture.AddProfileToCenterAsync(SystemProfile.DireccionClinica, scene.Admin.CenterId, scene.Admin.UnitId);
        var id = await RegisterAsync(scene.Admin, scene.ResidentId);

        var doctorList = await ListAsync(scene.Doctor);
        var doctorFind = await Service(scene.Doctor).FindAsync(scene.Doctor.ProfileScopeId, scene.Doctor.CenterId, id);
        var doctorCancel = await CancelAsync(scene.Doctor, id, 1);
        var doctorRegister = await Service(scene.Doctor).RegisterAsync(new RegisterExternalAppointmentCommand(scene.Doctor.ProfileScopeId, scene.Doctor.CenterId, scene.ResidentId, Cita()));

        Assert.Equal(id, Assert.Single(doctorList.Value!.Appointments).Id);
        Assert.False(doctorList.Value.CanManage);
        Assert.True(doctorFind.Ok);
        Assert.Equal(ApplicationFailureCode.AccessDenied, doctorCancel.Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, doctorRegister.Error?.Code);
        foreach (var profile in new[] { auxiliar, direction })
        {
            Assert.Equal(ApplicationFailureCode.AccessDenied, (await ListAsync(profile)).Error?.Code);
            Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(profile).FindAsync(profile.ProfileScopeId, profile.CenterId, id)).Error?.Code);
        }
    }

    [Fact]
    public async Task OtroCentro_NoVeNiCambiaNada_NiConElAmbitoDeOtroCentroPuestoAMano_YPlataformaTampoco()
    {
        var scene = await SceneAsync("Residente Cita Externa Aislamiento");
        var id = await RegisterAsync(scene.Admin, scene.ResidentId);
        var outsiderAdmin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var outsiderNurse = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria, [ProfilePermissions.ExternalAppointmentManage]);
        var platform = await CreatePlatformOperatorAsync();

        Assert.Empty((await ListAsync(outsiderAdmin)).Value!.Appointments);
        Assert.Empty((await ListAsync(outsiderNurse)).Value!.Appointments);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(outsiderAdmin).FindAsync(outsiderAdmin.ProfileScopeId, outsiderAdmin.CenterId, id)).Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await CancelAsync(outsiderAdmin, id, 1)).Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await CancelAsync(outsiderNurse, id, 1)).Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied,
            (await Service(outsiderAdmin).RegisterAsync(new RegisterExternalAppointmentCommand(outsiderAdmin.ProfileScopeId, outsiderAdmin.CenterId, scene.ResidentId, Cita()))).Error?.Code);

        // Cookie de ámbito manipulada: el ámbito es de otro centro pero se pide el del residente. El centro sale del ámbito verificado, no de lo que llega.
        var forged = await Service(outsiderAdmin).ListAsync(outsiderAdmin.ProfileScopeId, scene.Admin.CenterId, InDays(-30), InDays(60));
        var forgedCancel = await Service(outsiderAdmin).CancelAsync(new CancelExternalAppointmentCommand(
            outsiderAdmin.ProfileScopeId, scene.Admin.CenterId, id, 1, ExternalAppointmentReasonCode.CambioDelHospital, null));
        Assert.Equal(ApplicationFailureCode.AccessDenied, forged.Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, forgedCancel.Error?.Code);

        // Aunque conozca el identificador del residente y mande el actor a mano al repositorio: la base separa por centro y el repositorio vuelve a comprobar el ámbito.
        var handMade = new ExternalAppointmentActor(outsiderAdmin.AccountId, outsiderAdmin.ProfileScopeId, SystemProfile.Administracion, scene.Admin.CenterId);
        Assert.Null(await new SqlExternalAppointmentRepository(TestDatabase.ConnectionFactory).FindAsync(handMade, id));

        Assert.Equal(ApplicationFailureCode.AccessDenied, (await ListAsync(platform)).Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(platform).FindAsync(platform.ProfileScopeId, platform.CenterId, id)).Error?.Code);
        Assert.Equal(ExternalAppointmentStatus.Programada, (await Service(scene.Admin).FindAsync(scene.Admin.ProfileScopeId, scene.Admin.CenterId, id)).Value!.Status);
    }

    [Fact]
    public async Task EntreUnidades_EnfermeriaYMedicinaSoloVenLasDeSusUnidades_YAdministracionTodas()
    {
        var scene = await SceneAsync("Residente Cita Externa Unidad A");
        var otherUnit = await AdministracionResidentesTests.AddUnitAsync(scene.Admin.CenterId);
        var otherResident = await AdministracionResidentesTests.CreateResidentAsync(scene.Admin, "Residente Cita Externa Unidad B", otherUnit);
        var mine = await RegisterAsync(scene.Admin, scene.ResidentId);
        var foreign = await RegisterAsync(scene.Admin, otherResident, Cita(6));

        foreach (var clinical in new[] { scene.Nurse, scene.Doctor })
        {
            Assert.Equal(mine, Assert.Single((await ListAsync(clinical)).Value!.Appointments).Id);
            Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(clinical).FindAsync(clinical.ProfileScopeId, clinical.CenterId, foreign)).Error?.Code);
        }
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await CancelAsync(scene.Nurse, foreign, 1)).Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(scene.Nurse).RegisterAsync(
            new RegisterExternalAppointmentCommand(scene.Nurse.ProfileScopeId, scene.Nurse.CenterId, otherResident, Cita(8)))).Error?.Code);
        Assert.Equal(2, (await ListAsync(scene.Admin)).Value!.Appointments.Count);
        Assert.Equal(ExternalAppointmentStatus.Programada, (await Service(scene.Admin).FindAsync(scene.Admin.ProfileScopeId, scene.Admin.CenterId, foreign)).Value!.Status);
    }

    [Fact]
    public async Task EnfermeriaConElAmbitoRestringidoAUnResidente_SoloVeEsaCita()
    {
        var scene = await SceneAsync("Residente Cita Externa Restringido Uno");
        var second = await AdministracionResidentesTests.CreateResidentAsync(scene.Admin, "Residente Cita Externa Restringido Dos");
        var first = await RegisterAsync(scene.Admin, scene.ResidentId);
        var other = await RegisterAsync(scene.Admin, second, Cita(6));
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.ambitos_perfil_residente (id, ambito_perfil_id, centro_id, residente_id, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @scope, @center, @resident, SYSUTCDATETIME(), @admin)
                """, new { scope = scene.Nurse.ProfileScopeId, center = scene.Admin.CenterId.Value, resident = scene.ResidentId.Value, admin = scene.Admin.AccountId.Value });
        }

        Assert.Equal(first, Assert.Single((await ListAsync(scene.Nurse)).Value!.Appointments).Id);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(scene.Nurse).FindAsync(scene.Nurse.ProfileScopeId, scene.Nurse.CenterId, other)).Error?.Code);
    }

    [Fact]
    public async Task ReprogramarExigeMotivoYDiaNoPasado_ModificarOtraCosaNo_YSinCambiosNoSeCreaNada()
    {
        var scene = await SceneAsync("Residente Cita Externa Cambios");
        var id = await RegisterAsync(scene.Admin, scene.ResidentId);

        var noReason = await UpdateAsync(scene.Admin, id, 1, Cita(8));
        var past = await UpdateAsync(scene.Admin, id, 1, Cita(-3), ExternalAppointmentReasonCode.CambioDelHospital);
        var longLine = await UpdateAsync(scene.Admin, id, 1, Cita(8), ExternalAppointmentReasonCode.CambioDelHospital, new string('x', 101));
        Assert.Equal(ApplicationFailureCode.InvalidInput, noReason.Error?.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, past.Error?.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, longLine.Error?.Code);
        Assert.Equal(1, await ChangesAsync(id));

        var rescheduled = await UpdateAsync(scene.Admin, id, 1, Cita(8, new TimeOnly(9, 0), visibility: ExternalFamilyVisibility.Oculta),
            ExternalAppointmentReasonCode.FaltaTransporteOAcompanante, "Sin ambulancia ese día");
        Assert.Equal(2, rescheduled.Value);
        Assert.Equal(1, await CountAsync("""
            SELECT COUNT(*) FROM dbo.citas_externas_cambios
             WHERE cita_id = @id AND numero = 2 AND accion_codigo = 'REPROGRAMADA' AND fecha_anterior = @before AND fecha_nueva = @after
               AND hora_anterior = '10:30' AND hora_nueva = '09:00' AND motivo_codigo = 'FALTA_TRANSPORTE_O_ACOMPANANTE' AND motivo = 'Sin ambulancia ese día'
            """, new { id, before = InDays(5), after = InDays(8) }));
        Assert.Equal(1, await AuditAsync(id, "EXT_APPOINTMENT_RESCHEDULE"));
        Assert.Equal(1, await AuditAsync(id, "EXT_APPOINTMENT_VISIBILITY"));

        var modified = await UpdateAsync(scene.Admin, id, 2, Cita(8, new TimeOnly(9, 0), place: "Hospital Ficticio Sur", visibility: ExternalFamilyVisibility.Oculta));
        Assert.Equal(3, modified.Value);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.citas_externas_cambios WHERE cita_id = @id AND numero = 3 AND accion_codigo = 'MODIFICADA' AND motivo_codigo IS NULL AND motivo IS NULL", new { id }));
        Assert.Equal(1, await AuditAsync(id, "EXT_APPOINTMENT_UPDATE"));

        var same = await UpdateAsync(scene.Admin, id, 3, Cita(8, new TimeOnly(9, 0), place: "Hospital Ficticio Sur", visibility: ExternalFamilyVisibility.Oculta));
        Assert.Equal(3, same.Value);
        Assert.Equal(3, await ChangesAsync(id));
        Assert.Equal(ExternalFamilyVisibility.Oculta, (await Service(scene.Admin).FindAsync(scene.Admin.ProfileScopeId, scene.Admin.CenterId, id)).Value!.Appointment.FamilyVisibility);
    }

    [Fact]
    public async Task PonerLaHoraPorPrimeraVez_NoEsReprogramar_PeroCambiarUnaHoraPuestaOQuitarlaSi()
    {
        // Decisión de Orlando (2026-11-18): de «Por confirmar» a una hora solo completa la ficha (sin motivo, acción MODIFICADA); cambiar una hora ya puesta, o volver a «por confirmar», reprograma.
        var scene = await SceneAsync("Residente Cita Externa Hora");
        var id = await RegisterAsync(scene.Admin, scene.ResidentId, Cita(withoutTime: true));

        var firstTime = await UpdateAsync(scene.Admin, id, 1, Cita(time: new TimeOnly(11, 0)));
        Assert.True(firstTime.Ok, firstTime.Error?.Message);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.citas_externas_cambios WHERE cita_id = @id AND numero = 2 AND accion_codigo = 'MODIFICADA' AND hora_anterior IS NULL AND hora_nueva = '11:00' AND motivo_codigo IS NULL", new { id }));
        Assert.Equal(1, await AuditAsync(id, "EXT_APPOINTMENT_UPDATE"));
        Assert.Equal(0, await AuditAsync(id, "EXT_APPOINTMENT_RESCHEDULE"));

        var changedHourNoReason = await UpdateAsync(scene.Admin, id, 2, Cita(time: new TimeOnly(12, 0)));
        var cleared = await UpdateAsync(scene.Admin, id, 2, Cita(withoutTime: true));
        Assert.Equal(ApplicationFailureCode.InvalidInput, changedHourNoReason.Error?.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, cleared.Error?.Code);

        var changedHour = await UpdateAsync(scene.Admin, id, 2, Cita(time: new TimeOnly(12, 0)), ExternalAppointmentReasonCode.CambioDelHospital);
        Assert.True(changedHour.Ok, changedHour.Error?.Message);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.citas_externas_cambios WHERE cita_id = @id AND numero = 3 AND accion_codigo = 'REPROGRAMADA' AND hora_anterior = '11:00' AND hora_nueva = '12:00'", new { id }));
    }

    [Fact]
    public async Task DosPersonasEditanALaVez_LaSegundaRecibeElConflictoYNoPisa()
    {
        var scene = await SceneAsync("Residente Cita Externa Concurrencia");
        var id = await RegisterAsync(scene.Admin, scene.ResidentId);

        var first = await UpdateAsync(scene.Admin, id, 1, Cita(place: "Hospital Ficticio Este"));
        var second = await UpdateAsync(scene.Nurse, id, 1, Cita(place: "Hospital Ficticio Oeste"));
        var secondStatus = await StatusAsync(scene.Nurse, id, 1, ExternalAppointmentStatus.ConfirmadaHospital);
        var secondCancel = await CancelAsync(scene.Nurse, id, 1);

        Assert.Equal(2, first.Value);
        foreach (var losing in new[] { second, secondStatus, secondCancel })
        {
            Assert.Equal(ApplicationFailureCode.Conflict, losing.Error?.Code);
            Assert.Contains("Otra persona ha cambiado esta cita", losing.Error?.Message);
        }
        var found = (await Service(scene.Admin).FindAsync(scene.Admin.ProfileScopeId, scene.Admin.CenterId, id)).Value!;
        Assert.Equal("Hospital Ficticio Este", found.Appointment.Place);
        Assert.Equal(2, found.Version);
        Assert.Equal(2, await ChangesAsync(id));
    }

    [Fact]
    public async Task Estados_LaAbiertaPasaAConfirmadaYRealizada_YUnaCerradaYaNoCambia()
    {
        var scene = await SceneAsync("Residente Cita Externa Estados");
        var id = await RegisterAsync(scene.Nurse, scene.ResidentId, Cita(0));

        var cancelViaStatus = await StatusAsync(scene.Nurse, id, 1, ExternalAppointmentStatus.Cancelada);
        var same = await StatusAsync(scene.Nurse, id, 1, ExternalAppointmentStatus.Programada);
        var confirmed = await StatusAsync(scene.Nurse, id, 1, ExternalAppointmentStatus.ConfirmadaHospital);
        var done = await StatusAsync(scene.Admin, id, 2, ExternalAppointmentStatus.Realizada);

        Assert.Equal(ApplicationFailureCode.InvalidInput, cancelViaStatus.Error?.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, same.Error?.Code);
        Assert.Equal(2, confirmed.Value);
        Assert.Equal(3, done.Value);
        Assert.Equal(1, await CountAsync("""
            SELECT COUNT(*) FROM dbo.citas_externas_cambios
             WHERE cita_id = @id AND accion_codigo = 'ESTADO_CAMBIADO' AND estado_anterior_codigo = 'CONFIRMADA_HOSPITAL' AND estado_nuevo_codigo = 'REALIZADA'
            """, new { id }));
        Assert.Equal(2, await AuditAsync(id, "EXT_APPOINTMENT_STATUS"));

        Assert.Equal(ApplicationFailureCode.Conflict, (await StatusAsync(scene.Admin, id, 3, ExternalAppointmentStatus.NoAcudida)).Error?.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, (await CancelAsync(scene.Admin, id, 3)).Error?.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, (await UpdateAsync(scene.Admin, id, 3, Cita(0, place: "Otro"))).Error?.Code);
        Assert.Equal(3, await ChangesAsync(id));
    }

    [Fact]
    public async Task Estados_NoSeCierraUnaCitaFutura_YConfirmadaNoVuelveAProgramada()
    {
        // Decisión de Orlando (2026-11-18): «Realizada» y «No se acudió» solo desde el día de la cita; una cita confirmada por el hospital no vuelve a programada.
        var scene = await SceneAsync("Residente Cita Externa Estados Futuros");
        var future = await RegisterAsync(scene.Admin, scene.ResidentId, Cita(3));

        Assert.Equal(ApplicationFailureCode.InvalidInput, (await StatusAsync(scene.Admin, future, 1, ExternalAppointmentStatus.Realizada)).Error?.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, (await StatusAsync(scene.Admin, future, 1, ExternalAppointmentStatus.NoAcudida)).Error?.Code);
        Assert.True((await StatusAsync(scene.Admin, future, 1, ExternalAppointmentStatus.ConfirmadaHospital)).Ok);
        Assert.Equal(ApplicationFailureCode.InvalidInput, (await StatusAsync(scene.Admin, future, 2, ExternalAppointmentStatus.Programada)).Error?.Code);
        Assert.Equal(2, await ChangesAsync(future));
        Assert.True((await CancelAsync(scene.Admin, future, 2)).Ok);

        var today = await RegisterAsync(scene.Admin, scene.ResidentId, Cita(0));
        Assert.True((await StatusAsync(scene.Admin, today, 1, ExternalAppointmentStatus.Realizada)).Ok);
    }

    [Fact]
    public async Task Cancelar_ExigeMotivoDeLaListaYLaCierra_ConSuHistorialYAuditoria()
    {
        var scene = await SceneAsync("Residente Cita Externa Cancelar");
        var id = await RegisterAsync(scene.Admin, scene.ResidentId);

        var noReason = await CancelAsync(scene.Admin, id, 1, reason: null);
        var cancelled = await Service(scene.Admin).CancelAsync(new CancelExternalAppointmentCommand(
            scene.Admin.ProfileScopeId, scene.Admin.CenterId, id, 1, ExternalAppointmentReasonCode.PeticionDeLaFamilia, "La familia la anula"));

        Assert.Equal(ApplicationFailureCode.InvalidInput, noReason.Error?.Code);
        Assert.Equal(2, cancelled.Value);
        Assert.Equal(1, await CountAsync("""
            SELECT COUNT(*) FROM dbo.citas_externas_cambios
             WHERE cita_id = @id AND accion_codigo = 'CANCELADA' AND estado_anterior_codigo = 'PROGRAMADA' AND estado_nuevo_codigo = 'CANCELADA'
               AND motivo_codigo = 'PETICION_DE_LA_FAMILIA' AND motivo = 'La familia la anula' AND causa_codigo IS NULL
            """, new { id }));
        Assert.Equal(1, await AuditAsync(id, "EXT_APPOINTMENT_CANCEL"));
        Assert.Equal(ExternalAppointmentStatus.Cancelada, (await Service(scene.Admin).FindAsync(scene.Admin.ProfileScopeId, scene.Admin.CenterId, id)).Value!.Status);
    }

    [Fact]
    public async Task LaAuditoriaLlevaSoloIdentificadores_NuncaElContenidoDeLaCita()
    {
        var scene = await SceneAsync("Residente Cita Externa Auditoria");
        var id = await RegisterAsync(scene.Admin, scene.ResidentId, Cita(specialty: ExternalAppointmentSpecialty.Oncologia, place: "Hospital Ficticio Centro"));
        await UpdateAsync(scene.Admin, id, 1, Cita(specialty: ExternalAppointmentSpecialty.Oncologia, place: "Hospital Ficticio Centro", preparation: new ExternalPreparation(false, false, false, false, false, false, "Llevar gafas")));
        await CancelAsync(scene.Admin, id, 2);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var rows = (await connection.QueryAsync<string>("""
            SELECT CONCAT_WS('|', accion_codigo, tipo_recurso, perfil_activo, CAST(residente_id AS NVARCHAR(40)), CAST(recurso_id AS NVARCHAR(40)))
              FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE recurso_id = @id
            """, new { id })).ToList();
        Assert.Equal(3, rows.Count);
        Assert.DoesNotContain(rows, row => row.Contains("Oncolog", StringComparison.OrdinalIgnoreCase) || row.Contains("Hospital") || row.Contains("gafas"));
    }

    [Fact]
    public async Task ElHistorialNoSeCambiaNiSeBorra_LaCitaNoSeBorra_YLaCerradaNoVuelveACambiar()
    {
        var scene = await SceneAsync("Residente Cita Externa Inmutable");
        var id = await RegisterAsync(scene.Admin, scene.ResidentId);
        Assert.True((await CancelAsync(scene.Admin, id, 1)).Ok);

        Assert.Contains("EXTERNAL_APPOINTMENT_CHANGE_IMMUTABLE", (await SqlFailureAsync("UPDATE dbo.citas_externas_cambios SET motivo = N'x' WHERE cita_id = @id", new { id })).Message);
        Assert.Contains("EXTERNAL_APPOINTMENT_CHANGE_IMMUTABLE", (await SqlFailureAsync("DELETE FROM dbo.citas_externas_cambios WHERE cita_id = @id", new { id })).Message);
        Assert.Contains("EXTERNAL_APPOINTMENT_NO_DELETE", (await SqlFailureAsync("DELETE FROM dbo.citas_externas WHERE id = @id", new { id })).Message);
        Assert.Contains("EXTERNAL_APPOINTMENT_CLOSED", (await SqlFailureAsync(
            "UPDATE dbo.citas_externas SET lugar = N'Otro', version = version + 1 WHERE id = @id", new { id })).Message);
    }

    [Fact]
    public async Task LaBaseImponeLaIdentidadYLaVersion_DeUnaCitaAbierta()
    {
        var scene = await SceneAsync("Residente Cita Externa Guardas");
        var id = await RegisterAsync(scene.Admin, scene.ResidentId);

        Assert.Contains("EXTERNAL_APPOINTMENT_IDENTITY_IMMUTABLE", (await SqlFailureAsync(
            "UPDATE dbo.citas_externas SET creada_en = DATEADD(DAY, 1, creada_en), version = version + 1 WHERE id = @id", new { id })).Message);
        Assert.Contains("EXTERNAL_APPOINTMENT_IDENTITY_IMMUTABLE", (await SqlFailureAsync(
            "UPDATE dbo.citas_externas SET creada_por_perfil = 'ENFERMERIA', version = version + 1 WHERE id = @id", new { id })).Message);
        Assert.Contains("EXTERNAL_APPOINTMENT_VERSION_INVALID", (await SqlFailureAsync("UPDATE dbo.citas_externas SET lugar = N'Otro' WHERE id = @id", new { id })).Message);
        Assert.Contains("EXTERNAL_APPOINTMENT_VERSION_INVALID", (await SqlFailureAsync(
            "UPDATE dbo.citas_externas SET lugar = N'Otro', version = version + 2 WHERE id = @id", new { id })).Message);
    }

    [Theory]
    [InlineData("lugar", "N'Hospital' + NCHAR(10) + N'Oncología'")]
    [InlineData("lugar", "REPLICATE(N'x', 81)")]
    [InlineData("lugar", "N'   '")]
    [InlineData("preparar_texto", "REPLICATE(N'x', 101)")]
    [InlineData("preparar_texto", "N'a' + NCHAR(13) + N'b'")]
    [InlineData("especialidad_codigo", "N'ASTROLOGIA'")]
    [InlineData("tipo_codigo", "N'VISITA'")]
    [InlineData("transporte_modo_codigo", "N'TAXI'")]
    [InlineData("estado_codigo", "N'REPROGRAMADA'")]
    [InlineData("visibilidad_familia_codigo", "N'TODOS'")]
    public async Task LaBaseRechazaLoQueNoEsDeLaListaCerradaOPasaDelLimite(string column, string value)
    {
        var scene = await SceneAsync("Residente Cita Externa Listas");
        var id = await RegisterAsync(scene.Admin, scene.ResidentId);

        var failure = await SqlFailureAsync($"UPDATE dbo.citas_externas SET {column} = {value}, version = version + 1 WHERE id = @id", new { id });

        Assert.Contains(failure.Number, new[] { 547, 2628 }); // 547: restricción CHECK; 2628: la columna no admite más caracteres
    }

    [Theory]
    [InlineData("transporte_modo_codigo = 'SIN_DETERMINAR', transporte_estado_codigo = 'CONFIRMADO'")]
    [InlineData("acompanante_modo_codigo = 'SIN_DETERMINAR', acompanante_estado_codigo = 'CONFIRMADO'")]
    [InlineData("acompanante_modo_codigo = 'NO_NECESITA', acompanante_estado_codigo = 'PENDIENTE'")]
    public async Task LaBaseRechazaTransporteOAcompananteIncoherentes(string assignment)
    {
        var scene = await SceneAsync("Residente Cita Externa Coherencia");
        var id = await RegisterAsync(scene.Admin, scene.ResidentId);

        var failure = await SqlFailureAsync($"UPDATE dbo.citas_externas SET {assignment}, version = version + 1 WHERE id = @id", new { id });

        Assert.Equal(547, failure.Number);
    }

    [Fact]
    public async Task ElHistorialRechazaReprogramarOCancelarSinMotivo_YUnMotivoFueraDeLista()
    {
        var scene = await SceneAsync("Residente Cita Externa Motivos");
        var id = await RegisterAsync(scene.Admin, scene.ResidentId);
        var insert = """
            INSERT INTO dbo.citas_externas_cambios (id, centro_id, cita_id, numero, accion_codigo, actor_cuenta_id, actor_perfil_codigo, motivo_codigo, motivo, ocurrido_en)
            VALUES (NEWID(), @center, @id, 2, @action, @account, 'ADMINISTRACION', @code, @line, SYSUTCDATETIME())
            """;
        object Args(string action, string? code, string? line) => new { center = scene.Admin.CenterId.Value, id, account = scene.Admin.AccountId.Value, action, code, line };

        Assert.Equal(547, (await SqlFailureAsync(insert, Args("REPROGRAMADA", null, null))).Number);
        Assert.Equal(547, (await SqlFailureAsync(insert, Args("CANCELADA", null, null))).Number);
        Assert.Equal(547, (await SqlFailureAsync(insert, Args("CANCELADA", "PORQUE_SI", null))).Number);
        Assert.Equal(547, (await SqlFailureAsync(insert, Args("MODIFICADA", "OTRO", null))).Number);
        Assert.Equal(547, (await SqlFailureAsync(insert, Args("CANCELADA", "OTRO", "dos" + Environment.NewLine + "líneas"))).Number);
    }

    [Fact]
    public async Task LaCausaDeBaja_SoloLaAdmiteLaBaseConElTextoFijoYUnaBajaEnLaMismaTransaccion()
    {
        var scene = await SceneAsync("Residente Cita Externa Causa");
        var id = await RegisterAsync(scene.Admin, scene.ResidentId);
        var insert = """
            INSERT INTO dbo.citas_externas_cambios (id, centro_id, cita_id, numero, accion_codigo, actor_cuenta_id, actor_perfil_codigo, motivo, ocurrido_en, causa_codigo)
            VALUES (NEWID(), @center, @id, 2, @action, @account, 'ADMINISTRACION', @text, SYSUTCDATETIME(), @cause)
            """;
        object Args(string action, string? text, string cause) => new { center = scene.Admin.CenterId.Value, id, account = scene.Admin.AccountId.Value, action, text, cause };

        // Sin baja del residente en la misma transacción, ni con el texto fijo.
        Assert.Contains("EXTERNAL_APPOINTMENT_CAUSE_INVALID", (await SqlFailureAsync(insert, Args("CANCELADA", DischargeText, "BAJA_RESIDENTE"))).Message);
        Assert.Contains("EXTERNAL_APPOINTMENT_CAUSE_INVALID", (await SqlFailureAsync(insert, Args("CANCELADA", "Texto libre", "BAJA_RESIDENTE"))).Message);
        Assert.Equal(547, (await SqlFailureAsync(insert, Args("CANCELADA", DischargeText, "OTRA_CAUSA"))).Number);
        Assert.Equal(547, (await SqlFailureAsync(insert, Args("REPROGRAMADA", DischargeText, "BAJA_RESIDENTE"))).Number);
        Assert.Equal(1, await ChangesAsync(id));
    }

    [Fact]
    public async Task LaBaja_CancelaLasAbiertasDeHoyEnAdelanteDeEseResidente_ConCausaYTextoFijos_YNoTocaLoDemas()
    {
        var scene = await SceneAsync("Residente Cita Externa Baja Uno");
        var other = await AdministracionResidentesTests.CreateResidentAsync(scene.Admin, "Residente Cita Externa Baja Dos");
        var future = await RegisterAsync(scene.Admin, scene.ResidentId, Cita(5));
        var confirmed = await RegisterAsync(scene.Admin, scene.ResidentId, Cita(9));
        var today = await RegisterAsync(scene.Admin, scene.ResidentId, Cita(0));
        var alreadyDone = await RegisterAsync(scene.Admin, scene.ResidentId, Cita(0));
        var otherResident = await RegisterAsync(scene.Admin, other, Cita(5));
        Assert.True((await StatusAsync(scene.Admin, confirmed, 1, ExternalAppointmentStatus.ConfirmadaHospital)).Ok);
        Assert.True((await StatusAsync(scene.Admin, alreadyDone, 1, ExternalAppointmentStatus.NoAcudida)).Ok);
        // Una cita pasada y abierta (no se pudo cerrar a tiempo): no es «de hoy en adelante», no se toca.
        var past = Guid.NewGuid();
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.citas_externas (id, centro_id, residente_id, fecha, tipo_codigo, creada_por_cuenta_id, creada_por_perfil, creada_en, modificada_en)
                VALUES (@id, @center, @resident, DATEADD(DAY, -4, CAST(SYSUTCDATETIME() AS DATE)), 'CONSULTA', @account, 'ADMINISTRACION', SYSUTCDATETIME(), SYSUTCDATETIME())
                """, new { id = past, center = scene.Admin.CenterId.Value, resident = scene.ResidentId.Value, account = scene.Admin.AccountId.Value });
        }

       
        var discharged = await EstadoResidenteTests.Build(scene.Admin.ExternalSubject).DischargeAsync(new DischargeResidentCommand(
            scene.Admin.ProfileScopeId, scene.Admin.CenterId, scene.ResidentId, Guid.NewGuid(), ResidentDischargeReason.AltaVoluntaria, "Vuelve con su familia."));

        Assert.True(discharged.Ok, discharged.Error?.Message);
        Assert.Equal(3, discharged.Value!.ExternalAppointmentsCancelled);
        foreach (var (id, version) in new[] { (future, 2), (confirmed, 3), (today, 2) })
        {
            // La baja sube la versión en uno (la confirmada por el hospital ya iba por la 2).
            Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas WHERE id = @id AND estado_codigo = 'CANCELADA' AND version = @version", new { id, version }));
            Assert.Equal(1, await CountAsync("""
                SELECT COUNT(*) FROM dbo.citas_externas_cambios
                 WHERE cita_id = @id AND accion_codigo = 'CANCELADA' AND causa_codigo = 'BAJA_RESIDENTE' AND motivo = @text AND motivo_codigo IS NULL
                   AND actor_perfil_codigo = 'ADMINISTRACION' AND actor_cuenta_id = @account AND estado_nuevo_codigo = 'CANCELADA'
                """, new { id, text = DischargeText, account = scene.Admin.AccountId.Value }));
            Assert.Equal(1, await AuditAsync(id, "EXT_APPOINTMENT_CANCEL_DISCHARGE"));
        }
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas_cambios WHERE cita_id = @id AND estado_anterior_codigo = 'CONFIRMADA_HOSPITAL'", new { id = confirmed }));
        foreach (var (id, status) in new[] { (past, "PROGRAMADA"), (alreadyDone, "NO_ACUDIDA"), (otherResident, "PROGRAMADA") })
        {
            Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas WHERE id = @id AND estado_codigo = @status", new { id, status }));
            Assert.Equal(0, await AuditAsync(id, "EXT_APPOINTMENT_CANCEL_DISCHARGE"));
            Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas_cambios WHERE cita_id = @id AND causa_codigo IS NOT NULL", new { id }));
        }

        // Tras la baja Enfermería y Medicina ya no ven a ese residente (su ubicación vigente se cierra); Administración sigue viendo todo el centro.
        Assert.DoesNotContain((await ListAsync(scene.Nurse)).Value!.Appointments, a => a.ResidentId == scene.ResidentId);
        Assert.Equal(6, (await ListAsync(scene.Admin, -10)).Value!.Appointments.Count);

        // Tras la baja el residente ya no está activo: no se le registran citas nuevas ni se cambian las abiertas que le quedan.
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(scene.Admin).RegisterAsync(
            new RegisterExternalAppointmentCommand(scene.Admin.ProfileScopeId, scene.Admin.CenterId, scene.ResidentId, Cita(12)))).Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await CancelAsync(scene.Admin, past, 1)).Error?.Code);
    }

    [Fact]
    public async Task LaSuspension_NoCancelaLasCitas_YSeMarcaParaElPersonal_YSePuedeRegistrarOtra()
    {
        var scene = await SceneAsync("Residente Cita Externa Suspension");
        var id = await RegisterAsync(scene.Admin, scene.ResidentId);

        var suspended = await EstadoResidenteTests.Build(scene.Admin.ExternalSubject).SuspendAsync(
            new SuspendResidentCommand(scene.Admin.ProfileScopeId, scene.Admin.CenterId, scene.ResidentId, Guid.NewGuid(), null));
        var found = (await Service(scene.Admin).FindAsync(scene.Admin.ProfileScopeId, scene.Admin.CenterId, id)).Value!;
        var another = await Service(scene.Admin).RegisterAsync(new RegisterExternalAppointmentCommand(scene.Admin.ProfileScopeId, scene.Admin.CenterId, scene.ResidentId, Cita(9)));

        Assert.True(suspended.Ok, suspended.Error?.Message);
        Assert.Equal(ExternalAppointmentStatus.Programada, found.Status);
        Assert.True(found.ResidentSuspended);
        Assert.True(another.Ok, another.Error?.Message);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas_cambios WHERE cita_id = @id AND causa_codigo IS NOT NULL", new { id }));
    }

    [Fact]
    public async Task ElPermisoNuevo_SoloPuedeTenerloEnfermeria()
    {
        var scene = await SceneAsync("Residente Cita Externa Catalogo");
        var auxiliar = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, scene.Admin.CenterId, scene.Admin.UnitId);
        var insert = """
            INSERT INTO dbo.permisos_perfil (id, ambito_perfil_id, centro_id, permiso_codigo, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @scope, @center, 'EXTERNAL_APPOINTMENT_MANAGE', SYSUTCDATETIME(), @admin)
            """;

        foreach (var profile in new[] { scene.Admin, scene.Doctor, auxiliar })
        {
            var failure = await SqlFailureAsync(insert, new { scope = profile.ProfileScopeId, center = scene.Admin.CenterId.Value, admin = scene.Admin.AccountId.Value });
            Assert.Contains("PROFILE_PERMISSION_NOT_ALLOWED", failure.Message);
        }
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.permisos_perfil WHERE ambito_perfil_id = @scope AND permiso_codigo = 'EXTERNAL_APPOINTMENT_MANAGE'", new { scope = scene.Nurse.ProfileScopeId }));
    }

    [Fact]
    public async Task UnTrasladoDeUnidad_MueveLaVisibilidadDeLaCita_SinTocarLaCita()
    {
        // PR 2: la cita no guarda unidad, así que al trasladar al residente la cita deja de verla quien solo tiene la unidad de origen y pasa a verla quien tiene la de destino;
        // Administración (todo el centro) la ve siempre. Comprobado a la vez que se rechaza el cambio de quien ya no la ve.
        var scene = await SceneAsync("Residente Cita Traslado");
        var id = await RegisterAsync(scene.Admin, scene.ResidentId);
        var destination = await AdministracionResidentesTests.AddUnitAsync(scene.Admin.CenterId);
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @ProfileScopeId, @CenterId, @UnitId, SYSUTCDATETIME(), @AccountId)
                """, new { scene.Admin.ProfileScopeId, CenterId = scene.Admin.CenterId.Value, UnitId = destination.Value, AccountId = scene.Admin.AccountId.Value });
        }
        var nurseDestination = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, scene.Admin.CenterId, destination, [ProfilePermissions.ExternalAppointmentManage]);
        Assert.Single((await ListAsync(scene.Nurse)).Value!.Appointments);
        Assert.Single((await ListAsync(scene.Doctor)).Value!.Appointments);
        Assert.Empty((await ListAsync(nurseDestination)).Value!.Appointments);

        var transfer = new ResidentTransferApplicationService(
            new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory), new FixedExternalSessionIdentityProvider(scene.Admin.ExternalSubject),
            new SqlResidentTransferRepository(TestDatabase.ConnectionFactory));
        Assert.True((await transfer.TransferAsync(new TransferResidentCommand(
            scene.Admin.ProfileScopeId, scene.Admin.CenterId, scene.ResidentId, scene.Admin.UnitId, destination, null, null, Guid.NewGuid()))).Ok);

        Assert.Empty((await ListAsync(scene.Nurse)).Value!.Appointments);
        Assert.Empty((await ListAsync(scene.Doctor)).Value!.Appointments);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(scene.Nurse).FindAsync(scene.Nurse.ProfileScopeId, scene.Nurse.CenterId, id)).Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await UpdateAsync(scene.Nurse, id, 1, Cita(days: 5, place: "Otro hospital"))).Error?.Code);
        var atDestination = (await ListAsync(nurseDestination)).Value!.Appointments;
        Assert.Equal(id, Assert.Single(atDestination).Id);
        var byAdmin = (await Service(scene.Admin).FindAsync(scene.Admin.ProfileScopeId, scene.Admin.CenterId, id)).Value!;
        Assert.Equal(destination, byAdmin.UnitId);
        Assert.Equal(1, byAdmin.Version);
    }

    [Fact]
    public async Task ElAvisoDeCitasSinCubrir_SoloAdministracion_SoloAbiertasDeLosProximosSieteDias_ConAlgoPendiente()
    {
        var scene = await SceneAsync("Residente Cita Aviso");
        var covered = Cita(days: 3, transport: ExternalTransportMode.Familia, transportState: ExternalArrangementState.Confirmado,
            companion: ExternalCompanionMode.NoNecesita, companionState: ExternalArrangementState.Confirmado);
        var urgent = await RegisterAsync(scene.Admin, scene.ResidentId, Cita(days: 1));
        var soon = await RegisterAsync(scene.Admin, scene.ResidentId, Cita(days: 5, transport: ExternalTransportMode.Centro, transportState: ExternalArrangementState.Confirmado));
        await RegisterAsync(scene.Admin, scene.ResidentId, covered);
        await RegisterAsync(scene.Admin, scene.ResidentId, Cita(days: 20));
        var cancelled = await RegisterAsync(scene.Admin, scene.ResidentId, Cita(days: 2));
        Assert.True((await CancelAsync(scene.Admin, cancelled, 1)).Ok);
        var done = await RegisterAsync(scene.Admin, scene.ResidentId, Cita(days: 0));
        Assert.True((await StatusAsync(scene.Admin, done, 1, ExternalAppointmentStatus.Realizada)).Ok);

        var uncovered = await Service(scene.Admin).ListUncoveredAsync(scene.Admin.ProfileScopeId, scene.Admin.CenterId);

        Assert.True(uncovered.Ok, uncovered.Error?.Message);
        Assert.Equal([urgent, soon], uncovered.Value!.Select(a => a.Id).ToArray());
        Assert.All(uncovered.Value!, a => Assert.False(a.Appointment.Covered));
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(scene.Nurse).ListUncoveredAsync(scene.Nurse.ProfileScopeId, scene.Nurse.CenterId)).Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(scene.Doctor).ListUncoveredAsync(scene.Doctor.ProfileScopeId, scene.Doctor.CenterId)).Error?.Code);
        var otherCenterAdmin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        Assert.Empty((await Service(otherCenterAdmin).ListUncoveredAsync(otherCenterAdmin.ProfileScopeId, otherCenterAdmin.CenterId)).Value!);
    }

    [Fact]
    public async Task RegistrarDosVecesConLaMismaOperacion_CreaUnaSolaCita_SinRepetirHistorialNiAuditoria()
    {
        // Decisión de Orlando (2026-11-18, script 0102): un doble clic o un reenvío del formulario no crea otra cita.
        var scene = await SceneAsync("Residente Cita Doble Clic");
        var other = await AdministracionResidentesTests.CreateResidentAsync(scene.Admin, "Residente Cita Doble Clic Otro");
        var operation = Guid.NewGuid();
        Task<ApplicationResult<Guid>> Register(SeededProfile by, ResidentId resident, Guid? op) =>
            Service(by).RegisterAsync(new RegisterExternalAppointmentCommand(by.ProfileScopeId, by.CenterId, resident, Cita(), op));

        var first = await Register(scene.Admin, scene.ResidentId, operation);
        var second = await Register(scene.Admin, scene.ResidentId, operation);

        Assert.True(first.Ok, first.Error?.Message);
        Assert.Equal(first.Value, second.Value);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas WHERE residente_id = @r", new { r = scene.ResidentId.Value }));
        Assert.Equal(1, await ChangesAsync(first.Value));
        Assert.Equal(1, await AuditAsync(first.Value, "EXT_APPOINTMENT_CREATE"));

        // El mismo identificador para otro residente: error genérico, y no se crea nada.
        var forged = await Register(scene.Admin, other, operation);
        Assert.Equal(ApplicationFailureCode.AccessDenied, forged.Error?.Code);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas WHERE residente_id = @r", new { r = other.Value }));

        // Sin identificador (código sin formulario) cada registro es una cita nueva; dos envíos a la vez con el mismo identificador, una sola.
        Assert.NotEqual((await Register(scene.Admin, scene.ResidentId, null)).Value, (await Register(scene.Admin, scene.ResidentId, null)).Value);
        var racing = Guid.NewGuid();
        var both = await Task.WhenAll(Register(scene.Nurse, scene.ResidentId, racing), Register(scene.Nurse, scene.ResidentId, racing));
        Assert.All(both, r => Assert.True(r.Ok, r.Error?.Message));
        Assert.Equal(both[0].Value, both[1].Value);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas WHERE operacion_id = @racing", new { racing }));

        // El identificador no se puede cambiar después.
        var failure = await SqlFailureAsync("UPDATE dbo.citas_externas SET operacion_id = NEWID(), version = version + 1 WHERE id = @id", new { id = first.Value });
        Assert.Contains("EXTERNAL_APPOINTMENT_IDENTITY_IMMUTABLE", failure.Message);
    }

    [Fact]
    public async Task LasCitasPorCerrar_SonLasAbiertasConElDiaPasado_DelAmbitoDeCadaPerfil_YElFiltroPorResidente()
    {
        // Decisión de Orlando (2026-11-18): una cita abierta cuyo día pasó sin marcarse realizada ni «no se acudió» sale como «por cerrar» (últimos 365 días); se puede cerrar después de su día.
        var scene = await SceneAsync("Residente Cita Por Cerrar");
        var other = await AdministracionResidentesTests.CreateResidentAsync(scene.Admin, "Residente Cita Por Cerrar Otro");
        var late = await RegisterAsync(scene.Admin, scene.ResidentId, Cita(1));
        var lateConfirmed = await RegisterAsync(scene.Admin, scene.ResidentId, Cita(1));
        Assert.True((await StatusAsync(scene.Admin, lateConfirmed, 1, ExternalAppointmentStatus.ConfirmadaHospital)).Ok);
        var closed = await RegisterAsync(scene.Admin, scene.ResidentId, Cita(1));
        var tooOld = await RegisterAsync(scene.Admin, scene.ResidentId, Cita(1));
        var future = await RegisterAsync(scene.Admin, scene.ResidentId, Cita(10));
        var lateOther = await RegisterAsync(scene.Admin, other, Cita(1));
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            foreach (var (id, days) in new[] { (late, -3), (lateConfirmed, -1), (closed, -3), (tooOld, -400), (lateOther, -2) })
            {
                await connection.ExecuteAsync("UPDATE dbo.citas_externas SET fecha = @date, version = version + 1 WHERE id = @id", new { id, date = InDays(days).ToDateTime(TimeOnly.MinValue) });
            }
        }

        Assert.True((await StatusAsync(scene.Admin, closed, 2, ExternalAppointmentStatus.Realizada)).Ok);
        foreach (var by in new[] { scene.Admin, scene.Nurse, scene.Doctor })
        {
            var overdue = await Service(by).ListOverdueAsync(by.ProfileScopeId, by.CenterId);
            Assert.True(overdue.Ok, overdue.Error?.Message);
            Assert.Equal(new[] { late, lateOther, lateConfirmed }.OrderBy(i => i).ToArray(), overdue.Value!.Select(a => a.Id).OrderBy(i => i).ToArray());
            Assert.DoesNotContain(overdue.Value!, a => a.Id == closed || a.Id == tooOld || a.Id == future);
        }
        var filtered = await Service(scene.Admin).ListOverdueAsync(scene.Admin.ProfileScopeId, scene.Admin.CenterId, scene.ResidentId);
        Assert.Equal(new[] { late, lateConfirmed }.OrderBy(i => i).ToArray(), filtered.Value!.Select(a => a.Id).OrderBy(i => i).ToArray());
        var otherCenterAdmin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        Assert.Empty((await Service(otherCenterAdmin).ListOverdueAsync(otherCenterAdmin.ProfileScopeId, otherCenterAdmin.CenterId)).Value!);

        // Después de su día todavía se puede cerrar.
        var closing = await StatusAsync(scene.Admin, late, 2, ExternalAppointmentStatus.NoAcudida);
        Assert.True(closing.Ok, closing.Error?.Message);
        Assert.DoesNotContain((await Service(scene.Admin).ListOverdueAsync(scene.Admin.ProfileScopeId, scene.Admin.CenterId)).Value!, a => a.Id == late);
    }

    [Fact]
    public async Task ElListadoPorResidente_SoloTraeLasCitasDeEsePersona()
    {
        var scene = await SceneAsync("Residente Cita Filtro Uno");
        var other = await AdministracionResidentesTests.CreateResidentAsync(scene.Admin, "Residente Cita Filtro Dos");
        var mine = await RegisterAsync(scene.Admin, scene.ResidentId);
        await RegisterAsync(scene.Admin, other);

        var filtered = await Service(scene.Admin).ListAsync(scene.Admin.ProfileScopeId, scene.Admin.CenterId, InDays(-1), InDays(30), scene.ResidentId);
        var all = await ListAsync(scene.Admin);

        Assert.Equal(mine, Assert.Single(filtered.Value!.Appointments).Id);
        Assert.Equal(2, all.Value!.Appointments.Count);
    }

    [Fact]
    public async Task ElUsuarioDeLaAplicacion_InsertaYActualizaLaCita_PeroNoBorra_YDelHistorialSoloInserta()
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var permissions = (await connection.QueryAsync<(string Table, string Permission, string State)>("""
            SELECT OBJECT_NAME(p.major_id), p.permission_name, p.state_desc FROM sys.database_permissions p
             WHERE p.class = 1 AND p.grantee_principal_id = DATABASE_PRINCIPAL_ID('residapp_aplicacion')
               AND OBJECT_NAME(p.major_id) IN ('citas_externas', 'citas_externas_cambios') AND p.minor_id = 0
            """)).ToList();

        Assert.Contains(("citas_externas", "INSERT", "GRANT"), permissions);
        Assert.Contains(("citas_externas", "UPDATE", "GRANT"), permissions);
        Assert.Contains(("citas_externas", "DELETE", "DENY"), permissions);
        Assert.Contains(("citas_externas_cambios", "INSERT", "GRANT"), permissions);
        Assert.Contains(("citas_externas_cambios", "UPDATE", "DENY"), permissions);
        Assert.Contains(("citas_externas_cambios", "DELETE", "DENY"), permissions);
    }
}

file sealed class FixedExternalSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
