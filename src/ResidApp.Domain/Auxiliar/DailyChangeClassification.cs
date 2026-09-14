using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Auxiliar;

/// <summary>Clasificación inicial de un cambio registrado (AUX-10): organiza la bandeja de Enfermería, no
/// diagnostica.</summary>
public enum DailyChangeClassification
{
    [Code("ORDINARIO")] [Display(Name = "Ordinario")] Ordinario,
    [Code("PRIORITARIO")] [Display(Name = "Prioritario")] Prioritario,
}

/// <summary>Catálogo cerrado de motivos prioritarios (AUX-10, docs/flujos-clinicos/registro-cotidiano-auxiliar.md).
/// Organiza la atención de Enfermería; no constituye un diagnóstico ni sustituye el protocolo urgente del
/// centro.</summary>
public enum DailyChangePriorityReason
{
    [Code("ALTERACION_CONCIENCIA_ESTADO_GENERAL")]
    [Display(Name = "Alteración de conciencia o del estado general")]
    AlteracionConcienciaEstadoGeneral,

    [Code("CAIDA_LESION_TRAUMATISMO")]
    [Display(Name = "Caída, lesión o traumatismo")]
    CaidaLesionTraumatismo,

    [Code("FIEBRE_SOSPECHA_INFECCION")]
    [Display(Name = "Fiebre o sospecha de infección")]
    FiebreSospechaInfeccion,

    [Code("DOLOR_NUEVO_INTENSO")]
    [Display(Name = "Dolor nuevo o intenso")]
    DolorNuevoIntenso,

    [Code("DIFICULTAD_RESPIRATORIA")]
    [Display(Name = "Dificultad respiratoria")]
    DificultadRespiratoria,

    [Code("DEFICIT_NEUROLOGICO_NUEVO")]
    [Display(Name = "Déficit neurológico nuevo")]
    DeficitNeurologicoNuevo,
}
