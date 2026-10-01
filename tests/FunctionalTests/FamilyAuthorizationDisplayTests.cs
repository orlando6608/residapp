using ResidApp.Application.Ports;
using ResidApp.Domain.Families;
using ResidApp.Domain.Residents;
using ResidApp.Shared;
using ResidApp.Web.Models;

namespace ResidApp.FunctionalTests;

/// <summary>ADM-08/ADM-10 (0022): cómo se muestra la autorización de un familiar y qué cambios ofrece la pantalla.</summary>
public class FamilyAuthorizationDisplayTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static ResidentFamilyMember Member(params (FamilyAuthorizationStatus Status, DateOnly? Until)[] changes) => new(
        Guid.NewGuid(), "Lucía Pérez", "Hija", "600123456", null,
        changes.Select((c, i) => new FamilyAuthorizationChangeEntry(i + 1, c.Status, c.Until, null, DateTimeOffset.UtcNow)).ToList());

    private static FamilyAuthorizationViewModel Screen(ResidentFamilyMember member) => new(
        new AdministrativeResidentSummary(ResidentId.From(Guid.NewGuid()), "Residente", new DateOnly(1940, 1, 1), DocumentedSexCode.NoConsta,
            UnitId.From(Guid.NewGuid()), "Planta 1", DateTimeOffset.UtcNow),
        member, Today, new FamilyAuthorizationFormModel());

    [Fact]
    public void Etiquetas_DicenElEstadoDeHoy_EnEspañol()
    {
        Assert.Equal("Sin autorización", AdministrativeResidentDisplay.Authorization(Member(), Today));
        Assert.Equal("Pendiente", AdministrativeResidentDisplay.Authorization(Member((FamilyAuthorizationStatus.Pendiente, null)), Today));
        Assert.Equal("Activa", AdministrativeResidentDisplay.Authorization(
            Member((FamilyAuthorizationStatus.Pendiente, null), (FamilyAuthorizationStatus.Activa, null)), Today));
        Assert.Equal("Activa hasta el 01/10/2026", AdministrativeResidentDisplay.Authorization(
            Member((FamilyAuthorizationStatus.Pendiente, null), (FamilyAuthorizationStatus.Activa, Today)), Today));
        Assert.Equal("Caducada (fue válida hasta el 30/09/2026)", AdministrativeResidentDisplay.Authorization(
            Member((FamilyAuthorizationStatus.Pendiente, null), (FamilyAuthorizationStatus.Activa, Today.AddDays(-1))), Today));
    }

    [Fact]
    public void Pantalla_OfreceSoloLosCambiosPosibles()
    {
        var expired = Screen(Member((FamilyAuthorizationStatus.Pendiente, null), (FamilyAuthorizationStatus.Activa, Today.AddDays(-1))));
        var revoked = Screen(Member((FamilyAuthorizationStatus.Pendiente, null), (FamilyAuthorizationStatus.Revocada, null)));

        Assert.Equal([FamilyAuthorizationChange.Abrir], Screen(Member()).Allowed);
        Assert.Equal(FamilyAuthorizationStatus.Caducada, expired.Effective);
        Assert.Equal([FamilyAuthorizationChange.Activar, FamilyAuthorizationChange.Revocar], expired.Allowed);
        Assert.Empty(revoked.Allowed);
        Assert.Equal(["Abrir autorización", "Activar", "Suspender", "Revocar"],
            Enum.GetValues<FamilyAuthorizationChange>().Select(c => EnumDisplay.Label(c)));
    }
}
