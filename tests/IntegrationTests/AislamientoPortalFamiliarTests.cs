using Dapper;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.Sessions;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Appointments;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Families;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using static ResidApp.IntegrationTests.PortalFamiliarTests;

namespace ResidApp.IntegrationTests;

/// <summary>
/// La barrera del aislamiento entre familias de un mismo centro. La seguridad por filas de la base de datos solo separa centros y no distingue familiares: lo que separa una
/// familia de otra es el código de la aplicación (<c>SqlFamilyPortalReader.AccessibleLinks</c> y el «es de esta cuenta» de citas y solicitudes). Una función nueva del portal que se
/// olvide de esa comprobación enseñaría datos de otra familia sin que ningún otro control lo frene. Por eso:
/// <list type="bullet">
/// <item>Cada método de un puerto que recibe <see cref="FamilyPortalAccess"/> (es decir, cada operación de una familia) tiene aquí su escenario, o la prueba de la guardia falla.</item>
/// <item>Cada escenario se ejecuta como dos intrusos de ese mismo centro: otro familiar del MISMO residente y la familia de OTRO residente. Con el vínculo del titular y con el
/// suyo propio pero un identificador del titular, siempre se deniega, sin distinguir un identificador ajeno de uno inventado, y no cambia nada de lo del titular.</item>
/// </list>
/// Un comunicado es del residente (lo ve cualquier familiar autorizado), así que con su propio vínculo el otro familiar del mismo residente SÍ puede leerlo: ahí solo se
/// deniega a la familia de otro residente. Las citas y las solicitudes son de la cuenta que las creó: ahí se deniega a los dos.
/// </summary>
public class AislamientoPortalFamiliarTests
{
    private sealed record Actor(string Name, string Subject, Guid LinkId, bool SameResident);

    private sealed record World(Setup Owner, Actor Titular, Guid CommunicationId, Guid RequestId, Guid AppointmentId, Actor Relative, Actor Neighbor);

    private static FamilyPortalApplicationService PortalOf(Actor actor) => PortalFamiliarTests.Portal(actor.Subject);

    private static FamilyAppointmentsApplicationService AppointmentsOf(Actor actor) => CitasTests.Family(actor.Subject);

    private static FamilyAppointmentRequestsApplicationService RequestsOf(Actor actor) => new(
        new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedBarreraSessionIdentityProvider(actor.Subject),
        new SqlFamilyAppointmentRequests(TestDatabase.ConnectionFactory));

