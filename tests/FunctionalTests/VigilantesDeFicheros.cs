using System.Runtime.CompilerServices;

namespace ResidApp.FunctionalTests;

/// <summary>
/// Cada copia de la aplicación que arrancan las pruebas vigila sus ficheros de configuración y estáticos, y en Linux cada vigilante es una
/// instancia de inotify, con un tope por usuario. Con muchas copias vivas a la vez se agotaba y fallaba una prueba cualquiera (500 al abrir una
/// pantalla, o IOException al arrancar). En las pruebas nadie cambia esos ficheros en marcha: se vigilan preguntando cada pocos segundos.
/// </summary>
internal static class VigilantesDeFicheros
{
    [ModuleInitializer]
    internal static void SinInotify() => Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "true");
}
