using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ResidApp.Application.Ports;

namespace ResidApp.Infrastructure.Pdf;

/// <summary>
/// Identidad de marca de todo PDF de la app (guia-diseno-sistema-visual.md): cada página lleva arriba el icono y el nombre de ResidApp, o, si
/// el PDF es de un centro (script 0068), el logo y el nombre del centro (sin logo propio, el icono de ResidApp) y la mención «Generado con
/// ResidApp» al pie. Todo renderizador nuevo debe llamar a <see cref="ComposeHeader"/> en el encabezado de cada página y a
/// <see cref="ComposeGeneratedBy"/> en el pie.
/// </summary>
internal static class PdfBranding
{
    private const string Product = " · Plataforma Asistencial Geriátrica";

    /// <summary>wwwroot\images\logo.svg rasterizado a PNG (QuestPDF no importa SVG). El PNG va aplanado sobre blanco y sin canal alfa: con esquinas
    /// transparentes (RGB negro), al reducirlo los visores mezclan ese negro con el borde y dejan un halo oscuro en el arco.</summary>
    private static readonly byte[] Logo = EmbeddedResource("ResidApp.Images.logo.png");

    /// <summary>Encabezado de página: icono y nombre de la app (o logo y nombre del centro), separados del cuerpo por una línea fina.
    /// El icono de ResidApp es cuadrado de 1,4 cm; el logo de un centro, de 1,4 cm de alto, tan ancho como pida su proporción (hasta 4 cm).</summary>
    public static void ComposeHeader(IContainer container, ReferralReportBranding? branding = null)
    {
        var (logo, logoWidthCm) = branding?.Logo is { } own
            ? (own.Content, Math.Min(4.0, 1.4 * AspectRatio(own.Content)))
            : (Logo, 1.4);

        container.BorderBottom(0.5f).BorderColor(Colors.Grey.Medium).PaddingBottom(4).Row(row =>
        {
            row.ConstantItem((float)logoWidthCm, Unit.Centimetre).Height(1.4f, Unit.Centimetre).Image(logo).FitArea();
            row.ConstantItem(0.4f, Unit.Centimetre);
            row.RelativeItem().AlignMiddle().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(9).FontColor(Colors.Grey.Darken2));
                if (branding is null)
                {
                    text.Span("ResidApp").Bold();
                    text.Span(Product);
                }
                else
                {
                    text.Span(branding.CenterName).Bold();
                }
            });
        });
    }

    /// <summary>Con la marca del centro arriba, ResidApp queda al pie: una línea discreta tras el texto del pie del renderizador.</summary>
    public static void ComposeGeneratedBy(ColumnDescriptor footer, ReferralReportBranding? branding)
    {
        if (branding is not null)
        {
            footer.Item().Text("Generado con ResidApp" + Product);
        }
    }

    private static double AspectRatio(byte[] image)
    {
        using var decoded = QuestPDF.Infrastructure.Image.FromBinaryData(image);
        return (double)decoded.Size.Width / decoded.Size.Height;
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
