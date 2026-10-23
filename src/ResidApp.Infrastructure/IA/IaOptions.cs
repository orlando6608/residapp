namespace ResidApp.Infrastructure.IA;

/// <summary>
/// La IA de la aplicación (ADR 0011). Solo hay un caso de uso aprobado, el dictado de «Registrar cambio» (ADR 0009), y un solo proveedor
/// autorizado, Mistral. Añadir otro proveedor es un cambio legal (contrato de encargado, región de proceso, EIPD) y exige un ADR nuevo: no basta
/// con cambiar un ajuste, y por eso la aplicación no arranca si se configura uno que no está en <see cref="ProveedoresAutorizados"/>.
/// Sin clave, el dictado está desactivado. La clave va en los user-secrets o en un ajuste de la Web App (IA__Mistral__ApiKey), nunca en
/// appsettings.json.
/// </summary>
public sealed class IaOptions
{
    public const string Section = "IA";
    public const string RateLimitPolicy = "dictado";

    public static readonly IReadOnlyList<string> ProveedoresAutorizados = ["Mistral"];

    public string Proveedor { get; set; } = "Mistral";

    public MistralOptions Mistral { get; set; } = new();

    /// <summary>Peticiones de dictado por minuto y cuenta.</summary>
    public int PeticionesPorMinuto { get; set; } = 6;

    /// <summary>Hay un proveedor autorizado y una clave: el dictado se ofrece. Sin clave, el endpoint responde 404.</summary>
    public bool Activa => !string.IsNullOrWhiteSpace(Mistral.ApiKey);

    /// <summary>Falla cerrado: un proveedor que no está en la lista autorizada impide arrancar.</summary>
    public static void Validate(IaOptions options)
    {
        if (!ProveedoresAutorizados.Contains(options.Proveedor, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"IA:Proveedor «{options.Proveedor}» no está autorizado. Los autorizados son: {string.Join(", ", ProveedoresAutorizados)}. " +
                "Añadir otro exige un ADR (contrato de encargado, región de proceso y EIPD), no basta con cambiar un ajuste.");
        }
    }
}

public sealed class MistralOptions
{
    /// <summary>La dirección es fija a propósito: la clave solo se envía a Mistral, no a donde diga un ajuste.</summary>
    public const string BaseUrl = "https://api.mistral.ai/";

    public string? ApiKey { get; set; }

    public string ModeloVoz { get; set; } = "voxtral-mini-latest";

    public string ModeloTexto { get; set; } = "mistral-small-latest";

    public int TiempoEsperaSegundos { get; set; } = 60;

    /// <summary>Espera antes del único reintento ante un fallo transitorio.</summary>
    public int EsperaReintentoMilisegundos { get; set; } = 1000;
}
