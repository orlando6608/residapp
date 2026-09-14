using ResidApp.Shared;

namespace ResidApp.Domain.Auxiliar;

/// <summary>Catálogo cerrado de opciones rápidas predefinidas por área (AUX-07,
/// docs/legado-cloudflare/docs/2026-09-02_Wireframe_funcional_Auxiliar_v0.2_cerrado.pdf). Cada código solo
/// es válido para el área que lo define — ver <see cref="DailyChangeAreaOptionsCatalog"/> — aunque viva en
/// un único enum plano, mismo criterio que DailyChangePriorityReason.</summary>
public enum DailyChangeAreaOptionCode
{
    // 1. Alimentación / hidratación
    [Code("NULA_INGESTA")] NulaIngesta,
    [Code("RECHAZA_INGESTA")] RechazaIngesta,
    [Code("DISMINUCION_INGESTA_LIQUIDOS")] DisminucionIngestaLiquidos,
    [Code("ATRAGANTAMIENTO")] Atragantamiento,

    // 2. Movilidad / funcionalidad
    [Code("NO_QUIERE_LEVANTARSE")] NoQuiereLevantarse,
    [Code("INCAPACIDAD_CAMINAR")] IncapacidadCaminar,
    [Code("CAMINA_CON_DIFICULTAD")] CaminaConDificultad,
    [Code("DEBILIDAD_GENERALIZADA")] DebilidadGeneralizada,

    // 3. Ánimo / conducta
    [Code("DISMINUCION_ANIMO")] DisminucionAnimo,
    [Code("IRRITABILIDAD")] Irritabilidad,
    [Code("AGRESIVIDAD")] Agresividad,
    [Code("HIPERREACTIVIDAD")] Hiperreactividad,

    // 4. Dolor / malestar
    [Code("CEFALEA")] Cefalea,
    [Code("DOLOR_MMSS")] DolorMmss,
    [Code("DOLOR_MMII")] DolorMmii,
    [Code("DOLOR_ABDOMINAL")] DolorAbdominal,
    [Code("DOLOR_OTRO")] DolorOtro,

    // 5. Heces / diuresis
    [Code("DIARREA")] Diarrea,
    [Code("ESTRENIMIENTO")] Estrenimiento,
    [Code("DISMINUCION_DIURESIS")] DisminucionDiuresis,
    [Code("CAMBIOS_COLORACION_ORINA")] CambiosColoracionOrina,

    // 6. Sueño
    [Code("INSOMNIO")] Insomnio,
    [Code("SOMNOLENCIA")] Somnolencia,

    // 7. Lesiones en piel
    [Code("HERIDA")] Herida,
    [Code("UPP")] Upp,
    [Code("HEMATOMA")] Hematoma,
}
