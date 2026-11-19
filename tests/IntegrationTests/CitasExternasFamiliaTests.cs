using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Appointments;
using ResidApp.Domain.Families;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.AdministracionResidentesTests;
using static ResidApp.IntegrationTests.PortalFamiliarTests;

namespace ResidApp.IntegrationTests;

/// <summary>Citas externas, PR 5, la familia solo ve (script 0104; docs/tareas/alta-prioridad/pendientes-citas-externas.md, apartado 10). La familia ve una cita solo si el centro está encendido, el
/// residente no está restringido y la cita no es oculta; el nivel «sin especialidad» no deja salir la especialidad de la base; el interruptor del centro lo cambia solo Administración; el del residente,
/// Administración y la Enfermería de su unidad (sin el permiso de gestionar citas); ningún cambio pisa a otro (número visto); el historial es de solo añadir; y la auditoría lleva solo identificadores.
/// Los datos de las pruebas son inventados.</summary>
public class CitasExternasFamiliaTests
{
    private static SqlProfileScopeDirectoryProvider Scopes => new(TestDatabase.ConnectionFactory);

    private static ExternalAppointmentsApplicationService Staff(SeededProfile by) =>
        new(Scopes, new FixedFamilySessionIdentityProvider(by.ExternalSubject), new SqlExternalAppointmentRepository(TestDatabase.ConnectionFactory), TimeProvider.System);

    private static ExternalAppointmentFamilySettingsApplicationService SettingsFor(string subject) =>
        new(Scopes, new FixedFamilySessionIdentityProvider(subject), new SqlExternalAppointmentRepository(TestDatabase.ConnectionFactory), TimeProvider.System);

    private static ExternalAppointmentFamilySettingsApplicationService Settings(SeededProfile by) => SettingsFor(by.ExternalSubject);

    private static FamilyExternalAppointmentsApplicationService Family(string subject) =>
        new(Scopes, new FixedFamilySessionIdentityProvider(subject), new SqlFamilyExternalAppointments(TestDatabase.ConnectionFactory), TimeProvider.System);

    private static DateOnly InDays(int days) => ResidApp.Domain.Modules.CenterModuleRules.TodayInMadrid(DateTimeOffset.UtcNow).AddDays(days);

    private static ExternalAppointment Cita(
        int days = 5, ExternalFamilyVisibility visibility = ExternalFamilyVisibility.Visible, ExternalAppointmentType type = ExternalAppointmentType.Consulta,
        ExternalAppointmentSpecialty? specialty = ExternalAppointmentSpecialty.Cardiologia, string? place = "Hospital Ficticio Norte", string? prepText = null) =>
        new(InDays(days), new TimeOnly(10, 30), type, specialty, place, ExternalTransportMode.SinDeterminar, ExternalArrangementState.Pendiente,
            ExternalCompanionMode.SinDeterminar, ExternalArrangementState.Pendiente, prepText is null ? null : ExternalPreparation.None with { Text = prepText }, visibility);

