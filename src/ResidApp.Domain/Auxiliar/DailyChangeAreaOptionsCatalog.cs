namespace ResidApp.Domain.Auxiliar;

/// <summary>Qué opciones rápidas de <see cref="DailyChangeAreaOptionCode"/> corresponden a cada una de las
/// diez áreas (AUX-07). Las tres últimas áreas no tienen checklist en el wireframe legado — solo texto
/// libre — y devuelven una lista vacía.</summary>
public static class DailyChangeAreaOptionsCatalog
{
    private static readonly IReadOnlyDictionary<DailyChangeAreaCode, IReadOnlyList<DailyChangeAreaOptionCode>> ByArea =
        new Dictionary<DailyChangeAreaCode, IReadOnlyList<DailyChangeAreaOptionCode>>
        {
            [DailyChangeAreaCode.AlimentacionHidratacion] =
            [
                DailyChangeAreaOptionCode.NulaIngesta,
                DailyChangeAreaOptionCode.RechazaIngesta,
                DailyChangeAreaOptionCode.DisminucionIngestaLiquidos,
                DailyChangeAreaOptionCode.Atragantamiento,
            ],
            [DailyChangeAreaCode.MovilidadFuncionalidad] =
            [
                DailyChangeAreaOptionCode.NoQuiereLevantarse,
                DailyChangeAreaOptionCode.IncapacidadCaminar,
                DailyChangeAreaOptionCode.CaminaConDificultad,
                DailyChangeAreaOptionCode.DebilidadGeneralizada,
            ],
            [DailyChangeAreaCode.AnimoConducta] =
            [
                DailyChangeAreaOptionCode.DisminucionAnimo,
                DailyChangeAreaOptionCode.Irritabilidad,
                DailyChangeAreaOptionCode.Agresividad,
                DailyChangeAreaOptionCode.Hiperreactividad,
            ],
            [DailyChangeAreaCode.DolorMalestar] =
            [
                DailyChangeAreaOptionCode.Cefalea,
                DailyChangeAreaOptionCode.DolorMmss,
                DailyChangeAreaOptionCode.DolorMmii,
                DailyChangeAreaOptionCode.DolorAbdominal,
                DailyChangeAreaOptionCode.DolorOtro,
            ],
            [DailyChangeAreaCode.HecesDiuresis] =
            [
                DailyChangeAreaOptionCode.Diarrea,
                DailyChangeAreaOptionCode.Estrenimiento,
                DailyChangeAreaOptionCode.DisminucionDiuresis,
                DailyChangeAreaOptionCode.CambiosColoracionOrina,
            ],
            [DailyChangeAreaCode.Sueno] =
            [
                DailyChangeAreaOptionCode.Insomnio,
                DailyChangeAreaOptionCode.Somnolencia,
            ],
            [DailyChangeAreaCode.LesionesPiel] =
            [
                DailyChangeAreaOptionCode.Herida,
                DailyChangeAreaOptionCode.Upp,
                DailyChangeAreaOptionCode.Hematoma,
            ],
            [DailyChangeAreaCode.ParticipacionRelacionSocial] = [],
            [DailyChangeAreaCode.IncidenciasCaidas] = [],
            [DailyChangeAreaCode.EstadoConciencia] = [],
        };

    public static IReadOnlyList<DailyChangeAreaOptionCode> OptionsFor(DailyChangeAreaCode area) => ByArea[area];

    /// <summary>Áreas 8, 9 y 10: sin checklist en el wireframe legado, el texto libre es obligatorio por
    /// ser el único contenido posible.</summary>
    public static bool HasChecklist(DailyChangeAreaCode area) => ByArea[area].Count > 0;
}
