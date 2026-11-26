using System.Net;
using ResidApp.Application.Errors;

namespace ResidApp.FunctionalTests;

/// <summary>
/// La baja de avisos por familiar con la app entera y, en la CI, con el usuario limitado de la base de datos (script 0123): Administración la pone y la quita desde la ficha del
/// familiar, la ficha del residente la enseña, y quien está de baja sigue recibiendo el código para entrar.
/// </summary>
public partial class AccesoTests
{
    private static async Task<string> SetNoticesAsync(AdminFamily family, bool optOut, bool requested = false)
    {
        var page = await family.Admin.GetStringAsync(family.Edit);
        var form = new Dictionary<string, string>
        {
            ["residenteId"] = family.ResidentId.ToString(), ["vinculoId"] = family.LinkId.ToString(), ["baja"] = optOut ? "true" : "false",
        };
        if (!optOut)
        {
            form["bajaVista"] = SesionTests.ExtractValue(page, "bajaVista");
        }

        if (requested)
        {
            form["pedida"] = "true";
        }

        return await TextAsync(await PostAsync(family.Admin, "/Administracion/AvisosFamiliar", form, family.Edit));
    }

    [Fact]
    public async Task LaBajaDeAvisos_SePoneYSeQuitaDesdeLaFicha_YSeVeEnLaDelResidente()
    {
        var family = await ResidentWithFamilyAsync(factory);
        var resident = $"/Administracion/Residente?residenteId={family.ResidentId}";

        var before = WebUtility.HtmlDecode(await family.Admin.GetStringAsync(family.Edit));
        var optedOut = await SetNoticesAsync(family, optOut: true);
        var editPage = WebUtility.HtmlDecode(await family.Admin.GetStringAsync(family.Edit));
        var residentPage = WebUtility.HtmlDecode(await family.Admin.GetStringAsync(resident));
        var unrequested = await SetNoticesAsync(family, optOut: false);
        var reactivated = await SetNoticesAsync(family, optOut: false, requested: true);
        var after = WebUtility.HtmlDecode(await family.Admin.GetStringAsync(resident));

        Assert.Contains("No está de baja, pero no se le envían avisos porque no tiene una dirección confirmada.", before);
        Assert.Contains("Dar de baja de los avisos", before);
        Assert.Contains("Baja anotada: no se le enviarán avisos por correo sobre citas.", optedOut);
        Assert.Contains("De baja de los avisos desde el", editPage);
        Assert.Contains("Volver a activar los avisos", editPage);
        Assert.Contains("Esta persona me ha pedido volver a recibir los avisos", editPage);
        // Con el código apagado no se habla del código.
        Assert.DoesNotContain("El código para entrar y el aviso de seguridad", editPage);
        Assert.Contains("De baja de los avisos por correo desde el", residentPage);
        Assert.Contains(ApplicationResultRunner.FamilyNoticesRequestRequiredMessage, unrequested);
        Assert.Contains("Baja quitada: esta persona puede volver a recibir los avisos por correo.", reactivated);
        Assert.DoesNotContain("De baja de los avisos por correo desde el", after);
    }

    [Fact]
    public async Task ConElCodigo_QuienEstaDeBajaDeLosAvisosSigueRecibiendoElCodigoParaEntrar()
    {
        var mailbox = new Mailbox();
        var app = WithFamilyCode(mailbox, new TestClock());
        var family = await ResidentWithFamilyAsync(app);
        var subject = $"familia-{Guid.NewGuid():N}"[..20];
        var email = $"{subject}@example.test";
        await SaveEmailAsync(family, email);
        await GrantAccessAsync(family, subject);
        await AuthorizeAsync(family);
        await SetNoticesAsync(family, optOut: true);
        var editPage = WebUtility.HtmlDecode(await family.Admin.GetStringAsync(family.Edit));
        var sheet = await TextAsync(await IssueAsync(family.Admin, subject, family.Authorization));

        var relative = Client(app);
        await ChoosePasswordAsync(relative, InvitationUrl(sheet));

        Assert.Contains("El código para entrar y el aviso de seguridad por cambio de correo le siguen llegando.", editPage);
        Assert.Equal(email, mailbox.Sent[^1].To);
        Assert.True(OpensSession(await TypeCodeAsync(relative, mailbox.LastCode)));
    }
}
