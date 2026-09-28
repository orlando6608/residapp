using ResidApp.Application.Ports;

namespace ResidApp.Web.Models;

/// <summary>MED-01: contador de escalados recibidos. Seguimientos, indicaciones, comunicaciones e Historial
/// de Medicina llegarán con el resto del vertical.</summary>
public sealed record MedicinaInicioViewModel(int Escalados);

/// <summary>MED-03: detalle del escalado con su información reunida (solo lectura) y el basal vigente del
/// residente (null si no tiene).</summary>
public sealed record MedicinaEscaladoViewModel(PendingChangeDetail Event, CurrentBaselineSummary? Baseline);
