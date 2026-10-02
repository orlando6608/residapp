using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;

namespace ResidApp.Infrastructure.Pdf;

/// <summary>
/// Identidad de marca de todo PDF de la app (guia-diseno-sistema-visual.md): cada página lleva arriba el icono de ResidApp
/// en color y su nombre. Todo renderizador nuevo debe llamar a <see cref="AddHeader"/> en cada sección.
/// </summary>
internal static class PdfBranding
{
    /// <summary>wwwroot\images\logo.svg rasterizado a PNG (PDFsharp no importa SVG), en memoria con el prefijo base64: de MigraDoc.</summary>
    private static readonly string Logo = "base64:" + Convert.ToBase64String(EmbeddedResource("ResidApp.Images.logo.png"));

    /// <summary>Deja la sección en A4 con márgenes laterales de 2 cm (el ancho del encabezado cuenta con ellos), le hace
    /// sitio arriba y añade el encabezado: icono y nombre de la app, separados del cuerpo por una línea fina.</summary>
    public static void AddHeader(Section section)
    {
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromCentimeter(2);
        section.PageSetup.TopMargin = Unit.FromCentimeter(3.2);
        section.PageSetup.HeaderDistance = Unit.FromCentimeter(1);

        var header = section.Headers.Primary.AddTable();
        header.LeftPadding = header.RightPadding = 0;
        header.AddColumn(Unit.FromCentimeter(1.8));
        header.AddColumn(Unit.FromCentimeter(15.2));
        var row = header.AddRow();
        row.VerticalAlignment = VerticalAlignment.Center;
        row.BottomPadding = Unit.FromPoint(4);
        row.Borders.Bottom.Width = 0.5;
        row.Borders.Bottom.Color = Colors.Gray;
        var logo = row.Cells[0].AddImage(Logo);
        logo.Width = Unit.FromCentimeter(1.4);
        logo.LockAspectRatio = true;
        var brand = row.Cells[1].AddParagraph();
        brand.Format.Font.Size = 9;
        brand.Format.Font.Color = Colors.DimGray;
        brand.AddFormattedText("ResidApp", TextFormat.Bold);
        brand.AddText(" · Plataforma Asistencial Geriátrica");
    }

    public static byte[] EmbeddedResource(string name)
    {
        using var resource = typeof(PdfBranding).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Falta el recurso incrustado {name}.");
        using var bytes = new MemoryStream();
        resource.CopyTo(bytes);
        return bytes.ToArray();
    }
}
