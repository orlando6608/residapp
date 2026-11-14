using Microsoft.Extensions.Options;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Infrastructure.Correo;

namespace ResidApp.Web.Correo;

/// <summary>
/// La tarea programada del recordatorio de cita (ADR 0012; docs/privacidad/recordatorio-cita-por-correo.md): cada minuto pide a la base de datos que reserve los recordatorios de las
/// citas de mañana (solo dentro de la franja de las 10:00 a las 18:59, hora de Madrid) y manda los reservados. Con el correo apagado (<c>Correo:Activo</c> o <c>Correo:RecordatorioCita</c>)
/// no toca la base. Un fallo de una vuelta no la detiene, y no registra la excepción entera. Necesita «Siempre activo» en la Web App: una aplicación dormida a las 10:00 pierde el
/// recordatorio del día sin dejar fila ni error. Usa la misma conexión propia que el aviso de cambio (<see cref="ChangeNoticeTaskIdentity"/>, script 0083): con el usuario limitado
/// de la aplicación vería cero filas sin ningún error, así que antes de cada vuelta comprueba que su conexión es del rol de la tarea y, si no, lo registra COMO ERROR (una vez,
/// hasta que se corrija) y no procesa nada.
/// </summary>
public sealed class ReminderWorker(
    IServiceScopeFactory scopes, IOptions<CorreoOptions> options, ChangeNoticeTaskIdentity identity, ILogger<ReminderWorker> logger, TimeSpan? interval = null) : BackgroundService
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
                if (!(settings.Active && settings.Reminder))
                {
                    continue;
                }

                try
                {
                    using var scope = scopes.CreateScope();
                    if (!await scope.ServiceProvider.GetRequiredKeyedService<IReminderStore>(ChangeNoticeWorker.ServiceKey).HasTaskIdentityAsync(stoppingToken))
                    {
                        if (!identityErrorLogged)
                        {
                            logger.LogError(
                                "Recordatorio de cita: la conexión de la tarea no es del rol 'residapp_tareas' ({Cadena}); con el usuario limitado de la aplicación no verá ningún recordatorio pendiente. No se envía nada hasta corregirlo.",
                                identity.HasOwnConnection ? "falta darle el rol al usuario" : "falta la cadena de conexión 'ResidAppTareas'");
                            identityErrorLogged = true;
                        }

                        continue;
                    }

                    identityErrorLogged = false;
                    var service = scope.ServiceProvider.GetRequiredKeyedService<ReminderService>(ChangeNoticeWorker.ServiceKey);
                    var now = DateTimeOffset.UtcNow;
                    await service.ReserveDueAsync(now, stoppingToken);
                    await service.ProcessDueAsync(now, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("Recordatorio de cita: la vuelta de la tarea falló ({Tipo})", ex.GetType().Name);
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
