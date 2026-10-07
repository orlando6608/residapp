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

/// <summary>ADM-03: la identidad corregida, ya validada, su motivo y cuántas correcciones tenía el residente al abrir
/// el formulario (el token contra el doble envío).</summary>
public sealed record ResidentIdentityCorrectionPayload(ResidentIdentity Identity, string Reason, int ExpectedCorrections);

/// <summary>Traduce el payload de executeBaselineSign en request-context.ts.</summary>
public sealed record BaselineSignPayload(int ExpectedDraftRevision, Guid OperationId);

/// <summary>No existía en el original executeDirectionBaselineRead de request-context.ts (que no recibía
/// payload alguno): se añade para poder pasar OperationId sin romper la firma pública existente de
/// ExecuteDirectionBaselineReadAsync con un parámetro suelto. Justification es la que se declara al abrir (o la de la
/// declaración que se reutiliza); ReuseDeclarationId, la declaración vigente que ampara la lectura; DeclarationMinutes,
/// lo que dura una declaración nueva.</summary>
public sealed record ClinicalDirectionReadPayload(
    Guid OperationId, string Justification, Guid? ReuseDeclarationId, int DeclarationMinutes);

/// <summary>Traduce los campos de entrada a ENF-19/ENF-20 "crear borrador": los campos comunes de versión
/// (motivo, fuente y fecha de la información) que el flujo exige desde el momento de crear el borrador, no
/// como un paso separado editable después.</summary>
public sealed record BaselineDraftCreatePayload(
    Domain.Baseline.Catalogs.InformationSourceCode CommonInformationSourceCode,
    string? CommonInformationSourceOtherText, DateOnly CommonInformationDate, Guid OperationId);

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
            // cortaba antes de llegar a la política. Se habilita para BaselineCurrent (AUX-03/ENF-20) y, desde el
            // Historial (ENF-24), para BaselineHistory de Enfermería y Medicina, que la política ya permite.
            var directCareRead = resourceType == ClinicalResourceType.BaselineCurrent
                    && evidence.Profile is SystemProfile.Auxiliar or SystemProfile.Enfermeria or SystemProfile.Medicina
                || resourceType == ClinicalResourceType.BaselineHistory
                    && evidence.Profile is SystemProfile.Enfermeria or SystemProfile.Medicina;
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
            AuthorizationTarget.Read => resourceType switch
            {
                ClinicalResourceType.BaselineCurrent => ResidentBaselineAction.BaselineCurrentRead,
                ClinicalResourceType.ResidentTimeline => ResidentBaselineAction.ResidentTimelineRead,
                ClinicalResourceType.ClosedEventsHistory => ResidentBaselineAction.ClosedEventsHistoryRead,
                ClinicalResourceType.AssessmentAmendments => ResidentBaselineAction.AssessmentAmendmentsRead,
                ClinicalResourceType.ClinicalTraceability => ResidentBaselineAction.ClinicalTraceabilityRead,
                ClinicalResourceType.ReferralReports => ResidentBaselineAction.ReferralReportsRead,
                _ => ResidentBaselineAction.BaselineHistoryRead,
            },
            AuthorizationTarget.Draft draft => draft.Reason == BaselineReason.Alta
                ? ResidentBaselineAction.BaselineInitialComplete
                : ResidentBaselineAction.BaselineReevaluate,
            AuthorizationTarget.IdentityUpdate => ResidentBaselineAction.ResidentIdentityUpdate,
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

    /// <summary>ADM-03: corrección de la identidad administrativa. La política ya la reserva a Administración; se
    /// vuelve a comprobar aquí, igual que ExecuteResidentCreateAsync comprueba su perfil.</summary>
    public static async Task<int> ExecuteResidentIdentityUpdateAsync(
        RequestAuthorizationContext context, IResidentIdentityRepository repository, ResidentIdentityCorrectionPayload payload,
        CancellationToken ct = default)
    {
        var operation = RequireTarget<AuthorizationTarget.IdentityUpdate>(context);
        if (operation.Subject.ActiveProfile != SystemProfile.Administracion)
        {
            throw new AccessDeniedException();
        }
        var input = new CorrectResidentIdentityInput(
            operation.Subject.AccountId!.Value, operation.CenterId, operation.UnitId, operation.ResidentId!.Value,
            payload.Identity, payload.Reason, payload.ExpectedCorrections);
        return await repository.CorrectAsync(input, ct);
    }

    /// <summary>ADM-08 a ADM-11 (0022): familiares, autorizaciones y contacto urgente son datos administrativos del
    /// residente, así que se autorizan como la corrección de identidad (IdentityUpdate: Administración y residente de su
    /// ámbito). Devuelve el residente ya autorizado y la cuenta que firma.</summary>
    public static AdministrativeResidentTarget RequireResidentAdministration(RequestAuthorizationContext context)
    {
        var operation = RequireTarget<AuthorizationTarget.IdentityUpdate>(context);
        if (operation.Subject.ActiveProfile != SystemProfile.Administracion)
        {
            throw new AccessDeniedException();
        }
        return new AdministrativeResidentTarget(
            operation.Subject.AccountId!.Value, operation.CenterId, operation.UnitId, operation.ResidentId!.Value);
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
            obligation.ResourceType, obligation.Purpose, payload.OperationId,
            operation.ProfileScopeId, payload.Justification, payload.ReuseDeclarationId, payload.DeclarationMinutes);
        return await repository.ReadAsClinicalDirectionAsync(input, ct);
    }

    /// <summary>DIR-12: descarga del informe firmado de un evento por Dirección Clínica. La autorización (permiso clínico y finalidad, de la
    /// declaración vigente) ya está resuelta; el repositorio vuelve a exigir en SQL el ámbito, el evento del residente y la declaración, y audita la
    /// descarga en la misma transacción.</summary>
    public static async Task<ReferralReportPdf?> ExecuteDirectionReferralDownloadAsync(
        RequestAuthorizationContext context, IReferralReportRepository repository, Guid eventId, Guid declarationId, CancellationToken ct = default)
    {
        var operation = RequireTarget<AuthorizationTarget.Read>(context);
        if (operation.Decision.Obligations.Count == 0)
        {
            throw new AccessDeniedException();
        }
        var obligation = operation.Decision.Obligations[0];
        return await repository.DownloadAsDirectionAsync(new DirectionReferralDownloadInput(
            obligation.AccountId, operation.ProfileScopeId, obligation.CenterId, obligation.ResidentId, eventId, declarationId), ct);
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

    /// <summary>ENF-24: versiones firmadas del basal para Enfermería y Medicina (BaselineHistoryRead sin
    /// obligación de auditoría). Una decisión con obligaciones (Dirección Clínica) no entra por aquí: su
    /// lectura es ExecuteDirectionBaselineReadAsync, que audita antes de entregar el contenido.</summary>
    public static async Task<IReadOnlyList<BaselineHistoryEntry>> ExecuteBaselineHistoryReadAsync(
        RequestAuthorizationContext context, IBaselineRepository repository, CancellationToken ct = default)
    {
        var operation = RequireTarget<AuthorizationTarget.Read>(context);
        if (operation.Action != ResidentBaselineAction.BaselineHistoryRead || operation.Decision.Obligations.Count > 0)
        {
            throw new AccessDeniedException();
        }
        var input = new ReadCurrentBaselineSummaryInput(operation.CenterId, operation.ResidentId!.Value);
        return await repository.ReadHistoryAsync(input, ct);
    }

    /// <summary>ENF-24: el contenido de una versión firmada, con la misma autorización que
    /// ExecuteBaselineHistoryReadAsync (BaselineHistoryRead sin obligación de auditoría).</summary>
    public static async Task<BaselineVersionDetail?> ExecuteBaselineVersionReadAsync(
        RequestAuthorizationContext context, IBaselineRepository repository, int versionNumber, CancellationToken ct = default)
    {
        var operation = RequireTarget<AuthorizationTarget.Read>(context);
        if (operation.Action != ResidentBaselineAction.BaselineHistoryRead || operation.Decision.Obligations.Count > 0)
        {
            throw new AccessDeniedException();
        }
        var input = new ReadCurrentBaselineSummaryInput(operation.CenterId, operation.ResidentId!.Value);
        return await repository.ReadVersionAsync(input, versionNumber, ct);
    }

    /// <summary>ENF-19/ENF-20: crea el contenido de un borrador de basal, ya autorizado (permiso
    /// BASELINE_INITIAL_COMPLETE o BASELINE_REEVALUATE según el motivo elegido, comprobado en
    /// ResolveAsync). Sin obligación de auditoría propia: la propia fila del borrador, con su autoría y
    /// ámbito, es la evidencia.</summary>
    public static async Task<CreateBaselineDraftResult> ExecuteBaselineDraftCreateAsync(
        RequestAuthorizationContext context, IBaselineRepository repository, BaselineDraftCreatePayload payload, CancellationToken ct = default)
    {
        var operation = RequireTarget<AuthorizationTarget.Draft>(context);
        var activeProfile = operation.Subject.ActiveProfile!.Value;
        var draftTarget = (AuthorizationTarget.Draft)operation.Target;
        var input = new CreateBaselineDraftInput(
            operation.Subject.AccountId!.Value, activeProfile, operation.CenterId, operation.UnitId, draftTarget.ResidentId,
            draftTarget.Reason, payload.CommonInformationSourceCode, payload.CommonInformationSourceOtherText,
            payload.CommonInformationDate, payload.OperationId);
        return await repository.CreateDraftAsync(input, ct);
    }

    private static AuthorizedOperation RequireTarget<TTarget>(RequestAuthorizationContext context) where TTarget : AuthorizationTarget =>
        Operations.TryGetValue(context, out var operation) && operation.Target is TTarget
            ? operation
            : throw new AccessDeniedException();
}
