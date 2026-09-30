using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Catalogs;

/// <summary>Traduce COGNITION_CATEGORIES/COGNITION, ETIOLOGIES y GDS de baseline.ts/validation.ts (área COGNICION).</summary>
public enum CognitionCategoryCode
{
    [Code("SIN_DETERIORO_CONOCIDO_O_DOCUMENTADO")] [Display(Name = "Sin deterioro conocido o documentado")] SinDeterioroConocidoODocumentado,
    [Code("DETERIORO_COGNITIVO_LEVE_DOCUMENTADO")] [Display(Name = "Deterioro cognitivo leve documentado")] DeterioroCognitivoLeveDocumentado,
    [Code("DEMENCIA_DOCUMENTADA")] [Display(Name = "Demencia documentada")] DemenciaDocumentada,
    [Code("SITUACION_NO_DETERMINADA")] [Display(Name = "Situación no determinada")] SituacionNoDeterminada,
}

public enum EtiologyCode
{
    [Code("ENFERMEDAD_ALZHEIMER")] [Display(Name = "Enfermedad de Alzheimer")] EnfermedadAlzheimer,
    [Code("DEMENCIA_VASCULAR")] [Display(Name = "Demencia vascular")] DemenciaVascular,
    [Code("DEMENCIA_MIXTA")] [Display(Name = "Demencia mixta")] DemenciaMixta,
    [Code("DEMENCIA_CON_CUERPOS_DE_LEWY")] [Display(Name = "Demencia con cuerpos de Lewy")] DemenciaConCuerposDeLewy,
    [Code("DEMENCIA_FRONTOTEMPORAL")] [Display(Name = "Demencia frontotemporal")] DemenciaFrontotemporal,
    [Code("DEMENCIA_ASOCIADA_ENFERMEDAD_PARKINSON")] [Display(Name = "Demencia asociada a enfermedad de Parkinson")] DemenciaAsociadaEnfermedadParkinson,
    [Code("SINDROME_CORTICOBASAL")] [Display(Name = "Síndrome corticobasal")] SindromeCorticobasal,
    [Code("OTRA")] [Display(Name = "Otra")] Otra,
    [Code("ETIOLOGIA_NO_ESPECIFICADA")] [Display(Name = "Etiología no especificada")] EtiologiaNoEspecificada,
}

public enum GdsCode
{
    [Code("NO_DOCUMENTADO")] [Display(Name = "No documentado")] NoDocumentado,
    [Code("GDS_1")] [Display(Name = "GDS 1")] Gds1,
    [Code("GDS_2")] [Display(Name = "GDS 2")] Gds2,
    [Code("GDS_3")] [Display(Name = "GDS 3")] Gds3,
    [Code("GDS_4")] [Display(Name = "GDS 4")] Gds4,
    [Code("GDS_5")] [Display(Name = "GDS 5")] Gds5,
    [Code("GDS_6")] [Display(Name = "GDS 6")] Gds6,
    [Code("GDS_7")] [Display(Name = "GDS 7")] Gds7,
}
