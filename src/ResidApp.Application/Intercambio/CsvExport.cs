using System.Text;

namespace ResidApp.Application.Intercambio;

/// <summary>
/// CSV como lo abre Excel en España: UTF-8 con BOM, «;» como separador y fin de línea CRLF. Un valor con «;», comillas o saltos de
/// línea va entre comillas. Contra la inyección de fórmulas (OWASP), un valor que empieza por «=», «+», «-», «@», tabulador o retorno
/// de carro lleva delante un apóstrofo, para que la hoja de cálculo lo trate como texto.
/// </summary>
public static class CsvExport
{
    public static byte[] Write(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var text = new StringBuilder();
        AppendLine(text, headers);
        foreach (var row in rows)
        {
            AppendLine(text, row);
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    public static string Cell(string? value)
    {
        var text = value ?? "";
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            text = "'" + text;
        }

        return text.IndexOfAny([';', '"', '\r', '\n']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }

    private static void AppendLine(StringBuilder text, IEnumerable<string?> values) =>
        text.AppendJoin(';', values.Select(Cell)).Append("\r\n");
}
