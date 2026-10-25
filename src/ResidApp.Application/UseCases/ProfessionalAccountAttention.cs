using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>
/// Las cuentas profesionales que Administración conviene que revise (CJ, tercera 4.4, en segundo lugar: un riesgo de seguridad de acceso):
/// suspendidas, sin ningún perfil vigente en el centro, o con un perfil vigente sin ninguna unidad. Plataforma no trabaja con unidades.
/// </summary>
public static class ProfessionalAccountAttention
{
    public static bool NeedsReview(ProfessionalAccountSummary account) =>
        account.Status == AccountStatus.Suspended
        || !account.Profiles.Any(p => p.Active)
        || account.Profiles.Any(p => p.Active && p.Profile != SystemProfile.Plataforma && !p.Units.Any(u => u.Active));
}
