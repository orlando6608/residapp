using ResidApp.Application.Authorization;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>
/// Motor de decisión puro deny-by-default (ResidentBaselinePolicy). No cubre las 7 acciones al 100% de sus
/// combinaciones, pero sí cada AuthorizationDenialReason y el camino "allow" de cada acción, incluida la
/// obligación de auditoría de Dirección Clínica.
/// </summary>
public class ResidentBaselinePolicyTests
{
    private static readonly CenterId Center = CenterId.New();
    private static readonly CenterId OtherCenter = CenterId.New();
    private static readonly UnitId Unit = UnitId.New();
    private static readonly UnitId OtherUnit = UnitId.New();
    private static readonly ResidentId TargetResident = ResidentId.New();
    private static readonly ResidentId OtherResident = ResidentId.New();
    private static readonly AccountId Account = AccountId.New();

    private static ResidentBaselineProfileScope Scope(
        SystemProfile profile, IReadOnlyList<ResidentBaselinePermission>? permissions = null,
        IReadOnlyList<ResidentId>? residentIds = null, IReadOnlyList<ResidentId>? familyAuthorized = null) =>
        new(profile, Center, Unit, residentIds ?? [TargetResident], familyAuthorized ?? [], permissions ?? []);

    private static AuthorizationSubject Subject(SystemProfile activeProfile, params ResidentBaselineProfileScope[] scopes) =>
        new(Account, true, true, activeProfile, [activeProfile], scopes);

    private static AuthorizationDecision.Denied AssertDenied(AuthorizationDecision decision) => Assert.IsType<AuthorizationDecision.Denied>(decision);

    private static AuthorizationDecision.Allowed AssertAllowed(AuthorizationDecision decision) => Assert.IsType<AuthorizationDecision.Allowed>(decision);

    // --- Puerta común (TryAuthorizeContext) --------------------------------------------------------

    [Fact]
    public void NotAuthenticated_IsDenied()
    {
        var subject = new AuthorizationSubject(null, false, false, null, [], []);
        var request = new ResidentBaselineAuthorizationRequest(ResidentBaselineAction.ResidentIdentityCreate, subject, Center, Unit, null);

        var denied = AssertDenied(ResidentBaselinePolicy.Authorize(request));
        Assert.Equal(AuthorizationDenialReason.NotAuthenticated, denied.Reason);
    }

    [Fact]
    public void AccountInactive_IsDenied()
    {
        var subject = new AuthorizationSubject(Account, true, false, SystemProfile.Administracion, [SystemProfile.Administracion], []);
        var request = new ResidentBaselineAuthorizationRequest(ResidentBaselineAction.ResidentIdentityCreate, subject, Center, Unit, null);

        var denied = AssertDenied(ResidentBaselinePolicy.Authorize(request));
        Assert.Equal(AuthorizationDenialReason.AccountInactive, denied.Reason);
    }

    [Fact]
    public void NoActiveProfile_IsDenied()
    {
        var subject = new AuthorizationSubject(Account, true, true, null, [], []);
        var request = new ResidentBaselineAuthorizationRequest(ResidentBaselineAction.ResidentIdentityCreate, subject, Center, Unit, null);

        var denied = AssertDenied(ResidentBaselinePolicy.Authorize(request));
        Assert.Equal(AuthorizationDenialReason.ActiveProfileRequired, denied.Reason);
    }

    [Fact]
    public void ActiveProfileNotAssigned_IsDenied()
    {
        var subject = new AuthorizationSubject(Account, true, true, SystemProfile.Administracion, [SystemProfile.Enfermeria], []);
        var request = new ResidentBaselineAuthorizationRequest(ResidentBaselineAction.ResidentIdentityCreate, subject, Center, Unit, null);

        var denied = AssertDenied(ResidentBaselinePolicy.Authorize(request));
        Assert.Equal(AuthorizationDenialReason.ProfileNotAssigned, denied.Reason);
    }

    [Fact]
    public void CenterOutOfScope_IsDenied()
    {
        var subject = Subject(SystemProfile.Administracion, Scope(SystemProfile.Administracion));
        var request = new ResidentBaselineAuthorizationRequest(ResidentBaselineAction.ResidentIdentityCreate, subject, OtherCenter, Unit, null);

        var denied = AssertDenied(ResidentBaselinePolicy.Authorize(request));
        Assert.Equal(AuthorizationDenialReason.CenterOutOfScope, denied.Reason);
    }

    [Fact]
    public void UnitOutOfScope_IsDenied()
    {
        var subject = Subject(SystemProfile.Administracion, Scope(SystemProfile.Administracion));
        var request = new ResidentBaselineAuthorizationRequest(ResidentBaselineAction.ResidentIdentityCreate, subject, Center, OtherUnit, null);

        var denied = AssertDenied(ResidentBaselinePolicy.Authorize(request));
        Assert.Equal(AuthorizationDenialReason.UnitOutOfScope, denied.Reason);
    }

