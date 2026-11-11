using Microsoft.Extensions.Options;
using ResidApp.Application.Ports;
using ResidApp.Infrastructure.Correo;
using ResidApp.Web.Security;

namespace ResidApp.Web.Correo;

/// <summary>
/// La tarea que mira cada minuto si ingeniería ha dejado notas nuevas en los documentos de CJ y, si las hay, se lo avisa por correo (ver <see cref="AvisoNotasCj"/>). Sin
/// <c>PendientesCj:AvisarACj</c> o con el correo apagado no toca el disco. Un fallo de una vuelta no la detiene. Necesita «Siempre activo» en la Web App.
/// </summary>
public sealed class AvisoNotasCjWorker(
    RespuestasCjStore store, IServiceScopeFactory scopes, IOptions<CorreoOptions> options, IConfiguration configuration, ILogger<AvisoNotasCjWorker> logger,
    TimeProvider clock, TimeSpan? interval = null, string? documentsFolder = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var address = configuration["PendientesCj:AvisarACj"]?.Trim();
        var folder = documentsFolder ?? Path.Combine(AppContext.BaseDirectory, "pendientes-cj");
        var memory = new NotasAvisoMemoria();
        var notAllowedReported = false;
        using var timer = new PeriodicTimer(interval ?? AvisoRespuestasCjWorker.Interval);
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
                        logger.LogWarning("Aviso de notas a CJ: la dirección de PendientesCj:AvisarACj no está entre las permitidas (Correo:DireccionesPermitidas); no se envía.");
                        notAllowedReported = true;
                    }

                    continue;
                }

                try
                {
                    using var scope = scopes.CreateScope();
                    var notice = new AvisoNotasCj(store, folder, scope.ServiceProvider.GetRequiredService<IEmailSender>(), settings, address, memory, message => logger.LogWarning("{Mensaje}", message));
                    await notice.ProcessAsync(clock.GetUtcNow(), stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("Aviso de notas a CJ: la vuelta de la tarea falló ({Tipo})", ex.GetType().Name);
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
