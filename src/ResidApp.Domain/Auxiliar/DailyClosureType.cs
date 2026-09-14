using ResidApp.Shared;

namespace ResidApp.Domain.Auxiliar;

/// <summary>Las tres acciones de cierre cotidiano del Auxiliar (AUX-04/AUX-05/AUX-06 a AUX-12),
/// mutuamente excluyentes entre sí. Sin precedente en el prototipo legado: ese vertical nunca llegó a
/// migrarse allí.</summary>
public enum DailyClosureType
{
    [Code("SIN_CAMBIOS")] SinCambios,
    [Code("NO_VALORABLE")] NoValorable,
    [Code("CAMBIO_ENVIADO")] CambioEnviado,
}
