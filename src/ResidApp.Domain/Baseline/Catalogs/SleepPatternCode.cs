using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Catalogs;

/// <summary>Traduce SLEEP_PATTERNS de validation.ts (área SUENO). Sin opción OTRA/OTRO: no admite texto libre.</summary>
public enum SleepPatternCode
{
    [Code("PATRON_HABITUALMENTE_CONSERVADO")] [Display(Name = "Patrón habitualmente conservado")] PatronHabitualmenteConservado,
    [Code("DIFICULTAD_INICIO_SUENO")] [Display(Name = "Dificultad de inicio del sueño")] DificultadInicioSueno,
    [Code("DESPERTARES_FRECUENTES")] [Display(Name = "Despertares frecuentes")] DespertaresFrecuentes,
    [Code("DESPERTAR_PRECOZ")] [Display(Name = "Despertar precoz")] DespertarPrecoz,
    [Code("INVERSION_SUENO_VIGILIA")] [Display(Name = "Inversión sueño-vigilia")] InversionSuenoVigilia,
    [Code("SOMNOLENCIA_DIURNA_HABITUAL")] [Display(Name = "Somnolencia diurna habitual")] SomnolenciaDiurnaHabitual,
    [Code("PATRON_IRREGULAR_VARIABLE")] [Display(Name = "Patrón irregular o variable")] PatronIrregularVariable,
    [Code("NO_DOCUMENTADO")] [Display(Name = "No documentado")] NoDocumentado,
}
