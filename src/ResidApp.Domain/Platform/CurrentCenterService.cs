namespace ResidApp.Domain.Platform;

/// <summary>
/// El tipo de servicio del centro del ámbito activo durante la petición (ADR 0017, punto 7): el único ayudante con el que las pantallas y los textos de cara a las personas se adaptan al tipo.
/// Como <c>CenterTime</c>, es de la petición (<see cref="AsyncLocal{T}"/>): quien conozca el centro lo fija con <see cref="Use"/> (el middleware, leyéndolo de la base de datos, nunca de la cookie del
/// ámbito). Sin fijar, o en el centro de Plataforma, no hay tipo (<see cref="Type"/> es nulo) y todo se comporta como una Residencia, que es lo de siempre.
/// </summary>
public static class CurrentCenterService
{
    private static readonly AsyncLocal<CenterServiceType?> Current = new();

    /// <summary>El tipo del centro del ámbito activo, o nulo si no se conoce (sin sesión, sin ámbito o centro de Plataforma).</summary>
    public static CenterServiceType? Type => Current.Value;

    /// <summary>Solo un Centro de Día cambia las pantallas: sin tipo conocido, es el comportamiento de una Residencia.</summary>
    public static bool IsDayCenter => Current.Value == CenterServiceType.CentroDeDia;

    /// <summary>Fija el tipo hasta que se libere lo devuelto.</summary>
    public static IDisposable Use(CenterServiceType? type)
    {
        var previous = Current.Value;
        Current.Value = type;
        return new Restore(previous);
    }

    private sealed class Restore(CenterServiceType? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
