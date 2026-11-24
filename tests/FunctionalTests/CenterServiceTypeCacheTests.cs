using Microsoft.Extensions.Logging.Abstractions;
using ResidApp.Application.Ports;
using ResidApp.Domain.Platform;
using ResidApp.Web.Security;

namespace ResidApp.FunctionalTests;

/// <summary>La caché del tipo de servicio del centro del ámbito activo (ADR 0017): unos segundos, por cuenta y ámbito, y un fallo de lectura no se cachea.</summary>
public class CenterServiceTypeCacheTests
{
    private sealed class FakeDirectory(Func<CenterServiceType?> read) : ICenterServiceTypeDirectory
    {
        public int Reads { get; private set; }

        public Task<CenterServiceType?> FindForScopeAsync(string externalSubject, Guid profileScopeId, CancellationToken ct = default)
        {
            Reads++;
            return Task.FromResult(read());
        }
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly Guid Scope = Guid.NewGuid();

    private static Task<CenterServiceType?> Get(CenterServiceTypeCache cache, ICenterServiceTypeDirectory directory, TimeProvider clock, string subject = "cuenta", Guid? scope = null) =>
        cache.GetAsync(subject, scope ?? Scope, directory, NullLogger.Instance, clock, CancellationToken.None);

    [Fact]
    public async Task UnaSegundaLecturaDentroDelPlazoNoConsultaLaBaseDeDatos_YPasadoElPlazoSi()
    {
        var cache = new CenterServiceTypeCache();
        var directory = new FakeDirectory(() => CenterServiceType.CentroDeDia);
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 24, 10, 0, 0, TimeSpan.Zero));

        Assert.Equal(CenterServiceType.CentroDeDia, await Get(cache, directory, clock));
        clock.Now += CenterServiceTypeCache.Lifetime - TimeSpan.FromMilliseconds(1);
        await Get(cache, directory, clock);
        Assert.Equal(1, directory.Reads);

        clock.Now += TimeSpan.FromMilliseconds(1);
        await Get(cache, directory, clock);
        Assert.Equal(2, directory.Reads);
    }

    [Fact]
    public async Task LaCuentaYElAmbitoNoSeMezclan()
    {
        var cache = new CenterServiceTypeCache();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var day = new FakeDirectory(() => CenterServiceType.CentroDeDia);
        var residence = new FakeDirectory(() => CenterServiceType.Residencia);
        var otherScope = Guid.NewGuid();

        Assert.Equal(CenterServiceType.CentroDeDia, await Get(cache, day, clock, "cuenta-a"));
        Assert.Equal(CenterServiceType.Residencia, await Get(cache, residence, clock, "cuenta-b"));
        Assert.Equal(CenterServiceType.Residencia, await Get(cache, residence, clock, "cuenta-a", otherScope));
        Assert.Equal(CenterServiceType.CentroDeDia, await Get(cache, residence, clock, "cuenta-a"));
    }

    [Fact]
    public async Task UnFalloDeLecturaDevuelveNuloYNoSeCachea()
    {
        var cache = new CenterServiceTypeCache();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var failing = true;
        var directory = new FakeDirectory(() => failing ? throw new InvalidOperationException("SQL caído") : CenterServiceType.CentroDeDia);

        Assert.Null(await Get(cache, directory, clock));
        failing = false;
        Assert.Equal(CenterServiceType.CentroDeDia, await Get(cache, directory, clock));
        Assert.Equal(2, directory.Reads);
    }

    [Fact]
    public async Task UnTipoNuloSeCacheaIgual_YClearObligaAVolverALeer()
    {
        var cache = new CenterServiceTypeCache();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var directory = new FakeDirectory(() => null);

        Assert.Null(await Get(cache, directory, clock));
        Assert.Null(await Get(cache, directory, clock));
        Assert.Equal(1, directory.Reads);

        cache.Clear();
        await Get(cache, directory, clock);
        Assert.Equal(2, directory.Reads);
    }
}
