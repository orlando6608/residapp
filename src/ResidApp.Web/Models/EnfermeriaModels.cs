using ResidApp.Application.Ports;

namespace ResidApp.Web.Models;

/// <summary>ENF-18: identidad mínima del residente del ámbito más el resumen del basal vigente (null si
/// todavía no tiene ninguno firmado).</summary>
public sealed record EnfermeriaResidentDetailViewModel(ScopeResidentSummary Resident, CurrentBaselineSummary? Baseline);
