using ResidApp.Application.Email;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>El aviso a la dirección anterior cuando Administración cambia el correo con el que entra un familiar (ADR 0012, pieza 3): la plantilla y que ni un fallo del envío ni
/// uno del registro salgan hacia quien guardó, sin dejar la dirección en ningún registro (docs/privacidad/codigo-acceso-familiar-por-correo.md, C17, C25 y C26).</summary>
public class CambioCorreoFamiliaTests
{
    private const string Address = "anterior@example.test";
    private static readonly EmailSettings Open = new(true, "https://dev.example.test", "es", true, []);
    private static readonly CenterId Center = CenterId.From(Guid.NewGuid());

    private sealed class Sender(Func<CancellationToken, Task<EmailSendResult>> send) : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return send(ct);
        }
    }

    private sealed class Log(bool fails = false) : IFamilyEmailChangeLog
    {
        public List<FamilyEmailChangeEntry> Entries { get; } = [];

        public Task RecordAsync(FamilyEmailChangeEntry entry, CancellationToken ct = default)
        {
            Entries.Add(entry);
            return fails ? throw new InvalidOperationException(Address) : Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData("es", "Cambio en tu cuenta de ResidApp", "El centro ha cambiado o quitado el correo al que ResidApp envía tus códigos de acceso.", "No respondas a este correo.")]
    [InlineData("ca", "Canvi al teu compte de ResidApp", "El centre ha canviat o ha tret el correu on ResidApp envia els teus codis d'accés.", "No responguis aquest correu.")]
    public void LaPlantilla_NoLlevaNombresNiDireccionesNiEnlace_YElPieNoDaPorSuyaLaDireccion(string language, string subject, string first, string last)
    {
        var email = EmailChangeTemplate.Render(language);

        Assert.Equal(subject, email.Subject);
        Assert.StartsWith(first + "\n\n", email.PlainText);
        Assert.Contains(last, email.PlainText);
        Assert.DoesNotContain("@", email.PlainText);
        Assert.DoesNotContain("http", email.PlainText);
        Assert.DoesNotContain("<a ", email.Html);
        Assert.DoesNotContain("la tuya", email.PlainText);
        Assert.Same(EmailLayout.Icon, Assert.Single(email.InlineImages!));
    }

    [Fact]
    public async Task Enviado_SeAnotaUnaVez_SinLaDireccion()
    {
        var sender = new Sender(_ => Task.FromResult(EmailSendResult.Ok("op-1")));
        var log = new Log();
        var warnings = new List<string>();
        var family = Guid.NewGuid();

        var notice = await new FamilyEmailChangeNoticeService(log, sender, Open, TimeProvider.System, warnings.Add)
            .NotifyAsync(Center, new FamilyEmailChanged(family, Address));

        Assert.Equal(FamilyEmailNotice.Sent, notice);
        Assert.Equal(Address, Assert.Single(sender.Sent).To);
        var entry = Assert.Single(log.Entries);
        Assert.Equal((Center, family, "1", "es", "ENVIADO", (string?)null, "op-1", 1),
            (entry.CenterId, entry.FamilyId, entry.TemplateVersion, entry.Language, entry.State, entry.Reason, entry.OperationId, entry.Attempts));
        Assert.DoesNotContain(Address, entry.ToString());
        Assert.All(warnings, line => Assert.DoesNotContain(Address, line));
    }

    [Fact]
    public async Task SiElProveedorNoContesta_SeDaPorNoEnviado_ConSuPropioTiempoLimite()
    {
        var sender = new Sender(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return EmailSendResult.Ok(null);
        });
        var log = new Log();

        var notice = await new FamilyEmailChangeNoticeService(log, sender, Open, TimeProvider.System, sendTimeout: TimeSpan.FromMilliseconds(50))
            .NotifyAsync(Center, new FamilyEmailChanged(Guid.NewGuid(), Address));

        Assert.Equal(FamilyEmailNotice.NotSent, notice);
        Assert.Equal(("FALLIDO", "TIEMPO_AGOTADO"), (log.Entries[0].State, log.Entries[0].Reason));
    }

    [Fact]
    public async Task SiElRegistroFalla_NoSaleLaExcepcion_NiSeEscribeLaDireccion()
    {
        var warnings = new List<string>();

        var notice = await new FamilyEmailChangeNoticeService(
                new Log(fails: true), new Sender(_ => Task.FromResult(EmailSendResult.Ok(null))), Open, TimeProvider.System, warnings.Add)
            .NotifyAsync(Center, new FamilyEmailChanged(Guid.NewGuid(), Address));

        Assert.Equal(FamilyEmailNotice.Sent, notice);
        Assert.Contains(warnings, line => line.Contains("no se pudo registrar", StringComparison.Ordinal));
        Assert.All(warnings, line => Assert.DoesNotContain(Address, line));
    }

    [Fact]
    public void SinCorreoAlDarAcceso_SeDiceQueFalta()
    {
        var failure = ApplicationResultRunner.Normalize(new InvalidOperationException(FamilyAccessCodes.EmailRequired));

        Assert.Equal((ApplicationFailureCode.InvalidInput, ApplicationResultRunner.FamilyEmailRequiredMessage), (failure.Code, failure.Message));
    }
}
