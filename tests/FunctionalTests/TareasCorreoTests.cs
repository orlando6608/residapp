using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Infrastructure.Correo;
using ResidApp.Shared;
using ResidApp.Web.Correo;

namespace ResidApp.FunctionalTests;

/// <summary>Las dos tareas programadas del correo (ADR 0012): la de los avisos de cambio de cita (<see cref="ChangeNoticeWorker"/>) y la de la purga del registro de envíos
/// (<see cref="EmailLogPurgeWorker"/>). Sus reglas están en el código de cada tarea, no en el servicio que llaman: con el correo apagado no tocan la base, sin el rol de la tarea
/// lo registran UNA vez y no hacen nada, un fallo de una vuelta no las detiene y nunca registran la excepción entera (la de SQL o la de ACS puede llevar una dirección).
/// Con una vuelta de 10 ms en vez de un minuto o un día; sin base de datos ni envíos.</summary>
public class TareasCorreoTests
{
    private const string Address = "persona.ficticia@example.test";
    private static readonly TimeSpan Round = TimeSpan.FromMilliseconds(10);

    private static CorreoOptions Complete(bool active = true, bool changeNotice = true) => new()
    {
        Activo = active, AvisoCambioCita = changeNotice, CadenaConexion = "endpoint=https://ficticio.example.test/;accesskey=ficticia",
        UrlBase = "https://dev.example.test", DireccionesPermitidas = [Address],
    };

