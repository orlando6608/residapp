using System.Text.Json.Nodes;
using ResidApp.Domain.Auxiliar;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>
/// El esquema estricto que se le da al modelo para dictar «Registrar cambio» (ADR 0011): se genera desde los enums y el catálogo del dominio, no a
/// mano, de modo que si cambia el catálogo cambia el esquema. Cada área es una alternativa con solo sus propias opciones; las tres áreas sin lista
/// rápida no admiten ninguna. Sin temperatura: el rango válido lo fija CJ y mientras no lo haya, el dictado no sugiere ningún valor clínico numérico.
/// </summary>
public static class DailyChangeDictationSchema
{
    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());

    private static JsonObject ForArea(DailyChangeAreaCode area)
    {
        var options = DailyChangeAreaOptionsCatalog.OptionsFor(area).Select(o => o.ToCode()).ToList();
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = Strings(["area", "texto", "opciones"]),
            ["properties"] = new JsonObject
            {
                ["area"] = new JsonObject { ["type"] = "string", ["enum"] = Strings([area.ToCode()]) },
                ["texto"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                ["opciones"] = options.Count == 0
                    ? new JsonObject { ["type"] = "array", ["maxItems"] = 0, ["items"] = new JsonObject { ["type"] = "string" } }
                    : new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string", ["enum"] = Strings(options) } },
            },
        };
    }

    public static JsonObject Build() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = Strings(["areas"]),
        ["properties"] = new JsonObject
        {
            ["areas"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["anyOf"] = new JsonArray(Enum.GetValues<DailyChangeAreaCode>().Select(a => (JsonNode)ForArea(a)).ToArray()),
                },
            },
        },
    };
}
