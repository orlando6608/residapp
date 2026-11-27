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

    /// <summary>Una invitación de acceso sin usar, pendiente o ya caducada, de una cuenta que podría usarla: activa y con algún perfil vigente en el
    /// centro. La de una cuenta suspendida o sin perfiles ya sale entre las que conviene revisar.</summary>
    public static bool HasOpenInvitation(ProfessionalAccountSummary account) =>
        account.Status == AccountStatus.Active && account.OpenInvitationExpiresAt is not null && account.Profiles.Any(p => p.Active);

    /// <summary>La invitación abierta que ya no vale: caduca en el mismo instante que marca.</summary>
    public static bool HasExpiredInvitation(ProfessionalAccountSummary account, DateTimeOffset now) =>
        HasOpenInvitation(account) && account.OpenInvitationExpiresAt <= now;
}
