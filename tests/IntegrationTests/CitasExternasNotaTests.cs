using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Appointments;
using ResidApp.Domain.Audit;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Citas externas de los residentes, PR 4 (script 0103; docs/producto/citas-externas-diseno.md, valor 17, y la lectura del DPO, apartado 7): la nota «al terminar» y la siguiente cita.
/// La escribe cualquier Enfermería con acceso al residente (sin el permiso de gestionar citas), solo en citas realizadas; la leen Enfermería y Medicina; Administración solo ve la siguiente cita
/// prevista de la versión vigente. Cada cambio es una versión nueva (no se edita ni se borra), «retirar» no borra, y la auditoría lleva solo identificadores. «Registrar la siguiente cita» guarda de
/// cuál viene. Los nombres de las pruebas son inventados.</summary>
public class CitasExternasNotaTests
{
    private static ExternalAppointmentsApplicationService Service(SeededProfile profile)
    {
        var scopes = new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory);
        return new(scopes, new FixedExternalSessionIdentityProvider(profile.ExternalSubject), new SqlExternalAppointmentRepository(TestDatabase.ConnectionFactory), TimeProvider.System);
    }

    private static DateOnly InDays(int days) => ResidApp.Domain.Modules.CenterModuleRules.TodayInMadrid(DateTimeOffset.UtcNow).AddDays(days);

    private static ExternalAppointment Cita(int days = 0, ExternalFamilyVisibility visibility = ExternalFamilyVisibility.Visible, ExternalAppointmentSpecialty specialty = ExternalAppointmentSpecialty.Cardiologia) =>
        new(InDays(days), new TimeOnly(10, 30), ExternalAppointmentType.Consulta, specialty, "Hospital Ficticio Norte", ExternalTransportMode.SinDeterminar,
            ExternalArrangementState.Pendiente, ExternalCompanionMode.SinDeterminar, ExternalArrangementState.Pendiente, null, visibility);

    /// <summary>Enfermería SIN el permiso de gestionar citas (Nurse) a propósito: la nota no lo exige.</summary>
    private sealed record Scene(SeededProfile Admin, SeededProfile Nurse, SeededProfile Doctor, ResidentId ResidentId);

    private static async Task<Scene> SceneAsync(string residentName)
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var doctor = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Medicina, admin.CenterId, admin.UnitId);
        return new Scene(admin, nurse, doctor, await AdministracionResidentesTests.CreateResidentAsync(admin, residentName));
    }

    /// <summary>Una cita de hoy, registrada por Administración y dada por realizada o no acudida.</summary>
    private static async Task<Guid> ClosedAsync(Scene scene, ExternalAppointmentStatus status = ExternalAppointmentStatus.Realizada, ExternalFamilyVisibility visibility = ExternalFamilyVisibility.Visible,
        ExternalAppointmentSpecialty specialty = ExternalAppointmentSpecialty.Cardiologia)
    {
        var registered = await Service(scene.Admin).RegisterAsync(new RegisterExternalAppointmentCommand(scene.Admin.ProfileScopeId, scene.Admin.CenterId, scene.ResidentId, Cita(0, visibility, specialty)));
        Assert.True(registered.Ok, registered.Error?.Message);
        var closed = await Service(scene.Admin).ChangeStatusAsync(new ChangeExternalAppointmentStatusCommand(scene.Admin.ProfileScopeId, scene.Admin.CenterId, registered.Value, 1, status));
        Assert.True(closed.Ok, closed.Error?.Message);
        return registered.Value;
    }

    private static ExternalAppointmentNote Note(ExternalNoteResult result = ExternalNoteResult.PendienteInforme, string? text = "Recoger el informe el viernes", DateOnly? nextDate = null, int? nextMonths = null) =>
        new(result, text, nextDate, nextMonths);

    private static Task<ApplicationResult<int>> SaveAsync(SeededProfile by, Guid id, int seen, ExternalAppointmentNote? note = null, Guid? operation = null) =>
        Service(by).SaveNoteAsync(new SaveExternalAppointmentNoteCommand(by.ProfileScopeId, by.CenterId, id, seen, note ?? Note(), operation));

    private static Task<ApplicationResult<int>> RetractAsync(SeededProfile by, Guid id, int seen, ExternalNoteRetractionReason? reason = ExternalNoteRetractionReason.ErrorDeRegistro) =>
        Service(by).RetractNoteAsync(new RetractExternalAppointmentNoteCommand(by.ProfileScopeId, by.CenterId, id, seen, reason));

    private static Task<ApplicationResult<ExternalNoteVersion?>> CurrentAsync(SeededProfile by, Guid id) =>
        Service(by).FindNoteAsync(by.ProfileScopeId, by.CenterId, id);

    private static Task<ApplicationResult<ExternalAppointmentFollowUp>> FollowUpAsync(SeededProfile by, Guid id) =>
        Service(by).FindFollowUpAsync(by.ProfileScopeId, by.CenterId, id);

    private static async Task<int> CountAsync(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }

    private static Task<int> AuditAsync(string action, string resourceType, Guid resourceId) =>
        CountAsync("SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE recurso_id = @resourceId AND accion_codigo = @action AND tipo_recurso = @resourceType", new { resourceId, action, resourceType });

    private static async Task<SqlException> SqlFailureAsync(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql, parameters));
    }

    [Fact]
    public async Task Enfermeria_SinElPermisoDeGestionar_AnotaLaNota_YLaLeenEnfermeriaYMedicina()
    {
        var scene = await SceneAsync("Residente Nota Uno");
        var id = await ClosedAsync(scene);

        var saved = await SaveAsync(scene.Nurse, id, 0, Note(ExternalNoteResult.CambioPauta, "Revisar la pauta el lunes"));

        Assert.True(saved.Ok, saved.Error?.Message);
        Assert.Equal(1, saved.Value);
        foreach (var reader in new[] { scene.Nurse, scene.Doctor })
        {
            var note = (await CurrentAsync(reader, id)).Value!;
            Assert.Equal(1, note.Number);
            Assert.Equal(ExternalNoteResult.CambioPauta, note.Result);
            Assert.Equal("Revisar la pauta el lunes", note.Text);
            Assert.False(note.Retracted);
        }
        Assert.Equal(1, await CountAsync("""
            SELECT COUNT(*) FROM dbo.citas_externas_notas WHERE cita_id = @id AND numero = 1 AND actor_cuenta_id = @account AND actor_perfil_codigo = 'ENFERMERIA' AND retirada = 0
            """, new { id, account = scene.Nurse.AccountId.Value }));
    }

    [Fact]
    public async Task LaNota_NoLaEscribenNiLaLeenAdministracionAuxiliarNiDireccion_NiLaEscribeMedicina()
    {
        var scene = await SceneAsync("Residente Nota Perfiles");
        var id = await ClosedAsync(scene);
        Assert.True((await SaveAsync(scene.Nurse, id, 0)).Ok);
        var auxiliar = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, scene.Admin.CenterId, scene.Admin.UnitId);
        var direction = await SeedFixture.AddProfileToCenterAsync(SystemProfile.DireccionClinica, scene.Admin.CenterId, scene.Admin.UnitId);

        foreach (var other in new[] { scene.Admin, auxiliar, direction })
        {
            Assert.Equal(ApplicationFailureCode.AccessDenied, (await CurrentAsync(other, id)).Error?.Code);
            Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(other).ListNoteVersionsAsync(other.ProfileScopeId, other.CenterId, id)).Error?.Code);
            Assert.Equal(ApplicationFailureCode.AccessDenied, (await SaveAsync(other, id, 1)).Error?.Code);
            Assert.Equal(ApplicationFailureCode.AccessDenied, (await RetractAsync(other, id, 1)).Error?.Code);
        }
        // Medicina la lee, pero no la escribe ni la retira.
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await SaveAsync(scene.Doctor, id, 1)).Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await RetractAsync(scene.Doctor, id, 1)).Error?.Code);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas_notas WHERE cita_id = @id", new { id }));
    }

    [Fact]
    public async Task SoloSeAnotaEnUnaCitaRealizada()
    {
        var scene = await SceneAsync("Residente Nota Estados");
        var open = (await Service(scene.Admin).RegisterAsync(new RegisterExternalAppointmentCommand(scene.Admin.ProfileScopeId, scene.Admin.CenterId, scene.ResidentId, Cita(3)))).Value;
        var missed = await ClosedAsync(scene, ExternalAppointmentStatus.NoAcudida);
        var cancelled = (await Service(scene.Admin).RegisterAsync(new RegisterExternalAppointmentCommand(scene.Admin.ProfileScopeId, scene.Admin.CenterId, scene.ResidentId, Cita(4)))).Value;
        Assert.True((await Service(scene.Admin).CancelAsync(new CancelExternalAppointmentCommand(scene.Admin.ProfileScopeId, scene.Admin.CenterId, cancelled, 1, ExternalAppointmentReasonCode.CambioDelHospital, null))).Ok);

        foreach (var id in new[] { open, missed, cancelled })
        {
            Assert.Equal(ApplicationFailureCode.InvalidInput, (await SaveAsync(scene.Nurse, id, 0)).Error?.Code);
        }
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas_notas WHERE residente_id = @r", new { r = scene.ResidentId.Value }));
    }

    [Fact]
    public async Task CadaCambioEsUnaVersionNueva_ConSuAutoria_YSiOtraPersonaAnotoAntesNoSePisa()
    {
        var scene = await SceneAsync("Residente Nota Versiones");
        var id = await ClosedAsync(scene);
        var secondNurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, scene.Admin.CenterId, scene.Admin.UnitId);

        Assert.Equal(1, (await SaveAsync(scene.Nurse, id, 0, Note(ExternalNoteResult.PendienteInforme, "Primera línea"))).Value);
        Assert.Equal(2, (await SaveAsync(secondNurse, id, 1, Note(ExternalNoteResult.SinCambios, null))).Value);
        // La primera persona ya no ve la vigente: guarda sobre la versión 1 y recibe el conflicto sin pisar nada.
        Assert.Equal(ApplicationFailureCode.Conflict, (await SaveAsync(scene.Nurse, id, 1, Note(ExternalNoteResult.Traslado, "Otra"))).Error?.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, (await SaveAsync(scene.Nurse, id, 0)).Error?.Code);

        var versions = (await Service(scene.Doctor).ListNoteVersionsAsync(scene.Doctor.ProfileScopeId, scene.Doctor.CenterId, id)).Value!;
        Assert.Equal([2, 1], versions.Select(v => v.Number));
        Assert.Equal(ExternalNoteResult.SinCambios, versions[0].Result);
        Assert.Null(versions[0].Text);
        Assert.Equal("Primera línea", versions[1].Text);
        Assert.All(versions, v => Assert.False(string.IsNullOrWhiteSpace(v.AuthorName)));
        Assert.Equal(ExternalNoteResult.SinCambios, (await CurrentAsync(scene.Nurse, id)).Value!.Result);

        // Cada apertura de «versiones anteriores» deja un evento por apertura, sin contenido, y cada versión guardada, el suyo; leer la vigente no deja ninguno.
        Assert.Equal(1, await AuditAsync("EXT_APPOINTMENT_NOTE_HIST_READ", "EXTERNAL_APPOINTMENT", id));
        Assert.True((await Service(scene.Nurse).ListNoteVersionsAsync(scene.Nurse.ProfileScopeId, scene.Nurse.CenterId, id)).Ok);
        Assert.Equal(2, await AuditAsync("EXT_APPOINTMENT_NOTE_HIST_READ", "EXTERNAL_APPOINTMENT", id));
        Assert.Equal(2, await CountAsync("""
            SELECT COUNT(*) FROM dbo.eventos_auditoria a WITH (NOLOCK) JOIN dbo.citas_externas_notas n ON n.id = a.recurso_id
             WHERE n.cita_id = @id AND a.accion_codigo = 'EXT_APPOINTMENT_NOTE' AND a.tipo_recurso = 'EXTERNAL_APPOINTMENT_NOTE' AND a.residente_id = @r
            """, new { id, r = scene.ResidentId.Value }));
    }

    [Fact]
    public async Task RetirarLaNota_NoBorraNada_LaDejaSinVigente_YSePuedeVolverAAnotar()
    {
        var scene = await SceneAsync("Residente Nota Retirar");
        var id = await ClosedAsync(scene);
        Assert.True((await SaveAsync(scene.Nurse, id, 0, Note(nextMonths: 6))).Ok);

        Assert.Equal(ApplicationFailureCode.InvalidInput, (await RetractAsync(scene.Nurse, id, 1, null)).Error?.Code);
        var retracted = await RetractAsync(scene.Nurse, id, 1, ExternalNoteRetractionReason.DatosDeOtraPersona);

        Assert.Equal(2, retracted.Value);
        Assert.True((await CurrentAsync(scene.Doctor, id)).Value!.Retracted); // la última versión es la retirada: ya no hay nota vigente, pero su número es el de la versión vista
        Assert.False((await FollowUpAsync(scene.Admin, id)).Value!.Planned.IsSet);
        var versions = (await Service(scene.Nurse).ListNoteVersionsAsync(scene.Nurse.ProfileScopeId, scene.Nurse.CenterId, id)).Value!;
        Assert.True(versions[0].Retracted);
        Assert.Equal(ExternalNoteRetractionReason.DatosDeOtraPersona, versions[0].RetractionReason);
        Assert.Null(versions[0].Result);
        Assert.Null(versions[0].Text);
        Assert.Equal("Recoger el informe el viernes", versions[1].Text); // La versión anterior se conserva.
        Assert.Equal(1, await CountAsync("""
            SELECT COUNT(*) FROM dbo.eventos_auditoria a WITH (NOLOCK) JOIN dbo.citas_externas_notas n ON n.id = a.recurso_id
             WHERE n.cita_id = @id AND n.numero = 2 AND a.accion_codigo = 'EXT_APPOINTMENT_NOTE_RETRACT' AND a.tipo_recurso = 'EXTERNAL_APPOINTMENT_NOTE'
            """, new { id }));

        // No se retira dos veces seguidas ni se retira lo que no hay; después de retirar se puede anotar otra versión.
        Assert.Equal(ApplicationFailureCode.InvalidInput, (await RetractAsync(scene.Nurse, id, 2)).Error?.Code);
        var empty = await ClosedAsync(scene);
        Assert.Equal(ApplicationFailureCode.InvalidInput, (await RetractAsync(scene.Nurse, empty, 0)).Error?.Code);
        Assert.Equal(3, (await SaveAsync(scene.Nurse, id, 2, Note(ExternalNoteResult.SinCambios, null))).Value);
        Assert.Equal(ExternalNoteResult.SinCambios, (await CurrentAsync(scene.Nurse, id)).Value!.Result);
    }

    [Fact]
    public async Task Administracion_SoloVeLaSiguienteCitaPrevistaDeLaVersionVigente_NuncaElResto()
    {
        var scene = await SceneAsync("Residente Nota Siguiente");
        var id = await ClosedAsync(scene);

        Assert.False((await FollowUpAsync(scene.Admin, id)).Value!.Planned.IsSet); // Sin nota, nada.

        Assert.True((await SaveAsync(scene.Nurse, id, 0, Note(ExternalNoteResult.NuevaCita, "Pedir cita", nextMonths: 6))).Ok);
        var months = (await FollowUpAsync(scene.Admin, id)).Value!.Planned;
        Assert.Equal(6, months.Months);
        Assert.Null(months.Date);
        Assert.Equal(InDays(0).AddMonths(6), months.Suggested(InDays(0)));

        var date = InDays(40);
        Assert.True((await SaveAsync(scene.Nurse, id, 1, Note(ExternalNoteResult.NuevaCita, null, nextDate: date))).Ok);
        var planned = (await FollowUpAsync(scene.Admin, id)).Value!.Planned;
        Assert.Equal(date, planned.Date);
        Assert.Null(planned.Months);

        // Una versión nueva sin siguiente cita la quita: solo cuenta la vigente.
        Assert.True((await SaveAsync(scene.Nurse, id, 2, Note(ExternalNoteResult.SinCambios, null))).Ok);
        Assert.False((await FollowUpAsync(scene.Admin, id)).Value!.Planned.IsSet);

        // Lo que Administración recibe no tiene el resultado, la línea ni el autor: el modelo no los declara.
        var properties = typeof(ExternalAppointmentFollowUp).GetProperties().Select(p => p.Name).Order().ToArray();
        Assert.Equal(["Planned", "RegisteredDate", "RegisteredId"], properties);
        Assert.Equal(["Date", "IsSet", "Months"], typeof(ExternalNextVisit).GetProperties().Where(p => p.Name != "None").Select(p => p.Name).Order().ToArray());
    }


    [Fact]
    public void LaAuditoriaDeLaNota_NoSeMuestraAAdministracion_PorqueDelataQueExisteNota()
    {
        // DPO, segunda lectura, apartado a: si «nota anotada» saliera en la auditoría que ve Administración, sabría que hay nota. La lista es cerrada: estas acciones no se añaden.
        var visible = AdministrativeAudit.Actions.Select(a => a.Code).ToHashSet();

        foreach (var code in new[] { "EXT_APPOINTMENT_NOTE", "EXT_APPOINTMENT_NOTE_RETRACT", "EXT_APPOINTMENT_NOTE_HIST_READ" })
        {
            Assert.DoesNotContain(code, visible);
            Assert.True(code.Length <= 32); // eventos_auditoria.accion_codigo es NVARCHAR(32)
        }
    }
    [Fact]
    public void LaNota_RechazaLoQueNoEsValido()
    {
        Assert.Throws<DomainValidationException>(() => Note(nextDate: InDays(10), nextMonths: 3)); // fecha y plazo a la vez
        Assert.Throws<DomainValidationException>(() => Note(nextMonths: 0));
        Assert.Throws<DomainValidationException>(() => Note(nextMonths: 37));
        Assert.Throws<DomainValidationException>(() => Note(text: new string('a', 151)));
        Assert.Throws<DomainValidationException>(() => Note(text: "dos\nlíneas"));
        Assert.Throws<DomainValidationException>(() => Note((ExternalNoteResult)99));
        Assert.Throws<DomainValidationException>(() => Note(nextDate: InDays(0)).RequireNextAfter(InDays(0))); // no es posterior a la cita
        Assert.Equal(150, Note(text: new string('a', 150)).Text!.Length);
        Assert.Null(Note(text: "   ").Text);
    }

    [Fact]
    public async Task LaSiguienteCitaPrevistaDebeSerPosteriorAlDiaDeLaCita_AunqueSeGuardeDirectoEnLaBase()
    {
        var scene = await SceneAsync("Residente Nota Fecha");
        var id = await ClosedAsync(scene);

        Assert.Equal(ApplicationFailureCode.InvalidInput, (await SaveAsync(scene.Nurse, id, 0, Note(nextDate: InDays(0)))).Error?.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, (await SaveAsync(scene.Nurse, id, 0, Note(nextDate: InDays(-3)))).Error?.Code);
        Assert.Contains("EXTERNAL_APPOINTMENT_NOTE_INVALID", (await SqlFailureAsync(InsertNote("1", "'SIN_CAMBIOS'", "NULL", "CAST(SYSUTCDATETIME() AS DATE)", "NULL", "NULL"),
            DirectParameters(scene, id))).Message);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas_notas WHERE cita_id = @id", new { id }));
    }

    [Fact]
    public async Task EnfermeriaDeOtraUnidadOOtroCentro_NiLeeNiEscribeLaNota()
    {
        var scene = await SceneAsync("Residente Nota Unidades");
        var id = await ClosedAsync(scene);
        Assert.True((await SaveAsync(scene.Nurse, id, 0)).Ok);
        var otherUnit = await AdministracionResidentesTests.AddUnitAsync(scene.Admin.CenterId);
        var otherUnitNurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, scene.Admin.CenterId, otherUnit);
        var otherUnitDoctor = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Medicina, scene.Admin.CenterId, otherUnit);
        var otherCenterNurse = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);

        foreach (var outsider in new[] { otherUnitNurse, otherUnitDoctor, otherCenterNurse })
        {
            Assert.Equal(ApplicationFailureCode.AccessDenied, (await CurrentAsync(outsider, id)).Error?.Code);
            Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(outsider).ListNoteVersionsAsync(outsider.ProfileScopeId, outsider.CenterId, id)).Error?.Code);
            Assert.Equal(ApplicationFailureCode.AccessDenied, (await SaveAsync(outsider, id, 1)).Error?.Code);
            Assert.Equal(ApplicationFailureCode.AccessDenied, (await RetractAsync(outsider, id, 1)).Error?.Code);
            Assert.Equal(ApplicationFailureCode.AccessDenied, (await FollowUpAsync(outsider, id)).Error?.Code);
        }
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas_notas WHERE cita_id = @id", new { id }));
        Assert.Equal(0, await AuditAsync("EXT_APPOINTMENT_NOTE_HIST_READ", "EXTERNAL_APPOINTMENT", id));
    }

    [Fact]
    public async Task LaBajaDelResidente_ImpideLeerYEscribirLaNotaDesdeLaAplicacion_PeroLaHistoriaSeConserva()
    {
        var scene = await SceneAsync("Residente Nota Traslado");
        var id = await ClosedAsync(scene);
        Assert.True((await SaveAsync(scene.Nurse, id, 0)).Ok);
        var discharge = await EstadoResidenteTests.Build(scene.Admin.ExternalSubject).DischargeAsync(new DischargeResidentCommand(
            scene.Admin.ProfileScopeId, scene.Admin.CenterId, scene.ResidentId, Guid.NewGuid(), ResidentDischargeReason.AltaVoluntaria, "Vuelve con su familia."));
        Assert.True(discharge.Ok, discharge.Error?.Message);

        // Tras la baja ya no está en la unidad de nadie ni activo: la nota ya no se lee ni se cambia desde las pantallas (la historia sigue guardada en la base).
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await CurrentAsync(scene.Nurse, id)).Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await SaveAsync(scene.Nurse, id, 1)).Error?.Code);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas_notas WHERE cita_id = @id", new { id }));
    }

    [Fact]
    public async Task ConElResidenteSuspendido_SePuedeAnotar_PorqueSigueActivo()
    {
        var scene = await SceneAsync("Residente Nota Suspendido");
        var id = await ClosedAsync(scene);
        var suspended = await EstadoResidenteTests.Build(scene.Admin.ExternalSubject).SuspendAsync(
            new SuspendResidentCommand(scene.Admin.ProfileScopeId, scene.Admin.CenterId, scene.ResidentId, Guid.NewGuid(), null));
        Assert.True(suspended.Ok, suspended.Error?.Message);

        Assert.True((await SaveAsync(scene.Nurse, id, 0)).Ok);
    }

    [Fact]
    public async Task UnReenvioDelMismoFormulario_NoCreaOtraVersion_NiOtraAuditoria()
    {
        var scene = await SceneAsync("Residente Nota Doble Clic");
        var id = await ClosedAsync(scene);
        var other = await ClosedAsync(scene);
        var operation = Guid.NewGuid();

        var first = await SaveAsync(scene.Nurse, id, 0, Note(), operation);
        var second = await SaveAsync(scene.Nurse, id, 0, Note(), operation);
        var both = await Task.WhenAll(SaveAsync(scene.Nurse, other, 0, Note(), Guid.NewGuid()), SaveAsync(scene.Nurse, other, 0, Note(), Guid.NewGuid()));

        Assert.Equal(1, first.Value);
        Assert.True(second.Ok, second.Error?.Message);
        Assert.Equal(1, second.Value);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas_notas WHERE cita_id = @id", new { id }));
        // Con identificadores distintos y a la vez, una sola gana y la otra recibe el conflicto.
        Assert.Single(both, r => r.Ok);
        Assert.Single(both, r => r.Error?.Code == ApplicationFailureCode.Conflict);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas_notas WHERE cita_id = @id", new { id = other }));
        // El identificador de una cita no vale para otra: error genérico y nada se crea.
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await SaveAsync(scene.Nurse, other, 1, Note(), operation)).Error?.Code);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas_notas WHERE cita_id = @id", new { id = other }));
    }

    private static string InsertNote(string number, string result, string text, string nextDate, string nextMonths, string retracted) => $"""
        INSERT INTO dbo.citas_externas_notas (id, centro_id, cita_id, residente_id, numero, retirada, resultado_codigo, texto, siguiente_fecha, siguiente_meses, motivo_retirada_codigo,
                                              actor_cuenta_id, actor_perfil_codigo, ocurrido_en)
        VALUES (NEWID(), @center, @id, @resident, {number}, {(retracted == "NULL" ? "0" : retracted)}, {result}, {text}, {nextDate}, {nextMonths}, NULL, @account, 'ENFERMERIA', SYSUTCDATETIME())
        """;

    private static object DirectParameters(Scene scene, Guid id) =>
        new { center = scene.Admin.CenterId.Value, id, resident = scene.ResidentId.Value, account = scene.Nurse.AccountId.Value };

    [Fact]
    public async Task LaBaseImponeLasVersiones_LaInmutabilidad_YLaCoherenciaDeLaNota()
    {
        var scene = await SceneAsync("Residente Nota Base");
        var id = await ClosedAsync(scene);
        var p = DirectParameters(scene, id);
        var open = (await Service(scene.Admin).RegisterAsync(new RegisterExternalAppointmentCommand(scene.Admin.ProfileScopeId, scene.Admin.CenterId, scene.ResidentId, Cita(5)))).Value;

        // La primera versión es la 1 (no la 2) y no puede ser una retirada.
        Assert.Contains("EXTERNAL_APPOINTMENT_NOTE_INVALID", (await SqlFailureAsync(InsertNote("2", "'SIN_CAMBIOS'", "NULL", "NULL", "NULL", "NULL"), p)).Message);
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            var retractedFirst = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("""
                INSERT INTO dbo.citas_externas_notas (id, centro_id, cita_id, residente_id, numero, retirada, motivo_retirada_codigo, actor_cuenta_id, actor_perfil_codigo, ocurrido_en)
                VALUES (NEWID(), @center, @id, @resident, 1, 1, 'OTRO', @account, 'ENFERMERIA', SYSUTCDATETIME())
                """, p));
            Assert.Contains("EXTERNAL_APPOINTMENT_NOTE_INVALID", retractedFirst.Message);
        }
        // Una cita abierta no admite nota, aunque se escriba directo.
        Assert.Contains("EXTERNAL_APPOINTMENT_NOTE_INVALID", (await SqlFailureAsync(InsertNote("1", "'SIN_CAMBIOS'", "NULL", "NULL", "NULL", "NULL"),
            new { center = scene.Admin.CenterId.Value, id = open, resident = scene.ResidentId.Value, account = scene.Nurse.AccountId.Value })).Message);

        // La línea: una sola, sin saltos de línea ni tabuladores ni separadores Unicode, de hasta 150.
        foreach (var bad in new[] { "N'a' + NCHAR(10) + N'b'", "N'a' + NCHAR(13)", "N'a' + NCHAR(9)", "N'a' + NCHAR(8232)", "N'a' + NCHAR(133)", "N'   '" })
        {
            Assert.Contains("CK_cextn_texto", (await SqlFailureAsync(InsertNote("1", "'SIN_CAMBIOS'", bad, "NULL", "NULL", "NULL"), p)).Message);
        }
        Assert.Contains("CK_cextn_resultado", (await SqlFailureAsync(InsertNote("1", "'OTRO_VALOR'", "NULL", "NULL", "NULL", "NULL"), p)).Message);
        Assert.Contains("CK_cextn_siguiente_excluyente", (await SqlFailureAsync(InsertNote("1", "'SIN_CAMBIOS'", "NULL", "DATEADD(DAY, 30, CAST(SYSUTCDATETIME() AS DATE))", "6", "NULL"), p)).Message);
        Assert.Contains("CK_cextn_siguiente_meses", (await SqlFailureAsync(InsertNote("1", "'SIN_CAMBIOS'", "NULL", "NULL", "37", "NULL"), p)).Message);
        Assert.Contains("CK_cextn_perfil", (await SqlFailureAsync(InsertNote("1", "'SIN_CAMBIOS'", "NULL", "NULL", "NULL", "NULL").Replace("'ENFERMERIA'", "'MEDICINA'"), p)).Message);

        // Dos retiradas seguidas no; la inmutabilidad: ni se edita ni se borra.
        Assert.True((await SaveAsync(scene.Nurse, id, 0)).Ok);
        Assert.True((await RetractAsync(scene.Nurse, id, 1)).Ok);
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            var twice = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("""
                INSERT INTO dbo.citas_externas_notas (id, centro_id, cita_id, residente_id, numero, retirada, motivo_retirada_codigo, actor_cuenta_id, actor_perfil_codigo, ocurrido_en)
                VALUES (NEWID(), @center, @id, @resident, 3, 1, 'OTRO', @account, 'ENFERMERIA', SYSUTCDATETIME())
                """, p));
            Assert.Contains("EXTERNAL_APPOINTMENT_NOTE_INVALID", twice.Message);
        }
        Assert.Contains("EXTERNAL_APPOINTMENT_NOTE_IMMUTABLE", (await SqlFailureAsync("UPDATE dbo.citas_externas_notas SET texto = N'x' WHERE cita_id = @id", p)).Message);
        Assert.Contains("EXTERNAL_APPOINTMENT_NOTE_IMMUTABLE", (await SqlFailureAsync("DELETE FROM dbo.citas_externas_notas WHERE cita_id = @id", p)).Message);
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas_notas WHERE cita_id = @id", p));
    }

    [Fact]
    public async Task ElUsuarioDeLaAplicacion_SoloInsertaLasVersionesDeLaNota()
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var grants = (await connection.QueryAsync<string>("""
            SELECT p.permission_name + N':' + p.state_desc FROM sys.database_permissions p
             WHERE p.class = 1 AND p.major_id = OBJECT_ID(N'dbo.citas_externas_notas') AND p.grantee_principal_id = DATABASE_PRINCIPAL_ID(N'residapp_aplicacion')
               AND p.permission_name IN (N'INSERT', N'UPDATE', N'DELETE')
            """)).ToHashSet();

        Assert.Contains("INSERT:GRANT", grants);
        Assert.Contains("UPDATE:DENY", grants);
        Assert.Contains("DELETE:DENY", grants);
        Assert.DoesNotContain("UPDATE:GRANT", grants);
    }

    // ---- «Registrar la siguiente cita» (cita_origen_id) ----

    private static Task<ApplicationResult<Guid>> RegisterNextAsync(SeededProfile by, ResidentId resident, Guid? origin, ExternalAppointment? cita = null, Guid? operation = null) =>
        Service(by).RegisterAsync(new RegisterExternalAppointmentCommand(by.ProfileScopeId, by.CenterId, resident, cita ?? Cita(30), operation, origin));

    [Fact]
    public async Task LaSiguienteCita_GuardaDeCualViene_ElSeguimientoLaMuestra_YUnaCanceladaNoCuenta()
    {
        var scene = await SceneAsync("Residente Nota Origen");
        var origin = await ClosedAsync(scene);
        var missed = await ClosedAsync(scene, ExternalAppointmentStatus.NoAcudida);
        Assert.Null((await FollowUpAsync(scene.Admin, origin)).Value!.RegisteredId);

        var next = await RegisterNextAsync(scene.Admin, scene.ResidentId, origin);
        var afterMiss = await RegisterNextAsync(scene.Admin, scene.ResidentId, missed);

        Assert.True(next.Ok, next.Error?.Message);
        Assert.True(afterMiss.Ok, afterMiss.Error?.Message);
        Assert.Equal(origin, await ScalarGuidAsync("SELECT cita_origen_id FROM dbo.citas_externas WHERE id = @id", next.Value));
        foreach (var viewer in new[] { scene.Admin, scene.Nurse, scene.Doctor })
        {
            var followUp = (await FollowUpAsync(viewer, origin)).Value!;
            Assert.Equal(next.Value, followUp.RegisteredId);
            Assert.Equal(InDays(30), followUp.RegisteredDate);
        }

        // Si la siguiente se cancela, ya no cuenta como registrada y se puede registrar otra.
        Assert.True((await Service(scene.Admin).CancelAsync(new CancelExternalAppointmentCommand(
            scene.Admin.ProfileScopeId, scene.Admin.CenterId, next.Value, 1, ExternalAppointmentReasonCode.CambioDelHospital, null))).Ok);
        Assert.Null((await FollowUpAsync(scene.Admin, origin)).Value!.RegisteredId);
        Assert.True((await RegisterNextAsync(scene.Admin, scene.ResidentId, origin, Cita(45))).Ok);
    }

    [Fact]
    public async Task LaSiguienteCita_ExigeUnOrigenRealizadoONoAcudido_DelMismoResidenteYDelAmbitoDeQuienRegistra()
    {
        var scene = await SceneAsync("Residente Nota Origen Reglas");
        var other = await AdministracionResidentesTests.CreateResidentAsync(scene.Admin, "Residente Nota Origen Otro");
        var origin = await ClosedAsync(scene);
        var openOrigin = (await Service(scene.Admin).RegisterAsync(new RegisterExternalAppointmentCommand(scene.Admin.ProfileScopeId, scene.Admin.CenterId, scene.ResidentId, Cita(5)))).Value;
        var otherCenterAdmin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var otherCenterResident = await AdministracionResidentesTests.CreateResidentAsync(otherCenterAdmin, "Residente Nota Origen Fuera");

        Assert.Equal(ApplicationFailureCode.InvalidInput, (await RegisterNextAsync(scene.Admin, scene.ResidentId, openOrigin)).Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await RegisterNextAsync(scene.Admin, other, origin)).Error?.Code); // la cita es de otro residente
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await RegisterNextAsync(scene.Admin, scene.ResidentId, Guid.NewGuid())).Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await RegisterNextAsync(otherCenterAdmin, otherCenterResident, origin)).Error?.Code); // la de otro centro
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await RegisterNextAsync(scene.Doctor, scene.ResidentId, origin)).Error?.Code); // Medicina no registra
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas WHERE cita_origen_id IS NOT NULL AND residente_id IN (@a, @b)", new { a = scene.ResidentId.Value, b = other.Value }));
    }


    [Fact]
    public async Task UnaCitaDeOrigenSoloTieneUnaSiguienteViva_ConDosPersonasALaVezOUnInsertDirecto()
    {
        var scene = await SceneAsync("Residente Nota Origen Unica");
        var origin = await ClosedAsync(scene);
        var secondAdmin = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, scene.Admin.CenterId, scene.Admin.UnitId);

        var first = await RegisterNextAsync(scene.Admin, scene.ResidentId, origin);
        var second = await RegisterNextAsync(secondAdmin, scene.ResidentId, origin, Cita(40));

        Assert.True(first.Ok, first.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, second.Error?.Code);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas WHERE cita_origen_id = @origin", new { origin }));

        // A la vez, sobre otra cita de origen: una se crea y la otra no.
        var other = await ClosedAsync(scene);
        var both = await Task.WhenAll(RegisterNextAsync(scene.Admin, scene.ResidentId, other), RegisterNextAsync(secondAdmin, scene.ResidentId, other, Cita(41)));
        Assert.Single(both, r => r.Ok);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas WHERE cita_origen_id = @origin", new { origin = other }));

        // Directo en la base, el índice único lo impide.
        var failure = await SqlFailureAsync("""
            INSERT INTO dbo.citas_externas (id, centro_id, residente_id, fecha, tipo_codigo, creada_por_cuenta_id, creada_por_perfil, creada_en, modificada_en, cita_origen_id)
            VALUES (NEWID(), @center, @resident, DATEADD(DAY, 9, CAST(SYSUTCDATETIME() AS DATE)), 'CONSULTA', @account, 'ADMINISTRACION', SYSUTCDATETIME(), SYSUTCDATETIME(), @origin)
            """, new { center = scene.Admin.CenterId.Value, resident = scene.ResidentId.Value, account = scene.Admin.AccountId.Value, origin });
        Assert.Contains("UX_cext_origen_viva", failure.Message);
    }

    [Fact]
    public async Task LosLimitesDelPlazo_SeAceptan_UnMesYTreintaYSeisMeses_YElDiaSiguienteALaCita()
    {
        var scene = await SceneAsync("Residente Nota Limites");
        var id = await ClosedAsync(scene);

        Assert.Equal(1, (await SaveAsync(scene.Nurse, id, 0, Note(nextMonths: 1))).Value);
        Assert.Equal(2, (await SaveAsync(scene.Nurse, id, 1, Note(nextMonths: 36))).Value);
        Assert.Equal(3, (await SaveAsync(scene.Nurse, id, 2, Note(nextDate: InDays(1)))).Value);
        Assert.Equal(InDays(1), (await FollowUpAsync(scene.Admin, id)).Value!.Planned.Date);
        // Fin de mes: sumar meses recorta al último día del mes de llegada (31/08 + 6 meses es el último día de febrero).
        Assert.Equal(new DateOnly(2027, 2, 28), new ExternalNextVisit(null, 6).Suggested(new DateOnly(2026, 8, 31)));
        Assert.Equal(new DateOnly(2028, 2, 29), new ExternalNextVisit(null, 6).Suggested(new DateOnly(2027, 8, 31)));
    }
    [Fact]
    public async Task LaSiguienteCita_CopiaLaVisibilidadQueElFormularioLleve_YUnReenvioNoLaDuplica()
    {
        var scene = await SceneAsync("Residente Nota Origen Visibilidad");
        var origin = await ClosedAsync(scene, visibility: ExternalFamilyVisibility.Oculta, specialty: ExternalAppointmentSpecialty.SaludMental);
        var operation = Guid.NewGuid();

        var first = await RegisterNextAsync(scene.Admin, scene.ResidentId, origin, Cita(30, ExternalFamilyVisibility.Oculta, ExternalAppointmentSpecialty.SaludMental), operation);
        var second = await RegisterNextAsync(scene.Admin, scene.ResidentId, origin, Cita(30, ExternalFamilyVisibility.Oculta, ExternalAppointmentSpecialty.SaludMental), operation);

        Assert.Equal(first.Value, second.Value);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas WHERE cita_origen_id = @origin", new { origin }));
        Assert.Equal("OCULTA", await ScalarStringAsync("SELECT visibilidad_familia_codigo FROM dbo.citas_externas WHERE id = @id", first.Value));
    }

    [Fact]
    public async Task LaBaseImponeElOrigenDeLaSiguienteCita_YNoSePuedeCambiarDespues()
    {
        var scene = await SceneAsync("Residente Nota Origen Base");
        var origin = await ClosedAsync(scene);
        var open = (await Service(scene.Admin).RegisterAsync(new RegisterExternalAppointmentCommand(scene.Admin.ProfileScopeId, scene.Admin.CenterId, scene.ResidentId, Cita(5)))).Value;
        var next = (await RegisterNextAsync(scene.Admin, scene.ResidentId, origin)).Value;
        const string insert = """
            INSERT INTO dbo.citas_externas (id, centro_id, residente_id, fecha, tipo_codigo, creada_por_cuenta_id, creada_por_perfil, creada_en, modificada_en, cita_origen_id)
            VALUES (NEWID(), @center, @resident, DATEADD(DAY, 9, CAST(SYSUTCDATETIME() AS DATE)), 'CONSULTA', @account, 'ADMINISTRACION', SYSUTCDATETIME(), SYSUTCDATETIME(), @origin)
            """;
        var parameters = (Guid originId) => new { center = scene.Admin.CenterId.Value, resident = scene.ResidentId.Value, account = scene.Admin.AccountId.Value, origin = originId };

        Assert.Contains("EXTERNAL_APPOINTMENT_ORIGIN_INVALID", (await SqlFailureAsync(insert, parameters(open))).Message); // origen abierto
        Assert.Contains("FK_cext_origen", (await SqlFailureAsync(insert, parameters(Guid.NewGuid()))).Message); // origen inexistente
        Assert.Contains("EXTERNAL_APPOINTMENT_IDENTITY_IMMUTABLE", (await SqlFailureAsync(
            "UPDATE dbo.citas_externas SET cita_origen_id = NULL, version = version + 1 WHERE id = @id", new { id = next })).Message);
    }

    private static async Task<Guid?> ScalarGuidAsync(string sql, Guid id)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<Guid?>(sql, new { id });
    }

    private static async Task<string?> ScalarStringAsync(string sql, Guid id)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<string?>(sql, new { id });
    }
}

file sealed class FixedExternalSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
