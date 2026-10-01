using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record SupervisionQuery(Guid AmbitoPerfilId, CenterId CentroId);

public sealed record ListSupervisionPendingQuery(
    Guid AmbitoPerfilId, CenterId CentroId, DateOnly Today, SupervisionPendingType? Type = null, Guid? UnitId = null);

public sealed record FindSupervisionEpisodeQuery(Guid AmbitoPerfilId, CenterId CentroId, Guid EventId);

/// <summary>DIR-08 a DIR-10: el periodo, con From y To incluidos.</summary>
public sealed record ReadSupervisionIndicatorsQuery(Guid AmbitoPerfilId, CenterId CentroId, DateOnly From, DateOnly To);

/// <summary>DIR-01/DIR-02: contadores de una unidad. Open es el denominador de los demás (episodios abiertos de la unidad).</summary>
public sealed record SupervisionUnitSummary(
    UnitId UnitId, string UnitName, int Open, int WithoutAssessment, int InAssessment, int Escalated, int FollowUps,
    int FollowUpsOverdue, int IndicationsPending, int IndicationsNotDone, int UrgentProtocols);

/// <summary>DIR-03: los pendientes de la lista, más los episodios abiertos totales (denominador) para decir «N de M».</summary>
public sealed record SupervisionPendingList(IReadOnlyList<SupervisionEpisode> Episodes, int TotalOpen);

/// <summary>
/// Fachada del vertical Dirección/Coordinación Clínica, bloque 1: supervisión operativa en solo lectura (DIR-01 a DIR-04
/// y DIR-17). Sin permiso ni auditoría, porque no entrega contenido clínico (la matriz de permisos da «Ver panel de
/// supervisión clínica» y «Abrir detalle operativo de episodio» a Dirección); la lectura clínica detallada, con finalidad
/// y auditoría previa, es de otro bloque. Deny-by-default: el ámbito tiene que ser un ámbito activo de Dirección Clínica
/// de la cuenta, y el directorio devuelve vacío fuera de sus unidades.
/// </summary>
public sealed class DireccionApplicationService(
    IProfileScopeDirectoryProvider scopes, ISupervisionDirectory directory, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<IReadOnlyList<SupervisionUnitSummary>>> ReadPanelAsync(
        SupervisionQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync<IReadOnlyList<SupervisionUnitSummary>>(async () =>
        {
            await EnsureDirectionScopeAsync(query.AmbitoPerfilId, query.CentroId, ct);
            var info = await directory.FindScopeAsync(query.AmbitoPerfilId, query.CentroId, ct) ?? throw new AccessDeniedException();
            var episodes = await directory.ListOpenEpisodesAsync(query.AmbitoPerfilId, query.CentroId, ct);
            var today = DateOnly.FromDateTime(DateTime.Today);
            return info.Units.Select(unit =>
            {
                var own = episodes.Where(e => e.UnitId == unit.Id).ToList();
                return new SupervisionUnitSummary(
                    unit.Id, unit.Name, own.Count,
                    own.Count(e => e.Status == ClinicalEventStatus.Pendiente),
                    own.Count(e => e.Status is ClinicalEventStatus.EnValoracion or ClinicalEventStatus.EnValoracionMedica),
                    own.Count(e => e.Escalated), own.Count(e => e.HasFollowUp),
                    own.Count(e => SupervisionPendingRules.IsOverdue(e, today)),
                    own.Sum(e => e.IndicationsPending), own.Sum(e => e.IndicationsNotDone), own.Count(e => e.HasUrgentProtocol));
            }).ToList();
        });

    public Task<ApplicationResult<SupervisionPendingList>> ListPendingAsync(
        ListSupervisionPendingQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            await EnsureDirectionScopeAsync(query.AmbitoPerfilId, query.CentroId, ct);
            var episodes = await directory.ListOpenEpisodesAsync(query.AmbitoPerfilId, query.CentroId, ct);
            var shown = episodes
                .Where(e => query.UnitId is null || e.UnitId.Value == query.UnitId)
                .Where(e => query.Type is null || SupervisionPendingRules.Matches(query.Type.Value, e, query.Today))
                .ToList();
            return new SupervisionPendingList(shown, episodes.Count);
        });

    public Task<ApplicationResult<SupervisionEpisodeDetail>> FindEpisodeAsync(
        FindSupervisionEpisodeQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            await EnsureDirectionScopeAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await directory.FindEpisodeAsync(query.AmbitoPerfilId, query.CentroId, query.EventId, ct)
                ?? throw new AccessDeniedException();
        });

    public Task<ApplicationResult<SupervisionScopeInfo>> ReadScopeAsync(SupervisionQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            await EnsureDirectionScopeAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await directory.FindScopeAsync(query.AmbitoPerfilId, query.CentroId, ct) ?? throw new AccessDeniedException();
        });

    /// <summary>DIR-08 a DIR-10 y DIR-16 (bloque 4): indicadores agregados del periodo, sin permiso ni auditoría, porque no
    /// entregan contenido clínico ni datos de personas (la matriz da «Ver indicadores agregados» y «Generar informes
    /// agregados» a Dirección).</summary>
    public Task<ApplicationResult<SupervisionIndicators>> ReadIndicatorsAsync(
        ReadSupervisionIndicatorsQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            if (query.From > query.To || query.To.DayNumber - query.From.DayNumber + 1 > SupervisionIndicatorRules.MaxPeriodDays)
            {
                throw new DomainValidationException("APPLICATION_INPUT_INVALID");
            }

            await EnsureDirectionScopeAsync(query.AmbitoPerfilId, query.CentroId, ct);
            var info = await directory.FindScopeAsync(query.AmbitoPerfilId, query.CentroId, ct) ?? throw new AccessDeniedException();
            var facts = await directory.ListIndicatorFactsAsync(
                query.AmbitoPerfilId, query.CentroId, query.From.ToDateTime(TimeOnly.MinValue),
                query.To.AddDays(1).ToDateTime(TimeOnly.MinValue), ct);
            return SupervisionIndicatorRules.Aggregate(facts, info, query.From, query.To);
        });

    private async Task EnsureDirectionScopeAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
        var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
        var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == profileScopeId && s.CenterId == centerId);
        if (scope is null || scope.Profile != SystemProfile.DireccionClinica)
        {
            throw new AccessDeniedException();
        }
    }
}