    // La clave es «Puerto.Método»: la guardia la compara con los métodos de los puertos por reflexión.
    private static readonly Dictionary<string, Func<World, Actor, Task>> Scenarios = new()
    {
        ["IFamilyPortalReader.ListResidentsAsync"] = async (w, a) =>
        {
            var list = await PortalOf(a).ListResidentsAsync(await QueryAsync(a.Subject));
            Assert.True(list.Ok, list.Error?.Message);
            Assert.DoesNotContain(list.Value!, resident => resident.LinkId == w.Titular.LinkId);
        },
        ["IFamilyPortalReader.ReadResidentAsync"] = async (w, a) =>
            Denied(await PortalOf(a).ReadResidentAsync(await QueryAsync(a.Subject), w.Titular.LinkId)),
        ["IFamilyPortalReader.ReadCommunicationAsync"] = async (w, a) =>
        {
            var query = await QueryAsync(a.Subject);
            Denied(await PortalOf(a).ReadCommunicationAsync(query, w.Titular.LinkId, w.CommunicationId));
            if (!a.SameResident)
            {
                await ForeignIdAsync(id => PortalOf(a).ReadCommunicationAsync(query, a.LinkId, id), w.CommunicationId);
            }
        },

        ["IFamilyAppointmentRequests.ListAsync"] = async (w, a) =>
        {
            var query = await QueryAsync(a.Subject);
            Denied(await RequestsOf(a).ListAsync(query, w.Titular.LinkId));
            var own = await RequestsOf(a).ListAsync(query, a.LinkId);
            Assert.True(own.Ok, own.Error?.Message);
            Assert.DoesNotContain(own.Value!, request => request.Id == w.RequestId);
        },
        ["IFamilyAppointmentRequests.ReadAsync"] = async (w, a) =>
        {
            var query = await QueryAsync(a.Subject);
            Denied(await RequestsOf(a).ReadAsync(query, w.Titular.LinkId, w.RequestId));
            await ForeignIdAsync(id => RequestsOf(a).ReadAsync(query, a.LinkId, id), w.RequestId);
        },
        ["IFamilyAppointmentRequests.CreateAsync"] = async (w, a) =>
        {
            var query = await QueryAsync(a.Subject);
            Denied(await RequestsOf(a).CreateAsync(new CreateFamilyAppointmentRequestCommand(
                query.AmbitoPerfilId, query.CentroId, w.Titular.LinkId, AppointmentTeam.Medico, AppointmentModality.Presencial,
                AppointmentRequestReason.Resultados, null, [], AppointmentDayPart.Indiferente, Guid.NewGuid())));
        },
        ["IFamilyAppointmentRequests.AcceptAsync"] = async (w, a) =>
        {
            var query = await QueryAsync(a.Subject);
            Denied(await RequestsOf(a).AcceptAsync(query, w.Titular.LinkId, w.RequestId));
            await ForeignIdAsync(id => RequestsOf(a).AcceptAsync(query, a.LinkId, id), w.RequestId);
        },
        ["IFamilyAppointmentRequests.RequestAnotherTimeAsync"] = async (w, a) =>
        {
            var query = await QueryAsync(a.Subject);
            Denied(await RequestsOf(a).RequestAnotherTimeAsync(query, w.Titular.LinkId, w.RequestId));
            await ForeignIdAsync(id => RequestsOf(a).RequestAnotherTimeAsync(query, a.LinkId, id), w.RequestId);
        },
        ["IFamilyAppointmentRequests.CancelAsync"] = async (w, a) =>
        {
            var query = await QueryAsync(a.Subject);
            Denied(await RequestsOf(a).CancelAsync(query, w.Titular.LinkId, w.RequestId));
            await ForeignIdAsync(id => RequestsOf(a).CancelAsync(query, a.LinkId, id), w.RequestId);
        },

        ["IFamilyAppointments.ReadHomeAsync"] = async (w, a) =>
            Denied(await AppointmentsOf(a).ReadHomeAsync(await QueryAsync(a.Subject), w.Titular.LinkId)),
        ["IFamilyAppointments.ListSlotsAsync"] = async (w, a) =>
            Denied(await AppointmentsOf(a).ListSlotsAsync(
                await QueryAsync(a.Subject), w.Titular.LinkId, AppointmentTeam.Medico, AppointmentModality.Presencial, null)),
        ["IFamilyAppointments.BookAsync"] = async (w, a) =>
        {
            var query = await QueryAsync(a.Subject);
            Denied(await AppointmentsOf(a).BookAsync(new BookFamilyAppointmentCommand(
                query.AmbitoPerfilId, query.CentroId, w.Titular.LinkId, AppointmentTeam.Medico, AppointmentModality.Presencial,
                DateTimeOffset.UtcNow.AddDays(3), Guid.NewGuid())));
        },
        ["IFamilyAppointments.RescheduleAsync"] = async (w, a) =>
        {
            var query = await QueryAsync(a.Subject);
            var newStart = DateTimeOffset.UtcNow.AddDays(3);
            Denied(await AppointmentsOf(a).RescheduleAsync(new RescheduleFamilyAppointmentCommand(
                query.AmbitoPerfilId, query.CentroId, w.Titular.LinkId, w.AppointmentId, newStart)));
            await ForeignIdAsync(id => AppointmentsOf(a).RescheduleAsync(new RescheduleFamilyAppointmentCommand(
                query.AmbitoPerfilId, query.CentroId, a.LinkId, id, newStart)), w.AppointmentId);
        },
        ["IFamilyAppointments.CancelAsync"] = async (w, a) =>
        {
            var query = await QueryAsync(a.Subject);
            Denied(await AppointmentsOf(a).CancelAsync(query, w.Titular.LinkId, w.AppointmentId));
            await ForeignIdAsync(id => AppointmentsOf(a).CancelAsync(query, a.LinkId, id), w.AppointmentId);
        },
        ["IFamilyAppointments.ReadAppointmentAsync"] = async (w, a) =>
        {
            var query = await QueryAsync(a.Subject);
            Denied(await AppointmentsOf(a).ReadAppointmentAsync(query, w.Titular.LinkId, w.AppointmentId));
            await ForeignIdAsync(id => AppointmentsOf(a).ReadAppointmentAsync(query, a.LinkId, id), w.AppointmentId);
        },
    };

