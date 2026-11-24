namespace ResidApp.Domain.Platform;

/// <summary>
/// Los pocos términos que cambian según el tipo de servicio del centro del ámbito activo (ADR 0017, fase 3). Es el único sitio donde se escriben: las vistas llaman aquí y no llevan condicionales
/// repartidos, para que el día que haya traducciones se sustituya solo esta clase. «Residente» no cambia (decisión de Orlando, 2026-11-19). Sin tipo conocido, es el vocabulario de una Residencia.
/// </summary>
public static class CenterVocabulary
{
    private static bool Day => CurrentCenterService.IsDayCenter;

    /// <summary>«la residencia» o «el centro de día», para dentro de una frase.</summary>
    public static string TheService => Day ? "el centro de día" : "la residencia";

    /// <summary>«fuera de la residencia» o «fuera del centro de día».</summary>
    public static string OutsideTheService => Day ? "fuera del centro de día" : "fuera de la residencia";

    /// <summary>Igual, con la inicial en mayúscula, para empezar una frase.</summary>
    public static string TheServiceStart => Day ? "El centro de día" : "La residencia";

    /// <summary>Cabecera de la columna de ubicación del historial del residente: un Centro de Día no tiene habitaciones ni plazas.</summary>
    public static string UnitAndPlaceHeading => Day ? "Unidad" : "Unidad (y habitación o plaza)";

    /// <summary>Lo que cambia un traslado dentro del centro.</summary>
    public static string TransferChanges => Day ? "de unidad dentro del centro" : "de unidad dentro del centro, o de habitación o plaza dentro de su unidad";
}
