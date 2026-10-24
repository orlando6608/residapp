using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Domain.Platform;

/// <summary>Logo normalizado de un centro: Type es «PNG» o «JPEG» (el valor que guarda dbo.logos_centro).</summary>
public sealed record CenterLogoImage(byte[] Content, string Type)
{
    public string ContentType => Type == CenterLogo.Png ? "image/png" : "image/jpeg";
}

/// <summary>
/// Logo propio de un centro (script 0068). Solo se guarda un PNG o un JPEG de 512 KB como máximo y de 1024 píxeles por lado como máximo: el
/// navegador reduce el logo antes de enviarlo, y el servidor no tiene librería de imágenes. Normalize reconoce el tipo por los primeros bytes
/// (nunca por el nombre ni por el tipo que declare el navegador) y quita del fichero lo que no es la imagen y puede llevar datos personales:
/// en un JPEG, lo mismo que en la foto de un residente (ResidentPhoto.Normalize); en un PNG, todos los bloques salvo la cabecera, la paleta,
/// la transparencia, los datos de la imagen y el final. El SVG no se admite: puede llevar scripts, y el PDF no sabe dibujarlo.
/// </summary>
public static class CenterLogo
{
    public const int MaxBytes = 524_288;
    public const int MaxSide = 1024;
    public const string Png = "PNG";
    public const string Jpeg = "JPEG";
    public const string InvalidCode = "CENTER_LOGO_INVALID";

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static CenterLogoImage Normalize(byte[]? content)
    {
        if (content is null || content.Length < 4 || content.Length > MaxBytes)
        {
            throw new DomainValidationException(InvalidCode);
        }

        return content[0] == 0xFF && content[1] == 0xD8
            ? new CenterLogoImage(NormalizeJpeg(content), Jpeg)
            : content.AsSpan().StartsWith(PngSignature)
                ? new CenterLogoImage(NormalizePng(content), Png)
                : throw new DomainValidationException(InvalidCode);
    }

    private static byte[] NormalizeJpeg(byte[] content)
    {
        byte[] clean;
        try
        {
            clean = ResidentPhoto.Normalize(content);
        }
        catch (DomainValidationException)
        {
            throw new DomainValidationException(InvalidCode);
        }

        // Ancho y alto, en el primer bloque SOF (inicio de imagen), que Normalize ha dejado antes de los datos comprimidos.
        var position = 2;
        while (position + 8 < clean.Length && clean[position] == 0xFF)
        {
            var marker = clean[position + 1];
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                CheckSide((clean[position + 7] << 8) | clean[position + 8]);
                CheckSide((clean[position + 5] << 8) | clean[position + 6]);
                return clean;
            }
            position += 2 + ((clean[position + 2] << 8) | clean[position + 3]);
        }
        throw new DomainValidationException(InvalidCode);
    }

    private static byte[] NormalizePng(byte[] content)
    {
        using var output = new MemoryStream(content.Length);
        output.Write(PngSignature);
        var position = PngSignature.Length;
        var first = true;
        var hasData = false;
        while (position + 12 <= content.Length)
        {
            var length = (content[position] << 24) | (content[position + 1] << 16) | (content[position + 2] << 8) | content[position + 3];
            if (length < 0 || position + 12L + length > content.Length)
            {
                break;
            }
            var type = System.Text.Encoding.ASCII.GetString(content, position + 4, 4);
            if (first != (type == "IHDR"))
            {
                throw new DomainValidationException(InvalidCode);
            }
            if (first)
            {
                if (length != 13)
                {
                    throw new DomainValidationException(InvalidCode);
                }
                CheckSide((content[position + 8] << 24) | (content[position + 9] << 16) | (content[position + 10] << 8) | content[position + 11]);
                CheckSide((content[position + 12] << 24) | (content[position + 13] << 16) | (content[position + 14] << 8) | content[position + 15]);
                first = false;
            }
            if (type is "IHDR" or "PLTE" or "tRNS" or "IDAT" or "IEND")
            {
                output.Write(content, position, 12 + length);
                hasData |= type == "IDAT";
            }
            if (type == "IEND")
            {
                return hasData ? output.ToArray() : throw new DomainValidationException(InvalidCode);
            }
            position += 12 + length;
        }
        throw new DomainValidationException(InvalidCode);
    }

    private static void CheckSide(int side)
    {
        if (side < 1 || side > MaxSide)
        {
            throw new DomainValidationException(InvalidCode);
        }
    }
}