    private static void Denied<T>(ApplicationResult<T> result)
    {
        Assert.False(result.Ok, "Debía denegarse y se concedió.");
        Assert.Equal(ApplicationFailureCode.AccessDenied, result.Error!.Code);
    }

    /// <summary>Con el vínculo propio pero un identificador de otra cuenta: se deniega y la respuesta es idéntica a la de un identificador inventado, así que no confirma que exista.</summary>
    private static async Task ForeignIdAsync<T>(Func<Guid, Task<ApplicationResult<T>>> call, Guid foreignId)
    {
        var real = await call(foreignId);
        var invented = await call(Guid.NewGuid());
        Denied(real);
        Denied(invented);
        Assert.Equal(invented.Error!.Message, real.Error!.Message);
    }

    /// <summary>Un centro con dos residentes. La titular tiene un comunicado publicado, una cita confirmada (nacida de una solicitud aceptada) y otra solicitud propuesta y sin
    /// aceptar. Los intrusos son otro familiar del mismo residente y la familia del otro residente. Antes se comprueba que la titular sí puede con lo suyo: si no, la
    /// denegación de los demás no probaría nada.</summary>
    private static async Task<World> BuildWorldAsync()
    {
        var owner = await SetupAsync("Residente Barrera Titular");
        await CitasSolicitudesTests.ConfigureAsync(owner);
        var communicationId = await AddCommunicationAsync(
            owner, "Novedad de la familia titular.", FamilyCommunicationType.Ordinaria, DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-1));
        var titular = new Actor("la titular", owner.Subject, owner.LinkId, true);

        var first = await CitasSolicitudesTests.SentAsync(owner.Subject, owner.LinkId, AppointmentTeam.Medico);
        Assert.True((await CitasSolicitudesTests.ProposeAsync(owner, first.Id, await CitasSolicitudesTests.FarSlotAsync(owner, first.Id))).Ok);
        var accepted = await RequestsOf(titular).AcceptAsync(await QueryAsync(owner.Subject), owner.LinkId, first.Id);
        Assert.True(accepted.Ok, accepted.Error?.Message);
        Assert.NotNull(accepted.Value!.AppointmentId);
        var appointmentId = accepted.Value.AppointmentId.Value;

        var second = await CitasSolicitudesTests.SentAsync(owner.Subject, owner.LinkId, AppointmentTeam.Enfermeria);
        Assert.True((await CitasSolicitudesTests.ProposeAsync(owner, second.Id, await CitasSolicitudesTests.FarSlotAsync(owner, second.Id))).Ok);

        var query = await QueryAsync(owner.Subject);
        Assert.True((await PortalOf(titular).ReadCommunicationAsync(query, owner.LinkId, communicationId)).Ok);
        Assert.True((await RequestsOf(titular).ReadAsync(query, owner.LinkId, second.Id)).Ok);
        Assert.True((await AppointmentsOf(titular).ReadAppointmentAsync(query, owner.LinkId, appointmentId)).Ok);

        var relativeLink = await AddFamilyAsync(owner.Admin, owner.ResidentId, "Otro Familiar Barrera");
        var relativeSubject = $"portal-{Guid.NewGuid():N}"[..20];
        var grant = await AdministracionResidentesTests.Build(owner.Admin.ExternalSubject).GrantFamilyPortalAccessAsync(
            new GrantFamilyPortalAccessCommand(owner.Admin.ProfileScopeId, owner.Admin.CenterId, owner.ResidentId, relativeLink, relativeSubject));
        Assert.True(grant.Ok, grant.Error?.Message);
        await ChangeAsync(owner.Admin, owner.ResidentId, relativeLink, FamilyAuthorizationChange.Abrir, 0);
        await ChangeAsync(owner.Admin, owner.ResidentId, relativeLink, FamilyAuthorizationChange.Activar, 1);
        var neighbor = await CitasSolicitudesTests.OtherResidentFamilyAsync(owner, "Residente Barrera Vecino");

