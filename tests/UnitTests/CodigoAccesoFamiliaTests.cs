using System.Text.RegularExpressions;
using ResidApp.Application.Email;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>El código de acceso por correo de la familia (ADR 0012, pieza 3): la plantilla y que el servicio falle cerrado sin dejar la dirección ni el código en ningún
/// registro (docs/privacidad/codigo-acceso-familiar-por-correo.md, C3, C11 a C13).</summary>
public class CodigoAccesoFamiliaTests
{
    private const string Address = "familia@example.test";
    private static readonly string Key = new('k', 40);
    private static readonly AccountId Account = AccountId.From(Guid.NewGuid());
    private static readonly EmailSettings Open = new(true, "https://dev.example.test", "es", true, []);

    [Theory]
    [InlineData("es", "Tu código para entrar en ResidApp", "Tu código es: 048213", "No respondas a este correo.")]
    [InlineData("ca", "El teu codi per entrar a ResidApp", "El teu codi és: 048213", "No responguis aquest correu.")]
    public void LaPlantilla_LlevaElCodigoDentro_NoEnElAsunto_SinEnlaceNiDatos(string language, string subject, string codeLine, string lastLine)
    {
        var email = AccessCodeTemplate.Render(language, "048213");

        Assert.Equal(subject, email.Subject);
        Assert.StartsWith(codeLine + "\n\n", email.PlainText);
        Assert.Contains(lastLine, email.PlainText);
        Assert.DoesNotContain("048213", email.Subject);
        Assert.DoesNotContain("http", email.PlainText);
        Assert.DoesNotContain("<a ", email.Html);
        Assert.Same(EmailLayout.Icon, Assert.Single(email.InlineImages!));
        foreach (var forbidden in new[] { "residencia", "residència", "centro de día", "centre de dia" })
        {
            Assert.DoesNotContain(forbidden, email.PlainText + email.Html, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("es", "caduca en 10 minutos", "nunca te pedirá este código", "Puede que alguien sepa tu contraseña", "«Mi cuenta»", "avisa a tu centro")]
    [InlineData("ca", "caduca al cap de 10 minuts", "mai no et demanarà aquest codi", "Pot ser que algú sàpiga la teva contrasenya", "La pantalla de ResidApp és en castellà.", "avisa el teu centre")]
    public void LaPlantilla_LlevaLosAvisosAcordados_EnLosDosIdiomas(string language, params string[] sentences)
    {
        var text = AccessCodeTemplate.Render(language, "048213").PlainText;

        foreach (var sentence in sentences)
        {
            Assert.Contains(sentence, text);
        }
    }

    [Fact]
    public async Task ConTodoEnOrden_MandaUnSoloCorreo_YGuardaSoloElResumenConClave()
    {
        var store = new FakeStore();
        var sender = new FakeSender(_ => Task.FromResult(EmailSendResult.Ok("op-1")));
        var logs = new List<string>();

        var send = await Service(store, sender, log: logs.Add).SendAsync("sujeto", Account, []);

        Assert.Equal(AccessCodeSendOutcome.Sent, send.Outcome);
        var message = Assert.Single(sender.Sent);
        Assert.Equal(Address, message.To);
        var code = Regex.Match(message.PlainText, @"^Tu código es: (\d{6})\n").Groups[1].Value;
        Assert.Equal(6, code.Length);
        var reserved = Assert.Single(store.Reserved);
        Assert.Equal(send.CodeId, reserved.Id);
        Assert.Equal(32, reserved.Digest.Length);
        Assert.Equal(TimeSpan.FromMinutes(10), reserved.ExpiresAt - reserved.CreatedAt);
        Assert.Equal((reserved.Id, "op-1", (string?)null), Assert.Single(store.Recorded));
        Assert.All(logs, line => Assert.DoesNotContain(Address, line));
        Assert.All(logs, line => Assert.DoesNotContain(code, line));

        // El mismo código, comprobado con la misma clave y dirección, da el mismo resumen; con otra clave u otra dirección, no.
        await Service(store, sender).VerifyAsync("sujeto", Account, [], reserved.Id, code);
        Assert.Equal(reserved.Digest, store.Redeemed[^1]);
        await Service(store, sender, key: new string('x', 40)).VerifyAsync("sujeto", Account, [], reserved.Id, code);
        Assert.NotEqual(reserved.Digest, store.Redeemed[^1]);
        store.Address = "otra@example.test";
        await Service(store, sender).VerifyAsync("sujeto", Account, [], reserved.Id, code);
        Assert.NotEqual(reserved.Digest, store.Redeemed[^1]);
    }

    [Theory]
    [InlineData(false, 40)]
    [InlineData(true, 31)]
    [InlineData(true, 0)]
    public async Task SinCorreoOperativo_OSinClaveSuficiente_NoSeReservaNiSeManda(bool mailActive, int keyLength)
    {
        var store = new FakeStore();
        var sender = new FakeSender(_ => Task.FromResult(EmailSendResult.Ok(null)));
        var service = Service(store, sender, mailActive ? Open : EmailSettings.Off, keyLength == 0 ? null : new string('k', keyLength));

        var send = await service.SendAsync("sujeto", Account, []);

        Assert.Equal(AccessCodeSendOutcome.NotAvailable, send.Outcome);
        Assert.Empty(store.Reserved);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task SinDireccion_NoSeReservaNiSeManda()
    {
        var store = new FakeStore { Address = null };
        var sender = new FakeSender(_ => Task.FromResult(EmailSendResult.Ok(null)));

        Assert.Equal(AccessCodeSendOutcome.NoAddress, (await Service(store, sender).SendAsync("sujeto", Account, [])).Outcome);
        Assert.Equal(AccessCodeCheck.NotLive, await Service(store, sender).VerifyAsync("sujeto", Account, [], Guid.NewGuid(), "000000"));
        Assert.Empty(store.Reserved);
        Assert.Empty(store.Redeemed);
        Assert.Empty(sender.Sent);
    }

    [Theory]
    [InlineData(AccessCodeReservation.Blocked, AccessCodeSendOutcome.Blocked)]
    [InlineData(AccessCodeReservation.TooSoon, AccessCodeSendOutcome.TooSoon)]
    [InlineData(AccessCodeReservation.HourLimit, AccessCodeSendOutcome.HourLimit)]
    [InlineData(AccessCodeReservation.DayLimit, AccessCodeSendOutcome.DayLimit)]
    public async Task ConUnLimite_NoSeMandaNada(AccessCodeReservation reservation, AccessCodeSendOutcome expected)
    {
        var store = new FakeStore { Reservation = reservation };
        var sender = new FakeSender(_ => Task.FromResult(EmailSendResult.Ok(null)));

        var send = await Service(store, sender).SendAsync("sujeto", Account, []);

        Assert.Equal(expected, send.Outcome);
        Assert.Null(send.CodeId);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task DestinoNoPermitido_Rechazo_ErrorYTiempoAgotado_FallanCerrado_ConUnMotivoCerrado()
    {
        var logs = new List<string>();
        async Task<string?> FailureAsync(FakeSender sender, EmailSettings settings)
        {
            var store = new FakeStore();
            var send = await Service(store, sender, settings, log: logs.Add, timeout: TimeSpan.FromMilliseconds(50)).SendAsync("sujeto", Account, []);
            Assert.Equal(AccessCodeSendOutcome.SendFailed, send.Outcome);
            Assert.Null(send.CodeId);
            return Assert.Single(store.Recorded).Failure;
        }

        var notAllowed = new FakeSender(_ => Task.FromResult(EmailSendResult.Ok(null)));
        Assert.Equal(EmailLog.RecipientNotAllowed, await FailureAsync(notAllowed, Open with { AnyRecipient = false, AllowedRecipients = ["otra@example.test"] }));
        Assert.Empty(notAllowed.Sent);
        Assert.Equal(EmailLog.ProviderRejected, await FailureAsync(new FakeSender(_ => Task.FromResult(EmailSendResult.Fail(EmailLog.ProviderRejected))), Open));
        Assert.Equal(EmailLog.ProviderUnavailable,
            await FailureAsync(new FakeSender(_ => throw new InvalidOperationException($"no llega a {Address}")), Open));
        Assert.Equal(EmailLog.TimedOut, await FailureAsync(new FakeSender(async _ =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            return EmailSendResult.Ok(null);
        }), Open));
        Assert.All(logs, line => Assert.DoesNotContain(Address, line));
    }

    private static FamilyAccessCodeService Service(
        FakeStore store, FakeSender sender, EmailSettings? settings = null, string? key = "", Action<string>? log = null, TimeSpan? timeout = null) =>
        new(store, sender, settings ?? Open, new FamilyAccessCodeKey(key == "" ? Key : key), TimeProvider.System, log, timeout);

    private sealed class FakeSender(Func<EmailMessage, Task<EmailSendResult>> send) : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return send(message);
        }
    }

    private sealed class FakeStore : IFamilyAccessCodeStore
    {
        public string? Address { get; set; } = CodigoAccesoFamiliaTests.Address;
        public AccessCodeReservation Reservation { get; set; } = AccessCodeReservation.Reserved;
        public List<NewAccessCode> Reserved { get; } = [];
        public List<(Guid CodeId, string? Operation, string? Failure)> Recorded { get; } = [];
        public List<byte[]> Redeemed { get; } = [];

        public Task<string?> FindAddressAsync(
            string externalSubject, AccountId accountId, IReadOnlyList<ActiveProfileScope> scopes, CancellationToken ct = default) =>
            Task.FromResult(Address);

        public Task<AccessCodeReservation> ReserveAsync(NewAccessCode code, CancellationToken ct = default)
        {
            if (Reservation == AccessCodeReservation.Reserved)
            {
                Reserved.Add(code);
            }
            return Task.FromResult(Reservation);
        }

        public Task RecordSendAsync(Guid codeId, string? operationId, string? failureCode, DateTime now, CancellationToken ct = default)
        {
            Recorded.Add((codeId, operationId, failureCode));
            return Task.CompletedTask;
        }

        public Task<AccessCodeCheck> RedeemAsync(Guid codeId, AccountId accountId, byte[] digest, DateTime now, CancellationToken ct = default)
        {
            Redeemed.Add(digest);
            return Task.FromResult(AccessCodeCheck.Valid);
        }

        public Task ResetAsync(AccountId accountId, DateTime now, CancellationToken ct = default) => Task.CompletedTask;
    }
}
