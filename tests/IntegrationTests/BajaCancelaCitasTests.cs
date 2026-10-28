using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Appointments;
using ResidApp.Domain.Residents;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.CitasSolicitudesTests;
using static ResidApp.IntegrationTests.PortalFamiliarTests;

namespace ResidApp.IntegrationTests;

/// <summary>Al dar de baja a un residente se cancelan sus citas futuras y se cierran sus solicitudes de cita abiertas, como hecho del sistema a causa de la baja
/// (CJ, portal cuarta tanda 1.1 B; script 0078). La suspensión no las toca. Cada prueba crea su propio centro.</summary>
public class BajaCancelaCitasTests
{
    private const string CancelReason = "Cancelada por el sistema al dar de baja al residente.";

    private static async Task<Guid> InsertAppointmentAsync(Setup setup, Guid residentId, Guid linkId, string subject, string team, TimeSpan fromNow)
    {
        var id = Guid.NewGuid();
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.citas
                (id, centro_id, residente_id, vinculo_id, cuenta_id, equipo_codigo, modalidad_codigo, inicio_en, fin_en, estado_codigo, operacion_id, creada_en)
            SELECT @id, @center, @resident, @link, account.id, @team, 'PRESENCIAL', DATEADD(SECOND, @seconds, SYSUTCDATETIME()),
                   DATEADD(MINUTE, 30, DATEADD(SECOND, @seconds, SYSUTCDATETIME())), 'CONFIRMADA', NEWID(), SYSUTCDATETIME()
              FROM dbo.cuentas account WHERE account.sujeto_externo = @subject
            """, new { id, center = setup.Admin.CenterId.Value, resident = residentId, link = linkId, subject, team, seconds = (int)fromNow.TotalSeconds });
        return id;
    }

    private static DischargeResidentCommand Discharge(Setup setup) => new(
        setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, Guid.NewGuid(), ResidentDischargeReason.AltaVoluntaria, "Vuelve con su familia.");

    private static async Task<string> TextAsync(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<string>(sql, parameters) ?? string.Empty;
    }

    [Fact]
    public async Task Baja_CancelaLasCitasFuturasYCierraLasSolicitudesAbiertas_ConSuHistorialYAuditoria_YNoTocaLoDemas()
    {
        var setup = await SetupAsync("Residente Baja Citas Uno");
        await ConfigureAsync(setup);
        var other = await OtherResidentFamilyAsync(setup, "Residente Baja Citas Dos");
        var status = EstadoResidenteTests.Build(setup.Admin.ExternalSubject);

        // Del residente que se da de baja: una solicitud pendiente (médica), otra con propuesta viva (enfermería), una cita futura y otra ya pasada.
        var pending = await SentAsync(setup.Subject, setup.LinkId, AppointmentTeam.Medico);
        var proposed = await SentAsync(setup.Subject, setup.LinkId, AppointmentTeam.Enfermeria);
        var slot = await FarSlotAsync(setup, proposed.Id);
        Assert.True((await ProposeAsync(setup, proposed.Id, slot)).Ok);
        var future = await InsertAppointmentAsync(setup, setup.ResidentId.Value, setup.LinkId, setup.Subject, "MEDICO", TimeSpan.FromDays(5));
        var past = await InsertAppointmentAsync(setup, setup.ResidentId.Value, setup.LinkId, setup.Subject, "ENFERMERIA", TimeSpan.FromDays(-2));
        // Del otro residente de la misma residencia: una solicitud y una cita futura, que no deben cambiar.
        var otherRequest = await SentAsync(other.Subject, other.LinkId, AppointmentTeam.Enfermeria);
        var otherFuture = await InsertAppointmentAsync(setup, other.ResidentId, other.LinkId, other.Subject, "MEDICO", TimeSpan.FromDays(6));
        Assert.DoesNotContain(await ProposalSlotsAsync(setup, otherRequest.Id), s => s.StartsAt == slot.StartsAt);

        var preview = await status.PreviewDischargeAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId);
        Assert.Equal(1, preview.Value!.FutureAppointments);
        Assert.Equal(2, preview.Value.OpenRequests);

        var discharged = await status.DischargeAsync(Discharge(setup));

        Assert.True(discharged.Ok, discharged.Error?.Message);
        Assert.Equal(0, discharged.Value!.EventsClosed);
        Assert.Equal(1, discharged.Value.AppointmentsCancelled);
        Assert.Equal(2, discharged.Value.AppointmentRequestsClosed);
        Assert.Equal("CANCELADA", await TextAsync("SELECT estado_codigo FROM dbo.citas WHERE id = @id", new { id = future }));
        Assert.Equal("CONFIRMADA", await TextAsync("SELECT estado_codigo FROM dbo.citas WHERE id = @id", new { id = past }));
        Assert.Equal(1, await SavedAsync(pending.Id, "CERRADA"));
        Assert.Equal(1, await SavedAsync(proposed.Id, "CERRADA"));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM dbo.citas_solicitudes WHERE id = @id AND (propuesta_inicio_en IS NOT NULL OR propuesta_en IS NOT NULL)", new { id = proposed.Id }));

        // Historial y auditoría: lo hizo el sistema a causa de la baja, con la cuenta de Administración que la dio y un motivo fijo; la cita pasada no se toca.
        Assert.Equal(1, await CountAsync("""
            SELECT COUNT(*) FROM dbo.citas_cambios
             WHERE cita_id = @id AND accion_codigo = 'CANCELADA' AND causa_codigo = 'BAJA_RESIDENTE' AND motivo = @reason
               AND actor_perfil_codigo = 'ADMINISTRACION' AND actor_cuenta_id = @account
            """, new { id = future, reason = CancelReason, account = setup.Admin.AccountId.Value }));
        Assert.Equal(1, await AuditAsync(future, "APPOINTMENT_CANCEL_DISCHARGE"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.citas_cambios WHERE cita_id = @id", new { id = past }));
        foreach (var request in new[] { pending.Id, proposed.Id })
        {
            Assert.Equal(1, await CountAsync("""
                SELECT COUNT(*) FROM dbo.citas_solicitudes_cambios
                 WHERE solicitud_id = @id AND accion_codigo = 'CERRADA' AND causa_codigo = 'BAJA_RESIDENTE' AND actor_cuenta_id = @account
                """, new { id = request, account = setup.Admin.AccountId.Value }));
            Assert.Equal(1, await AuditAsync(request, "APPOINTMENT_REQUEST_CLOSE_SYSTEM"));
        }

        var view = await AdminViewAsync(setup, pending.Id);
        Assert.True(view.Changes[^1].FromDischarge);
        Assert.False(view.Changes[0].FromDischarge);

        // Otro residente: nada cambia, y el hueco de la propuesta cerrada queda libre para su solicitud.
        Assert.Equal(1, await SavedAsync(otherRequest.Id, "PENDIENTE"));
        Assert.Equal("CONFIRMADA", await TextAsync("SELECT estado_codigo FROM dbo.citas WHERE id = @id", new { id = otherFuture }));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.citas_cambios WHERE cita_id = @id", new { id = otherFuture }));
        Assert.Contains(await ProposalSlotsAsync(setup, otherRequest.Id), s => s.StartsAt == slot.StartsAt);
    }

    [Fact]
    public async Task Suspension_NoTocaLasCitasNiLasSolicitudes()
    {
        var setup = await SetupAsync("Residente Suspension Citas");
        await ConfigureAsync(setup);
        var request = await SentAsync(setup.Subject, setup.LinkId, AppointmentTeam.Medico);
        var future = await InsertAppointmentAsync(setup, setup.ResidentId.Value, setup.LinkId, setup.Subject, "ENFERMERIA", TimeSpan.FromDays(5));
        var status = EstadoResidenteTests.Build(setup.Admin.ExternalSubject);

        var suspended = await status.SuspendAsync(new SuspendResidentCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, Guid.NewGuid(), null));

        Assert.True(suspended.Ok, suspended.Error?.Message);
        Assert.Equal(1, await SavedAsync(request.Id, "PENDIENTE"));
        Assert.Equal("CONFIRMADA", await TextAsync("SELECT estado_codigo FROM dbo.citas WHERE id = @id", new { id = future }));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.citas_cambios WHERE cita_id = @id", new { id = future }));
    }

    [Fact]
    public async Task SinCitasNiSolicitudes_LaBajaNoCancelaNiCierraNada_YElAvisoPrevioEstaAZero_YSoloSuAdministracionLoVe()
    {
        var setup = await SetupAsync("Residente Baja Sin Citas");
        var outsider = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var status = EstadoResidenteTests.Build(setup.Admin.ExternalSubject);

        var preview = await status.PreviewDischargeAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId);
        var foreign = await EstadoResidenteTests.Build(outsider.ExternalSubject).PreviewDischargeAsync(outsider.ProfileScopeId, outsider.CenterId, setup.ResidentId);
        var discharged = await status.DischargeAsync(Discharge(setup));

        Assert.Equal(0, preview.Value!.FutureAppointments + preview.Value.OpenRequests);
        Assert.False(foreign.Ok);
        Assert.Equal(0, discharged.Value!.AppointmentsCancelled + discharged.Value.AppointmentRequestsClosed);
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE residente_id = @id AND accion_codigo IN ('APPOINTMENT_CANCEL_DISCHARGE', 'APPOINTMENT_REQUEST_CLOSE_SYSTEM')",
            new { id = setup.ResidentId.Value }));
    }

    [Fact]
    public async Task LaBaseDeDatos_SoloAdmiteLaCausaDeBajaConUnaBajaEnLaMismaTransaccion()
    {
        var setup = await SetupAsync("Residente Causa Ilegal");
        await ConfigureAsync(setup);
        var request = await SentAsync(setup.Subject, setup.LinkId, AppointmentTeam.Medico);
        var future = await InsertAppointmentAsync(setup, setup.ResidentId.Value, setup.LinkId, setup.Subject, "ENFERMERIA", TimeSpan.FromDays(5));

        async Task ForbiddenAsync(string sql, string code, object parameters)
        {
            using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
            var error = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql, parameters));
            Assert.Contains(code, error.Message);
        }

        // Sin baja del residente, nadie puede anotar «a causa de la baja»: ni siquiera con el motivo fijo.
        await ForbiddenAsync("""
            INSERT INTO dbo.citas_cambios (id, centro_id, cita_id, numero, accion_codigo, actor_cuenta_id, actor_perfil_codigo, motivo, ocurrido_en, causa_codigo)
            SELECT NEWID(), centro_id, id, 2, 'CANCELADA', cuenta_id, 'ADMINISTRACION', @reason, SYSUTCDATETIME(), 'BAJA_RESIDENTE' FROM dbo.citas WHERE id = @id
            """, "APPOINTMENT_CAUSE_INVALID", new { id = future, reason = CancelReason });
        await ForbiddenAsync("""
            INSERT INTO dbo.citas_solicitudes_cambios (id, centro_id, solicitud_id, numero, accion_codigo, actor_cuenta_id, actor_perfil_codigo, detalle, ocurrido_en, causa_codigo)
            SELECT NEWID(), centro_id, id, 2, 'CERRADA', cuenta_id, 'ADMINISTRACION', 'Cerrada por el sistema al dar de baja al residente.', SYSUTCDATETIME(), 'BAJA_RESIDENTE'
              FROM dbo.citas_solicitudes WHERE id = @id
            """, "APPOINTMENT_REQUEST_CAUSE_INVALID", new { id = request.Id });

        // Una baja ya hecha en otra transacción tampoco sirve de coartada.
        Assert.True((await EstadoResidenteTests.Build(setup.Admin.ExternalSubject).DischargeAsync(Discharge(setup))).Ok);
        await ForbiddenAsync("""
            INSERT INTO dbo.citas_cambios (id, centro_id, cita_id, numero, accion_codigo, actor_cuenta_id, actor_perfil_codigo, motivo, ocurrido_en, causa_codigo)
            SELECT NEWID(), centro_id, id, 9, 'CANCELADA', cuenta_id, 'ADMINISTRACION', @reason, SYSUTCDATETIME(), 'BAJA_RESIDENTE' FROM dbo.citas WHERE id = @id
            """, "APPOINTMENT_CAUSE_INVALID", new { id = future, reason = CancelReason });
        // La causa solo vale para una cancelación (o un cierre).
        await ForbiddenAsync("""
            INSERT INTO dbo.citas_cambios (id, centro_id, cita_id, numero, accion_codigo, actor_cuenta_id, actor_perfil_codigo, ocurrido_en, causa_codigo)
            SELECT NEWID(), centro_id, id, 10, 'REPROGRAMADA', cuenta_id, 'ADMINISTRACION', SYSUTCDATETIME(), 'BAJA_RESIDENTE' FROM dbo.citas WHERE id = @id
            """, "CK_citas_cambios_causa", new { id = future });
    }
}
