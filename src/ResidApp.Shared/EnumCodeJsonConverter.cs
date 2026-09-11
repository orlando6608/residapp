using System.Text.Json;
using System.Text.Json.Serialization;

namespace ResidApp.Shared;

/// <summary>
/// Serializa/deserializa cualquier enum de catálogo usando su código string (vía EnumCode), no el nombre
/// del miembro C#. Necesario para que answer_payload y result_json mantengan compatibilidad con el
/// formato JSON que ya persistía el prototipo TypeScript (p. ej. "DEAMBULA_INDEPENDIENTE_SIN_AYUDA", no
/// "DeambulaIndependienteSinAyuda"). Se registra una vez en las JsonSerializerOptions compartidas de
/// Infrastructure (ver ResidAppJson.Options), no como atributo en cada enum.
/// </summary>
public sealed class EnumCodeJsonConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var code = reader.GetString() ?? throw new JsonException($"Se esperaba un string para {typeof(TEnum).Name}.");
        return EnumCode.ParseCode<TEnum>(code);
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToCode());
}

public sealed class EnumCodeJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(EnumCodeJsonConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}
