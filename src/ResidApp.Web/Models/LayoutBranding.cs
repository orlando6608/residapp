using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Ports;
using ResidApp.Web.Security;

namespace ResidApp.Web.Models;

/// <summary>La marca que pintan los encabezados cuando quien navega trabaja en un centro: su nombre y, si tiene, la dirección de su logo. Sin
/// ella (sin sesión, Plataforma o ámbito que no vale) los encabezados llevan la marca de ResidApp.</summary>
public sealed record LayoutBranding(string Name, string? LogoUrl);

public static class LayoutBrandingResolver
{
    /// <summary>La marca del centro del ámbito activo, comprobada en SQL contra la cuenta de la sesión (la cookie de ámbito no es de fiar,
    /// ADR 0008). Es solo aspecto: si la lectura falla, devuelve null y la pantalla sale con la marca de ResidApp.</summary>
    public static async Task<LayoutBranding?> ResolveAsync(
        ClaimsPrincipal user, HttpRequest request, ICenterBrandingDirectory directory, IUrlHelper url, CancellationToken ct)
    {
        var subject = SessionAuthentication.Subject(user);
        var activeScope = subject is null ? null : ActiveProfileScopeCookie.Read(request);
        if (subject is null || activeScope is null)
        {
            return null;
        }

        try
        {
            var branding = await directory.FindForScopeAsync(subject, activeScope.ProfileScopeId, ct);
            return branding is null
                ? null
                : new LayoutBranding(branding.Name, branding.LogoUpdatedAt is { } updatedAt
                    ? url.Action("Ver", "CentroLogo", new { v = updatedAt.UtcTicks })
                    : null);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
