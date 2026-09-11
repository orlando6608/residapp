namespace ResidApp.Shared;

/// <summary>
/// Jerarquía mínima de excepciones de dominio. Cada una lleva un código 1:1 con los strings de error que
/// lanzaba el TS original (p. ej. "BASELINE_SIGNATURE_BEFORE_CREATION"), para que la capa de aplicación
/// pueda normalizarlos igual que lib/application/errors.ts. Vive en Shared porque Domain, Application e
/// Infrastructure la lanzan y/o capturan todas.
/// </summary>
public abstract class DomainException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>Entrada inválida o payload que no cumple una regla de catálogo/formato.</summary>
public sealed class DomainValidationException(string code) : DomainException(code);

/// <summary>Transición de estado no permitida (p. ej. firmar un borrador ya firmado).</summary>
public sealed class DomainStateException(string code) : DomainException(code);
