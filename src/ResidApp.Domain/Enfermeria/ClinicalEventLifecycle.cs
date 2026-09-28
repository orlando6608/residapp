using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Enfermeria;

/// <summary>De dónde procede un evento asistencial (dbo.eventos_asistenciales.origen_codigo). Cada origen
/// conserva su autoría real: un evento propio de Enfermería nunca aparenta venir de Auxiliar (ENF-16).</summary>
public enum ClinicalEventOrigin
{
    [Code("CAMBIO_AUXILIAR")] [Display(Name = "Cambio registrado por Auxiliar")] CambioAuxiliar,
    [Code("EVENTO_ENFERMERIA")] [Display(Name = "Evento observado por Enfermería")] EventoEnfermeria,
}

/// <summary>Estados del evento (docs/flujos-clinicos/valoracion-escalado-enfermeria.md, "Estados del
/// evento"). Solo los que existen hoy; el protocolo urgente llegará con la historia 6 de la decisión
/// asistencial, y los estados propios de Medicina con su vertical.</summary>
public enum ClinicalEventStatus
{
    [Code("PENDIENTE")] [Display(Name = "Pendiente")] Pendiente,
    [Code("EN_VALORACION")] [Display(Name = "En valoración")] EnValoracion,
    [Code("EN_SEGUIMIENTO")] [Display(Name = "En seguimiento")] EnSeguimiento,
    [Code("ESCALADO_MEDICINA")] [Display(Name = "Escalado a Medicina")] EscaladoMedicina,
    [Code("EN_VALORACION_MEDICA")] [Display(Name = "En valoración médica")] EnValoracionMedica,
    [Code("CON_INDICACION_PENDIENTE")] [Display(Name = "Con indicación pendiente")] ConIndicacionPendiente,
    [Code("CERRADO")] [Display(Name = "Cerrado por Enfermería")] Cerrado,
}
