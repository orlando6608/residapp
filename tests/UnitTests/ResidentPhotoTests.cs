using System.Text;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>Foto de identificación del residente (script 0059): solo JPEG de 1 MB como máximo, sin los bloques que pueden llevar datos
/// personales (EXIF con GPS, XMP, comentarios) ni lo que venga tras el fin de la imagen.</summary>
public class ResidentPhotoTests
{
    private static readonly byte[] Jfif = Segment(0xE0, [.. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 0, 0]);
    private static readonly byte[] Exif = Segment(0xE1, [.. "Exif\0\0"u8, .. "GPS 40.4168 N, 3.7038 W"u8]);
    private static readonly byte[] Comment = Segment(0xFE, "Comentario de la camara"u8.ToArray());
    private static readonly byte[] Adobe = Segment(0xEE, [.. "Adobe"u8, 0, 100, 0, 0, 0, 0, 1]);
    private static readonly byte[] Quantization = Segment(0xDB, new byte[65]);
    // Cabecera del barrido y datos comprimidos, con un FF 00 (byte de relleno) y un reinicio FF D0 dentro, y el fin de la imagen.
    private static readonly byte[] Scan = [.. Segment(0xDA, [1, 1, 0, 0, 63, 0]), 0x12, 0xFF, 0x00, 0x34, 0xFF, 0xD0, 0x56, 0xFF, 0xD9];

    [Fact]
    public void Normalize_QuitaElExifLosComentariosYLoQueSigueAlFin_YConservaLaImagen()
    {
        byte[] photo = [0xFF, 0xD8, .. Jfif, .. Exif, .. Comment, .. Adobe, .. Quantization, .. Scan, .. "Otra imagen con su EXIF"u8];

        var normalized = ResidentPhoto.Normalize(photo);

        Assert.Equal([0xFF, 0xD8, .. Jfif, .. Adobe, .. Quantization, .. Scan], normalized);
        Assert.DoesNotContain("GPS", Encoding.Latin1.GetString(normalized));
    }

    [Fact]
    public void Normalize_UnJpegSinMetadatos_QuedaIgual()
    {
        byte[] photo = [0xFF, 0xD8, .. Jfif, .. Quantization, .. Scan];

        Assert.Equal(photo, ResidentPhoto.Normalize(photo));
    }

    [Fact]
    public void Normalize_AdmiteRellenoAntesDeUnMarcador()
    {
        byte[] photo = [0xFF, 0xD8, 0xFF, .. Jfif, .. Scan];

        Assert.Equal([0xFF, 0xD8, .. Jfif, .. Scan], ResidentPhoto.Normalize(photo));
    }

    public static TheoryData<byte[]?> Invalid => new()
    {
        null,
        (byte[])[],
        // PNG
        (byte[])[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0],
        // Sin la imagen comprimida
        (byte[])[0xFF, 0xD8, .. Jfif, 0xFF, 0xD9],
        // Cortada a mitad de un bloque
        (byte[])[0xFF, 0xD8, .. Jfif[..8]],
        // Cortada antes del fin de la imagen
        (byte[])[0xFF, 0xD8, .. Jfif, .. Scan[..^2]],
        // Basura entre bloques
        (byte[])[0xFF, 0xD8, .. Jfif, 0x00, .. Scan],
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Normalize_RechazaLoQueNoEsUnJpegCompleto(byte[]? content)
    {
        var error = Assert.Throws<DomainValidationException>(() => ResidentPhoto.Normalize(content));
        Assert.Equal(ResidentPhoto.InvalidCode, error.Code);
    }

    [Fact]
    public void Normalize_RechazaMasDeUnMega()
    {
        byte[] photo = [0xFF, 0xD8, .. Jfif, .. Segment(0xDA, [1, 1, 0, 0, 63, 0]), .. new byte[ResidentPhoto.MaxBytes], 0xFF, 0xD9];

        Assert.Throws<DomainValidationException>(() => ResidentPhoto.Normalize(photo));
    }

    private static byte[] Segment(byte marker, byte[] payload) =>
        [0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2), .. payload];
}
