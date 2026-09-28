using ResidApp.Domain.Baseline;
using ResidApp.Domain.Baseline.Answers;
using ResidApp.Domain.Baseline.Catalogs;

namespace ResidApp.IntegrationTests.TestSupport;

/// <summary>Contenido válido y completo de un borrador de basal (9 áreas + Barthel de 100 puntos), para
/// los tests que necesitan llegar a firmar una versión.</summary>
internal static class BaselineTestData
{
    public static IReadOnlyList<(BaselineArea Area, IBaselineAreaAnswer Answer)> NineAreas() =>
    [
        (BaselineArea.Movilidad,
            new MobilityAreaAnswer(MobilityDisplacementCode.DeambulaIndependienteSinAyuda, MobilityAidCode.Ninguna, null, MobilityTransferCode.Independiente)),
        (BaselineArea.Alimentacion,
            new FeedingAreaAnswer(FeedingRouteCode.Oral, FoodTextureCode.Normal, null, LiquidConsistencyCode.Iddsi0FinoSinEspesar,
                FeedingAssistanceCode.Independiente, SwallowingPrecautionsCode.NingunaDocumentada, null)),
        (BaselineArea.Continencia,
            new ContinenceAreaAnswer(ContinenceValueCode.Continente, ContinenceValueCode.Continente, [ContinenceManagementCode.Ninguno], null)),
        (BaselineArea.AseoHigiene, new PersonalCareAreaAnswer(PersonalCareCode.Independiente, BathingCode.Independiente)),
        (BaselineArea.Cognicion,
            new CognitionAreaAnswer(CognitionCategoryCode.SinDeterioroConocidoODocumentado, null, null, null, null, null, null)),
        (BaselineArea.Comunicacion,
            new CommunicationAreaAnswer(ComprehensionCode.ComprensionFuncional, ExpressionCode.ExpresaNecesidadesEficazmente, [CommunicationFormCode.LenguajeOral], null)),
        (BaselineArea.Conducta, new BehaviorAreaAnswer(BehaviorStatusCode.SinConductasRelevantesConocidas, [], null)),
        (BaselineArea.Sueno, new SleepAreaAnswer([SleepPatternCode.PatronHabitualmenteConservado])),
        (BaselineArea.AyudasHabituales, new UsualAidsAreaAnswer([UsualAidCode.Ninguno], null, null)),
    ];

    public static List<BarthelItem> FullBarthelItems() =>
    [
        new(BarthelItemCode.Comer, "INDEPENDIENTE", BarthelCatalog.ScoreOf(BarthelItemCode.Comer, "INDEPENDIENTE")),
        new(BarthelItemCode.Lavarse, "SOLO_COMPLETO", BarthelCatalog.ScoreOf(BarthelItemCode.Lavarse, "SOLO_COMPLETO")),
        new(BarthelItemCode.Vestirse, "INDEPENDIENTE", BarthelCatalog.ScoreOf(BarthelItemCode.Vestirse, "INDEPENDIENTE")),
        new(BarthelItemCode.Arreglarse, "INDEPENDIENTE_HIGIENE_PERSONAL_BASICA", BarthelCatalog.ScoreOf(BarthelItemCode.Arreglarse, "INDEPENDIENTE_HIGIENE_PERSONAL_BASICA")),
        new(BarthelItemCode.Deposicion, "CONTINENTE", BarthelCatalog.ScoreOf(BarthelItemCode.Deposicion, "CONTINENTE")),
        new(BarthelItemCode.Miccion, "CONTINENTE", BarthelCatalog.ScoreOf(BarthelItemCode.Miccion, "CONTINENTE")),
        new(BarthelItemCode.UsoRetrete, "INDEPENDIENTE", BarthelCatalog.ScoreOf(BarthelItemCode.UsoRetrete, "INDEPENDIENTE")),
        new(BarthelItemCode.TrasladoCamaSillon, "INDEPENDIENTE", BarthelCatalog.ScoreOf(BarthelItemCode.TrasladoCamaSillon, "INDEPENDIENTE")),
        new(BarthelItemCode.Deambulacion, "CAMINA_50M_INDEPENDIENTE_CON_AYUDA_TECNICA_SI_PRECISA",
            BarthelCatalog.ScoreOf(BarthelItemCode.Deambulacion, "CAMINA_50M_INDEPENDIENTE_CON_AYUDA_TECNICA_SI_PRECISA")),
        new(BarthelItemCode.Escaleras, "SUBE_BAJA_UN_PISO_SOLO", BarthelCatalog.ScoreOf(BarthelItemCode.Escaleras, "SUBE_BAJA_UN_PISO_SOLO")),
    ];
}