    private static async Task<Guid> RegisterAsync(Setup setup, ExternalAppointment? cita = null, ResidentId? residentId = null)
    {
        var registered = await Staff(setup.Admin).RegisterAsync(new RegisterExternalAppointmentCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, residentId ?? setup.ResidentId, cita ?? Cita()));
        Assert.True(registered.Ok, registered.Error?.Message);
        return registered.Value;
    }

    private static async Task<int> OpenCenterAsync(Setup setup, int seen = 0)
    {
        var result = await Settings(setup.Admin).SetCenterAsync(new SetExternalFamilyCenterCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, seen, true, true, true, true));
        Assert.True(result.Ok, result.Error?.Message);
        return result.Value;
    }

    private static async Task<int> CloseCenterAsync(Setup setup, int seen)
    {
        var result = await Settings(setup.Admin).SetCenterAsync(new SetExternalFamilyCenterCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, seen, false, false, false, false));
        Assert.True(result.Ok, result.Error?.Message);
        return result.Value;
    }

    private static Task<ApplicationResult<int>> RestrictAsync(SeededProfile by, Setup setup, int seen, bool restrict, ResidentId? residentId = null) =>
        Settings(by).SetResidentAsync(new SetExternalFamilyResidentCommand(by.ProfileScopeId, by.CenterId, residentId ?? setup.ResidentId, seen, restrict));

    private static async Task<ApplicationResult<FamilyExternalAppointments>> ListAsync(Setup setup) =>
        await Family(setup.Subject).ListAsync(await QueryAsync(setup.Subject), setup.LinkId);

    private static async Task<IReadOnlyList<ExternalAppointmentForFamily>> VisibleAsync(Setup setup)
    {
        var list = await ListAsync(setup);
        Assert.True(list.Ok, list.Error?.Message);
        return list.Value!.Appointments;
    }

    private static async Task<bool> AvailableAsync(Setup setup)
    {
        var result = await Family(setup.Subject).IsAvailableAsync(await QueryAsync(setup.Subject), setup.LinkId);
        Assert.True(result.Ok, result.Error?.Message);
        return result.Value;
    }

    private static async Task<ApplicationResult<(FamilyPortalResident Resident, ExternalAppointmentForFamily Appointment)>> FindAsync(Setup setup, Guid appointmentId) =>
        await Family(setup.Subject).FindAsync(await QueryAsync(setup.Subject), setup.LinkId, appointmentId);

    private static async Task<int> CountAsync(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }

    private static async Task<SqlException> SqlFailureAsync(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql, parameters));
    }

    [Fact]
    public async Task ConElCentroApagado_LaFamiliaNoVeNada_NiElEnlace_NiLaCita_NiPorLaDireccionDirecta()
    {
        var setup = await SetupAsync("Residente Familia Apagado");
        var id = await RegisterAsync(setup);

        Assert.False(await AvailableAsync(setup));
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await ListAsync(setup)).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await FindAsync(setup, id)).Error!.Code);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas_familia_centro WHERE centro_id = @c", new { c = setup.Admin.CenterId.Value }));
    }

    [Fact]
    public async Task EncenderExigeLasTresConfirmaciones_YApagarNoPideNinguna()
    {
        var setup = await SetupAsync("Residente Familia Confirmaciones");
        var admin = Settings(setup.Admin);

        foreach (var (informed, will, impact) in new[] { (false, true, true), (true, false, true), (true, true, false), (false, false, false) })
        {
            var refused = await admin.SetCenterAsync(new SetExternalFamilyCenterCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, 0, true, informed, will, impact));
            Assert.Equal(ApplicationFailureCode.InvalidInput, refused.Error?.Code);
        }
        Assert.False((await admin.ReadCenterAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId)).Value!.Enabled);

        Assert.Equal(1, await OpenCenterAsync(setup));
        Assert.Equal(2, await CloseCenterAsync(setup, 1));
        var state = (await admin.ReadCenterAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId)).Value!;
        Assert.False(state.Enabled);
        Assert.Equal(2, state.Number);
    }

    [Fact]
    public async Task CadaNivel_SeVeComoToca_YLaEspecialidadSinEspecialidadNoSaleDeLaBase()
    {
        var setup = await SetupAsync("Residente Familia Niveles");
        var visible = await RegisterAsync(setup, Cita(3, ExternalFamilyVisibility.Visible, specialty: ExternalAppointmentSpecialty.Oncologia, place: "Hospital Visible", prepText: "Llevar el abrigo"));
        var noSpecialty = await RegisterAsync(setup, Cita(4, ExternalFamilyVisibility.SinEspecialidad, specialty: ExternalAppointmentSpecialty.SaludMental, place: "Hospital Sin Especialidad"));
        var hidden = await RegisterAsync(setup, Cita(5, ExternalFamilyVisibility.Oculta, specialty: ExternalAppointmentSpecialty.Paliativos, place: "Lugar Reconocible Oculto"));
        await OpenCenterAsync(setup);

        var seen = await VisibleAsync(setup);
        Assert.Equal([visible, noSpecialty], seen.Select(a => a.Id));
        var first = seen[0];
        Assert.Equal(ExternalAppointmentSpecialty.Oncologia, first.Specialty);
        Assert.Equal("Hospital Visible", first.Place);
        Assert.Equal("Llevar el abrigo", first.Preparation.Text);
        var second = seen[1];
        Assert.Null(second.Specialty); // redactada en la consulta, no oculta en pantalla
        Assert.Equal("Hospital Sin Especialidad", second.Place);

        // Una cita oculta no sale en la lista ni por su dirección directa, igual que una inexistente o de otro residente.
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await FindAsync(setup, hidden)).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await FindAsync(setup, Guid.NewGuid())).Error!.Code);
        Assert.True((await FindAsync(setup, visible)).Ok);
        Assert.Null((await FindAsync(setup, noSpecialty)).Value.Appointment.Specialty);
    }

    [Fact]
    public async Task EnSinEspecialidad_NingunaEspecialidadDeLaListaSaleEnElModelo()
    {
        var setup = await SetupAsync("Residente Familia Todas Especialidades");
        var ids = new List<Guid>();
        foreach (var specialty in Enum.GetValues<ExternalAppointmentSpecialty>())
        {
            ids.Add(await RegisterAsync(setup, Cita(6, ExternalFamilyVisibility.SinEspecialidad, specialty: specialty)));
        }
        await OpenCenterAsync(setup);

        var seen = await VisibleAsync(setup);
        Assert.Equal(ids.Count, seen.Count);
        Assert.All(seen, appointment => Assert.Null(appointment.Specialty));
    }

    [Fact]
    public async Task ElResidenteRestringido_CortaTodo_SinImportarElNivel_YSeAbreDeNuevoAlLiberarlo()
    {
        var setup = await SetupAsync("Residente Familia Restringido");
        var id = await RegisterAsync(setup);
        await OpenCenterAsync(setup);
        Assert.True(await AvailableAsync(setup));

        // Enfermería de la unidad, SIN el permiso de gestionar citas, puede restringir: es una herramienta de privacidad.
        var restricted = await RestrictAsync(setup.Nurse, setup, 0, true);
        Assert.True(restricted.Ok, restricted.Error?.Message);
        Assert.False(await AvailableAsync(setup));
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await ListAsync(setup)).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await FindAsync(setup, id)).Error!.Code);

        // Administración lo libera; la familia vuelve a verla.
        Assert.True((await RestrictAsync(setup.Admin, setup, 1, false)).Ok);
        Assert.True(await AvailableAsync(setup));
        Assert.Equal(id, Assert.Single(await VisibleAsync(setup)).Id);
    }

    [Fact]
    public async Task AlApagarElCentro_LaFamiliaDejaDeVerlo_EnElActo()
    {
        var setup = await SetupAsync("Residente Familia Cierre");
        await RegisterAsync(setup);
        var number = await OpenCenterAsync(setup);
        Assert.Single(await VisibleAsync(setup));

        await CloseCenterAsync(setup, number);

        Assert.False(await AvailableAsync(setup));
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await ListAsync(setup)).Error!.Code);
    }

    [Fact]
    public async Task EstadosYVentana_RealizadasNoAcudidasNoSeVen_CanceladasSi_YLaListaLlegaA90Dias()
    {
        var setup = await SetupAsync("Residente Familia Ventana");
        var today = await RegisterAsync(setup, Cita(0));
        var edge = await RegisterAsync(setup, Cita(ExternalFamilyRules.WindowDays));
        await RegisterAsync(setup, Cita(ExternalFamilyRules.WindowDays + 1));
        var done = await RegisterAsync(setup, Cita(0));
        var missed = await RegisterAsync(setup, Cita(0));
        var cancelled = await RegisterAsync(setup, Cita(7));
        Assert.True((await Staff(setup.Admin).ChangeStatusAsync(new ChangeExternalAppointmentStatusCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, done, 1, ExternalAppointmentStatus.Realizada))).Ok);
        Assert.True((await Staff(setup.Admin).ChangeStatusAsync(new ChangeExternalAppointmentStatusCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, missed, 1, ExternalAppointmentStatus.NoAcudida))).Ok);
        Assert.True((await Staff(setup.Admin).CancelAsync(new CancelExternalAppointmentCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, cancelled, 1, ExternalAppointmentReasonCode.CambioDelHospital, "Motivo que la familia no debe ver"))).Ok);
        await OpenCenterAsync(setup);

        var seen = await VisibleAsync(setup);

        Assert.Equal([today, cancelled, edge], seen.Select(a => a.Id)); // por fecha: hoy, +7 (cancelada) y +90
        Assert.False(seen[0].Cancelled);
        Assert.True(seen[1].Cancelled);
        Assert.DoesNotContain(seen, a => a.Id == done || a.Id == missed);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await FindAsync(setup, done)).Error!.Code);
    }

    [Fact]
    public async Task ElAislamiento_OtroCentro_OtroVinculo_OtrosPerfiles_YUnAmbitoManipulado_NoDanNada()
    {
        var a = await SetupAsync("Residente Familia Aislamiento A");
        var b = await SetupAsync("Residente Familia Aislamiento B");
        var idA = await RegisterAsync(a);
        await RegisterAsync(b);
        await OpenCenterAsync(a);
        await OpenCenterAsync(b);

        // La familia de B no entra con el vínculo ni la cita de A, y es el mismo error que con algo inexistente.
        var queryB = await QueryAsync(b.Subject);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Family(b.Subject).ListAsync(queryB, a.LinkId)).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Family(b.Subject).FindAsync(queryB, b.LinkId, idA)).Error!.Code);
        Assert.False((await Family(b.Subject).IsAvailableAsync(queryB, a.LinkId)).Value);

        // Con el ámbito de A pero el centro de B (cookie manipulada): el ámbito no existe en ese centro.
        var queryA = await QueryAsync(a.Subject);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Family(a.Subject).ListAsync(new FamilyPortalQuery(queryA.AmbitoPerfilId, b.Admin.CenterId), a.LinkId)).Error!.Code);

        // Ni Enfermería, ni Administración, ni Plataforma entran por la ruta de la familia, aunque pongan su propio ámbito.
        foreach (var profile in new[] { a.Admin, a.Nurse })
        {
            var asStaff = await Family(profile.ExternalSubject).ListAsync(new FamilyPortalQuery(profile.ProfileScopeId, profile.CenterId), a.LinkId);
            Assert.Equal(ApplicationFailureCode.AccessDenied, asStaff.Error!.Code);
        }

        // La Administración de B no ve ni cambia los interruptores de A.
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Settings(b.Admin).ReadResidentAsync(b.Admin.ProfileScopeId, b.Admin.CenterId, a.ResidentId)).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await RestrictAsync(b.Admin, a, 0, true, a.ResidentId)).Error!.Code);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas_familia_residente WHERE centro_id = @c", new { c = a.Admin.CenterId.Value }));

        // Lo de A sigue visible para su familia y lo de B para la suya, cada una con lo suyo.
        Assert.Equal(idA, Assert.Single(await VisibleAsync(a)).Id);
        Assert.NotEqual(idA, Assert.Single(await VisibleAsync(b)).Id);
    }

    [Fact]
    public async Task DentroDelMismoCentro_CadaFamiliaVeSoloLoSuyo_YCadaResidenteTieneSuPropioInterruptor()
    {
        var setup = await SetupAsync("Residente Familia Dos Uno");
        var other = await CreateResidentAsync(setup.Admin, "Residente Familia Dos Otro");
        var otherLink = await AddFamilyAsync(setup.Admin, other, "Marta Gómez");
        var otherSubject = $"portal-{Guid.NewGuid():N}"[..20];
        var grant = await Build(setup.Admin.ExternalSubject).GrantFamilyPortalAccessAsync(
            new GrantFamilyPortalAccessCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, other, otherLink, otherSubject));
        Assert.True(grant.Ok, grant.Error?.Message);
        await ChangeAsync(setup.Admin, other, otherLink, FamilyAuthorizationChange.Abrir, 0);
        await ChangeAsync(setup.Admin, other, otherLink, FamilyAuthorizationChange.Activar, 1);
        var mine = await RegisterAsync(setup);
        var theirs = await RegisterAsync(setup, Cita(6), other);
        await OpenCenterAsync(setup);

        // Cada familia ve lo de su residente, y la cita de otro residente del mismo centro es para ella como una que no existe.
        var query = await QueryAsync(setup.Subject);
        Assert.Equal(mine, Assert.Single((await Family(setup.Subject).ListAsync(query, setup.LinkId)).Value!.Appointments).Id);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Family(setup.Subject).ListAsync(query, otherLink)).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Family(setup.Subject).FindAsync(query, setup.LinkId, theirs)).Error!.Code);
        var otherQuery = await QueryAsync(otherSubject);
        Assert.Equal(theirs, Assert.Single((await Family(otherSubject).ListAsync(otherQuery, otherLink)).Value!.Appointments).Id);

        // Restringir a uno no toca al otro.
        Assert.True((await RestrictAsync(setup.Admin, setup, 0, true, other)).Ok);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Family(otherSubject).ListAsync(otherQuery, otherLink)).Error!.Code);
        Assert.Equal(mine, Assert.Single((await Family(setup.Subject).ListAsync(query, setup.LinkId)).Value!.Appointments).Id);
    }

    [Fact]
    public async Task SinAccesoAlPortal_NoHayCitas_AutorizacionSuspendida_ResidenteSuspendidoODeBaja()
    {
        var setup = await SetupAsync("Residente Familia Sin Acceso");
        await RegisterAsync(setup);
        await OpenCenterAsync(setup);
        Assert.Single(await VisibleAsync(setup));

        await ChangeAsync(setup.Admin, setup.ResidentId, setup.LinkId, FamilyAuthorizationChange.Suspender, 2, "Prueba.");
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await ListAsync(setup)).Error!.Code);
        Assert.False(await AvailableAsync(setup));
        await ChangeAsync(setup.Admin, setup.ResidentId, setup.LinkId, FamilyAuthorizationChange.Activar, 3);
        Assert.Single(await VisibleAsync(setup));

        var status = EstadoResidenteTests.Build(setup.Admin.ExternalSubject);
        Assert.True((await status.SuspendAsync(new SuspendResidentCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, Guid.NewGuid(), null))).Ok);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await ListAsync(setup)).Error!.Code);
        Assert.True((await status.ResumeAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId)).Ok);
        Assert.Single(await VisibleAsync(setup));

        Assert.True((await status.DischargeAsync(new DischargeResidentCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, Guid.NewGuid(), ResidApp.Domain.Residents.ResidentDischargeReason.AltaVoluntaria, null))).Ok);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await ListAsync(setup)).Error!.Code);
        // La baja cancela las futuras, y aun así la familia no ve ni la «Cancelada»: el residente de baja no sale en sus vínculos.
        Assert.False(await AvailableAsync(setup));
    }

    [Fact]
    public async Task AutorizacionRevocadaOVinculoDesvinculado_NoDanNadaDeLasCitas()
    {
        var revoked = await SetupAsync("Residente Familia Revocada");
        await RegisterAsync(revoked);
        await OpenCenterAsync(revoked);
        Assert.Single(await VisibleAsync(revoked));
        await ChangeAsync(revoked.Admin, revoked.ResidentId, revoked.LinkId, FamilyAuthorizationChange.Revocar, 2, "Prueba.");
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await ListAsync(revoked)).Error!.Code);
        Assert.False(await AvailableAsync(revoked));

        var unlinked = await SetupAsync("Residente Familia Desvinculado");
        await RegisterAsync(unlinked);
        await OpenCenterAsync(unlinked);
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync(
                "UPDATE dbo.residentes_familiares SET desvinculado_en = SYSUTCDATETIME(), desvinculado_por_cuenta_id = @a, desvinculado_motivo = 'Prueba.' WHERE id = @l",
                new { a = unlinked.Admin.AccountId.Value, l = unlinked.LinkId });
        }
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await ListAsync(unlinked)).Error!.Code);
        Assert.False(await AvailableAsync(unlinked));
    }

    [Fact]
    public async Task ElRecuento_CuentaPorNivel_SinLasOcultasNiLasDeResidentesRestringidos_ParaElCentroYParaUnResidente()
    {
        var setup = await SetupAsync("Residente Familia Recuento");
        var other = await AdministracionResidentesTests.CreateResidentAsync(setup.Admin, "Residente Familia Recuento Otro");
        await RegisterAsync(setup, Cita(3, ExternalFamilyVisibility.Visible));
        await RegisterAsync(setup, Cita(4, ExternalFamilyVisibility.Visible));
        await RegisterAsync(setup, Cita(5, ExternalFamilyVisibility.SinEspecialidad));
        await RegisterAsync(setup, Cita(6, ExternalFamilyVisibility.Oculta));
        await RegisterAsync(setup, Cita(7, ExternalFamilyVisibility.Visible), other);
        await RegisterAsync(setup, Cita(ExternalFamilyRules.WindowDays + 5, ExternalFamilyVisibility.Visible)); // fuera de la ventana: no cuenta
        var cancelled = await RegisterAsync(setup, Cita(8, ExternalFamilyVisibility.Visible));
        Assert.True((await Staff(setup.Admin).CancelAsync(new CancelExternalAppointmentCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, cancelled, 1, ExternalAppointmentReasonCode.CambioDelHospital, null))).Ok);

        var admin = Settings(setup.Admin);
        var center = (await admin.ReadCenterAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId)).Value!;
        Assert.False(center.Enabled);
        Assert.Equal((3, 1), (center.Count.Visible, center.Count.WithoutSpecialty));

        // Un residente restringido sale del recuento del centro, pero su propio recuento dice cuántas se abrirían al liberarlo.
        Assert.True((await RestrictAsync(setup.Admin, setup, 0, true, other)).Ok);
        center = (await admin.ReadCenterAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId)).Value!;
        Assert.Equal((2, 1), (center.Count.Visible, center.Count.WithoutSpecialty));
        var otherState = (await admin.ReadResidentAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId, other)).Value!;
        Assert.True(otherState.Restricted);
        Assert.Equal(1, otherState.Count.Total);
        var mine = (await admin.ReadResidentAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId)).Value!;
        Assert.False(mine.Restricted);
        Assert.False(mine.CenterEnabled);
        Assert.Equal((2, 1), (mine.Count.Visible, mine.Count.WithoutSpecialty));
    }

    [Fact]
    public async Task QuienPuedeCambiarCadaInterruptor_EntreAdministracionEnfermeriaMedicinaYOtraUnidad()
    {
        var setup = await SetupAsync("Residente Familia Permisos");
        var doctor = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Medicina, setup.Admin.CenterId, setup.Admin.UnitId);
        var otherUnit = await AdministracionResidentesTests.AddUnitAsync(setup.Admin.CenterId);
        var foreignNurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, setup.Admin.CenterId, otherUnit);
        var assistant = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, setup.Admin.CenterId, setup.Admin.UnitId);

        // El interruptor del centro: solo Administración, ni leerlo.
        foreach (var other in new[] { setup.Nurse, doctor, foreignNurse, assistant })
        {
            Assert.Equal(ApplicationFailureCode.AccessDenied, (await Settings(other).ReadCenterAsync(other.ProfileScopeId, other.CenterId)).Error?.Code);
            Assert.Equal(ApplicationFailureCode.AccessDenied, (await Settings(other).SetCenterAsync(
                new SetExternalFamilyCenterCommand(other.ProfileScopeId, other.CenterId, 0, true, true, true, true))).Error?.Code);
        }
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas_familia_centro WHERE centro_id = @c", new { c = setup.Admin.CenterId.Value }));

        // El del residente: Medicina lo ve y no lo cambia; Enfermería de otra unidad y Auxiliar, ni una cosa ni la otra.
        Assert.True((await Settings(doctor).ReadResidentAsync(doctor.ProfileScopeId, doctor.CenterId, setup.ResidentId)).Ok);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await RestrictAsync(doctor, setup, 0, true)).Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Settings(foreignNurse).ReadResidentAsync(foreignNurse.ProfileScopeId, foreignNurse.CenterId, setup.ResidentId)).Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await RestrictAsync(foreignNurse, setup, 0, true)).Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Settings(assistant).ReadResidentAsync(assistant.ProfileScopeId, assistant.CenterId, setup.ResidentId)).Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await RestrictAsync(assistant, setup, 0, true)).Error?.Code);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas_familia_residente WHERE centro_id = @c", new { c = setup.Admin.CenterId.Value }));

        // Un residente que no existe en el centro, o la propia familia: el mismo acceso denegado.
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await RestrictAsync(setup.Admin, setup, 0, true, ResidentId.From(Guid.NewGuid()))).Error?.Code);
        var familyScope = await QueryAsync(setup.Subject);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await SettingsFor(setup.Subject).ReadCenterAsync(familyScope.AmbitoPerfilId, familyScope.CentroId)).Error?.Code);
    }

    [Fact]
    public async Task NingunCambioPisaAOtro_YUnCambioQueNoCambiaNadaEsUnConflicto()
    {
        var setup = await SetupAsync("Residente Familia Versiones");
        Assert.Equal(1, await OpenCenterAsync(setup));

        // Otra persona abrió antes con el mismo número visto, o el número visto es viejo: conflicto, no se pisa.
        Assert.Equal(ApplicationFailureCode.Conflict, (await Settings(setup.Admin).SetCenterAsync(
            new SetExternalFamilyCenterCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, 0, true, true, true, true))).Error?.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, (await Settings(setup.Admin).SetCenterAsync(
            new SetExternalFamilyCenterCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, 1, true, true, true, true))).Error?.Code); // ya está encendido
        Assert.Equal(ApplicationFailureCode.Conflict, (await Settings(setup.Admin).SetCenterAsync(
            new SetExternalFamilyCenterCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, 0, false, false, false, false))).Error?.Code); // número viejo

        Assert.True((await RestrictAsync(setup.Nurse, setup, 0, true)).Ok);
        Assert.Equal(ApplicationFailureCode.Conflict, (await RestrictAsync(setup.Admin, setup, 0, true)).Error?.Code); // el otro ya lo hizo
        Assert.Equal(ApplicationFailureCode.Conflict, (await RestrictAsync(setup.Admin, setup, 1, true)).Error?.Code); // ya está restringido
        Assert.Equal(ApplicationFailureCode.Conflict, (await RestrictAsync(setup.Admin, setup, 0, false)).Error?.Code);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.citas_externas_familia_residente WHERE residente_id = @r", new { r = setup.ResidentId.Value }));
    }

    [Fact]
    public async Task LaBaseImpideCambiarOBorrarElHistorial_YRechazaFilasQueNoCambianNada()
    {
        var setup = await SetupAsync("Residente Familia Historial");
        await OpenCenterAsync(setup);
        Assert.True((await RestrictAsync(setup.Admin, setup, 0, true)).Ok);
        var center = setup.Admin.CenterId.Value;

        Assert.Contains("EXTERNAL_APPOINTMENT_FAMILY_IMMUTABLE", (await SqlFailureAsync("UPDATE dbo.citas_externas_familia_centro SET familia_ve = 0 WHERE centro_id = @c", new { c = center })).Message);
        Assert.Contains("EXTERNAL_APPOINTMENT_FAMILY_IMMUTABLE", (await SqlFailureAsync("DELETE FROM dbo.citas_externas_familia_centro WHERE centro_id = @c", new { c = center })).Message);
        Assert.Contains("EXTERNAL_APPOINTMENT_FAMILY_IMMUTABLE", (await SqlFailureAsync("UPDATE dbo.citas_externas_familia_residente SET familia_no_ve = 0 WHERE centro_id = @c", new { c = center })).Message);
        Assert.Contains("EXTERNAL_APPOINTMENT_FAMILY_IMMUTABLE", (await SqlFailureAsync("DELETE FROM dbo.citas_externas_familia_residente WHERE centro_id = @c", new { c = center })).Message);

        var account = setup.Admin.AccountId.Value;
        // La primera fila de un residente ha de restringir; una fila igual a la vigente, o que se salta un número, no entra.
        Assert.Contains("EXTERNAL_APPOINTMENT_FAMILY_SEQUENCE_INVALID", (await SqlFailureAsync("""
            INSERT INTO dbo.citas_externas_familia_residente (id, centro_id, residente_id, numero, familia_no_ve, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (NEWID(), @c, @r, 2, 1, @a, 'ADMINISTRACION', SYSUTCDATETIME())
            """, new { c = center, r = setup.ResidentId.Value, a = account })).Message);
        Assert.Contains("EXTERNAL_APPOINTMENT_FAMILY_SEQUENCE_INVALID", (await SqlFailureAsync("""
            INSERT INTO dbo.citas_externas_familia_residente (id, centro_id, residente_id, numero, familia_no_ve, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (NEWID(), @c, @r, 3, 0, @a, 'ADMINISTRACION', SYSUTCDATETIME())
            """, new { c = center, r = setup.ResidentId.Value, a = account })).Message);
        // Encender sin una de las confirmaciones, o con un perfil que no es Administración, lo rechaza la base aunque la aplicación se saltara su comprobación.
        Assert.Contains("CK_cextfc_confirmaciones", (await SqlFailureAsync("""
            INSERT INTO dbo.citas_externas_familia_centro (id, centro_id, numero, familia_ve, confirma_informada, confirma_voluntad, confirma_eipd, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (NEWID(), @c, 3, 1, 1, 1, 0, @a, 'ADMINISTRACION', SYSUTCDATETIME())
            """, new { c = center, a = account })).Message);
        Assert.Contains("CK_cextfc_perfil", (await SqlFailureAsync("""
            INSERT INTO dbo.citas_externas_familia_centro (id, centro_id, numero, familia_ve, confirma_informada, confirma_voluntad, confirma_eipd, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (NEWID(), @c, 3, 0, 0, 0, 0, @a, 'ENFERMERIA', SYSUTCDATETIME())
            """, new { c = center, a = account })).Message);
    }

    [Fact]
    public async Task CadaCambioDejaUnaAuditoria_ConSoloIdentificadores_YElDelCentroSinResidente()
    {
        var setup = await SetupAsync("Residente Familia Auditoria");
        var center = setup.Admin.CenterId.Value;
        var number = await OpenCenterAsync(setup);
        await RestrictAsync(setup.Nurse, setup, 0, true);
        await RestrictAsync(setup.Admin, setup, 1, false);
        await CloseCenterAsync(setup, number);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var events = (await connection.QueryAsync<(string Action, string Profile, string Resource, Guid? ResidentId, Guid? UnitId)>("""
            SELECT accion_codigo, perfil_activo, tipo_recurso, residente_id, unidad_id FROM dbo.eventos_auditoria
             WHERE centro_id = @center AND (accion_codigo LIKE 'EXT\_APPOINTMENT\_FAMILY%' ESCAPE '\' OR accion_codigo LIKE 'EXT\_APPOINTMENT\_RESIDENT%' ESCAPE '\')
            """, new { center })).ToList();

        Assert.Equal(
            new[] { "EXT_APPOINTMENT_FAMILY_OFF", "EXT_APPOINTMENT_FAMILY_ON", "EXT_APPOINTMENT_RESIDENT_HIDE", "EXT_APPOINTMENT_RESIDENT_SHOW" },
            events.Select(e => e.Action).Order());
        Assert.All(events, e => Assert.Equal("EXTERNAL_APPOINTMENT_FAMILY", e.Resource));
        Assert.All(events.Where(e => e.Action.Contains("FAMILY_O")), e => { Assert.Null(e.ResidentId); Assert.Null(e.UnitId); Assert.Equal("ADMINISTRACION", e.Profile); });
        Assert.All(events.Where(e => e.Action.Contains("RESIDENT_")), e => Assert.Equal(setup.ResidentId.Value, e.ResidentId));
        Assert.Contains(events, e => e.Action == "EXT_APPOINTMENT_RESIDENT_HIDE" && e.Profile == "ENFERMERIA");
        // Un cambio rechazado no deja rastro de auditoría.
        var before = events.Count;
        await RestrictAsync(setup.Admin, setup, 0, true); // número viejo: conflicto
        Assert.Equal(before, await CountAsync(
            @"SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE centro_id = @c AND (accion_codigo LIKE 'EXT\_APPOINTMENT\_FAMILY%' ESCAPE '\' OR accion_codigo LIKE 'EXT\_APPOINTMENT\_RESIDENT%' ESCAPE '\')",
            new { c = center }));
    }
}

file sealed class FixedFamilySessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