    private static async Task WaitUntilAsync(Func<bool> condition, string expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "No ocurrió a tiempo: " + expected);
            await Task.Delay(5);
        }
    }

    // ---- La tarea de los avisos de cambio de cita ----

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task AvisoDeCambio_ConElCorreoOElInterruptorPropioApagados_NoTocaLaBaseNiRegistraNada(bool active, bool changeNotice)
    {
        await using var task = new ChangeNoticeHarness(Complete(active, changeNotice));
        await task.StartAsync();
        await Task.Delay(200); // unas veinte vueltas

        Assert.Equal(0, task.Store.IdentityChecks);
        Assert.Equal(0, task.Store.ListDueCalls);
        Assert.Empty(task.Log.Snapshot());
    }

    [Theory]
    [InlineData(true, "falta darle el rol al usuario")]
    [InlineData(false, "falta la cadena de conexión 'ResidAppTareas'")]
    public async Task AvisoDeCambio_SinElRolDeLaTarea_LoRegistraUnaSolaVezComoError_YNoProcesaNada(bool ownConnection, string cause)
    {
        await using var task = new ChangeNoticeHarness(Complete(), ownConnection);
        task.Store.HasRole = false;
        await task.StartAsync();
        await WaitUntilAsync(() => task.Store.IdentityChecks >= 5, "cinco vueltas sin el rol");

        var entry = Assert.Single(task.Log.Snapshot());
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("residapp_tareas", entry.Message);
        Assert.Contains(cause, entry.Message);
        Assert.Equal(0, task.Store.ListDueCalls);
    }

    [Fact]
    public async Task AvisoDeCambio_AlCorregirseElRol_Procesa_YSiLoPierdeDeNuevoLoRegistraOtraVez()
    {
        await using var task = new ChangeNoticeHarness(Complete());
        task.Store.HasRole = false;
        await task.StartAsync();
        await WaitUntilAsync(() => task.Store.IdentityChecks >= 3, "tres vueltas sin el rol");
        Assert.Single(task.Log.Snapshot(), e => e.Level == LogLevel.Error);

        task.Store.HasRole = true;
        await WaitUntilAsync(() => task.Store.ListDueCalls >= 2, "procesar al volver el rol");

        task.Store.HasRole = false;
        await WaitUntilAsync(() => task.Log.Snapshot().Count(e => e.Level == LogLevel.Error) == 2, "el segundo error");
        var checks = task.Store.IdentityChecks;
        await WaitUntilAsync(() => task.Store.IdentityChecks >= checks + 3, "tres vueltas más sin el rol");

        Assert.Equal(2, task.Log.Snapshot().Count(e => e.Level == LogLevel.Error));
    }

    [Fact]
    public async Task AvisoDeCambio_SiUnaVueltaFalla_NoSeDetiene_YNoRegistraLaExcepcionEntera()
    {
        await using var task = new ChangeNoticeHarness(Complete());
        task.Store.Explode = true;
        await task.StartAsync();
        await WaitUntilAsync(() => task.Store.IdentityChecks >= 3, "tres vueltas que fallan");

        task.Store.Explode = false;
        await WaitUntilAsync(() => task.Store.ListDueCalls >= 1, "seguir tras el fallo");

        var entries = task.Log.Snapshot();
        Assert.NotEmpty(entries);
        Assert.All(entries, entry =>
        {
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Null(entry.Exception);
            Assert.DoesNotContain(Address, entry.Message);
            Assert.Contains(nameof(InvalidOperationException), entry.Message);
        });
    }

    [Fact]
    public async Task AvisoDeCambio_ConElRol_ProcesaEnCadaVuelta_SinRegistrarNada()
    {
        await using var task = new ChangeNoticeHarness(Complete());
        await task.StartAsync();
        await WaitUntilAsync(() => task.Store.ListDueCalls >= 3, "tres vueltas procesadas");

        Assert.Empty(task.Log.Snapshot());
    }

    // ---- La tarea de la purga del registro de envíos ----

    [Theory]
    [InlineData(true, "falta darle el rol al usuario")]
    [InlineData(false, "falta la cadena de conexión 'ResidAppTareas'")]
    public async Task Purga_SinElRolDeLaTarea_AvisaUnaSolaVez_YNoPurga(bool ownConnection, string cause)
    {
        await using var task = new PurgeHarness(ownConnection);
        task.Purge.HasRole = false;
        await task.StartAsync();
        await WaitUntilAsync(() => task.Purge.IdentityChecks >= 5, "cinco vueltas sin el rol");

        var entry = Assert.Single(task.Log.Snapshot());
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("residapp_tareas", entry.Message);
        Assert.Contains(cause, entry.Message);
        Assert.Equal(0, task.Purge.PurgeCalls);
    }

    [Fact]
    public async Task Purga_ConElRol_PurgaEnCadaVuelta_YRegistraSoloCuantasFilasBorro()
    {
        await using var task = new PurgeHarness();
        task.Purge.Deleted = 7;
        await task.StartAsync();
        await WaitUntilAsync(() => task.Purge.PurgeCalls >= 2, "dos purgas");

        var entries = task.Log.Snapshot();
        Assert.All(entries, entry =>
        {
            Assert.Equal(LogLevel.Information, entry.Level);
            Assert.Contains("7 filas", entry.Message);
            Assert.Null(entry.Exception);
        });
    }

    [Fact]
    public async Task Purga_SiUnaVueltaFalla_NoSeDetiene_YNoRegistraLaExcepcionEntera()
    {
        await using var task = new PurgeHarness();
        task.Purge.Explode = true;
        await task.StartAsync();
        await WaitUntilAsync(() => task.Purge.PurgeCalls >= 3, "tres vueltas que fallan");

        var entries = task.Log.Snapshot();
        Assert.NotEmpty(entries);
        Assert.All(entries, entry =>
        {
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Null(entry.Exception);
            Assert.DoesNotContain(Address, entry.Message);
            Assert.Contains(nameof(InvalidOperationException), entry.Message);
        });
    }

    [Fact]
    public async Task Purga_EsperaElRetrasoDeArranqueAntesDeLaPrimeraVuelta()
    {
        // Si la espera no se respetara, habría vueltas en estos 200 ms; si no se cancelara al parar, el DisposeAsync se quedaría colgado.
        await using var task = new PurgeHarness(startDelay: TimeSpan.FromHours(1));
        await task.StartAsync();
        await Task.Delay(200);

        Assert.Equal(0, task.Purge.IdentityChecks);
        Assert.Equal(0, task.Purge.PurgeCalls);
    }

    // ---- La tarea que cierra los avisos que quedaron PENDIENTE (script 0089) ----

    [Theory]
    [InlineData(true, "falta darle el rol al usuario")]
    [InlineData(false, "falta la cadena de conexión 'ResidAppTareas'")]
    public async Task Cierre_SinElRolDeLaTarea_AvisaUnaSolaVez_YNoCierraNada(bool ownConnection, string cause)
    {
        await using var task = new CloserHarness(ownConnection);
        task.Closer.HasRole = false;
        await task.StartAsync();
        await WaitUntilAsync(() => task.Closer.IdentityChecks >= 5, "cinco vueltas sin el rol");

        var entry = Assert.Single(task.Log.Snapshot());
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("residapp_tareas", entry.Message);
        Assert.Contains(cause, entry.Message);
        Assert.Equal(0, task.Closer.CloseCalls);
    }

    [Fact]
    public async Task Cierre_ConElRol_SiCierraFilas_RegistraSoloElRecuento_ComoAdvertencia()
    {
        await using var task = new CloserHarness();
        task.Closer.Closed = 3;
        await task.StartAsync();
        await WaitUntilAsync(() => task.Closer.CloseCalls >= 2, "dos vueltas");

        var entries = task.Log.Snapshot();
        Assert.NotEmpty(entries);
        Assert.All(entries, entry =>
        {
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Contains("3 filas", entry.Message);
            Assert.Null(entry.Exception);
        });
    }

    [Fact]
    public async Task Cierre_ConElRol_SinFilasQueCerrar_NoRegistraNada()
    {
        await using var task = new CloserHarness();
        await task.StartAsync();
        await WaitUntilAsync(() => task.Closer.CloseCalls >= 3, "tres vueltas");

        Assert.Empty(task.Log.Snapshot());
    }

    [Fact]
    public async Task Cierre_SiUnaVueltaFalla_NoSeDetiene_YNoRegistraLaExcepcionEntera()
    {
        await using var task = new CloserHarness();
        task.Closer.Explode = true;
        await task.StartAsync();
        await WaitUntilAsync(() => task.Closer.CloseCalls >= 3, "tres vueltas que fallan");

        var entries = task.Log.Snapshot();
        Assert.NotEmpty(entries);
        Assert.All(entries, entry =>
        {
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Null(entry.Exception);
            Assert.DoesNotContain(Address, entry.Message);
            Assert.Contains(nameof(InvalidOperationException), entry.Message);
        });
    }

    [Fact]
    public async Task Cierre_EsperaElRetrasoDeArranqueAntesDeLaPrimeraVuelta()
    {
        await using var task = new CloserHarness(startDelay: TimeSpan.FromHours(1));
        await task.StartAsync();
        await Task.Delay(200);

        Assert.Equal(0, task.Closer.IdentityChecks);
        Assert.Equal(0, task.Closer.CloseCalls);
    }

    // ---- Dobles ----

    private sealed class ChangeNoticeHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly ChangeNoticeWorker _worker;

        public ChangeNoticeHarness(CorreoOptions options, bool ownConnection = true)
        {
            _services = new ServiceCollection()
                .AddKeyedSingleton<IChangeNoticeStore>(ChangeNoticeWorker.ServiceKey, Store)
                .AddKeyedSingleton(ChangeNoticeWorker.ServiceKey, new ChangeNoticeService(Store, new RefusingSender(), options.ToSettings()))
                .BuildServiceProvider();
            _worker = new ChangeNoticeWorker(
                _services.GetRequiredService<IServiceScopeFactory>(), Microsoft.Extensions.Options.Options.Create(options),
                new ChangeNoticeTaskIdentity(ownConnection), Log, Round);
        }

        public FakeStore Store { get; } = new();

        public CapturingLogger<ChangeNoticeWorker> Log { get; } = new();

        public Task StartAsync() => _worker.StartAsync(default);

        public async ValueTask DisposeAsync()
        {
            await _worker.StopAsync(default);
            _worker.Dispose();
            await _services.DisposeAsync();
        }
    }

    private sealed class PurgeHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly EmailLogPurgeWorker _worker;

        public PurgeHarness(bool ownConnection = true, TimeSpan? startDelay = null)
        {
            _services = new ServiceCollection().AddScoped<IEmailLogPurge>(_ => Purge).BuildServiceProvider();
            _worker = new EmailLogPurgeWorker(
                _services.GetRequiredService<IServiceScopeFactory>(), new ChangeNoticeTaskIdentity(ownConnection), Log, Round, startDelay ?? TimeSpan.Zero);
        }

        public FakePurge Purge { get; } = new();

        public CapturingLogger<EmailLogPurgeWorker> Log { get; } = new();

        public Task StartAsync() => _worker.StartAsync(default);

        public async ValueTask DisposeAsync()
        {
            await _worker.StopAsync(default);
            _worker.Dispose();
            await _services.DisposeAsync();
        }
    }

    private sealed class CloserHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly UnresolvedNoticeWorker _worker;

        public CloserHarness(bool ownConnection = true, TimeSpan? startDelay = null)
        {
            _services = new ServiceCollection().AddScoped<IUnresolvedNoticeCloser>(_ => Closer).BuildServiceProvider();
            _worker = new UnresolvedNoticeWorker(
                _services.GetRequiredService<IServiceScopeFactory>(), new ChangeNoticeTaskIdentity(ownConnection), Log, Round, startDelay ?? TimeSpan.Zero);
        }

        public FakeCloser Closer { get; } = new();

        public CapturingLogger<UnresolvedNoticeWorker> Log { get; } = new();

        public Task StartAsync() => _worker.StartAsync(default);

        public async ValueTask DisposeAsync()
        {
            await _worker.StopAsync(default);
            _worker.Dispose();
            await _services.DisposeAsync();
        }
    }

    private sealed class FakeCloser : IUnresolvedNoticeCloser
    {
        private int _identityChecks;
        private int _closeCalls;
        public volatile bool HasRole = true;
        public volatile bool Explode;
        public int Closed;

        public int IdentityChecks => Volatile.Read(ref _identityChecks);

        public int CloseCalls => Volatile.Read(ref _closeCalls);

        public Task<bool> HasTaskIdentityAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _identityChecks);
            return Task.FromResult(HasRole);
        }

        public Task<int> CloseAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _closeCalls);
            return Explode ? throw new InvalidOperationException("fallo con " + Address) : Task.FromResult(Closed);
        }
    }

    /// <summary>El almacén de los avisos: solo cuenta las llamadas de la tarea. Sin avisos pendientes, así que nada se reclama ni se envía.</summary>
    private sealed class FakeStore : IChangeNoticeStore
    {
        private int _identityChecks;
        private int _listDueCalls;
        public volatile bool HasRole = true;
        public volatile bool Explode;

        public int IdentityChecks => Volatile.Read(ref _identityChecks);

        public int ListDueCalls => Volatile.Read(ref _listDueCalls);

        public Task<bool> HasTaskIdentityAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _identityChecks);
            return Explode ? throw new InvalidOperationException("fallo con " + Address) : Task.FromResult(HasRole);
        }

        public Task<IReadOnlyList<PendingChangeNotice>> ListDueAsync(DateTimeOffset reservedBefore, int limit, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _listDueCalls);
            return Task.FromResult<IReadOnlyList<PendingChangeNotice>>([]);
        }

        public Task RegisterAsync(CenterId centerId, Guid appointmentId, int changeNumber, string language, string templateVersion, DateTimeOffset now, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> ClaimAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<ChangeNoticePreparation> PrepareAsync(PendingChangeNotice notice, DateTimeOffset now, CancellationToken ct = default) => throw new NotSupportedException();

        public Task ResolveAsync(Guid id, string state, string? reason, string? operationId, int attempts, DateTimeOffset now, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FakePurge : IEmailLogPurge
    {
        private int _identityChecks;
        private int _purgeCalls;
        public volatile bool HasRole = true;
        public volatile bool Explode;
        public int Deleted;

        public int IdentityChecks => Volatile.Read(ref _identityChecks);

        public int PurgeCalls => Volatile.Read(ref _purgeCalls);

        public Task<bool> HasTaskIdentityAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _identityChecks);
            return Task.FromResult(HasRole);
        }

        public Task<int> PurgeAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _purgeCalls);
            return Explode ? throw new InvalidOperationException("fallo con " + Address) : Task.FromResult(Deleted);
        }
    }

    private sealed class RefusingSender : IEmailSender
    {
        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default) => throw new NotSupportedException("En estas pruebas no se envía nada.");
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<LogEntry> _entries = [];

        public List<LogEntry> Snapshot()
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_entries)
            {
                _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
            }
        }
    }
}
