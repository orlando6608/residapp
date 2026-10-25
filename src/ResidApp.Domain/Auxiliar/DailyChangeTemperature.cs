namespace ResidApp.Domain.Auxiliar;

/// <summary>Temperatura de «Registrar cambio» (historia 4 del Auxiliar). Rango válido fijado por CJ el 2026-10-25
/// (dictado-por-voz-confirmaciones-cj, 3.1 y 3.2): de 34,0 a 42,0 °C, amplio para admitir la hipotermia leve y la fiebre alta de una
/// persona mayor; fuera de él casi siempre es un error de dictado o de tecleo («378»), así que no se guarda.</summary>
public static class DailyChangeTemperature
{
    public const decimal Min = 34.0m;
    public const decimal Max = 42.0m;

    public static bool IsValid(decimal celsius) => celsius is >= Min and <= Max;
}
