using ResidApp.Domain.Audit;

namespace ResidApp.UnitTests;

/// <summary>ADM-28: la lista cerrada de acciones que ve Administración.</summary>
public class AdministrativeAuditTests
{
    [Fact]
    public void LasAcciones_NoSeRepiten_YTodasLasCategoriasTienenAlgunaAccion()
    {
        Assert.Equal(AdministrativeAudit.Actions.Count, AdministrativeAudit.Actions.Select(a => a.Code).Distinct().Count());
        Assert.Equal(Enum.GetValues<AuditCategory>().Order(), AdministrativeAudit.Actions.Select(a => a.Category).Distinct().Order());
    }

    [Theory]
    [InlineData("CLINICAL_DETAIL_READ")]
    [InlineData("REFERENCE_RANGES_UPDATE")]
    [InlineData("CLINICAL_EVENT_REGISTER")]
    [InlineData("MEDICAL_INDICATION_ISSUE")]
    [InlineData("REFERRAL_REPORT_DOWNLOAD")]
    [InlineData("")]
    [InlineData(null)]
    public void LasAccionesClinicas_NoSonAdministrativas(string? code) =>
        Assert.False(AdministrativeAudit.IsAdministrative(code));

    [Theory]
    [InlineData("ACCOUNT_CREATE")]
    [InlineData("UNIT_DEACTIVATE")]
    [InlineData("EMERGENCY_CONTACT_DESIGNATE")]
    public void LasAccionesAdministrativas_Lo_Son(string code) =>
        Assert.True(AdministrativeAudit.IsAdministrative(code));
}