    [Fact]
    public void ResidentOutOfScope_ForActionThatRequiresResident_IsDenied()
    {
        var subject = Subject(SystemProfile.Auxiliar, Scope(SystemProfile.Auxiliar, residentIds: [OtherResident]));
        var request = new ResidentBaselineAuthorizationRequest(ResidentBaselineAction.BaselineCurrentRead, subject, Center, Unit, TargetResident);

        var denied = AssertDenied(ResidentBaselinePolicy.Authorize(request));
        Assert.Equal(AuthorizationDenialReason.ResidentOutOfScope, denied.Reason);
    }

    // --- ResidentIdentityCreate ---------------------------------------------------------------------

    [Fact]
    public void ResidentIdentityCreate_Administracion_IsAllowed()
    {
        var subject = Subject(SystemProfile.Administracion, Scope(SystemProfile.Administracion));
        var request = new ResidentBaselineAuthorizationRequest(ResidentBaselineAction.ResidentIdentityCreate, subject, Center, Unit, null);

        AssertAllowed(ResidentBaselinePolicy.Authorize(request));
    }

    [Fact]
    public void ResidentIdentityCreate_EnfermeriaWithoutPermission_IsDenied()
    {
        var subject = Subject(SystemProfile.Enfermeria, Scope(SystemProfile.Enfermeria));
        var request = new ResidentBaselineAuthorizationRequest(ResidentBaselineAction.ResidentIdentityCreate, subject, Center, Unit, null);

        var denied = AssertDenied(ResidentBaselinePolicy.Authorize(request));
        Assert.Equal(AuthorizationDenialReason.PermissionRequired, denied.Reason);
    }

    [Fact]
    public void ResidentIdentityCreate_EnfermeriaWithPermission_IsAllowed()
    {
        var subject = Subject(SystemProfile.Enfermeria, Scope(SystemProfile.Enfermeria, [ResidentBaselinePermission.ResidentIdentityCreate]));
        var request = new ResidentBaselineAuthorizationRequest(ResidentBaselineAction.ResidentIdentityCreate, subject, Center, Unit, null);

        AssertAllowed(ResidentBaselinePolicy.Authorize(request));
    }

    [Fact]
    public void ResidentIdentityCreate_Auxiliar_IsDenied_ActionNotAllowed()
    {
        var subject = Subject(SystemProfile.Auxiliar, Scope(SystemProfile.Auxiliar));
        var request = new ResidentBaselineAuthorizationRequest(ResidentBaselineAction.ResidentIdentityCreate, subject, Center, Unit, null);

        var denied = AssertDenied(ResidentBaselinePolicy.Authorize(request));
        Assert.Equal(AuthorizationDenialReason.ActionNotAllowed, denied.Reason);
    }

    // --- ResidentIdentityUpdate ----------------------------------------------------------------------

    [Fact]
    public void ResidentIdentityUpdate_NonAdministracion_IsDenied()
    {
        var subject = Subject(SystemProfile.Enfermeria, Scope(SystemProfile.Enfermeria));
        var request = new ResidentBaselineAuthorizationRequest(ResidentBaselineAction.ResidentIdentityUpdate, subject, Center, Unit, TargetResident);

        var denied = AssertDenied(ResidentBaselinePolicy.Authorize(request));
        Assert.Equal(AuthorizationDenialReason.ActionNotAllowed, denied.Reason);
    }

    // --- BaselineInitialComplete / BaselineReevaluate (escritura profesional) ------------------------

    [Fact]
    public void BaselineInitialComplete_EnfermeriaWithPermission_IsAllowed()
    {
        var subject = Subject(SystemProfile.Enfermeria, Scope(SystemProfile.Enfermeria, [ResidentBaselinePermission.BaselineInitialComplete]));
        var request = new ResidentBaselineAuthorizationRequest(ResidentBaselineAction.BaselineInitialComplete, subject, Center, Unit, TargetResident);

        AssertAllowed(ResidentBaselinePolicy.Authorize(request));
    }

    [Fact]
    public void BaselineInitialComplete_EnfermeriaWithoutPermission_IsDenied()
    {
        var subject = Subject(SystemProfile.Enfermeria, Scope(SystemProfile.Enfermeria));
        var request = new ResidentBaselineAuthorizationRequest(ResidentBaselineAction.BaselineInitialComplete, subject, Center, Unit, TargetResident);

        var denied = AssertDenied(ResidentBaselinePolicy.Authorize(request));
        Assert.Equal(AuthorizationDenialReason.PermissionRequired, denied.Reason);
    }

