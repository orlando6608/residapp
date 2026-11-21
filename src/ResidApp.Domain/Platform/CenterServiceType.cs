using ResidApp.Shared;

namespace ResidApp.Domain.Platform;

// Tipo de servicio del centro (ADR 0017, script 0113): Residencia o Centro de Día. No es un módulo: lo fija Plataforma al crear el centro y solo se corrige un error de alta, con el centro sin personas.

/// <summary>El tipo de servicio de un centro. Los códigos son los de la base de datos (<c>centros.tipo_de_servicio</c>); un centro anterior al script 0113 es una Residencia.</summary>
public enum CenterServiceType
{
    [Code("RESIDENCIA")] Residencia,
    [Code("CENTRO_DE_DIA")] CentroDeDia,
}

/// <summary>Una fila de la constancia de un centro (<c>tipos_servicio_centro</c>): su alta o una corrección de error de alta. Sin la cuenta de quien la fijó: Plataforma ve qué y cuándo, no una cuenta.</summary>
public sealed record CenterServiceTypeChange(int Number, CenterServiceType Type, string Reason, DateTimeOffset ChangedAt);

/// <summary>El tipo vigente de un centro, su constancia (la más reciente primero) y si tiene personas. «Tiene personas» es solo sí o no: Plataforma no ve cuántas.</summary>
public sealed record CenterServiceTypeState(CenterServiceType Current, IReadOnlyList<CenterServiceTypeChange> History, bool HasPeople)
{
    /// <summary>Solo un centro sin ninguna persona (residentes de cualquier estado ni familiares) admite la corrección de un error de alta.</summary>
    public bool CanBeCorrected => !HasPeople;
}

public static class CenterServiceTypeRules
{
    public const string Invalid = "CENTER_SERVICE_TYPE_INVALID";

    /// <summary>El tipo elegido al crear el centro o al corregirlo. No hay valor por defecto: quien no elige, no crea (el formulario no preselecciona).</summary>
    public static CenterServiceType Validate(CenterServiceType? type) =>
        type is { } value && Enum.IsDefined(value) ? value : throw new DomainValidationException(Invalid);
}
