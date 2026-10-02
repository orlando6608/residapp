namespace ResidApp.Infrastructure.Persistence;

/// <summary>Quién puede confirmar la recepción de una transferencia de seguimiento (Enfermería y Medicina): los miembros vigentes del equipo
/// entrante (equipos_miembros, script 0027). Sin equipo vinculado (transferencias anteriores a 0028) o con un equipo sin miembros vigentes,
/// la confirma cualquiera del ámbito, como antes, para que ninguna transferencia quede sin salida. Los identificadores de los parámetros
/// del fragmento son fijos: no entra texto del usuario.</summary>
internal static class TransferReceptionSql
{
    /// <summary>Condición verdadera si la cuenta puede confirmar la recepción de la transferencia <paramref name="transfer"/>
    /// (alias de una fila con equipo_entrante_id).</summary>
    public static string CanReceive(string transfer, string accountId) => $"""
        ({transfer}.equipo_entrante_id IS NULL
         OR NOT EXISTS (SELECT 1 FROM dbo.equipos_miembros tm WHERE tm.equipo_id = {transfer}.equipo_entrante_id AND tm.revocado_en IS NULL)
         OR EXISTS (SELECT 1 FROM dbo.equipos_miembros tm WHERE tm.equipo_id = {transfer}.equipo_entrante_id AND tm.revocado_en IS NULL
                       AND tm.cuenta_id = {accountId}))
        """;
}
