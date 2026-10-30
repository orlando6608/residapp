using ResidApp.Infrastructure.Correo;

namespace ResidApp.IntegrationTests;

/// <summary>Cuándo está encendido el correo (ADR 0012): apagado por defecto y solo con todo lo necesario. No toca la base de datos.</summary>
public class CorreoOptionsTests
{
    private static CorreoOptions Complete() => new()
    {
        Activo = true, CadenaConexion = "endpoint=https://ficticio.example.test/;accesskey=ficticia", UrlBase = "https://dev.example.test",
        DireccionesPermitidas = ["persona.ficticia@example.test"],
    };

    [Fact]
    public void PorDefecto_EstaApagado_ConElRemitenteFijoYSinResponderA()
    {
        var options = new CorreoOptions();

        Assert.False(options.Operativo);
        Assert.False(options.ToSettings().Active);
        Assert.Equal("no-responder@avisos.residapp.es", options.Remitente);
    }

    [Fact]
    public void ConTodoLoNecesario_EstaEncendido_ConLaDireccionBaseConfigurada()
    {
        var settings = Complete().ToSettings();

        Assert.True(settings.Active);
        Assert.Equal("https://dev.example.test", settings.BaseUrl);
        Assert.Equal(["persona.ficticia@example.test"], settings.AllowedRecipients);
    }

    [Theory]
    [InlineData("interruptor")]
    [InlineData("cadena")]
    [InlineData("remitente")]
    [InlineData("urlVacia")]
    [InlineData("urlHttp")]
    [InlineData("urlConParametros")]
    [InlineData("idioma")]
    [InlineData("sinDestinos")]
    public void Si_FaltaAlgo_ElCorreoSigueApagado(string missing)
    {
        var options = Complete();
        switch (missing)
        {
            case "interruptor": options.Activo = false; break;
            case "cadena": options.CadenaConexion = " "; break;
            case "remitente": options.Remitente = ""; break;
            case "urlVacia": options.UrlBase = null; break;
            case "urlHttp": options.UrlBase = "http://dev.example.test"; break;
            case "urlConParametros": options.UrlBase = "https://dev.example.test/?a=1"; break;
            case "idioma": options.Idioma = "en"; break;
            case "sinDestinos": options.DireccionesPermitidas = []; break;
        }

        Assert.False(options.Operativo);
        Assert.False(options.ToSettings().Active);
    }

    [Fact]
    public void SinListaDeDestinos_SoloSeEnciendeSiElEntornoManda_ACualquiera()
    {
        var options = Complete();
        options.DireccionesPermitidas = [];
        options.TodosLosDestinos = true;

        Assert.True(options.ToSettings().Active);
        Assert.True(options.ToSettings().AnyRecipient);
    }
}
