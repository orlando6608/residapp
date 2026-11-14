namespace ResidApp.Web.Security;

/// <summary>
/// Funciones que borran datos y que, hasta que una persona experta decida cómo, no deben usarse con datos reales (<c>docs/privacidad/antes-de-datos-reales.md</c>,
/// apartado 8). Se deciden por entorno, no por centro: apagadas por defecto y en todo entorno con centros reales; encendidas en Development (local y pruebas) y en Azure dev.
/// </summary>
public sealed class PrivacidadOptions
{
    public const string Section = "Privacidad";

    /// <summary>«Anonimizar a este familiar…» (script 0087): borra sin guardar copia bloqueada. Apagada, la pantalla no ofrece el botón y la ruta responde 404.</summary>
    public bool AnonimizarFamiliarActivo { get; set; }
}
