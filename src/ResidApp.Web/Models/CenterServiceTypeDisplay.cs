using ResidApp.Domain.Platform;
using ResidApp.Shared;

namespace ResidApp.Web.Models;

/// <summary>
/// El texto del tipo de servicio de un centro en pantalla (ADR 0017): el único sitio que lo escribe, para que todas las listas de centros digan lo mismo. Es siempre texto, no solo color. El centro reservado de
/// Plataforma no es un servicio y no lleva etiqueta (su tipo llega como null).
/// </summary>
public static class CenterServiceTypeDisplay
{
    public static string Label(CenterServiceType type) => type switch
    {
        CenterServiceType.Residencia => "Residencia",
        CenterServiceType.CentroDeDia => "Centro de Día",
        _ => type.ToString(),
    };

    /// <summary>La etiqueta, o vacío si no hay tipo (el centro de Plataforma).</summary>
    public static string LabelOrEmpty(CenterServiceType? type) => type is { } value ? Label(value) : "";

    /// <summary>El tipo de una fila de la constancia, por su código de base de datos.</summary>
    public static string LabelOfCode(string code) => EnumCode.TryParseCode<CenterServiceType>(code, out var type) ? Label(type) : code;

    /// <summary>El motivo de una fila de la constancia: el alta o la corrección de un error de alta (lista cerrada, sin texto libre).</summary>
    public static string ReasonOfCode(string code) => code switch
    {
        "ALTA" => "Alta del centro",
        "ERROR_DE_ALTA" => "Corrección de un error de alta",
        _ => code,
    };
}
