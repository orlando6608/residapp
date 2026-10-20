using ResidApp.Shared;

namespace ResidApp.Domain.Residents;

/// <summary>
/// Foto de identificación del residente (estudio de mercado, R2; script 0059). Solo se guarda un JPEG de 1 MB como máximo: el navegador
/// reduce la foto antes de enviarla, y el servidor no tiene librería de imágenes. Normalize quita del fichero lo que no es la imagen y puede
/// llevar datos personales: los bloques APP1 a APP15 (EXIF con la posición GPS y la cámara, XMP, IPTC, perfil ICC), salvo APP14 (solo
/// indicadores técnicos de Adobe, sin los que algunos JPEG salen con los colores mal), los comentarios y lo que haya tras el fin de la imagen
/// (otras imágenes añadidas por el móvil). Se pierde también la orientación EXIF: sin el reductor del navegador, una foto girada se ve girada.
/// </summary>
public static class ResidentPhoto
{
    public const int MaxBytes = 1_048_576;
    public const string InvalidCode = "RESIDENT_PHOTO_INVALID";

    public static byte[] Normalize(byte[]? content)
    {
        if (content is null || content.Length < 4 || content.Length > MaxBytes || content[0] != 0xFF || content[1] != 0xD8)
        {
            throw new DomainValidationException(InvalidCode);
        }

        using var output = new MemoryStream(content.Length);
        output.Write(content, 0, 2);
        var position = 2;
        while (true)
        {
            if (position + 1 >= content.Length || content[position] != 0xFF)
            {
                throw new DomainValidationException(InvalidCode);
            }
            var marker = content[position + 1];
            if (marker == 0xFF)
            {
                // Relleno antes de un marcador.
                position++;
                continue;
            }
            if (marker is 0x00 or 0xD8 or 0xD9 or (>= 0xD0 and <= 0xD7) || position + 3 >= content.Length)
            {
                throw new DomainValidationException(InvalidCode);
            }
            var length = (content[position + 2] << 8) | content[position + 3];
            if (length < 2 || position + 2 + length >= content.Length)
            {
                throw new DomainValidationException(InvalidCode);
            }
            if (marker == 0xDA)
            {
                // Inicio de la imagen comprimida: se copia hasta el fin (FF D9), que no aparece dentro de los datos (cada FF va seguido de 00 o de
                // un reinicio D0-D7).
                var end = EndOfImage(content, position + 2 + length);
                output.Write(content, position, end - position);
                return output.ToArray();
            }
            var personal = marker is (>= 0xE1 and <= 0xEF and not 0xEE) or 0xFE;
            if (!personal)
            {
                output.Write(content, position, 2 + length);
            }
            position += 2 + length;
        }
    }

    private static int EndOfImage(byte[] content, int from)
    {
        for (var i = from; i + 1 < content.Length; i++)
        {
            if (content[i] == 0xFF && content[i + 1] == 0xD9)
            {
                return i + 2;
            }
        }
        throw new DomainValidationException(InvalidCode);
    }
}
