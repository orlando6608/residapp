using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Supervision;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record SupervisionQuery(Guid AmbitoPerfilId, CenterId CentroId);

public sealed record ListSupervisionPendingQuery(
    Guid AmbitoPerfilId, CenterId CentroId, DateOnly Today, SupervisionPendingType? Type = null, Guid? UnitId = null);

public sealed record FindSupervisionEpisodeQuery(Guid AmbitoPerfilId, CenterId CentroId, Guid EventId);

/// <summary>Cierre excepcional de un episodio con indicaciones sin cumplir (CJ, tercera 5.1): el motivo es obligatorio.</summary>
public sealed record CloseEpisodeExceptionallyCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid EventId, string? Motivo);

/// <summary>DIR-08 a DIR-10: el periodo, con From y To incluidos.</summary>
/// <summary>RecordView: es una apertura de la pantalla (no un cambio del filtro de periodo) y se deja constancia de quién la abre (CJ, 2026-10-28, 2.3).</summary>
public sealed record ReadSupervisionIndicatorsQuery(Guid AmbitoPerfilId, CenterId CentroId, DateOnly From, DateOnly To, bool RecordView = false);

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
    IProfileScopeDirectoryProvider scopes, ISupervisionDirectory directory, ISessionIdentityProvider session,
    IEnfermeriaResidentDirectory residents, IProcessDeadlineRepository deadlines)
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

    /// <summary>DIR-12: el estado de las derivaciones del ámbito (episodios abiertos con protocolo urgente). Con closedFrom y closedTo
    /// (días locales, ambos incluidos) salen además las de episodios ya cerrados cuyo protocolo se activó en ese periodo (CJ, 2026-10-07).
    /// Solo el estado del proceso, sin permiso ni auditoría (CJ, 2026-10-07), como el resto de la supervisión operativa: el informe
    /// firmado no se entrega aquí.</summary>
    public Task<ApplicationResult<IReadOnlyList<SupervisionReferral>>> ListReferralsAsync(
        SupervisionQuery query, DateOnly? closedFrom = null, DateOnly? closedTo = null, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            if (closedFrom is null != closedTo is null
                || (closedFrom is { } from && closedTo is { } to
                    && (from > to || to.DayNumber - from.DayNumber + 1 > SupervisionIndicatorRules.MaxPeriodDays)))
            {
                throw new DomainValidationException("APPLICATION_INPUT_INVALID");
            }

            await EnsureDirectionScopeAsync(query.AmbitoPerfilId, query.CentroId, ct);
            if (closedFrom is not { } start || closedTo is not { } end)
            {
                return await directory.ListReferralsAsync(query.AmbitoPerfilId, query.CentroId, ct: ct);
            }

            var (fromUtc, toExclusiveUtc) = SupervisionIndicatorRules.UtcBounds(start, end, TimeZoneInfo.Local);
            return await directory.ListReferralsAsync(query.AmbitoPerfilId, query.CentroId, fromUtc, toExclusiveUtc, ct);
        });

    public Task<ApplicationResult<SupervisionEpisodeDetail>> FindEpisodeAsync(
        FindSupervisionEpisodeQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            await EnsureDirectionScopeAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await directory.FindEpisodeAsync(query.AmbitoPerfilId, query.CentroId, query.EventId, ct)
                ?? throw new AccessDeniedException();
        });

    /// <summary>Cierre excepcional (CJ, tercera 5.1; script 0075): lo único que escribe Dirección sobre un episodio. Solo Dirección Clínica, con motivo obligatorio y solo si el
    /// episodio tiene indicaciones médicas abiertas. El ámbito y todo lo demás lo comprueba el directorio dentro de su transacción; queda auditado con el nombre de quien cierra.</summary>
    public Task<ApplicationResult<bool>> CloseEpisodeExceptionallyAsync(
        CloseEpisodeExceptionallyCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var reason = ExceptionalEpisodeClosure.NormalizeReason(command.Motivo);
            var scope = await EnsureDirectionScopeAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await directory.CloseExceptionallyAsync(command.AmbitoPerfilId, command.CentroId, scope.AccountId, command.EventId, reason, ct);
            return true;
        });

    public Task<ApplicationResult<SupervisionScopeInfo>> ReadScopeAsync(SupervisionQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            await EnsureDirectionScopeAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await directory.FindScopeAsync(query.AmbitoPerfilId, query.CentroId, ct) ?? throw new AccessDeniedException();
        });

    /// <summary>DIR-08 a DIR-10 y DIR-16 (bloque 4): indicadores agregados del periodo. No entregan contenido clínico ni datos de personas; solo con el permiso
    /// QUALITY_REVIEW (CJ, 2.1 de la tercera tanda; script 0076), que concede Administración. Al abrir la pantalla (RecordView) queda constancia de quién y cuándo, sin residente (CJ, 2026-10-28, 2.3).</summary>
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
            EnsureQualityReview(info);
            if (query.RecordView)
            {
                await directory.AuditDirectionViewAsync(query.AmbitoPerfilId, query.CentroId, "DIRECTION_INDICATORS_VIEW", ct);
            }

            // El periodo son días de la hora local del servidor (la misma con la que se muestran las fechas) y las fechas se
            // guardan en UTC.
            var zone = TimeZoneInfo.Local;
            var (fromUtc, toExclusiveUtc) = SupervisionIndicatorRules.UtcBounds(query.From, query.To, zone);
            var facts = await directory.ListIndicatorFactsAsync(query.AmbitoPerfilId, query.CentroId, fromUtc, toExclusiveUtc, ct);
            return SupervisionIndicatorRules.Aggregate(facts, info, query.From, query.To, zone);
        });

    /// <summary>DIR-11: la revisión de calidad de proceso del periodo (días locales, ambos incluidos): hitos medidos y fuera de plazo por
    /// unidad, nunca por profesional ni por residente. Sin contenido clínico; solo con el permiso QUALITY_REVIEW.</summary>
    public Task<ApplicationResult<ProcessQualityReport>> ReadProcessQualityAsync(
        ReadSupervisionIndicatorsQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            if (query.From > query.To || query.To.DayNumber - query.From.DayNumber + 1 > SupervisionIndicatorRules.MaxPeriodDays)
            {
                throw new DomainValidationException("APPLICATION_INPUT_INVALID");
            }

            await EnsureDirectionScopeAsync(query.AmbitoPerfilId, query.CentroId, ct);
            var info = await directory.FindScopeAsync(query.AmbitoPerfilId, query.CentroId, ct) ?? throw new AccessDeniedException();
            EnsureQualityReview(info);
            if (query.RecordView)
            {
                await directory.AuditDirectionViewAsync(query.AmbitoPerfilId, query.CentroId, "DIRECTION_QUALITY_VIEW", ct);
            }

            var (fromUtc, toExclusiveUtc) = SupervisionIndicatorRules.UtcBounds(query.From, query.To, TimeZoneInfo.Local);
            var facts = await directory.ListMilestoneFactsAsync(query.AmbitoPerfilId, query.CentroId, fromUtc, toExclusiveUtc, ct);
            var effective = await deadlines.GetEffectiveAsync(query.CentroId, ct);
            // CJ (2026-10-28, 2.2): solo cifras por unidad y periodo, sin episodios ni residentes; la lista de excepciones es de Enfermería y Medicina.
            return ProcessQualityRules.Build(facts, effective, info.Units, query.From, query.To, DateTime.UtcNow) with { Exceptions = [] };
        });

    /// <summary>Las peticiones de historial tras un traslado del periodo (CJ, 2026-10-28, 3.3), sin contenido clínico y sin permiso aparte: basta el ámbito de Dirección Clínica. Al abrir
    /// la pantalla (RecordView) se deja constancia primero y solo después se entrega la lista: sin la constancia no se muestra nada.</summary>
    public Task<ApplicationResult<TransferHistoryRequestPage>> ReadTransferHistoryRequestsAsync(
        ReadSupervisionIndicatorsQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            if (query.From > query.To || query.To.DayNumber - query.From.DayNumber + 1 > SupervisionIndicatorRules.MaxPeriodDays)
            {
                throw new DomainValidationException("APPLICATION_INPUT_INVALID");
            }

            await EnsureDirectionScopeAsync(query.AmbitoPerfilId, query.CentroId, ct);
            if (query.RecordView)
            {
                await directory.AuditDirectionViewAsync(query.AmbitoPerfilId, query.CentroId, "TRANSFER_HISTORY_REVIEW", ct);
            }

            var (fromUtc, toExclusiveUtc) = SupervisionIndicatorRules.UtcBounds(query.From, query.To, TimeZoneInfo.Local);
            return await directory.ListTransferHistoryRequestsAsync(query.AmbitoPerfilId, query.CentroId, fromUtc, toExclusiveUtc, ct);
        });

    /// <summary>Residentes del ámbito para elegir a quién consultar en la consulta auditada de basal: nombre y unidad,
    /// con el mismo criterio de ámbito que la autorización de esa lectura. No entrega contenido clínico; ese sigue saliendo
    /// solo de la consulta auditada.</summary>
    public Task<ApplicationResult<IReadOnlyList<ScopeResidentSummary>>> ListResidentsAsync(
        SupervisionQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            await EnsureDirectionScopeAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await residents.ListAsync(query.AmbitoPerfilId, query.CentroId, ct);
        });

    private static void EnsureQualityReview(SupervisionScopeInfo info)
    {
        if (!info.Permissions.Contains(ResidApp.Domain.Accounts.ProfilePermissions.QualityReview))
        {
            throw new AccessDeniedException();
        }
    }

    private async Task<ActiveProfileScope> EnsureDirectionScopeAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
        var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
        var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == profileScopeId && s.CenterId == centerId);
        if (scope is null || scope.Profile != SystemProfile.DireccionClinica)
        {
            throw new AccessDeniedException();
        }

        return scope;
    }
}
