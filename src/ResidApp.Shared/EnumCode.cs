using System.Collections.Concurrent;
using System.Reflection;

namespace ResidApp.Shared;

/// <summary>
/// Fija el código string exacto (SNAKE_CASE) de un valor de enum, tal como aparece en los catálogos
/// TypeScript originales y en las columnas de base de datos. Sin este atributo, EnumCode usa el nombre
/// del miembro tal cual.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class CodeAttribute(string code) : Attribute
{
    public string Code { get; } = code;
}

/// <summary>
/// Codec genérico enum C# &lt;-&gt; código string, dirigido por <see cref="CodeAttribute"/>. Traduce el patrón
/// "catálogo cerrado de strings" (`as const` + union type) de los ficheros TypeScript originales.
/// </summary>
public static class EnumCode
{
    private static readonly ConcurrentDictionary<Type, object> ForwardCaches = new();
    private static readonly ConcurrentDictionary<Type, object> ReverseCaches = new();

    public static string ToCode<TEnum>(this TEnum value) where TEnum : struct, Enum
    {
        var map = (IReadOnlyDictionary<TEnum, string>)ForwardCaches.GetOrAdd(typeof(TEnum), _ => BuildForwardMap<TEnum>());
        return map.TryGetValue(value, out var code)
            ? code
            : throw new ArgumentException($"No hay código definido para {typeof(TEnum).Name}.{value}.", nameof(value));
    }

    public static TEnum ParseCode<TEnum>(string code) where TEnum : struct, Enum
    {
        var map = (IReadOnlyDictionary<string, TEnum>)ReverseCaches.GetOrAdd(typeof(TEnum), _ => BuildReverseMap<TEnum>());
        return map.TryGetValue(code, out var value)
            ? value
            : throw new ArgumentException($"Código '{code}' desconocido para el catálogo {typeof(TEnum).Name}.", nameof(code));
    }

    public static bool TryParseCode<TEnum>(string code, out TEnum value) where TEnum : struct, Enum
    {
        var map = (IReadOnlyDictionary<string, TEnum>)ReverseCaches.GetOrAdd(typeof(TEnum), _ => BuildReverseMap<TEnum>());
        return map.TryGetValue(code, out value);
    }

    private static Dictionary<TEnum, string> BuildForwardMap<TEnum>() where TEnum : struct, Enum
    {
        var map = new Dictionary<TEnum, string>();
        foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var value = (TEnum)field.GetValue(null)!;
            var code = field.GetCustomAttribute<CodeAttribute>()?.Code ?? field.Name;
            map[value] = code;
        }
        return map;
    }

    private static Dictionary<string, TEnum> BuildReverseMap<TEnum>() where TEnum : struct, Enum
    {
        var map = new Dictionary<string, TEnum>(StringComparer.Ordinal);
        foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var value = (TEnum)field.GetValue(null)!;
            var code = field.GetCustomAttribute<CodeAttribute>()?.Code ?? field.Name;
            map[code] = value;
        }
        return map;
    }
}
