using ResidApp.Domain.Platform;

namespace ResidApp.Web.Models;

/// <summary>El campo para elegir el logo del centro (_LogoCentroCampo), en el alta de un centro por Plataforma y en los datos del centro de
/// Administración. CurrentUrl, si el centro ya tiene logo, para la vista previa.</summary>
public sealed record LogoCentroCampoViewModel(string Name, string Id, string Label, string? CurrentUrl = null);

/// <summary>Lo que comparten los dos formularios que reciben un logo.</summary>
public static class CenterLogoForm
{
    public const long MaxRequestBytes = 20 * 1024 * 1024;

    public const string Invalid = "El logo tiene que ser una imagen PNG o JPEG de 512 KB y 1024 píxeles por lado como máximo. Si la eliges desde el ordenador o el móvil, la app la reduce sola.";

    public static bool TooLarge(IFormFile file) => file.Length > CenterLogo.MaxBytes;

    public static async Task<byte[]> ReadAsync(IFormFile file, CancellationToken ct)
    {
        using var content = new MemoryStream((int)file.Length);
        await file.CopyToAsync(content, ct);
        return content.ToArray();
    }
}