    [Fact]
    public void BaselineInitialComplete_Administracion_IsDenied_ActionNotAllowed()
    {
        var subject = Subject(SystemProfile.Administracion, Scope(SystemProfile.Administracion, [ResidentBaselinePermission.BaselineInitialComplete]));
        var request = new ResidentBaselineAuthorizationRequest(ResidentBaselineAction.BaselineInitialComplete, subject, Center, Unit, TargetResident);

        var denied = AssertDenied(ResidentBaselinePolicy.Authorize(request));
        Assert.Equal(AuthorizationDenialReason.ActionNotAllowed, denied.Reason);
    }

    // --- BaselineCurrentRead / BaselineHistoryRead -----------------------------------------------------

    [Fact]
    public void BaselineCurrentRead_Auxiliar_IsAllowed()
    {
        var subject = Subject(SystemProfile.Auxiliar, Scope(SystemProfile.Auxiliar));
        var request = new ResidentBaselineAuthorizationRequest(ResidentBaselineAction.BaselineCurrentRead, subject, Center, Unit, TargetResident);

        AssertAllowed(ResidentBaselinePolicy.Authorize(request));
    }

    [Fact]
    public void BaselineCurrentRead_DireccionClinica_WithoutPermission_IsDenied()
    {
        var subject = Subject(SystemProfile.DireccionClinica, Scope(SystemProfile.DireccionClinica));
        var request = new ResidentBaselineAuthorizationRequest(
            ResidentBaselineAction.BaselineCurrentRead, subject, Center, Unit, TargetResident, ClinicalDetailAccessPurpose.SupervisionClinica);

        var denied = AssertDenied(ResidentBaselinePolicy.Authorize(request));
        Assert.Equal(AuthorizationDenialReason.PermissionRequired, denied.Reason);
    }

    [Fact]
    public void BaselineCurrentRead_DireccionClinica_WithPermissionButWithoutPurpose_IsDenied()
    {
        var subject = Subject(SystemProfile.DireccionClinica, Scope(SystemProfile.DireccionClinica, [ResidentBaselinePermission.ClinicalDetailRead]));
        var request = new ResidentBaselineAuthorizationRequest(ResidentBaselineAction.BaselineCurrentRead, subject, Center, Unit, TargetResident);

        var denied = AssertDenied(ResidentBaselinePolicy.Authorize(request));
        Assert.Equal(AuthorizationDenialReason.AccessPurposeRequired, denied.Reason);
    }

    [Fact]
    public void BaselineHistoryRead_DireccionClinica_WithPermissionAndPurpose_IsAllowedWithAuditObligation()
    {
        var subject = Subject(SystemProfile.DireccionClinica, Scope(SystemProfile.DireccionClinica, [ResidentBaselinePermission.ClinicalDetailRead]));
        var request = new ResidentBaselineAuthorizationRequest(
            ResidentBaselineAction.BaselineHistoryRead, subject, Center, Unit, TargetResident, ClinicalDetailAccessPurpose.SupervisionClinica);

        var allowed = AssertAllowed(ResidentBaselinePolicy.Authorize(request));
        var obligation = Assert.Single(allowed.Obligations);
        Assert.Equal(ClinicalResourceType.BaselineHistory, obligation.ResourceType);
        Assert.Equal(TargetResident, obligation.ResidentId);
    }

    [Fact]
    public void BaselineHistoryRead_Auxiliar_IsDenied_ActionNotAllowed()
    {
        var subject = Subject(SystemProfile.Auxiliar, Scope(SystemProfile.Auxiliar));
        var request = new ResidentBaselineAuthorizationRequest(ResidentBaselineAction.BaselineHistoryRead, subject, Center, Unit, TargetResident);

        var denied = AssertDenied(ResidentBaselinePolicy.Authorize(request));
        Assert.Equal(AuthorizationDenialReason.ActionNotAllowed, denied.Reason);
    }

    // --- ResidentIdentityRead (perfil Familiar) --------------------------------------------------------

    [Fact]
    public void ResidentIdentityRead_Familiar_WithoutFamilyAuthorization_IsDenied()
    {
        var subject = Subject(SystemProfile.Familiar, Scope(SystemProfile.Familiar, familyAuthorized: []));
        var request = new ResidentBaselineAuthorizationRequest(ResidentBaselineAction.ResidentIdentityRead, subject, Center, Unit, TargetResident);

        var denied = AssertDenied(ResidentBaselinePolicy.Authorize(request));
        Assert.Equal(AuthorizationDenialReason.FamilyAuthorizationRequired, denied.Reason);
    }

    [Fact]
    public void ResidentIdentityRead_Familiar_WithFamilyAuthorization_IsAllowed()
    {
        var subject = Subject(SystemProfile.Familiar, Scope(SystemProfile.Familiar, familyAuthorized: [TargetResident]));
        var request = new ResidentBaselineAuthorizationRequest(ResidentBaselineAction.ResidentIdentityRead, subject, Center, Unit, TargetResident);

        AssertAllowed(ResidentBaselinePolicy.Authorize(request));
    }
}
