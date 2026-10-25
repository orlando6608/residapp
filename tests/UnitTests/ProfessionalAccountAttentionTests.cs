using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Accounts;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>Las cuentas que el inicio de Administración pide revisar (CJ, tercera 4.4): suspendidas, sin perfil vigente o con un perfil sin unidades.</summary>
public class ProfessionalAccountAttentionTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);

    private static AccountScopeGrant Unit(bool active = true) => new(Guid.NewGuid(), "Planta 1", At, "admin", active ? null : At, active ? null : "admin");

    private static AccountProfileScope Profile(SystemProfile profile, bool active = true, params AccountScopeGrant[] units) =>
        new(Guid.NewGuid(), profile, At, "admin", active ? null : At, active ? null : "admin", units, [], []);

    private static ProfessionalAccountSummary Account(AccountStatus status, params AccountProfileScope[] profiles) =>
        new(AccountId.From(Guid.NewGuid()), "cuenta", null, status, profiles);

    [Fact]
    public void SeRevisan_LasSuspendidas_LasSinPerfilVigente_YLasConUnPerfilSinUnidades()
    {
        Assert.False(ProfessionalAccountAttention.NeedsReview(Account(AccountStatus.Active, Profile(SystemProfile.Enfermeria, true, Unit()))));
        Assert.True(ProfessionalAccountAttention.NeedsReview(Account(AccountStatus.Suspended, Profile(SystemProfile.Enfermeria, true, Unit()))));
        Assert.True(ProfessionalAccountAttention.NeedsReview(Account(AccountStatus.Active, Profile(SystemProfile.Enfermeria, false, Unit()))));
        Assert.True(ProfessionalAccountAttention.NeedsReview(Account(AccountStatus.Active, Profile(SystemProfile.Medicina, true, Unit(active: false)))));
        Assert.True(ProfessionalAccountAttention.NeedsReview(Account(AccountStatus.Active,
            Profile(SystemProfile.Enfermeria, true, Unit()), Profile(SystemProfile.Medicina, true))));
        // Un perfil revocado sin unidades no cuenta si queda otro vigente con ellas; Plataforma no trabaja con unidades.
        Assert.False(ProfessionalAccountAttention.NeedsReview(Account(AccountStatus.Active,
            Profile(SystemProfile.Enfermeria, true, Unit()), Profile(SystemProfile.Medicina, false))));
        Assert.False(ProfessionalAccountAttention.NeedsReview(Account(AccountStatus.Active, Profile(SystemProfile.Plataforma, true))));
    }
}
