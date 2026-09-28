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
/// evento"). Solo los que existen hoy; seguimiento, escalado y protocolo urgente llegarán con las historias
/// 4 a 6 de la decisión asistencial.</summary>
public enum ClinicalEventStatus
{
    [Code("PENDIENTE")] [Display(Name = "Pendiente")] Pendiente,
    [Code("EN_VALORACION")] [Display(Name = "En valoración")] EnValoracion,
    [Code("CERRADO")] [Display(Name = "Cerrado por Enfermería")] Cerrado,
}
