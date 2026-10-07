using ResidApp.Application.Ports;
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

        var sections = ReferralReportBuilder.Build(detail, baseline, identification);
        var text = string.Join("\n", sections.SelectMany(s => s.Lines.Prepend(s.Title)));

        Assert.All(sections, s => Assert.True(s.Automatic));
        Assert.Equal(
            new[] { "Identificación del residente y del centro", "Basal vigente", "Observación de origen", "Valoraciones",
                "Constantes y oxigenoterapia", "Actuaciones", "Evolución" },
            sections.Select(s => s.Title));
        foreach (var expected in new[]
        {
            "Fecha de nacimiento: 20/02/1938", "Sexo documentado: Mujer", "Centro: Centro Norte", "Barthel total: 65 / 100",
            "Comunicación — Comprensión: Comprensión funcional; Expresión: Expresa necesidades eficazmente; Formas habituales de comunicación: Lenguaje oral, Gestos",
            "Disnea brusca al levantarse.", "Datos clínicos pertinentes: Tiraje intercostal.",
            "Enfermería, valoración: Insuficiencia respiratoria.", "oxigenoterapia a 3 L/min",
            "Enfermería: Oxigenoterapia a 3 L/min.", "Vía periférica.", "Satura 88 %.",
        })
        {
            Assert.Contains(expected, text);
        }
        foreach (var excluded in new[]
        {
            "112 Emergencias", "Piden traslado.", "Aviso a la hija", "CFS", "System.Collections", "Lucía Contacto", "600 999 888", "Buzón de voz.",
        })
        {
            Assert.DoesNotContain(excluded, text);
        }
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
            DocumentedSexCode.NoConsta, "Centro", null));

        Assert.Equal("Sin basal vigente registrado.", sections.Single(s => s.Title == "Basal vigente").Lines.Single());
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
            DocumentedSexCode.NoConsta, "Centro", null));
        var origin = sections.Single(s => s.Title == "Observación de origen").Lines;

        Assert.StartsWith("Observada por Medicina el ", origin[0]);
        Assert.Contains("Dolor torácico opresivo.", origin);
        Assert.DoesNotContain(origin, line => line.StartsWith("Motivo del escalado"));
        Assert.DoesNotContain(sections.SelectMany(s => s.Lines), line => line.StartsWith("Enfermería"));
    }
}
