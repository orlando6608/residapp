using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Families;

namespace ResidApp.UnitTests;

/// <summary>Portal Familiar (FAM-07): lo publicado se ve 30 días si es ordinario y 6 meses si es relevante, contados desde la publicación.</summary>
public class FamilyPortalVisibilityTests
{
    private static readonly DateTimeOffset Scheduled = new(2026, 10, 8, 19, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void SinPublicar_NoSeVe_NiSiquieraLaRelevante()
    {
        var beforeScheduled = Scheduled.AddMinutes(-1);

        Assert.False(FamilyPortalVisibility.IsVisible(FamilyCommunicationType.Ordinaria, Scheduled, null, beforeScheduled));
        Assert.False(FamilyPortalVisibility.IsVisible(FamilyCommunicationType.Relevante, Scheduled, null, beforeScheduled));
    }

    [Fact]
    public void ALaHora_SeVe()
    {
        Assert.True(FamilyPortalVisibility.IsVisible(FamilyCommunicationType.Ordinaria, Scheduled, null, Scheduled));
    }

    [Fact]
    public void PublicadaAntes_SeVeDesdeEntonces_YLaVentanaCuentaDesdeEntonces()
    {
        var early = Scheduled.AddHours(-5);

        Assert.True(FamilyPortalVisibility.IsVisible(FamilyCommunicationType.Ordinaria, Scheduled, early, early));
        Assert.Equal(early, FamilyPortalVisibility.PublishedAt(Scheduled, early));
        Assert.Equal(early.AddDays(30), FamilyPortalVisibility.VisibleUntil(FamilyCommunicationType.Ordinaria, early));
    }

    [Fact]
    public void Ordinaria_SeVe30Dias_ElDia30YaNo()
    {
        Assert.True(FamilyPortalVisibility.IsVisible(FamilyCommunicationType.Ordinaria, Scheduled, null, Scheduled.AddDays(30).AddTicks(-1)));
        Assert.False(FamilyPortalVisibility.IsVisible(FamilyCommunicationType.Ordinaria, Scheduled, null, Scheduled.AddDays(30)));
    }

    [Fact]
    public void Relevante_SeVe6Meses_AunqueLaOrdinariaYaNo()
    {
        var afterOrdinaryWindow = Scheduled.AddDays(31);
        var lastMoment = Scheduled.AddMonths(6).AddTicks(-1);

        Assert.False(FamilyPortalVisibility.IsVisible(FamilyCommunicationType.Ordinaria, Scheduled, null, afterOrdinaryWindow));
        Assert.True(FamilyPortalVisibility.IsVisible(FamilyCommunicationType.Relevante, Scheduled, null, afterOrdinaryWindow));
        Assert.True(FamilyPortalVisibility.IsVisible(FamilyCommunicationType.Relevante, Scheduled, null, lastMoment));
        Assert.False(FamilyPortalVisibility.IsVisible(FamilyCommunicationType.Relevante, Scheduled, null, Scheduled.AddMonths(6)));
    }

    [Fact]
    public void UnaCorrectoraSale_EnElActo_YDejaDeVerseConElOriginal()
    {
        var corrected = Scheduled.AddDays(5);

        Assert.False(FamilyPortalVisibility.IsCorrectionVisible(corrected, FamilyCommunicationType.Ordinaria, Scheduled, corrected.AddTicks(-1)));
        Assert.True(FamilyPortalVisibility.IsCorrectionVisible(corrected, FamilyCommunicationType.Ordinaria, Scheduled, corrected));
        // La ventana es la del original, contada desde su publicación: la correctora no la alarga.
        Assert.True(FamilyPortalVisibility.IsCorrectionVisible(corrected, FamilyCommunicationType.Ordinaria, Scheduled, Scheduled.AddDays(30).AddTicks(-1)));
        Assert.False(FamilyPortalVisibility.IsCorrectionVisible(corrected, FamilyCommunicationType.Ordinaria, Scheduled, Scheduled.AddDays(30)));
        Assert.True(FamilyPortalVisibility.IsCorrectionVisible(corrected, FamilyCommunicationType.Relevante, Scheduled, Scheduled.AddDays(31)));
        Assert.False(FamilyPortalVisibility.IsCorrectionVisible(corrected, FamilyCommunicationType.Relevante, Scheduled, Scheduled.AddMonths(6)));
    }
}

/// <summary>Retirada excepcional de un comunicado publicado (portal-familiar.md, historia 3): hace falta un motivo, de hasta 500 caracteres.</summary>
public class FamilyCommunicationWithdrawalTests
{
    [Fact]
    public void ElMotivo_SeRecorta_YDebeTenerTexto()
    {
        Assert.Equal("Era de otro residente.", FamilyCommunicationWithdrawal.NormalizeReason("  Era de otro residente.  "));
        Assert.Equal(new string('x', 500), FamilyCommunicationWithdrawal.NormalizeReason(new string('x', 500)));
        foreach (var invalid in new string?[] { null, "", "   ", new string('x', 501) })
        {
            var error = Assert.Throws<ResidApp.Shared.DomainValidationException>(() => FamilyCommunicationWithdrawal.NormalizeReason(invalid));
            Assert.Equal("FAMILY_COMMUNICATION_WITHDRAWAL_INVALID", error.Message);
        }
    }
}
