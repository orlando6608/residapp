using Microsoft.Extensions.Options;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Infrastructure.Correo;

namespace ResidApp.Web.Correo;

/// <summary>Si la tarea programada tiene su propio usuario de base de datos (<c>ConnectionStrings:ResidAppTareas</c>).</summary>
public sealed record ChangeNoticeTaskIdentity(bool HasOwnConnection);

/// <summary>
/// La tarea programada de la aplicación (ADR 0012): cada minuto manda los avisos de cambio de cita que llevan reservados el tiempo de espera. Con el correo apagado
/// (<c>Correo:Activo</c> o <c>Correo:AvisoCambioCita</c>) no toca la base. Un fallo de una vuelta no la detiene, y no registra la excepción entera. Necesita
/// «Siempre activo» en la Web App: una aplicación dormida no ejecuta nada. Con varias copias de la aplicación, el servicio reclama cada aviso antes de enviarlo.
/// No hay nadie conectado, así que no tiene ámbito para la seguridad por filas: usa su propio usuario (<see cref="ChangeNoticeTaskIdentity"/>, script 0083). Con el usuario
/// limitado de la aplicación vería cero filas sin ningún error, así que antes de cada vuelta comprueba que su conexión es del rol de la tarea y, si no, lo registra COMO ERROR
/// (una vez, hasta que se corrija) y no procesa nada.
/// </summary>
public sealed class ChangeNoticeWorker(
    IServiceScopeFactory scopes, IOptions<CorreoOptions> options, ChangeNoticeTaskIdentity identity, ILogger<ChangeNoticeWorker> logger) : BackgroundService
{
    public const string ServiceKey = "tareas";

    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.ToSettings();
        var identityErrorLogged = false;
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                if (!(settings.Active && settings.ChangeNotice))
                {
                    continue;
                }

                try
                {
                    using var scope = scopes.CreateScope();
                    if (!await scope.ServiceProvider.GetRequiredKeyedService<IChangeNoticeStore>(ServiceKey).HasTaskIdentityAsync(stoppingToken))
                    {
                        if (!identityErrorLogged)
                        {
                            logger.LogError(
                                "Aviso de cambio de cita: la conexión de la tarea no es del rol 'residapp_tareas' ({Cadena}); con el usuario limitado de la aplicación no verá ningún aviso pendiente. No se envía nada hasta corregirlo.",
                                identity.HasOwnConnection ? "falta darle el rol al usuario" : "falta la cadena de conexión 'ResidAppTareas'");
                            identityErrorLogged = true;
                        }

                        continue;
                    }

                    identityErrorLogged = false;
                    await scope.ServiceProvider.GetRequiredKeyedService<ChangeNoticeService>(ServiceKey).ProcessDueAsync(DateTimeOffset.UtcNow, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("Aviso de cambio de cita: la vuelta de la tarea falló ({Tipo})", ex.GetType().Name);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // la aplicación se está parando
        }
    }
}
