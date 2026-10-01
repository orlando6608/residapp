using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

public class FollowUpTests
{
    private static readonly DateOnly Today = new(2026, 9, 28);

    [Fact]
    public void Plan_SinFechaNiCriterio_SeRechaza()
    {
        var ex = Assert.Throws<DomainValidationException>(() => new FollowUpPlan(null, "   "));
        Assert.Equal("FOLLOW_UP_PLAN_REQUIRED", ex.Message);
    }

    [Fact]
    public void Plan_SoloConCriterio_NoVenceNunca()
    {
        var plan = new FollowUpPlan(null, "  Si reaparece la fiebre.  ");

        Assert.Equal("Si reaparece la fiebre.", plan.Criterion);
        Assert.False(plan.IsOverdue(Today.AddYears(1)));
    }

    [Fact]
    public void Plan_ConFecha_VenceSoloCuandoLaFechaHaPasado()
    {
        var plan = new FollowUpPlan(Today, null);

        Assert.False(plan.IsOverdue(Today));
        Assert.True(plan.IsOverdue(Today.AddDays(1)));
    }

    [Fact]
    public void Acciones_ConSusDatos_SeAceptan()
    {
        Assert.Equal("Tolera la dieta.", FollowUpAction.Note(" Tolera la dieta. ").Text);
        var reschedule = FollowUpAction.Reschedule(new FollowUpPlan(Today, null), "Sigue con tos.");
        Assert.Equal(Today, reschedule.Plan!.DueDate);
        var teamId = Guid.NewGuid();
        var transfer = FollowUpAction.Transfer(teamId, "  ");
        Assert.Equal(teamId, transfer.IncomingTeamId);
        Assert.Null(transfer.Text);
        var kept = FollowUpAction.Keep("  ");
        Assert.Equal(FollowUpActionType.Conservacion, kept.Type);
        Assert.Null(kept.Text);
        Assert.Equal("Reviso el informe mañana.", FollowUpAction.Keep(" Reviso el informe mañana. ").Text);
    }

    public static TheoryData<Func<FollowUpAction>> AccionesIncompletas => new()
    {
        () => FollowUpAction.Note(" "),
        () => FollowUpAction.Reschedule(new FollowUpPlan(Today, null), null),
        () => FollowUpAction.Transfer(null, "Nota."),
        () => FollowUpAction.Transfer(Guid.Empty, null),
        () => FollowUpAction.Receive(Guid.Empty),
        () => FollowUpAction.Note(new string('a', FollowUpAction.MaxTextLength + 1)),
        () => FollowUpAction.Keep(new string('a', FollowUpAction.MaxTextLength + 1)),
    };

    [Theory]
    [MemberData(nameof(AccionesIncompletas))]
    public void Acciones_Incompletas_SeRechazan(Func<FollowUpAction> build)
    {
        var ex = Assert.Throws<DomainValidationException>(() => build());
        Assert.Equal("FOLLOW_UP_ACTION_INVALID", ex.Message);
    }
}
