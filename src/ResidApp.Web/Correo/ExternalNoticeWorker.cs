using Microsoft.Extensions.Options;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Infrastructure.Correo;

namespace ResidApp.Web.Correo;

/// <summary>
/// La tarea programada de los avisos de citas externas a la familia (PR 6; script 0105): cada minuto pide a la base de datos que reserve los avisos del día (solo dentro de la franja de las
/// 10:00 a las 18:59, hora del centro) y manda los reservados. Con el correo apagado (<c>Correo:Activo</c> o <c>Correo:AvisoCitaExterna</c>) no toca la base; cada centro, además, tiene su propio
/// interruptor, que la base comprueba al reservar y al enviar. Un fallo de una vuelta no la detiene, y no registra la excepción entera. Necesita «Siempre activo» en la Web App. Usa la misma conexión
/// propia que el resto de avisos (<see cref="ChangeNoticeTaskIdentity"/>, script 0083): antes de cada vuelta comprueba que su conexión es del rol de la tarea y, si no, lo registra COMO ERROR
/// (una vez, hasta que se corrija) y no procesa nada.
/// </summary>
public sealed class ExternalNoticeWorker(
    IServiceScopeFactory scopes, IOptions<CorreoOptions> options, ChangeNoticeTaskIdentity identity, ILogger<ExternalNoticeWorker> logger, TimeSpan? interval = null) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.ToSettings();
        var identityErrorLogged = false;
        using var timer = new PeriodicTimer(interval ?? Interval);
        try
        {
            do
            {
                if (!(settings.Active && settings.ExternalNotice))
                {
                    continue;
                }

                try
                {
                    using var scope = scopes.CreateScope();
                    if (!await scope.ServiceProvider.GetRequiredKeyedService<IExternalNoticeStore>(ChangeNoticeWorker.ServiceKey).HasTaskIdentityAsync(stoppingToken))
                    {
                        if (!identityErrorLogged)
                        {
                            logger.LogError(
                                "Avisos de citas externas: la conexión de la tarea no es del rol 'residapp_tareas' ({Cadena}); con el usuario limitado de la aplicación no verá ningún aviso pendiente. No se envía nada hasta corregirlo.",
                                identity.HasOwnConnection ? "falta darle el rol al usuario" : "falta la cadena de conexión 'ResidAppTareas'");
                            identityErrorLogged = true;
                        }

                        continue;
                    }

                    identityErrorLogged = false;
                    var service = scope.ServiceProvider.GetRequiredKeyedService<ExternalNoticeService>(ChangeNoticeWorker.ServiceKey);
                    var now = DateTimeOffset.UtcNow;
                    await service.ReserveDueAsync(now, stoppingToken);
                    await service.ProcessDueAsync(now, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("Avisos de citas externas: la vuelta de la tarea falló ({Tipo})", ex.GetType().Name);
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
