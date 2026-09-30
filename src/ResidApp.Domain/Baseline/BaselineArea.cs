using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Baseline;

/// <summary>Traduce BASELINE_AREAS de lib/domain/baseline/baseline.ts. Las 9 áreas del estado basal.</summary>
public enum BaselineArea
{
    [Code("MOVILIDAD")] Movilidad,
    [Code("ALIMENTACION")] Alimentacion,
    [Code("CONTINENCIA")] Continencia,
    [Code("ASEO_HIGIENE")] AseoHigiene,
    [Code("COGNICION")] Cognicion,
    [Code("COMUNICACION")] Comunicacion,
    [Code("CONDUCTA")] Conducta,
    [Code("SUENO")] Sueno,
    [Code("AYUDAS_HABITUALES")] AyudasHabituales,
}

/// <summary>Traduce BASELINE_REASONS de baseline.ts.</summary>
public enum BaselineReason
{
    [Code("ALTA")] [Display(Name = "Alta")] Alta,
    [Code("REVISION_PROGRAMADA")] [Display(Name = "Revisión programada")] RevisionProgramada,
    [Code("CAMBIO_FUNCIONAL_CONSOLIDADO")] [Display(Name = "Cambio funcional consolidado")] CambioFuncionalConsolidado,
}
