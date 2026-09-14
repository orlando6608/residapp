using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record RegisterDailyClosureCommand(
    Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, DailyClosureType Tipo, string? Motivo, Guid OperacionId);

/// <summary>
/// Traduce AUX-04 (Sin cambios) y AUX-05 (No valorable): las dos primeras de las tres acciones de cierre
/// cotidiano, mutuamente excluyentes entre sí — de ahí un único caso de uso con un parámetro Tipo, en vez
/// de dos casos de uso casi idénticos. Repite la misma comprobación que ListAssignedResidents (ámbito
/// Auxiliar + residente asignado), porque esta acción tampoco encaja en el modelo de
/// RequestAuthorizationContext (pensado para las 7 acciones de ResidentBaselinePolicy, ninguna de las
/// cuales es "cierre cotidiano" — ese vertical nunca se migró en el prototipo legado).
/// </summary>
public sealed class RegisterDailyClosure(
    IProfileScopeDirectoryProvider scopes, IAssignedResidentDirectory directory,
    ISessionIdentityProvider session, IDailyClosureRepository repository)
{
    public Task<ApplicationResult<DailyClosureResult>> ExecuteAsync(
        RegisterDailyClosureCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            if (command.Tipo == DailyClosureType.NoValorable && string.IsNullOrWhiteSpace(command.Motivo))
            {
                throw new DomainValidationException("DAILY_CLOSURE_REASON_REQUIRED");
            }

            var identity = await session.GetVerifiedIdentityAsync(ct);
            if (identity is null)
            {
                throw new AccessDeniedException();
            }

            var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
            var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == command.AmbitoPerfilId && s.CenterId == command.CentroId);
            if (scope is null || scope.Profile != SystemProfile.Auxiliar)
            {
                throw new AccessDeniedException();
            }

            var assigned = (await directory.ListAsync(command.AmbitoPerfilId, command.CentroId, ct))
                .FirstOrDefault(r => r.ResidentId == command.ResidenteId);
            if (assigned is null)
            {
                throw new AccessDeniedException();
            }

            var input = new RegisterDailyClosureInput(
                scope.AccountId, command.CentroId, assigned.UnitId, command.ResidenteId,
                command.Tipo, command.Motivo, command.OperacionId);
            return await repository.RegisterAsync(input, ct);
        });
}
