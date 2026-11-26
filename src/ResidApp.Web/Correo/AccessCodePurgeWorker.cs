using ResidApp.Application.Ports;

namespace ResidApp.Web.Correo;

/// <summary>
/// La purga de los códigos de acceso de la familia (script 0121): una vez al día cierra los caducados y borra los de más de 30 días con el procedimiento de la base de datos (el
/// plazo no está aquí). Como <see cref="EmailLogPurgeWorker"/>, usa la conexión propia de la tarea (<c>ConnectionStrings:ResidAppTareas</c>) y, si no es del rol, lo registra UNA
/// vez y no hace nada.
/// </summary>
public sealed class AccessCodePurgeWorker(
    IServiceScopeFactory scopes, ILogger<AccessCodePurgeWorker> logger, TimeSpan? interval = null, TimeSpan? startDelay = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(startDelay ?? EmailLogPurgeWorker.StartDelay, stoppingToken);
            var identityWarningLogged = false;
            using var timer = new PeriodicTimer(interval ?? EmailLogPurgeWorker.Interval);
            do
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var purge = scope.ServiceProvider.GetRequiredService<IAccessCodePurge>();
                    if (!await purge.HasTaskIdentityAsync(stoppingToken))
                    {
                        if (!identityWarningLogged)
                        {
                            logger.LogWarning("Códigos de acceso: no se purgan porque la conexión de la tarea no es del rol 'residapp_tareas'.");
                            identityWarningLogged = true;
                        }

                        continue;
                    }

                    identityWarningLogged = false;
                    logger.LogInformation("Códigos de acceso: {Borradas} filas con más de 30 días borradas", await purge.PurgeAsync(stoppingToken));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("Códigos de acceso: la purga falló ({Tipo})", ex.GetType().Name);
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
