using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Appointments;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using static ResidApp.IntegrationTests.AdministracionResidentesTests;
using static ResidApp.IntegrationTests.PortalFamiliarTests;

namespace ResidApp.IntegrationTests;

/// <summary>Citas externas, la familia actúa: acciones a la vez sobre citas distintas. Dos citas distintas no comparten fila, así que no tienen por qué esperarse ni bloquearse entre sí; si SQL Server
/// elige una víctima de interbloqueo (error 1205), la aplicación lo devuelve como «No disponible» y la persona pierde su pulsación.</summary>
public class CitasExternasFamiliaActuaEstresTests
{
    private static SqlProfileScopeDirectoryProvider Scopes => new(TestDatabase.ConnectionFactory);

    private static ExternalAppointmentsApplicationService Staff(SeededProfile by) =>
        new(Scopes, new FixedFamilySessionIdentityProvider(by.ExternalSubject), new SqlExternalAppointmentRepository(TestDatabase.ConnectionFactory), TimeProvider.System);

    private static ExternalAppointmentFamilySettingsApplicationService Settings(SeededProfile by) =>
        new(Scopes, new FixedFamilySessionIdentityProvider(by.ExternalSubject), new SqlExternalAppointmentRepository(TestDatabase.ConnectionFactory), TimeProvider.System);

    private static FamilyExternalAppointmentsApplicationService Family(string subject) =>
        new(Scopes, new FixedFamilySessionIdentityProvider(subject), new SqlFamilyExternalAppointments(TestDatabase.ConnectionFactory), TimeProvider.System);

    private static DateOnly InDays(int days) => ResidApp.Domain.Modules.CenterModuleRules.TodayInMadrid(DateTimeOffset.UtcNow).AddDays(days);

    private static async Task<Guid> RegisterAsync(Setup setup, int days)
    {
        var registered = await Staff(setup.Admin).RegisterAsync(new RegisterExternalAppointmentCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId,
            new ExternalAppointment(InDays(days), new TimeOnly(10, 30), ExternalAppointmentType.Consulta, ExternalAppointmentSpecialty.Cardiologia, "Hospital Ficticio Norte",
                ExternalTransportMode.Familia, ExternalArrangementState.Pendiente, ExternalCompanionMode.Familiar, ExternalArrangementState.Pendiente, null, ExternalFamilyVisibility.Visible)));
        Assert.True(registered.Ok, registered.Error?.Message);
        return registered.Value;
    }

    [Fact]
    public async Task AccionesALaVezEnVariosCentros_NingunaPierdeSuPulsacion()
    {
        var worlds = new List<(Setup Setup, ResidApp.Application.UseCases.FamilyPortalQuery Access)>();
        for (var w = 0; w < 6; w++)
        {
            var setup = await SetupAsync($"Residente Actua Estres {w}");
            var open = await Settings(setup.Admin).SetCenterAsync(new SetExternalFamilyCenterCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, 0, true, true, true, true));
            Assert.True(open.Ok, open.Error?.Message);
            worlds.Add((setup, await QueryAsync(setup.Subject)));
        }

        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        for (var round = 0; round < 12; round++)
        {
            var work = new List<Task>();
            foreach (var (setup, access) in worlds)
            {
                var ids = new List<Guid>();
                for (var i = 0; i < 6; i++)
                {
                    ids.Add(await RegisterAsync(setup, 3 + i));
                }

                for (var i = 0; i < ids.Count; i++)
                {
                    var (id, action) = (ids[i], i % 2 == 0 ? FamilyArrangementAction.Refuse : FamilyArrangementAction.Take);
                    var s = setup;
                    var a = access;
                    work.Add(Task.Run(async () =>
                    {
                        var result = await Family(s.Subject).ArrangeAsync(a, s.LinkId, id, ExternalArrangementScope.Transporte, action);
                        if (!result.Ok)
                        {
                            failures.Add($"{action}: {result.Error?.Message}");
                        }
                    }));
                }
            }

            await Task.WhenAll(work);
        }

        Assert.True(failures.IsEmpty, $"{failures.Count} acciones fallaron: {string.Join(" | ", failures.Distinct())}");
    }
}

file sealed class FixedFamilySessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
