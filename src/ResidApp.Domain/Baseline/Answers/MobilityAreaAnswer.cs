using System.Text.Json.Serialization;
using ResidApp.Domain.Baseline.Catalogs;

namespace ResidApp.Domain.Baseline.Answers;

/// <summary>Marcador común de los 9 payloads de respuesta por área basal.</summary>
public interface IBaselineAreaAnswer;

/// <summary>Traduce el caso "MOVILIDAD" de assertCompleteBaselineAreaAnswer (validation.ts). La
/// validación cruzada (texto libre solo con OTRA) vive en el constructor, no en 'field' por propiedad:
/// depende de dos campos a la vez.</summary>
public sealed record MobilityAreaAnswer : IBaselineAreaAnswer
{
    public MobilityDisplacementCode DisplacementModeCode { get; init; }
    public MobilityAidCode TechnicalAidCode { get; init; }
    public string? TechnicalAidOtherText { get; init; }
    public MobilityTransferCode TransferCode { get; init; }

    [JsonConstructor]
    public MobilityAreaAnswer(MobilityDisplacementCode displacementModeCode, MobilityAidCode technicalAidCode,
        string? technicalAidOtherText, MobilityTransferCode transferCode)
    {
        BaselineValidation.AssertOpenText(technicalAidCode == MobilityAidCode.Otra, technicalAidOtherText);

        DisplacementModeCode = displacementModeCode;
        TechnicalAidCode = technicalAidCode;
        TechnicalAidOtherText = technicalAidOtherText;
        TransferCode = transferCode;
    }
}
