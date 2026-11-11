using Microsoft.Extensions.Options;
using ResidApp.Application.Ports;
using ResidApp.Infrastructure.Correo;
using ResidApp.Web.Security;

namespace ResidApp.Web.Correo;

/// <summary>
/// La tarea que mira cada minuto si CJ ha dejado respuestas sin avisar y ha dejado de cambiar (ver <see cref="AvisoRespuestasCj"/>). Sin <c>PendientesCj:AvisarA</c> o con el
/// correo apagado no toca el disco. Un fallo de una vuelta no la detiene. Necesita «Siempre activo» en la Web App, como la del aviso de cambio de cita.
/// </summary>
public sealed class AvisoRespuestasCjWorker(
    RespuestasCjStore store, IServiceScopeFactory scopes, IOptions<CorreoOptions> options, IConfiguration configuration, ILogger<AvisoRespuestasCjWorker> logger,
    TimeProvider clock, TimeSpan? interval = null) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    public const int DefaultQuietMinutes = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var address = configuration["PendientesCj:AvisarA"];
        var quietMinutes = int.TryParse(configuration["PendientesCj:SilencioAvisoMinutos"], out var minutes) && minutes >= 0 ? minutes : DefaultQuietMinutes;
        var notAllowedReported = false;
        using var timer = new PeriodicTimer(interval ?? Interval);
        try
        {
            do
            {
                var settings = options.Value.ToSettings();
                if (!settings.Active || string.IsNullOrWhiteSpace(address))
                {
                    continue;
                }

                if (!AvisoRespuestasCj.RecipientAllowed(settings, address))
                {
                    if (!notAllowedReported)
                    {
                        logger.LogWarning("Aviso de respuestas de CJ: la dirección de PendientesCj:AvisarA no está entre las permitidas (Correo:DireccionesPermitidas); no se envía.");
                        notAllowedReported = true;
                    }

                    continue;
                }

                try
                {
                    using var scope = scopes.CreateScope();
                    var notice = new AvisoRespuestasCj(
                        store, scope.ServiceProvider.GetRequiredService<IEmailSender>(), settings, address, TimeSpan.FromMinutes(quietMinutes),
                        message => logger.LogWarning("{Mensaje}", message));
                    await notice.ProcessAsync(clock.GetUtcNow(), stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("Aviso de respuestas de CJ: la vuelta de la tarea falló ({Tipo})", ex.GetType().Name);
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
