namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Los dos trozos de SQL de la regla única de visibilidad de las citas externas para la familia (script 0104): el centro está encendido y el residente no está restringido. Cada interruptor es una
/// tabla de solo añadir y su estado vigente es el de la fila de mayor número; sin fila, el centro está apagado y el residente no está restringido. Se evalúan en cada lectura, sin caché. Los usan la
/// lectura de la familia y el recuento de los ajustes del personal, para que las dos cuenten lo mismo. Parámetro: @CenterId.
/// </summary>
internal static class ExternalFamilyVisibilitySql
{
    /// <summary>El centro @CenterId tiene encendido el interruptor para las familias.</summary>
    internal const string CenterEnabled = """
        EXISTS (SELECT 1 FROM dbo.citas_externas_familia_centro center_switch
                 WHERE center_switch.centro_id = @CenterId AND center_switch.familia_ve = 1
                   AND center_switch.numero = (SELECT MAX(latest.numero) FROM dbo.citas_externas_familia_centro latest WHERE latest.centro_id = center_switch.centro_id))
        """;

    /// <summary>El residente {R} (una expresión) está restringido: la familia no ve las citas externas de esta persona.</summary>
    internal const string ResidentRestrictedTemplate = """
        EXISTS (SELECT 1 FROM dbo.citas_externas_familia_residente resident_switch
                 WHERE resident_switch.centro_id = @CenterId AND resident_switch.residente_id = {R} AND resident_switch.familia_no_ve = 1
                   AND resident_switch.numero = (SELECT MAX(latest.numero) FROM dbo.citas_externas_familia_residente latest
                                                  WHERE latest.centro_id = resident_switch.centro_id AND latest.residente_id = resident_switch.residente_id))
        """;

    internal static string ResidentRestricted(string residentExpression) => ResidentRestrictedTemplate.Replace("{R}", residentExpression);
}
