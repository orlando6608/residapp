using System.Text;
using ResidApp.Domain.Platform;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>Logo del centro (script 0068): solo PNG o JPEG, de 512 KB y 1024 píxeles por lado como máximo, sin metadatos que puedan llevar datos
/// personales; el SVG y cualquier otro tipo se rechazan por sus primeros bytes, diga lo que diga el nombre del fichero.</summary>
public class CenterLogoTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Jfif = Segment(0xE0, [.. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 0, 0]);
    private static readonly byte[] Exif = Segment(0xE1, [.. "Exif\0\0"u8, .. "GPS 40.4168 N, 3.7038 W"u8]);
    private static readonly byte[] Scan = [.. Segment(0xDA, [1, 1, 0, 0, 63, 0]), 0x12, 0xFF, 0x00, 0x34, 0xFF, 0xD9];

    [Fact]
    public void Normalize_UnPng_QuitaLosBloquesQueNoSonLaImagen_YConservaLaImagen()
    {
        var header = Chunk("IHDR", Ihdr(200, 100));
        var palette = Chunk("PLTE", [0, 0, 0, 255, 255, 255]);
        var transparency = Chunk("tRNS", [0, 255]);
        var data = Chunk("IDAT", [1, 2, 3, 4]);
        var end = Chunk("IEND", []);
        byte[] logo = [.. PngSignature, .. header, .. Chunk("tEXt", "Author\0GPS 40.4168 N"u8.ToArray()), .. palette, .. Chunk("eXIf", [1, 2]),
            .. transparency, .. data, .. end, .. "Otra imagen"u8];

        var normalized = CenterLogo.Normalize(logo);

        Assert.Equal(CenterLogo.Png, normalized.Type);
        Assert.Equal("image/png", normalized.ContentType);
        Assert.Equal([.. PngSignature, .. header, .. palette, .. transparency, .. data, .. end], normalized.Content);
        Assert.DoesNotContain("GPS", Encoding.Latin1.GetString(normalized.Content));
    }

    [Fact]
    public void Normalize_UnJpeg_QuitaElExif_YDevuelveElTipoJpeg()
    {
        byte[] logo = [0xFF, 0xD8, .. Jfif, .. Exif, .. Sof(120, 300), .. Scan];

        var normalized = CenterLogo.Normalize(logo);

        Assert.Equal(CenterLogo.Jpeg, normalized.Type);
        Assert.Equal("image/jpeg", normalized.ContentType);
        Assert.Equal([0xFF, 0xD8, .. Jfif, .. Sof(120, 300), .. Scan], normalized.Content);
    }

    public static TheoryData<string, byte[]?> Invalid => new()
    {
        { "nulo", null },
        { "vacío", [] },
        { "SVG", Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>") },
        { "GIF", Encoding.ASCII.GetBytes("GIF89a....") },
        { "PNG sin IHDR primero", [.. PngSignature, .. Chunk("IDAT", [1, 2]), .. Chunk("IEND", [])] },
        { "PNG sin datos de imagen", [.. PngSignature, .. Chunk("IHDR", Ihdr(10, 10)), .. Chunk("IEND", [])] },
        { "PNG sin final", [.. PngSignature, .. Chunk("IHDR", Ihdr(10, 10)), .. Chunk("IDAT", [1, 2])] },
        { "PNG de 1025 píxeles de ancho", [.. PngSignature, .. Chunk("IHDR", Ihdr(1025, 10)), .. Chunk("IDAT", [1, 2]), .. Chunk("IEND", [])] },
        { "PNG de ancho cero", [.. PngSignature, .. Chunk("IHDR", Ihdr(0, 10)), .. Chunk("IDAT", [1, 2]), .. Chunk("IEND", [])] },
        { "PNG cortado a mitad de un bloque", [.. PngSignature, .. Chunk("IHDR", Ihdr(10, 10)), 0, 0, 1, 0, (byte)'I', (byte)'D'] },
        { "JPEG de 1025 píxeles de alto", [0xFF, 0xD8, .. Jfif, .. Sof(1025, 10), .. Scan] },
        { "JPEG sin cabecera de imagen", [0xFF, 0xD8, .. Jfif, .. Scan] },
        { "JPEG cortado antes del fin", [0xFF, 0xD8, .. Jfif, .. Sof(10, 10), .. Scan[..^2]] },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Normalize_RechazaLoQueNoEsUnPngOUnJpegValido(string caso, byte[]? content)
    {
        var error = Assert.Throws<DomainValidationException>(() => CenterLogo.Normalize(content));
        Assert.Equal(CenterLogo.InvalidCode, error.Code);
        Assert.NotEmpty(caso);
    }

    [Fact]
    public void Normalize_Admite1024PixelesPorLado()
    {
        byte[] logo = [.. PngSignature, .. Chunk("IHDR", Ihdr(1024, 1024)), .. Chunk("IDAT", [1, 2]), .. Chunk("IEND", [])];

        Assert.Equal(CenterLogo.Png, CenterLogo.Normalize(logo).Type);
    }

    [Fact]
    public void Normalize_RechazaMasDe512Kb()
    {
        byte[] logo = [.. PngSignature, .. Chunk("IHDR", Ihdr(10, 10)), .. Chunk("IDAT", new byte[CenterLogo.MaxBytes]), .. Chunk("IEND", [])];

        Assert.Throws<DomainValidationException>(() => CenterLogo.Normalize(logo));
    }

    [Fact]
    public void ValidateName_RecortaYRechazaVacioOMuyLargo()
    {
        Assert.Equal("Residencia Los Almendros", NewCenter.ValidateName("  Residencia Los Almendros "));
        Assert.Equal(NewCenter.InvalidCode, Assert.Throws<DomainValidationException>(() => NewCenter.ValidateName("   ")).Code);
        Assert.Equal(NewCenter.InvalidCode, Assert.Throws<DomainValidationException>(() => NewCenter.ValidateName(new string('a', 201))).Code);
    }

    [Fact]
    public void Validate_ConLogo_LoNormaliza_YUnLogoInvalidoDaSuPropioCodigo()
    {
        byte[] logo = [.. PngSignature, .. Chunk("IHDR", Ihdr(10, 10)), .. Chunk("tEXt", [1]), .. Chunk("IDAT", [1, 2]), .. Chunk("IEND", [])];

        var data = NewCenter.Validate("CENTRO-1", "Centro uno", "U1", "Unidad 1", "admin.uno", "Admin Uno", logo);

        Assert.Equal(CenterLogo.Png, data.Logo!.Type);
        Assert.Null(NewCenter.Validate("CENTRO-1", "Centro uno", "U1", "Unidad 1", "admin.uno", "Admin Uno").Logo);
        Assert.Equal(CenterLogo.InvalidCode, Assert.Throws<DomainValidationException>(() =>
            NewCenter.Validate("CENTRO-1", "Centro uno", "U1", "Unidad 1", "admin.uno", "Admin Uno", "<svg/>"u8.ToArray())).Code);
    }

    private static byte[] Ihdr(int width, int height) =>
        [(byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
         (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height, 8, 2, 0, 0, 0];

    // El tamaño del bloque, su tipo, sus datos y una suma de comprobación que el servidor no verifica.
    private static byte[] Chunk(string type, byte[] data) =>
        [(byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length, .. Encoding.ASCII.GetBytes(type), .. data, 0, 0, 0, 0];

    // Cabecera de imagen de un JPEG: precisión, alto, ancho y un componente.
    private static byte[] Sof(int height, int width) =>
        Segment(0xC0, [8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 1, 1, 0x11, 0]);

    private static byte[] Segment(byte marker, byte[] payload) =>
        [0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2), .. payload];
}
