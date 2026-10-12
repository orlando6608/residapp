using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;

namespace ResidApp.Web.Models;

/// <summary>Los cuatro estados que el portal no confunde (portal-familiar.md, historia 6): sesión sin ámbito, sin autorización activa, sin
/// publicaciones y fallo técnico. Un fallo técnico nunca se presenta como «sin novedades».</summary>
public sealed record PortalFamiliarEstadoViewModel(string Titulo, string Mensaje, bool PuedeReintentar = false);

/// <summary>El filtro del historial: sin valor son todas; si no, ordinarias o relevantes.</summary>
public sealed record PortalFamiliarResidenteViewModel(
    FamilyPortalResidentHome Home, FamilyCommunicationType? Tipo, IReadOnlyList<FamilyPortalCommunication> Comunicados);

public sealed record PortalFamiliarComunicadoViewModel(
    FamilyPortalResident Resident, FamilyPortalCommunication Comunicado, IReadOnlyList<FamilyPortalCommunication> Correcciones);
