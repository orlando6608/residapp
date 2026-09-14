using ResidApp.Shared;

namespace ResidApp.Domain.Auxiliar;

/// <summary>Las diez áreas de "Registrar cambio" (AUX-06/AUX-07), distintas de las nueve áreas del basal
/// clínico (BaselineArea): estas son de observación cotidiana, no de valoración clínica.
/// docs/flujos-clinicos/registro-cotidiano-auxiliar.md. Sin catálogo de opciones rápidas todavía (el PDF
/// legado que las detallaba no se pudo leer en esta sesión) — por ahora cada área solo admite texto
/// libre; añadir opciones rápidas después es un cambio aditivo.</summary>
public enum DailyChangeAreaCode
{
    [Code("ALIMENTACION_HIDRATACION")] AlimentacionHidratacion,
    [Code("MOVILIDAD_FUNCIONALIDAD")] MovilidadFuncionalidad,
    [Code("ANIMO_CONDUCTA")] AnimoConducta,
    [Code("DOLOR_MALESTAR")] DolorMalestar,
    [Code("HECES_DIURESIS")] HecesDiuresis,
    [Code("SUENO")] Sueno,
    [Code("LESIONES_PIEL")] LesionesPiel,
    [Code("PARTICIPACION_RELACION_SOCIAL")] ParticipacionRelacionSocial,
    [Code("INCIDENCIAS_CAIDAS")] IncidenciasCaidas,
    [Code("ESTADO_CONCIENCIA")] EstadoConciencia,
}
