using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Baseline;
using ResidApp.Domain.Baseline.Answers;
using ResidApp.Domain.Baseline.Catalogs;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Residents;
using ResidApp.Shared;
using ResidApp.Web.Models;

namespace ResidApp.FunctionalTests;

/// <summary>DER-03/DER-04: los datos automáticos del informe de derivación. No necesita BD: arma el detalle
/// del evento a mano.</summary>
public class ReferralReportBuilderTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 29, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Build_ReuneLasFuentesEnOrden_YNuncaIncluyeLosContactosNiLasComunicaciones()
    {
        var vitals = new VitalSigns(37.9m, 150, 85, 110, 28, 86, RespiratorySupportCode.Oxigenoterapia, 3, null, null, null, null);
        var detail = new PendingChangeDetail(
            EventId: Guid.NewGuid(), Origin: ClinicalEventOrigin.EventoEnfermeria, ResidentId: ResidentId.New(),
            ResidentDisplayName: "Residente Informe", UnitId: UnitId.New(), UnitName: "Unidad 2",
            Classification: DailyChangeClassification.Prioritario, Areas: [], TemperatureCelsius: null,
            Observation: "Disnea brusca al levantarse.", ClinicalData: "Tiraje intercostal.",
            AuthorProfile: SystemProfile.Enfermeria, PriorityReason: null, DirectNoticeNotes: null, OccurredAt: At,
            Status: ClinicalEventStatus.ProtocoloUrgente, Revision: 7, AssessmentStartedByCurrentAccount: true, AssessmentStartedAt: At,
            Assessment: new NursingAssessmentDraft(
                new NursingAssessmentContent("Crepitantes.", "Insuficiencia respiratoria.", "Oxigenoterapia a 3 L/min.",
                    "Aviso a la hija por teléfono.", "Sin mejoría.", vitals),
                true, At),
            ReferenceRanges: [], Closure: null, FollowUp: null, Escalation: null,
            Medical: new MedicalDetail(null, null, null, [], null),
            UrgentProtocol: new UrgentProtocolDetail(SystemProfile.Enfermeria, "Desaturación.", true, At,
            [
                new UrgentProtocolEntrySummary(UrgentProtocolEntryType.Actuacion, "Vía periférica.", null, null, true, At.AddMinutes(5)),
                new UrgentProtocolEntrySummary(UrgentProtocolEntryType.Contacto, "Piden traslado.", "112 Emergencias", At.AddMinutes(6), true, At.AddMinutes(7)),
                new UrgentProtocolEntrySummary(UrgentProtocolEntryType.Evolucion, "Satura 88 %.", null, null, true, At.AddMinutes(10)),
            ]),
            // Ya firmado y con contacto urgente (0022): ni el contacto ni las llamadas entran en el informe (DER-04).
            Referral: new ReferralDetail(SystemProfile.Enfermeria, "Desaturación.", true, At, new string('0', 64),
                [new FamilyCallAttemptSummary("Lucía Contacto (Hija)", At, FamilyCallResult.NoContesta, "Buzón de voz.", true, At)],
                [new EmergencyContactSummary("Lucía Contacto", "Hija", "600 999 888")]));
        var baseline = new CurrentBaselineSummary(BaselineVersionId.New(), 2, BaselineReason.Alta, At.AddDays(-30),
        [
            new BaselineAreaSummary(BaselineArea.Comunicacion, new CommunicationAreaAnswer(
                ComprehensionCode.ComprensionFuncional, ExpressionCode.ExpresaNecesidadesEficazmente,
                [CommunicationFormCode.LenguajeOral, CommunicationFormCode.Gestos], null), null),
        ], 65);
        var identification = new ResidentIdentification("Residente Informe", new DateOnly(1938, 2, 20), DocumentedSexCode.Mujer, "Centro Norte", "Unidad 2");

        var health = new HealthBackgroundView(new AllergyRecord(2,
            new AllergyRecordContent(HealthBackgroundState.NoRefiere, [new AllergyItem(AllergyItemType.Intolerancia, "Fármaco Alfa", "náuseas")], HealthBackgroundSource.Residente),
            SystemProfile.Medicina, At.AddDays(-3)), true);
        var sections = ReferralReportBuilder.Build(detail, baseline, identification, health);
        var text = string.Join("\n", sections.SelectMany(s => s.Lines.Prepend(s.Title)));

        Assert.All(sections, s => Assert.True(s.Automatic));
        Assert.Equal(
            new[] { "Identificación del residente y del centro", "Alergias e intolerancias a medicamentos", "Enfermedades e intervenciones", "Basal vigente", "Observación de origen", "Valoraciones",
                "Constantes y oxigenoterapia", "Actuaciones", "Evolución" },
            sections.Select(s => s.Title));
        foreach (var expected in new[]
        {
            "Fecha de nacimiento: 20/02/1938", "Sexo documentado: Mujer", "Centro: Centro Norte", "Barthel total: 65 / 100",
            "Comunicación — Comprensión: Comprensión funcional; Expresión: Expresa necesidades eficazmente; Formas habituales de comunicación: Lenguaje oral, Gestos",
            "Disnea brusca al levantarse.", "Datos clínicos pertinentes: Tiraje intercostal.",
            "Enfermería, valoración: Insuficiencia respiratoria.", "oxigenoterapia a 3 L/min",
            "Enfermería: Oxigenoterapia a 3 L/min.", "Vía periférica.", "Satura 88 %.",
            "No refiere alergias a medicamentos.", "Intolerancia o efecto adverso: Fármaco Alfa (náuseas)",
            "Fuente: el propio residente.", "(versión 2)", "última modificación el 26/09/2026", "por Medicina", "Esta ficha no se revisa de forma periódica.",
        })
        {
            Assert.Contains(expected, text);
        }
        foreach (var excluded in new[]
        {
            "112 Emergencias", "Piden traslado.", "Aviso a la hija", "CFS", "System.Collections", "Lucía Contacto", "600 999 888", "Buzón de voz.", "revisado",
        })
        {
            Assert.DoesNotContain(excluded, text);
        }
    }

    private static HealthBackgroundView Health(AllergyRecordContent content, int version = 1) =>
        new(new AllergyRecord(version, content, SystemProfile.Enfermeria, At), true);

    [Fact]
    public void AlergiasDelInforme_NoRefiereNoConstaPendienteYConEntradasSalenDistintos_YNingunoDiceRevisado()
    {
        var noRefiere = HealthBackgroundDisplay.ReportLines(Health(new AllergyRecordContent(HealthBackgroundState.NoRefiere, null, null)));
        var noConsta = HealthBackgroundDisplay.ReportLines(Health(new AllergyRecordContent(HealthBackgroundState.NoConsta, null, null)));
        var pendiente = HealthBackgroundDisplay.ReportLines(new HealthBackgroundView(null, true));
        var conEntradas = HealthBackgroundDisplay.ReportLines(Health(new AllergyRecordContent(
            HealthBackgroundState.ConEntradas, [new AllergyItem(AllergyItemType.Alergia, "Fármaco Beta", "urticaria")], HealthBackgroundSource.InformeMedico)));
        var ilegible = HealthBackgroundDisplay.ReportLines(new HealthBackgroundView(null, true, UnreliableVersion: 3));
        var sinLeer = HealthBackgroundDisplay.ReportLines(null);

        Assert.Equal("No refiere alergias a medicamentos.", noRefiere[0]);
        Assert.StartsWith("No consta: información no fiable o sin confirmar. No equivale a «sin alergias».", noConsta[0]);
        Assert.NotEqual(noRefiere[0], noConsta[0]);
        Assert.Equal(HealthBackgroundDisplay.Pending, Assert.Single(pendiente));
        Assert.Contains("No equivale a «sin alergias»", pendiente[0]);
        Assert.Equal("Tiene alergias a medicamentos:", conEntradas[0]);
        Assert.Contains("Alergia a: Fármaco Beta (urticaria)", conEntradas);
        Assert.Contains("Fuente: informe médico.", conEntradas);
        // Una ficha ilegible o sin leer nunca se imprime como válida, y no se confunde con «No refiere».
        Assert.Equal(sinLeer, ilegible);
        Assert.Contains("no es fiable", Assert.Single(ilegible));
        foreach (var lines in new[] { noRefiere, noConsta, pendiente, conEntradas, ilegible })
        {
            Assert.DoesNotContain(lines, line => line.Contains("revisad", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(lines, line => line.Contains("sin alergias conocidas", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void AlergiasDelInforme_SiLaFichaCambia_LaHuellaDelInformeCambia_ParaQueNoSeFirmeLoQueNoSeVio()
    {
        ReferralReportContent Compose(HealthBackgroundView health) => ReferralReportContent.Compose(
            [new ReferralReportSection(HealthBackgroundDisplay.AllergiesTitle, true, HealthBackgroundDisplay.ReportLines(health))], new ReferralReportInput("Motivo.", null));
        var before = Compose(Health(new AllergyRecordContent(HealthBackgroundState.NoRefiere, null, null)));
        var after = Compose(Health(new AllergyRecordContent(
            HealthBackgroundState.ConEntradas, [new AllergyItem(AllergyItemType.Alergia, "Fármaco Beta", null)], null), version: 2));
        var same = Compose(Health(new AllergyRecordContent(HealthBackgroundState.NoRefiere, null, null)));

        Assert.NotEqual(before.Hash(), after.Hash());
        Assert.Equal(before.Hash(), same.Hash());
    }

    private static HealthBackgroundView ConditionsHealth(ConditionRecordContent content, int version = 1) =>
        new(null, true, Conditions: new ConditionRecord(version, content, SystemProfile.Medicina, At));

    [Fact]
    public void EnfermedadesDelInforme_ConEntradasSinEnfermedadesNoConstaPendienteEIlegibleSalenDistintos_YNingunoDiceRevisado()
    {
        var conEntradas = HealthBackgroundDisplay.ConditionsReportLines(ConditionsHealth(new ConditionRecordContent(HealthBackgroundState.ConEntradas,
            [new ConditionItem(ConditionItemType.Enfermedad, "Enfermedad Alfa", null), new ConditionItem(ConditionItemType.Intervencion, "Intervención Beta", 1999)],
            HealthBackgroundSource.InformeMedico), version: 3));
        var sinEnfermedades = HealthBackgroundDisplay.ConditionsReportLines(ConditionsHealth(new ConditionRecordContent(HealthBackgroundState.NoRefiere, null, null)));
        var noConsta = HealthBackgroundDisplay.ConditionsReportLines(ConditionsHealth(new ConditionRecordContent(HealthBackgroundState.NoConsta, null, null)));
        var pendiente = HealthBackgroundDisplay.ConditionsReportLines(new HealthBackgroundView(null, true));
        var ilegible = HealthBackgroundDisplay.ConditionsReportLines(new HealthBackgroundView(null, true, UnreliableConditionsVersion: 2));
        var sinLeer = HealthBackgroundDisplay.ConditionsReportLines(null);

        Assert.Equal("Enfermedades e intervenciones:", conEntradas[0]);
        // La lista completa, con el año solo si se sabe; la versión y la última modificación, sin «revisado».
        Assert.Contains("Enfermedad: Enfermedad Alfa", conEntradas);
        Assert.Contains("Intervención: Intervención Beta (1999)", conEntradas);
        Assert.Contains("Fuente: informe médico.", conEntradas);
        Assert.Contains(conEntradas, line => line.Contains("(versión 3)") && line.Contains("última modificación el 29/09/2026") && line.Contains("por Medicina"));
        Assert.Contains(conEntradas, line => line.Contains("Esta ficha no se revisa de forma periódica.") && line.Contains("Contrastar con la documentación clínica del centro y con el residente o su familia."));
        Assert.Equal("Sin enfermedades ni intervenciones conocidas.", sinEnfermedades[0]);
        Assert.StartsWith("No se sabe o no consta", noConsta[0]);
        Assert.NotEqual(sinEnfermedades[0], noConsta[0]);
        Assert.Equal(HealthBackgroundDisplay.ConditionsPending, Assert.Single(pendiente));
        Assert.Contains("No equivale a «sin enfermedades»", pendiente[0]);
        Assert.Equal(sinLeer, ilegible);
        Assert.Contains("no es fiable", Assert.Single(ilegible));
        foreach (var lines in new[] { conEntradas, sinEnfermedades, noConsta, pendiente, ilegible })
        {
            Assert.DoesNotContain(lines, line => line.Contains("revisad", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void EnfermedadesDelInforme_SiLaFichaCambia_LaHuellaDelInformeCambia_ParaQueNoSeFirmeLoQueNoSeVio()
    {
        ReferralReportContent Compose(HealthBackgroundView health) => ReferralReportContent.Compose(
            [new ReferralReportSection(HealthBackgroundDisplay.ConditionsTitle, true, HealthBackgroundDisplay.ConditionsReportLines(health))], new ReferralReportInput("Motivo.", null));
        var before = Compose(ConditionsHealth(new ConditionRecordContent(HealthBackgroundState.NoRefiere, null, null)));
        var after = Compose(ConditionsHealth(new ConditionRecordContent(
            HealthBackgroundState.ConEntradas, [new ConditionItem(ConditionItemType.Enfermedad, "Enfermedad Alfa", 2010)], null), version: 2));
        var same = Compose(ConditionsHealth(new ConditionRecordContent(HealthBackgroundState.NoRefiere, null, null)));

        Assert.NotEqual(before.Hash(), after.Hash());
        Assert.Equal(before.Hash(), same.Hash());
    }

    [Fact]
    public void Build_SinBasalNiEvolucion_LoDiceEnVezDeDejarLaSeccionVacia()
    {
        var detail = new PendingChangeDetail(
            Guid.NewGuid(), ClinicalEventOrigin.EventoEnfermeria, ResidentId.New(), "Residente", UnitId.New(), null,
            DailyChangeClassification.Ordinario, [], null, "Caída.", null, SystemProfile.Enfermeria, null, null, At,
            ClinicalEventStatus.ProtocoloUrgente, 3, true, At, null, [], null, null, null,
            new MedicalDetail(null, null, null, [], null), null, null);

        var sections = ReferralReportBuilder.Build(detail, null, new ResidentIdentification("Residente", new DateOnly(1940, 1, 1),
            DocumentedSexCode.NoConsta, "Centro", null), null);

        Assert.Equal("Sin basal vigente registrado.", sections.Single(s => s.Title == "Basal vigente").Lines.Single());
        // Ficha ilegible o incoherente: el informe lo dice y no la imprime como válida; ficha sin rellenar: «pendiente», que no equivale a «sin alergias».
        Assert.Contains("no es fiable", sections.Single(s => s.Title == "Alergias e intolerancias a medicamentos").Lines.Single());
        var pending = ReferralReportBuilder.Build(detail, null, new ResidentIdentification("Residente", new DateOnly(1940, 1, 1), DocumentedSexCode.NoConsta, "Centro", null),
            new HealthBackgroundView(null, true));
        Assert.Equal(HealthBackgroundDisplay.Pending, pending.Single(s => s.Title == "Alergias e intolerancias a medicamentos").Lines.Single());
        Assert.Contains("No equivale a «sin alergias»", HealthBackgroundDisplay.Pending);
        Assert.Contains("no es fiable", sections.Single(s => s.Title == "Enfermedades e intervenciones").Lines.Single());
        Assert.Equal(HealthBackgroundDisplay.ConditionsPending, pending.Single(s => s.Title == "Enfermedades e intervenciones").Lines.Single());
        Assert.Equal("Sin datos registrados.", sections.Single(s => s.Title == "Evolución").Lines.Single());
        Assert.All(sections, s => Assert.NotEmpty(s.Lines));
    }

    [Fact]
    public void Build_EventoPropioDeMedicina_SaleComoObservadoPorMedicina_SinEscaladoNiValoracionDeEnfermeria()
    {
        var detail = new PendingChangeDetail(
            Guid.NewGuid(), ClinicalEventOrigin.EventoMedicina, ResidentId.New(), "Residente", UnitId.New(), null,
            DailyChangeClassification.Ordinario, [], null, "Dolor torácico opresivo.", null, SystemProfile.Medicina, null, null, At,
            ClinicalEventStatus.ProtocoloUrgenteMedico, 3, null, null, null, [], null, null, null,
            new MedicalDetail(true, At, null, [], null), null, null);

        var sections = ReferralReportBuilder.Build(detail, null, new ResidentIdentification("Residente", new DateOnly(1940, 1, 1),
            DocumentedSexCode.NoConsta, "Centro", null), null);
        var origin = sections.Single(s => s.Title == "Observación de origen").Lines;

        Assert.StartsWith("Observada por Medicina el ", origin[0]);
        Assert.Contains("Dolor torácico opresivo.", origin);
        Assert.DoesNotContain(origin, line => line.StartsWith("Motivo del escalado"));
        Assert.DoesNotContain(sections.SelectMany(s => s.Lines), line => line.StartsWith("Enfermería"));
    }
}
