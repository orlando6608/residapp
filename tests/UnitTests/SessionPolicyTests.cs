using ResidApp.Application.Ports;
using ResidApp.Application.Sessions;

namespace ResidApp.UnitTests;

/// <summary>Reglas de la sesión (ADR 0007, incremento I0): todo falla cerrado, la inactividad y la caducidad absoluta cierran la sesión y
/// la última actividad se anota como mucho una vez por minuto.</summary>
public class SessionPolicyTests
{
    private static readonly SessionPolicy Policy = new(TimeSpan.FromMinutes(30), TimeSpan.FromHours(12));
    private static readonly DateTime T0 = new(2026, 10, 21, 8, 0, 0, DateTimeKind.Utc);

    private static StoredSession Session(
        bool active = true, DateTime? lastActivity = null, DateTime? closedAt = null, string subject = "ana") =>
        new(Guid.NewGuid(), subject, active, T0.AddHours(12), lastActivity ?? T0, closedAt);

    [Fact]
    public void Vigente_EsValida() => Assert.Equal(SessionCheck.Valid, Policy.Check(Session(), "ana", T0.AddMinutes(29)));

    [Fact]
    public void SinSesion_Cerrada_DeOtroSujeto_OConLaCuentaSuspendida_SeRechaza()
    {
        Assert.Equal(SessionCheck.Rejected, Policy.Check(null, "ana", T0));
        Assert.Equal(SessionCheck.Rejected, Policy.Check(Session(closedAt: T0), "ana", T0));
        Assert.Equal(SessionCheck.Rejected, Policy.Check(Session(), "otra", T0));
        Assert.Equal(SessionCheck.Rejected, Policy.Check(Session(), null, T0));
        Assert.Equal(SessionCheck.Rejected, Policy.Check(Session(subject: "Ana"), "ana", T0));
        Assert.Equal(SessionCheck.Rejected, Policy.Check(Session(active: false), "ana", T0));
    }

    [Fact]
    public void MasDeLaInactividadSinUso_EsInactiva()
    {
        Assert.Equal(SessionCheck.Valid, Policy.Check(Session(), "ana", T0.AddMinutes(30)));
        Assert.Equal(SessionCheck.Idle, Policy.Check(Session(), "ana", T0.AddMinutes(30).AddSeconds(1)));
    }

    [Fact]
    public void LaCaducidadAbsoluta_GanaAunqueHayaActividadReciente() =>
        Assert.Equal(SessionCheck.Expired, Policy.Check(Session(lastActivity: T0.AddHours(12).AddMinutes(-1)), "ana", T0.AddHours(12)));

    [Fact]
    public void LaActividadSeAnota_ComoMuchoUnaVezPorMinuto()
    {
        Assert.False(Policy.NeedsTouch(Session(), T0.AddSeconds(59)));
        Assert.True(Policy.NeedsTouch(Session(), T0.AddMinutes(1)));
    }
}
