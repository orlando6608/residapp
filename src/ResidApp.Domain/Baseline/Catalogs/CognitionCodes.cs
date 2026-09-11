using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Catalogs;

/// <summary>Traduce COGNITION_CATEGORIES/COGNITION, ETIOLOGIES y GDS de baseline.ts/validation.ts (área COGNICION).</summary>
public enum CognitionCategoryCode
{
    [Code("SIN_DETERIORO_CONOCIDO_O_DOCUMENTADO")] SinDeterioroConocidoODocumentado,
    [Code("DETERIORO_COGNITIVO_LEVE_DOCUMENTADO")] DeterioroCognitivoLeveDocumentado,
    [Code("DEMENCIA_DOCUMENTADA")] DemenciaDocumentada,
    [Code("SITUACION_NO_DETERMINADA")] SituacionNoDeterminada,
}

public enum EtiologyCode
{
    [Code("ENFERMEDAD_ALZHEIMER")] EnfermedadAlzheimer,
    [Code("DEMENCIA_VASCULAR")] DemenciaVascular,
    [Code("DEMENCIA_MIXTA")] DemenciaMixta,
    [Code("DEMENCIA_CON_CUERPOS_DE_LEWY")] DemenciaConCuerposDeLewy,
    [Code("DEMENCIA_FRONTOTEMPORAL")] DemenciaFrontotemporal,
    [Code("DEMENCIA_ASOCIADA_ENFERMEDAD_PARKINSON")] DemenciaAsociadaEnfermedadParkinson,
    [Code("SINDROME_CORTICOBASAL")] SindromeCorticobasal,
    [Code("OTRA")] Otra,
    [Code("ETIOLOGIA_NO_ESPECIFICADA")] EtiologiaNoEspecificada,
}

public enum GdsCode
{
    [Code("NO_DOCUMENTADO")] NoDocumentado,
    [Code("GDS_1")] Gds1,
    [Code("GDS_2")] Gds2,
    [Code("GDS_3")] Gds3,
    [Code("GDS_4")] Gds4,
    [Code("GDS_5")] Gds5,
    [Code("GDS_6")] Gds6,
    [Code("GDS_7")] Gds7,
}
