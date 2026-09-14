using ResidApp.Shared;

namespace ResidApp.Domain.Baseline;

/// <summary>Traduce el catálogo `BARTHEL_COMUN_V0_1` documentado en
/// docs/legado-cloudflare/docs/architecture/0002-contrato-datos-residente-basal.md (tabla "Catálogo
/// BARTHEL_COMUN_V0_1"): las opciones y puntuaciones de cada uno de los diez ítems. La base de datos
/// deliberadamente no tiene una tabla `barthel_catalog_options` (fuera de alcance, ver comentario en
/// database/scripts/0001_init_sqlserver.sql); este catálogo es la única fuente de verdad para construir el
/// formulario ENF-21 y para derivar la puntuación otorgada a partir de la opción elegida.</summary>
public sealed record BarthelCatalogOption(string OptionCode, string Label, int Score);

public static class BarthelCatalog
{
    public static readonly IReadOnlyDictionary<BarthelItemCode, IReadOnlyList<BarthelCatalogOption>> Options =
        new Dictionary<BarthelItemCode, IReadOnlyList<BarthelCatalogOption>>
        {
            [BarthelItemCode.Comer] =
            [
                new("INDEPENDIENTE", "Independiente", 10),
                new("AYUDA_PREPARAR_O_CORTAR", "Necesita ayuda para preparar o cortar", 5),
                new("DEPENDIENTE", "Dependiente", 0),
            ],
            [BarthelItemCode.Lavarse] =
            [
                new("SOLO_COMPLETO", "Se ducha o baña completamente solo", 5),
                new("NECESITA_AYUDA", "Necesita ayuda", 0),
            ],
            [BarthelItemCode.Vestirse] =
            [
                new("INDEPENDIENTE", "Independiente", 10),
                new("AYUDA_REALIZA_AL_MENOS_MITAD", "Necesita ayuda, pero realiza al menos la mitad", 5),
                new("DEPENDIENTE", "Dependiente", 0),
            ],
            [BarthelItemCode.Arreglarse] =
            [
                new("INDEPENDIENTE_HIGIENE_PERSONAL_BASICA", "Independiente en higiene personal básica", 5),
                new("NECESITA_AYUDA", "Necesita ayuda", 0),
            ],
            [BarthelItemCode.Deposicion] =
            [
                new("CONTINENTE", "Continente", 10),
                new("INCONTINENCIA_OCASIONAL_O_AYUDA_ENEMAS_SUPOSITORIOS", "Incontinencia ocasional o ayuda con enemas/supositorios", 5),
                new("INCONTINENTE", "Incontinente", 0),
            ],
            [BarthelItemCode.Miccion] =
            [
                new("CONTINENTE", "Continente", 10),
                new("MAX_UN_EPISODIO_24H_O_AYUDA_SONDA_COLECTOR", "Máximo un episodio en 24 horas o ayuda con sonda/colector", 5),
                new("INCONTINENTE", "Incontinente", 0),
            ],
            [BarthelItemCode.UsoRetrete] =
            [
                new("INDEPENDIENTE", "Independiente", 10),
                new("PEQUENA_AYUDA_ROPA_O_TRANSFERENCIA_SE_LIMPIA_SOLO", "Pequeña ayuda con ropa o transferencia, pero se limpia solo", 5),
                new("DEPENDIENTE", "Dependiente", 0),
            ],
            [BarthelItemCode.TrasladoCamaSillon] =
            [
                new("INDEPENDIENTE", "Independiente", 15),
                new("SUPERVISION_O_MINIMA_AYUDA", "Supervisión o mínima ayuda", 10),
                new("GRAN_AYUDA_MANTIENE_SEDESTACION", "Gran ayuda, pero mantiene sedestación", 5),
                new("DEPENDIENTE_GRUA_O_DOS_PERSONAS", "Dependiente, grúa o dos personas", 0),
            ],
            [BarthelItemCode.Deambulacion] =
            [
                new("CAMINA_50M_INDEPENDIENTE_CON_AYUDA_TECNICA_SI_PRECISA", "Camina al menos 50 metros independientemente, con ayuda técnica si precisa", 15),
                new("NECESITA_AYUDA_O_SUPERVISION", "Necesita ayuda o supervisión", 10),
                new("INDEPENDIENTE_EN_SILLA_RUEDAS", "Independiente en silla de ruedas", 5),
                new("DEPENDIENTE", "Dependiente", 0),
            ],
            [BarthelItemCode.Escaleras] =
            [
                new("SUBE_BAJA_UN_PISO_SOLO", "Sube y baja un piso solo", 10),
                new("NECESITA_AYUDA_O_SUPERVISION", "Necesita ayuda o supervisión", 5),
                new("DEPENDIENTE", "Dependiente", 0),
            ],
        };

    /// <summary>Deriva la puntuación otorgada a partir del ítem y la opción elegida; nunca se acepta la
    /// puntuación como dato de entrada independiente (D1: "Puntuación del ítem... Servidor al cambiar la
    /// opción en borrador").</summary>
    public static int ScoreOf(BarthelItemCode item, string optionCode) =>
        Options[item].FirstOrDefault(o => o.OptionCode == optionCode)?.Score
            ?? throw new DomainValidationException("BASELINE_BARTHEL_OPTION_INVALID");
}
