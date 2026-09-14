using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record RegisterDailyChangeAreaCommand(
    DailyChangeAreaCode AreaCode, IReadOnlyList<DailyChangeAreaOptionCode> Opciones, string? Texto);

public sealed record RegisterDailyChangeCommand(
    Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId,
    IReadOnlyList<RegisterDailyChangeAreaCommand> Areas, decimal? TemperaturaCelsius,
    DailyChangeClassification Clasificacion, DailyChangePriorityReason? MotivoPrioritario,
    string? AvisoDirecto, Guid OperacionId);

/// <summary>
/// Traduce AUX-06 a AUX-12 "Registrar cambio": la tercera acción de cierre cotidiano, mutuamente
/// excluyente con Sin cambios (RegisterDailyClosure) y No valorable. Repite la misma comprobación de
/// ámbito Auxiliar + residente asignado que las otras dos (ver su comentario: este vertical no encaja en
/// RequestAuthorizationContext, pensado para las 7 acciones de ResidentBaselinePolicy).
/// </summary>
public sealed class RegisterDailyChange(
    IProfileScopeDirectoryProvider scopes, IAssignedResidentDirectory directory,
    ISessionIdentityProvider session, IDailyClosureRepository repository)
{
    public Task<ApplicationResult<DailyClosureResult>> ExecuteAsync(
        RegisterDailyChangeCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            // AUX-06: exige al menos un área. AUX-07: cada área con contenido, ya sea alguna opción rápida
            // del catálogo cerrado de esa área (DailyChangeAreaOptionsCatalog) o texto libre; las tres
            // áreas sin checklist (8, 9, 10) solo admiten texto libre, así que su catálogo está vacío y
            // exigen texto por descarte.
            if (command.Areas.Count == 0)
            {
                throw new DomainValidationException("DAILY_CHANGE_AREAS_REQUIRED");
            }
            foreach (var area in command.Areas)
            {
                var catalogo = DailyChangeAreaOptionsCatalog.OptionsFor(area.AreaCode);
                if (area.Opciones.Any(o => !catalogo.Contains(o)))
                {
                    throw new DomainValidationException("DAILY_CHANGE_AREA_OPTION_INVALID");
                }
                if (area.Opciones.Count == 0 && string.IsNullOrWhiteSpace(area.Texto))
                {
                    throw new DomainValidationException("DAILY_CHANGE_AREA_CONTENT_REQUIRED");
                }
            }
            if (command.Areas.Select(a => a.AreaCode).Distinct().Count() != command.Areas.Count)
            {
                throw new DomainValidationException("DAILY_CHANGE_AREA_DUPLICATED");
            }

            // AUX-10/AUX-11B: un prioritario exige motivo de catálogo cerrado y aviso directo
            // documentado; un ordinario no lleva ninguno de los dos (se normaliza, no se rechaza, por si
            // el cliente envía sobrantes de un cambio de clasificación en el propio formulario).
            var (motivoPrioritario, avisoDirecto) = command.Clasificacion == DailyChangeClassification.Prioritario
                ? (command.MotivoPrioritario, command.AvisoDirecto)
                : ((DailyChangePriorityReason?)null, (string?)null);
            if (command.Clasificacion == DailyChangeClassification.Prioritario &&
                (motivoPrioritario is null || string.IsNullOrWhiteSpace(avisoDirecto)))
            {
                throw new DomainValidationException("DAILY_CHANGE_PRIORITY_DOCUMENTATION_REQUIRED");
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

            var input = new RegisterDailyChangeInput(
                scope.AccountId, command.CentroId, assigned.UnitId, command.ResidenteId,
                command.Areas.Select(a => new DailyChangeAreaInput(a.AreaCode, a.Opciones, a.Texto)).ToList(),
                command.TemperaturaCelsius, command.Clasificacion, motivoPrioritario, avisoDirecto, command.OperacionId);
            return await repository.RegisterChangeAsync(input, ct);
        });
}
