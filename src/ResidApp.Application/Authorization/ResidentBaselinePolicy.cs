using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.Authorization;

/// <summary>
/// Traduce lib/authorization/policy.ts con fidelidad 1:1: motor puro de decisión, deny-by-default. No
/// traduce la validación estructural de "unknown" (isAuthorizationRequest y afines) porque el único
/// llamador es RequestAuthorizationContext, código propio y ya tipado — el compilador de C# hace
/// estructuralmente imposible construir una petición mal formada.
/// </summary>
public static class ResidentBaselinePolicy
{
    public static AuthorizationDecision DenyByDefault() => AuthorizationDecision.Deny(AuthorizationDenialReason.DenyByDefault);

    public static AuthorizationDecision Authorize(ResidentBaselineAuthorizationRequest request)
    {
        if (!TryAuthorizeContext(request, out var context, out var denial))
        {
            return denial;
        }

        return request.Action switch
        {
            ResidentBaselineAction.ResidentIdentityCreate => AuthorizeResidentIdentityCreate(context),
            ResidentBaselineAction.ResidentIdentityRead => AuthorizeResidentIdentityRead(request, context),
            ResidentBaselineAction.ResidentIdentityUpdate => context.ActiveProfile == SystemProfile.Administracion
                ? AuthorizationDecision.Allow()
                : AuthorizationDecision.Deny(AuthorizationDenialReason.ActionNotAllowed),
            ResidentBaselineAction.BaselineCurrentRead => AuthorizeBaselineCurrentRead(request, context),
            ResidentBaselineAction.BaselineInitialComplete =>
                AuthorizeProfessionalWrite(context, ResidentBaselinePermission.BaselineInitialComplete),
            ResidentBaselineAction.BaselineReevaluate =>
                AuthorizeProfessionalWrite(context, ResidentBaselinePermission.BaselineReevaluate),
            ResidentBaselineAction.BaselineHistoryRead => AuthorizeBaselineHistoryRead(request, context),
            _ => DenyByDefault(),
        };
    }

    private readonly record struct AuthorizedContext(
        AccountId AccountId, SystemProfile ActiveProfile, IReadOnlyList<ResidentBaselineProfileScope> Scopes);

    private static bool TryAuthorizeContext(
        ResidentBaselineAuthorizationRequest request, out AuthorizedContext context, out AuthorizationDecision denial)
    {
        var subject = request.Subject;
        context = default;
        denial = null!;

        if (!subject.Authenticated)
        {
            denial = AuthorizationDecision.Deny(AuthorizationDenialReason.NotAuthenticated);
            return false;
        }
        if (subject.AccountId is not { } accountId)
        {
            denial = AuthorizationDecision.Deny(AuthorizationDenialReason.AccountIdRequired);
            return false;
        }
        if (!subject.AccountActive)
        {
            denial = AuthorizationDecision.Deny(AuthorizationDenialReason.AccountInactive);
            return false;
        }
        if (subject.ActiveProfile is not { } activeProfile)
        {
            denial = AuthorizationDecision.Deny(AuthorizationDenialReason.ActiveProfileRequired);
            return false;
        }
        if (!subject.AssignedProfiles.Contains(activeProfile))
        {
            denial = AuthorizationDecision.Deny(AuthorizationDenialReason.ProfileNotAssigned);
            return false;
        }

        var profileScopes = subject.ProfileScopes.Where(scope => scope.Profile == activeProfile).ToList();
        var centerScopes = profileScopes.Where(scope => scope.CenterId == request.CenterId).ToList();
        if (centerScopes.Count == 0)
        {
            denial = AuthorizationDecision.Deny(AuthorizationDenialReason.CenterOutOfScope);
            return false;
        }

        var unitScopes = centerScopes.Where(scope => scope.UnitId == request.UnitId).ToList();
        if (unitScopes.Count == 0)
        {
            denial = AuthorizationDecision.Deny(AuthorizationDenialReason.UnitOutOfScope);
            return false;
        }

        var scopes = request.RequiresResident
            ? unitScopes.Where(scope => scope.ResidentIds.Contains(request.ResidentId!.Value)).ToList()
            : (IReadOnlyList<ResidentBaselineProfileScope>)unitScopes;
        if (request.RequiresResident && scopes.Count == 0)
        {
            denial = AuthorizationDecision.Deny(AuthorizationDenialReason.ResidentOutOfScope);
            return false;
        }

        context = new AuthorizedContext(accountId, activeProfile, scopes);
        return true;
    }

