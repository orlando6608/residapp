using ResidApp.Application.Ports;

namespace ResidApp.IntegrationTests.TestSupport;

/// <summary>La marca de entorno de la base de datos, fija: por defecto, base de producción (sin marca).</summary>
internal sealed class FakeEnvironmentMarker(bool development = false) : IEnvironmentMarker
{
    public Task<bool> IsDevelopmentDatabaseAsync(CancellationToken ct = default) => Task.FromResult(development);
}
