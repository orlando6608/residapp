using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record IssueAccountLinkCommand(Guid AmbitoPerfilId, CenterId CentroId, SystemProfile? Perfil, IReadOnlyList<Guid> Unidades);

public sealed record AccountLinkCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid VinculacionId);

/// <summary>La vinculación recién emitida: el token solo existe aquí, para ponerlo en la hoja que se entrega.</summary>
public sealed record IssuedLink(string Token, IssuedAccountLink Details);

/// <summary>
/// Vincular una cuenta que ya existe en otro centro (ADR 0007, incremento I2b). La parte de la Administración exige el ámbito activo de
/// Administración del centro, como el resto de la gestión de cuentas, y el repositorio lo repite dentro de la transacción. La parte de la
/// persona solo exige su sesión: lo que se canjea es el token, y la Administración tiene que confirmarlo después.
/// </summary>
public sealed class AccountLinksApplicationService(
    AdministrationAccessResolver administrationAccess, IAccountLinkRepository links, TimeProvider clock)
{
    public Task<ApplicationResult<IssuedLink>> IssueAsync(IssueAccountLinkCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            if (command.Perfil is not { } profile || !ProfessionalAccount.IsGrantable(profile) || command.Unidades.Count == 0)
            {
                throw new DomainValidationException("PROFILE_SCOPE_INVALID");
            }

            var (access, _) = await administrationAccess.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            var token = AccessInvitation.NewToken();
            return new IssuedLink(token, await links.IssueAsync(
                access, profile, command.Unidades.Select(UnitId.From).ToList(), AccessInvitation.Hash(token), Now, ct));
        });

    public Task<ApplicationResult<IReadOnlyList<AccountLinkSummary>>> ListOpenAsync(
        Guid ambitoPerfilId, CenterId centerId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await administrationAccess.ResolveAsync(ambitoPerfilId, centerId, ct);
            return await links.ListOpenAsync(access, Now, ct);
        });

    public Task<ApplicationResult<bool>> CancelAsync(AccountLinkCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await administrationAccess.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await links.CancelAsync(access, command.VinculacionId, Now, ct);
            return true;
        });

    /// <summary>Devuelve el ámbito creado al confirmar, o null al rechazar.</summary>
    public Task<ApplicationResult<Guid?>> ResolveAsync(AccountLinkCommand command, bool confirm, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await administrationAccess.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            return await links.ResolveAsync(access, command.VinculacionId, confirm, Now, ct);
        });

    public Task<ApplicationResult<LiveAccountLink?>> FindAsync(string? token, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            string.IsNullOrWhiteSpace(token) ? null : await links.FindLiveAsync(AccessInvitation.Hash(token), Now, ct));

    public Task<ApplicationResult<bool>> RedeemAsync(AccountId accountId, Guid linkId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            await links.RedeemAsync(accountId, linkId, Now, ct);
            return true;
        });

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}
