using System.Runtime.CompilerServices;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Baseline;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.Authorization;

/// <summary>Traduce ResidentCreatePayload de lib/authorization/request-context.ts (los campos de
/// CreateResidentInput que NO derivan del contexto de autorización).</summary>
public sealed record ResidentCreatePayload(
    string DisplayName, DateOnly BirthDate, DocumentedSexCode DocumentedSexCode,
    string? InternalReference, Guid? BuildingId, Guid? FloorId, Guid? RoomId, Guid? PlaceId, Guid OperationId);

/// <summary>Traduce el payload de executeBaselineSign en request-context.ts.</summary>
public sealed record BaselineSignPayload(int ExpectedDraftRevision, Guid OperationId);

/// <summary>No existía en el original executeDirectionBaselineRead de request-context.ts (que no recibía
/// payload alguno): se añade para poder pasar OperationId sin romper la firma pública existente de
/// ExecuteDirectionBaselineReadAsync con un parámetro suelto.</summary>
public sealed record ClinicalDirectionReadPayload(Guid OperationId);

/// <summary>
/// Traduce RequestAuthorizationContext/resolveRequestContext/operationFor/execute* de
/// lib/authorization/request-context.ts. En TS la autoridad vive en un WeakMap privado, indexado por un
/// objeto marcador vacío (Object.create(null)) — el contexto no es más que esa marca; ningún dato de
/// autorización es alcanzable sin pasar por resolveRequestContext. Aquí se replica exactamente ese
/// patrón con ConditionalWeakTable: RequestAuthorizationContext no expone ningún miembro público: la
/// única forma de obtener una autoridad utilizable es ResolveAsync.
/// </summary>
public sealed class RequestAuthorizationContext;

internal sealed record AuthorizedOperation(
    AuthorizationTarget Target, ResidentBaselineAction Action, Guid ProfileScopeId,
    AuthorizationSubject Subject, CenterId CenterId, UnitId UnitId, ResidentId? ResidentId,
    AuthorizationDecision.Allowed Decision, string ExternalSubject, AuthorizationSelection Selection, string EvidenceFingerprint);

public static class RequestAuthorizationContextResolver
{
    private static readonly ConditionalWeakTable<RequestAuthorizationContext, AuthorizedOperation> Operations = new();

