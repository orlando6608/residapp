using ResidApp.Application.Ports;

namespace ResidApp.Web.Correo;

/// <summary>
/// El cierre de los avisos de correo que quedaron PENDIENTE (ADR 0012, script 0089): cada 10 minutos los marca FALLIDO / RESULTADO_DESCONOCIDO con el procedimiento de la base de datos
/// (el plazo y el motivo no están aquí). Usa la misma conexión propia que las otras tareas (<c>ConnectionStrings:ResidAppTareas</c>): con el usuario limitado de la aplicación no tiene
/// permiso, así que antes comprueba la identidad y, si no es del rol, lo registra UNA vez y no hace nada. Un fallo de una vuelta no la detiene. Solo registra cuántas filas cerró
/// (como advertencia si es más de cero: indica reinicios o fallos al anotar), nunca datos de ninguna.
/// </summary>
public sealed class UnresolvedNoticeWorker(
    IServiceScopeFactory scopes, ChangeNoticeTaskIdentity identity, ILogger<UnresolvedNoticeWorker> logger, TimeSpan? interval = null, TimeSpan? startDelay = null) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    /// <summary>Espera tras arrancar, para no competir con el arranque (migraciones, primeras peticiones).</summary>
    public static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(startDelay ?? StartDelay, stoppingToken);
            var identityWarningLogged = false;
            using var timer = new PeriodicTimer(interval ?? Interval);
            do
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var closer = scope.ServiceProvider.GetRequiredService<IUnresolvedNoticeCloser>();
                    if (!await closer.HasTaskIdentityAsync(stoppingToken))
                    {
                        if (!identityWarningLogged)
                        {
                            logger.LogWarning(
                                "Avisos de correo sin resolver: no se cierran porque la conexión de la tarea no es del rol 'residapp_tareas' ({Cadena}). Seguirán como pendientes hasta corregirlo.",
                                identity.HasOwnConnection ? "falta darle el rol al usuario" : "falta la cadena de conexión 'ResidAppTareas'");
                            identityWarningLogged = true;
                        }

                        continue;
                    }

                    identityWarningLogged = false;
                    var closed = await closer.CloseAsync(stoppingToken);
                    if (closed > 0)
                    {
                        logger.LogWarning("Avisos de correo sin resolver: {Cerradas} filas pendientes cerradas como «resultado desconocido»", closed);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("Avisos de correo sin resolver: el cierre falló ({Tipo})", ex.GetType().Name);
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
