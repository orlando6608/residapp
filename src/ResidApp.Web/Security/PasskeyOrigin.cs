namespace ResidApp.Web.Security;

/// <summary>
/// Qué orígenes pueden usar una passkey (ADR 0007, incremento I4). El navegador ya liga la credencial al dominio; esto lo comprueba también el
/// servidor con el origen que declara el dispositivo: HTTPS y el dominio configurado o uno de sus subdominios. Solo en Development vale
/// http://localhost, que Chrome trata como contexto seguro.
/// </summary>
public static class PasskeyOrigin
{
    public static bool IsAllowed(string? origin, string domain, bool allowLocalHttp)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.PathAndQuery != "/" || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        if (allowLocalHttp && uri.Scheme == Uri.UriSchemeHttp && uri.Host == "localhost" && domain == "localhost")
        {
            return true;
        }

        return uri.Scheme == Uri.UriSchemeHttps
            && (string.Equals(uri.Host, domain, StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
    }
}
