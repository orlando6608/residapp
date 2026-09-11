namespace ResidApp.Shared;

/// <summary>
/// Traduce lib/domain/shared/identifiers.ts: allí los IDs son strings UUID v4 con marca de tipo
/// (branded types) validados en runtime por isOpaqueEntityId. Aquí son structs de solo lectura sobre
/// Guid que se autovalidan en el constructor (rechazan Guid.Empty). Conocer el identificador nunca
/// sustituye una comprobación de autorización.
/// </summary>
internal static class OpaqueId
{
    public static Guid Validate(Guid value, string paramName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("El identificador no puede ser Guid.Empty.", paramName);
        }
        return value;
    }
}

public readonly record struct AccountId(Guid Value)
{
    public Guid Value { get; } = OpaqueId.Validate(Value, nameof(Value));
    public static AccountId New() => new(Guid.NewGuid());
    public static AccountId From(Guid value) => new(value);
    public static AccountId From(string value) => new(Guid.Parse(value));
    public override string ToString() => Value.ToString();
}

public readonly record struct CenterId(Guid Value)
{
    public Guid Value { get; } = OpaqueId.Validate(Value, nameof(Value));
    public static CenterId New() => new(Guid.NewGuid());
    public static CenterId From(Guid value) => new(value);
    public static CenterId From(string value) => new(Guid.Parse(value));
    public override string ToString() => Value.ToString();
}

public readonly record struct UnitId(Guid Value)
{
    public Guid Value { get; } = OpaqueId.Validate(Value, nameof(Value));
    public static UnitId New() => new(Guid.NewGuid());
    public static UnitId From(Guid value) => new(value);
    public static UnitId From(string value) => new(Guid.Parse(value));
    public override string ToString() => Value.ToString();
}

public readonly record struct ResidentId(Guid Value)
{
    public Guid Value { get; } = OpaqueId.Validate(Value, nameof(Value));
    public static ResidentId New() => new(Guid.NewGuid());
    public static ResidentId From(Guid value) => new(value);
    public static ResidentId From(string value) => new(Guid.Parse(value));
    public override string ToString() => Value.ToString();
}

public readonly record struct BaselineVersionId(Guid Value)
{
    public Guid Value { get; } = OpaqueId.Validate(Value, nameof(Value));
    public static BaselineVersionId New() => new(Guid.NewGuid());
    public static BaselineVersionId From(Guid value) => new(value);
    public static BaselineVersionId From(string value) => new(Guid.Parse(value));
    public override string ToString() => Value.ToString();
}

/// <summary>
/// No existía como tipo bránded independiente en el TS original (draftId viajaba como string validado
/// solo por isOpaqueEntityId); se añade aquí por fidelidad de patrón, ya que se usa como opaco en todo
/// el flujo de firma del basal.
/// </summary>
public readonly record struct BaselineDraftId(Guid Value)
{
    public Guid Value { get; } = OpaqueId.Validate(Value, nameof(Value));
    public static BaselineDraftId New() => new(Guid.NewGuid());
    public static BaselineDraftId From(Guid value) => new(value);
    public static BaselineDraftId From(string value) => new(Guid.Parse(value));
    public override string ToString() => Value.ToString();
}
