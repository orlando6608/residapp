using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Infrastructure.Persistence;

namespace ResidApp.IntegrationTests.TestSupport;

/// <summary>El acceso a módulos como en desarrollo (ADR 0015): todos activos y la clave global puesta, para las pruebas que no tratan de los módulos sino de un caso de uso que los exige (el dictado).</summary>
internal static class ModulosActivos
{
    private sealed class Settings : IModuleSettings
    {
        public bool ActivateAll => true;

        public bool IsThirdPartyConfigured(string moduleCode) => true;
    }

    public static CenterModuleAccess Acceso() => new(
        new SqlCenterModuleRepository(TestDatabase.ConnectionFactory), new Settings(), new FakeEnvironmentMarker(development: true), null!, null!, TimeProvider.System);
}