    private static AuthorizationDecision AuthorizeResidentIdentityCreate(AuthorizedContext context)
    {
        if (context.ActiveProfile == SystemProfile.Administracion)
        {
            return AuthorizationDecision.Allow();
        }
        if (context.ActiveProfile == SystemProfile.Enfermeria)
        {
            return HasPermission(context, ResidentBaselinePermission.ResidentIdentityCreate)
                ? AuthorizationDecision.Allow()
                : AuthorizationDecision.Deny(AuthorizationDenialReason.PermissionRequired);
        }
        return AuthorizationDecision.Deny(AuthorizationDenialReason.ActionNotAllowed);
    }

    private static AuthorizationDecision AuthorizeResidentIdentityRead(
        ResidentBaselineAuthorizationRequest request, AuthorizedContext context)
    {
        if (context.ActiveProfile == SystemProfile.Familiar &&
            !context.Scopes.Any(scope => scope.ActiveFamilyAuthorizationResidentIds.Contains(request.ResidentId!.Value)))
        {
            return AuthorizationDecision.Deny(AuthorizationDenialReason.FamilyAuthorizationRequired);
        }
        return AuthorizationDecision.Allow();
    }

    private static AuthorizationDecision AuthorizeBaselineCurrentRead(
        ResidentBaselineAuthorizationRequest request, AuthorizedContext context)
    {
        if (context.ActiveProfile is SystemProfile.Auxiliar or SystemProfile.Enfermeria or SystemProfile.Medicina)
        {
            return AuthorizationDecision.Allow();
        }
        return AuthorizeClinicalDirectionRead(request, context, ClinicalResourceType.BaselineCurrent);
    }

    private static AuthorizationDecision AuthorizeBaselineHistoryRead(
        ResidentBaselineAuthorizationRequest request, AuthorizedContext context)
    {
        if (context.ActiveProfile is SystemProfile.Enfermeria or SystemProfile.Medicina)
        {
            return AuthorizationDecision.Allow();
        }
        return AuthorizeClinicalDirectionRead(request, context, ClinicalResourceType.BaselineHistory);
    }

    private static AuthorizationDecision AuthorizeProfessionalWrite(AuthorizedContext context, ResidentBaselinePermission permission)
    {
        if (context.ActiveProfile is not (SystemProfile.Enfermeria or SystemProfile.Medicina))
        {
            return AuthorizationDecision.Deny(AuthorizationDenialReason.ActionNotAllowed);
        }
        return HasPermission(context, permission)
            ? AuthorizationDecision.Allow()
            : AuthorizationDecision.Deny(AuthorizationDenialReason.PermissionRequired);
    }

    private static AuthorizationDecision AuthorizeClinicalDirectionRead(
        ResidentBaselineAuthorizationRequest request, AuthorizedContext context, ClinicalResourceType resourceType)
    {
        if (context.ActiveProfile != SystemProfile.DireccionClinica)
        {
            return AuthorizationDecision.Deny(AuthorizationDenialReason.ActionNotAllowed);
        }
        if (!HasPermission(context, ResidentBaselinePermission.ClinicalDetailRead))
        {
            return AuthorizationDecision.Deny(AuthorizationDenialReason.PermissionRequired);
        }
        if (request.Purpose != ClinicalDetailAccessPurpose.SupervisionClinica)
        {
            return AuthorizationDecision.Deny(AuthorizationDenialReason.AccessPurposeRequired);
        }
        var obligation = new ClinicalDetailAuditObligation(
            resourceType, context.AccountId, request.CenterId, request.UnitId, request.ResidentId!.Value,
            ClinicalDetailAccessPurpose.SupervisionClinica);
        return AuthorizationDecision.Allow(obligation);
    }

    private static bool HasPermission(AuthorizedContext context, ResidentBaselinePermission permission) =>
        context.Scopes.Any(scope => scope.Permissions.Contains(permission));
}