        return new World(
            owner, titular, communicationId, second.Id, appointmentId,
            new Actor("otro familiar del mismo residente", relativeSubject, relativeLink, true),
            new Actor("la familia de otro residente", neighbor.Subject, neighbor.LinkId, false));
    }

    /// <summary>Todo lo de la titular que una operación ajena podría tocar, para comprobar que no cambia.</summary>
    private static async Task<string> SnapshotAsync(World world)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var parameters = new { requestId = world.RequestId, appointmentId = world.AppointmentId, center = world.Owner.Admin.CenterId.Value };
        var request = await connection.ExecuteScalarAsync<string>(
            "SELECT CONCAT(estado_codigo, '|', CONVERT(VARCHAR(33), propuesta_en, 126)) FROM dbo.citas_solicitudes WHERE id = @requestId", parameters);
        var appointment = await connection.ExecuteScalarAsync<string>(
            "SELECT CONCAT(estado_codigo, '|', CONVERT(VARCHAR(33), inicio_en, 126)) FROM dbo.citas WHERE id = @appointmentId", parameters);
        var counts = await connection.QuerySingleAsync<(int Requests, int Appointments, int AppointmentChanges, int RequestChanges)>("""
            SELECT (SELECT COUNT(*) FROM dbo.citas_solicitudes WHERE centro_id = @center),
                   (SELECT COUNT(*) FROM dbo.citas WHERE centro_id = @center),
                   (SELECT COUNT(*) FROM dbo.citas_cambios WHERE centro_id = @center),
                   (SELECT COUNT(*) FROM dbo.citas_solicitudes_cambios WHERE centro_id = @center)
            """, parameters);
        return $"{request}|{appointment}|{counts}";
    }

    [Fact]
    public void Todo_metodo_de_un_puerto_de_familias_tiene_su_escenario_de_aislamiento()
    {
        var methods = typeof(FamilyPortalAccess).Assembly.GetTypes()
            .Where(type => type.IsInterface && type.Namespace == typeof(FamilyPortalAccess).Namespace)
            .SelectMany(type => type.GetMethods().Select(method => (Type: type, Method: method)))
            .Where(x => x.Method.GetParameters().FirstOrDefault()?.ParameterType == typeof(FamilyPortalAccess))
            .Select(x => $"{x.Type.Name}.{x.Method.Name}")
            .Order().ToList();

        var missing = methods.Where(name => !Scenarios.ContainsKey(name)).ToList();
        var obsolete = Scenarios.Keys.Where(name => !methods.Contains(name)).Order().ToList();

        Assert.True(missing.Count == 0,
            "Métodos de familias sin escenario de aislamiento: " + string.Join(", ", missing) +
            ". Añade su escenario a Scenarios: la familia B, del mismo centro, no debe alcanzar lo de la familia A.");
        Assert.True(obsolete.Count == 0, "Escenarios de métodos que ya no existen, quítalos: " + string.Join(", ", obsolete));
        Assert.NotEmpty(methods);
    }

    [Fact]
    public async Task Ningun_familiar_alcanza_lo_de_otra_familia_del_mismo_centro_por_ningun_metodo_del_portal()
    {
        var world = await BuildWorldAsync();
        var before = await SnapshotAsync(world);
        var failures = new List<string>();

        foreach (var (name, scenario) in Scenarios.OrderBy(s => s.Key))
        {
            foreach (var intruder in new[] { world.Relative, world.Neighbor })
            {
                try
                {
                    await scenario(world, intruder);
                }
                catch (Exception ex)
                {
                    failures.Add($"{name} como {intruder.Name}: {ex.Message.Split('\n')[0]}");
                }
            }
        }

        Assert.True(failures.Count == 0, "Operaciones que dejan pasar a una familia ajena:\n" + string.Join("\n", failures));
        Assert.Equal(before, await SnapshotAsync(world));
    }
}

file sealed class FixedBarreraSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
