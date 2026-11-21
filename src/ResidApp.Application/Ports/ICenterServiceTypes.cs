using ResidApp.Domain.Platform;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>
/// El tipo de servicio de un centro (ADR 0017, script 0113), visto y corregido por Plataforma. La fuente de verdad es <c>centros.tipo_de_servicio</c>; <c>tipos_servicio_centro</c> es la constancia
/// de solo añadir. Corregir es una sola transacción que llama al procedimiento <c>corregir_tipo_servicio_centro</c>, única vía de cambio: comprueba otra vez que el ámbito es de Plataforma, que el
/// centro no tiene ninguna persona y deja la auditoría <c>CENTER_SERVICE_TYPE_CHANGE</c> sin datos.
/// </summary>
public interface ICenterServiceTypeRepository
{
    /// <summary>El tipo vigente de un centro, su constancia y si tiene personas (sí o no, nunca cuántas). El centro de Plataforma o uno que no existe: CENTER_SERVICE_TYPE_CENTER_INVALID.</summary>
    Task<CenterServiceTypeState> GetAsync(PlatformAccess access, CenterId centerId, CancellationToken ct = default);

    /// <summary>
    /// Corrige el tipo de un centro (error de alta). El mismo tipo que ya tiene: CENTER_SERVICE_TYPE_SEQUENCE_INVALID. Un centro con personas: CENTER_SERVICE_TYPE_NOT_EMPTY. Quien no es Plataforma:
    /// CENTER_SERVICE_TYPE_FORBIDDEN.
    /// </summary>
    Task ChangeAsync(PlatformAccess access, CenterId centerId, CenterServiceType type, CancellationToken ct = default);
}
