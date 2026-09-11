using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Catalogs;

/// <summary>Traduce SLEEP_PATTERNS de validation.ts (área SUENO). Sin opción OTRA/OTRO: no admite texto libre.</summary>
public enum SleepPatternCode
{
    [Code("PATRON_HABITUALMENTE_CONSERVADO")] PatronHabitualmenteConservado,
    [Code("DIFICULTAD_INICIO_SUENO")] DificultadInicioSueno,
    [Code("DESPERTARES_FRECUENTES")] DespertaresFrecuentes,
    [Code("DESPERTAR_PRECOZ")] DespertarPrecoz,
    [Code("INVERSION_SUENO_VIGILIA")] InversionSuenoVigilia,
    [Code("SOMNOLENCIA_DIURNA_HABITUAL")] SomnolenciaDiurnaHabitual,
    [Code("PATRON_IRREGULAR_VARIABLE")] PatronIrregularVariable,
    [Code("NO_DOCUMENTADO")] NoDocumentado,
}
