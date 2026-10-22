using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Usuario es el identificador de acceso de la cuenta (del personal o de un familiar del centro).</summary>
public sealed record AccountInvitationCommand(Guid AmbitoPerfilId, CenterId CentroId, string? Usuario);

/// <summary>La invitación recién emitida: el token solo existe aquí, para ponerlo en la hoja que se entrega.</summary>
public sealed record IssuedInvitation(string Token, IssuedAccessInvitation Details);

/// <summary>
/// Invitaciones de acceso que entrega Administración (ADR 0007, incremento I2): la primera, para que la persona elija su contraseña (y,
/// si es del personal, active el segundo factor); y la de recuperación, si ya la tenía. Exige el ámbito activo de Administración del
/// centro, como el resto de la gestión de cuentas, y el repositorio lo repite dentro de la transacción.
/// </summary>
public sealed class AccessInvitationsApplicationService(
    AdministrationAccessResolver administrationAccess, IAccessInvitationRepository invitations, TimeProvider clock)
{
    public Task<ApplicationResult<IssuedInvitation>> IssueAsync(AccountInvitationCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var subject = ProfessionalAccount.ValidateSubject(command.Usuario);
            var (access, _) = await administrationAccess.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            var token = AccessInvitation.NewToken();
            return new IssuedInvitation(token, await invitations.IssueAsync(access, subject, AccessInvitation.Hash(token), Now, ct));
        });

    public Task<ApplicationResult<bool>> CancelAsync(AccountInvitationCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var subject = ProfessionalAccount.ValidateSubject(command.Usuario);
            var (access, _) = await administrationAccess.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await invitations.CancelAsync(access, subject, Now, ct);
            return true;
        });

    public Task<ApplicationResult<AccessInvitationStatus>> FindStatusAsync(AccountInvitationCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var subject = ProfessionalAccount.ValidateSubject(command.Usuario);
            var (access, _) = await administrationAccess.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            return await invitations.FindStatusAsync(access, subject, Now, ct);
        });

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}