    /// <summary>Una resolución nueva por caso de uso; no se cachea la autorización de una sesión — igual
    /// que el comentario original de resolveRequestContext.</summary>
    public static async Task<RequestAuthorizationContext> ResolveAsync(
        IAuthorizationEvidenceProvider evidenceProvider, ISessionIdentityProvider session,
        AuthorizationSelection selection, AuthorizationTarget target,
        ClinicalResourceType? resourceType = null, ClinicalDetailAccessPurpose? purpose = null, CancellationToken ct = default)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct);
        if (identity is null || string.IsNullOrWhiteSpace(identity.ExternalSubject))
        {
            throw new AccessDeniedException();
        }

        var evidence = await evidenceProvider.LoadEvidenceAsync(identity.ExternalSubject, selection, target, ct);
        if (evidence is null)
        {
            throw new AccessDeniedException();
        }

        if (target is AuthorizationTarget.Read)
        {
            if (resourceType is null)
            {
                throw new AccessDeniedException();
            }
            // ResidentBaselinePolicy.AuthorizeBaselineCurrentRead ya permite Auxiliar/Enfermeria/Medicina
            // sin permiso ni auditoría (a diferencia de Dirección Clínica); hasta ahora esa rama estaba
            // documentada como inalcanzable (pendientes-migracion-inicial.md, punto 5) porque aquí se
            // cortaba antes de llegar a la política. Se habilita solo para BaselineCurrent (AUX-03/ENF-20).
            var directCareRead = resourceType == ClinicalResourceType.BaselineCurrent
                && evidence.Profile is SystemProfile.Auxiliar or SystemProfile.Enfermeria or SystemProfile.Medicina;
            if (evidence.Profile != SystemProfile.DireccionClinica && !directCareRead)
            {
                throw new AccessDeniedException();
            }
        }
        if (target is AuthorizationTarget.Sign && evidence.DraftReason is null)
        {
            throw new AccessDeniedException();
        }

        var permissions = evidence.Permissions
            .Select(p => EnumCode.TryParseCode<ResidentBaselinePermission>(p.Code, out var code) ? code : (ResidentBaselinePermission?)null)
            .Where(code => code is not null)
            .Select(code => code!.Value)
            .Distinct()
            .ToList();

        var profileScope = new ResidentBaselineProfileScope(
            evidence.Profile, evidence.CenterId, evidence.UnitId,
            evidence.ResidentId is { } residentId ? [residentId] : [], [], permissions);

        var subject = new AuthorizationSubject(
            evidence.AccountId, true, true, evidence.Profile, [evidence.Profile], [profileScope]);

        var action = target switch
        {
            AuthorizationTarget.Create => ResidentBaselineAction.ResidentIdentityCreate,
            AuthorizationTarget.Sign => evidence.DraftReason == BaselineReason.Alta
                ? ResidentBaselineAction.BaselineInitialComplete
                : ResidentBaselineAction.BaselineReevaluate,
            AuthorizationTarget.Read => resourceType == ClinicalResourceType.BaselineCurrent
                ? ResidentBaselineAction.BaselineCurrentRead
                : ResidentBaselineAction.BaselineHistoryRead,
            _ => throw new AccessDeniedException(),
        };

        var request = new ResidentBaselineAuthorizationRequest(
            action, subject, evidence.CenterId, evidence.UnitId, evidence.ResidentId, purpose);
        var decision = ResidentBaselinePolicy.Authorize(request);
        if (decision is not AuthorizationDecision.Allowed allowed)
        {
            throw new AccessDeniedException();
        }

        var context = new RequestAuthorizationContext();
        var operation = new AuthorizedOperation(
            target, action, evidence.ProfileScopeId, subject, evidence.CenterId, evidence.UnitId, evidence.ResidentId,
            allowed, identity.ExternalSubject, selection, EvidenceFingerprint.Of(evidence));
        Operations.Add(context, operation);
        return context;
    }

    /// <summary>Traduce executeResidentCreate de request-context.ts.</summary>
    public static async Task<CreateResidentResult> ExecuteResidentCreateAsync(
        RequestAuthorizationContext context, IResidentRepository repository, ResidentCreatePayload payload, CancellationToken ct = default)
    {
        var operation = RequireTarget<AuthorizationTarget.Create>(context);
        var activeProfile = operation.Subject.ActiveProfile!.Value;
        if (activeProfile != SystemProfile.Administracion && activeProfile != SystemProfile.Enfermeria)
        {
            throw new AccessDeniedException();
        }
        var input = new CreateResidentInput(
            operation.Subject.AccountId!.Value, activeProfile, operation.CenterId, operation.UnitId,
            payload.DisplayName, payload.BirthDate, payload.DocumentedSexCode, payload.InternalReference,
            payload.BuildingId, payload.FloorId, payload.RoomId, payload.PlaceId, payload.OperationId);
        return await repository.CreateWithInitialLocationAsync(input, ct);
    }

    /// <summary>Traduce executeBaselineSign de request-context.ts.</summary>
    public static async Task<SignBaselineDraftResult> ExecuteBaselineSignAsync(
        RequestAuthorizationContext context, IBaselineRepository repository, BaselineSignPayload payload, CancellationToken ct = default)
    {
        var operation = RequireTarget<AuthorizationTarget.Sign>(context);
        var activeProfile = operation.Subject.ActiveProfile!.Value;
        if (activeProfile != SystemProfile.Enfermeria && activeProfile != SystemProfile.Medicina)
        {
            throw new AccessDeniedException();
        }
        var sign = (AuthorizationTarget.Sign)operation.Target;
        var input = new SignBaselineDraftInput(
            operation.Subject.AccountId!.Value, activeProfile, operation.CenterId, operation.UnitId,
            sign.ResidentId, sign.DraftId, payload.ExpectedDraftRevision, payload.OperationId);
        return await repository.SignDraftAsync(input, ct);
    }

    /// <summary>Traduce executeDirectionBaselineRead de request-context.ts.</summary>
    public static async Task<IReadOnlyList<AuditedBaselineHeader>> ExecuteDirectionBaselineReadAsync(
        RequestAuthorizationContext context, IBaselineRepository repository, ClinicalDirectionReadPayload payload, CancellationToken ct = default)
    {
        var operation = RequireTarget<AuthorizationTarget.Read>(context);
        if (operation.Decision.Obligations.Count == 0)
        {
            throw new AccessDeniedException();
        }
        var obligation = operation.Decision.Obligations[0];
        var input = new ClinicalDirectionReadInput(
            obligation.AccountId, obligation.CenterId, obligation.UnitId, obligation.ResidentId,
            obligation.ResourceType, obligation.Purpose, payload.OperationId);
        return await repository.ReadAsClinicalDirectionAsync(input, ct);
    }

    /// <summary>Lectura resumida del basal vigente para el cuidado cotidiano (AUX-03/ENF-20/MED-21): a
    /// diferencia de ExecuteDirectionBaselineReadAsync (Dirección Clínica, con obligación de auditoría),
    /// AuthorizeBaselineCurrentRead permite a Auxiliar/Enfermería/Medicina sin permiso ni auditoría
    /// adicional, así que aquí no se exige ninguna obligación.</summary>
    public static async Task<CurrentBaselineSummary?> ExecuteBaselineCurrentReadAsync(
        RequestAuthorizationContext context, IBaselineRepository repository, CancellationToken ct = default)
    {
        var operation = RequireTarget<AuthorizationTarget.Read>(context);
        var input = new ReadCurrentBaselineSummaryInput(operation.CenterId, operation.ResidentId!.Value);
        return await repository.ReadCurrentSummaryAsync(input, ct);
    }

    private static AuthorizedOperation RequireTarget<TTarget>(RequestAuthorizationContext context) where TTarget : AuthorizationTarget =>
        Operations.TryGetValue(context, out var operation) && operation.Target is TTarget
            ? operation
            : throw new AccessDeniedException();
}
