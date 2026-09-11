using System.Text.Json;
using ResidApp.Shared;

namespace ResidApp.Infrastructure;

/// <summary>
/// Opciones de serialización compartidas por todo Infrastructure. Registra EnumCodeJsonConverterFactory
/// para que answer_payload y result_json usen el código string de cada enum de catálogo (no el nombre del
/// miembro C#), manteniendo compatibilidad con el formato que ya persistía el prototipo TypeScript.
/// PropertyNameCaseInsensitive porque los constructores de los records de Answers usan parámetros
/// camelCase (idénticos a los nombres de campo JSON originales) mientras que las demás propiedades del
/// proyecto son PascalCase.
/// </summary>
public static class ResidAppJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new EnumCodeJsonConverterFactory() },
    };
}
