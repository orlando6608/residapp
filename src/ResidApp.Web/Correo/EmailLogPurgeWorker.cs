using ResidApp.Application.Ports;

namespace ResidApp.Web.Correo;

/// <summary>
/// La purga del registro de envíos de correo (ADR 0012, script 0085): una vez al día borra lo que lleva más de 12 meses, con el procedimiento de la base de datos (el plazo no está
/// aquí). Usa la misma conexión propia que la tarea de los avisos (<c>ConnectionStrings:ResidAppTareas</c>): con el usuario limitado de la aplicación no tiene permiso, así que antes
/// comprueba la identidad y, si no es del rol, lo registra UNA vez y no hace nada (la aplicación sigue funcionando). Un fallo de una vuelta no la detiene. Solo registra cuántas filas
/// borró, nunca datos de ninguna.
/// </summary>
public sealed class EmailLogPurgeWorker(
    IServiceScopeFactory scopes, ChangeNoticeTaskIdentity identity, ILogger<EmailLogPurgeWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>Espera tras arrancar, para no competir con el arranque (migraciones, primeras peticiones).</summary>
    public static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, stoppingToken);
            var identityWarningLogged = false;
            using var timer = new PeriodicTimer(Interval);
            do
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var purge = scope.ServiceProvider.GetRequiredService<IEmailLogPurge>();
                    if (!await purge.HasTaskIdentityAsync(stoppingToken))
                    {
                        if (!identityWarningLogged)
                        {
                            logger.LogWarning(
                                "Registro de envíos de correo: no se purga porque la conexión de la tarea no es del rol 'residapp_tareas' ({Cadena}). El registro seguirá creciendo hasta corregirlo.",
                                identity.HasOwnConnection ? "falta darle el rol al usuario" : "falta la cadena de conexión 'ResidAppTareas'");
                            identityWarningLogged = true;
                        }

                        continue;
                    }

                    identityWarningLogged = false;
                    var deleted = await purge.PurgeAsync(stoppingToken);
                    logger.LogInformation("Registro de envíos de correo: {Borradas} filas con más de 12 meses borradas", deleted);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("Registro de envíos de correo: la purga falló ({Tipo})", ex.GetType().Name);
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
