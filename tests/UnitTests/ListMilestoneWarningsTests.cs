using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Supervision;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>DIR-11 (4.1 C): el equipo responsable ve los hitos a punto de vencer o vencidos que le corresponden.</summary>
public class ListMilestoneWarningsTests
{
    private static readonly Guid ScopeId = Guid.NewGuid();
    private static readonly CenterId Center = CenterId.From(Guid.NewGuid());
    private static readonly UnitId Unit = UnitId.From(Guid.NewGuid());

    private sealed class Scopes(SystemProfile profile) : IProfileScopeDirectoryProvider
    {
        public Task<IReadOnlyList<ActiveProfileScope>> ListActiveAsync(string externalSubject, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ActiveProfileScope>>([new(ScopeId, AccountId.From(Guid.NewGuid()), Center, "Centro", profile)]);

        public Task<IReadOnlyList<ScopeUnit>> ListUnitsAsync(string externalSubject, Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ScopeUnit>>([]);

        public Task<IReadOnlyList<string>> ListPermissionsAsync(string externalSubject, Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class Deadlines(IReadOnlyDictionary<ProcessMilestone, MilestoneDeadline>? map = null) : IProcessDeadlineRepository
    {
        public Task<ProcessDeadlinesView> ReadAsync(ProcessDeadlinesAccess access, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<int> SaveAsync(SaveProcessDeadlinesInput input, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<ProcessMilestone, MilestoneDeadline>> GetEffectiveAsync(CenterId centerId, CancellationToken ct = default) =>
            Task.FromResult(map ?? ProcessMilestoneRules.Defaults);
    }

    private sealed class Session(bool signedIn = true) : ISessionIdentityProvider
    {
        public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
            Task.FromResult<VerifiedIdentity?>(signedIn ? new VerifiedIdentity("sujeto") : null);
    }

    private sealed class Facts(params MilestoneFact[] facts) : IMilestoneFactDirectory
    {
        public Task<IReadOnlyList<MilestoneFact>> ListMilestoneFactsAsync(
            Guid profileScopeId, CenterId centerId, DateTime from, DateTime toExclusive, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MilestoneFact>>(facts);
    }

    private static MilestoneFact Fact(
        ProcessMilestone milestone, TimeSpan startedAgo, DateTime? end = null, SystemProfile responsible = SystemProfile.Enfermeria, bool priority = false) =>
        new(milestone, Guid.NewGuid(), ResidentId.From(Guid.NewGuid()), "Residente", Unit, "Unidad", priority, DateTime.UtcNow - startedAgo, end, false, responsible);

    [Fact]
    public async Task Enfermeria_VeSusHitosVencidosYAlPuntoDeVencer_LosMasUrgentesPrimero_YNadaDeLoDemas()
    {
        var useCase = new ListMilestoneWarnings(new Scopes(SystemProfile.Enfermeria), new Facts(
            Fact(ProcessMilestone.LecturaIndicacion, TimeSpan.FromHours(7)),                               // 8 h, lleva 7: a punto de vencer
            Fact(ProcessMilestone.ValoracionEnfermeria, TimeSpan.FromHours(9)),                            // vencido
            Fact(ProcessMilestone.ValoracionEnfermeria, TimeSpan.FromHours(1)),                            // en plazo, aún lejos
            Fact(ProcessMilestone.ValoracionEnfermeria, TimeSpan.FromHours(9), end: DateTime.UtcNow),      // hecho tarde: no es un aviso
            Fact(ProcessMilestone.ValoracionMedica, TimeSpan.FromHours(30), responsible: SystemProfile.Medicina)), // de Medicina
            new Session(), new Deadlines());

        var result = await useCase.ExecuteAsync(new ListMilestoneWarningsCommand(ScopeId, Center));

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal([ProcessMilestone.ValoracionEnfermeria, ProcessMilestone.LecturaIndicacion], result.Value!.Select(w => w.Milestone));
        Assert.Equal([MilestoneStatus.FueraDePlazo, MilestoneStatus.APuntoDeVencer], result.Value!.Select(w => w.Status));
    }

    [Fact]
    public async Task Medicina_VeSoloLosSuyos()
    {
        var useCase = new ListMilestoneWarnings(new Scopes(SystemProfile.Medicina), new Facts(
            Fact(ProcessMilestone.ValoracionMedica, TimeSpan.FromHours(30), responsible: SystemProfile.Medicina),
            Fact(ProcessMilestone.InformeDerivacion, TimeSpan.FromMinutes(40), responsible: SystemProfile.Enfermeria),
            Fact(ProcessMilestone.InformeDerivacion, TimeSpan.FromMinutes(40), responsible: SystemProfile.Medicina)), new Session(), new Deadlines());

        var result = await useCase.ExecuteAsync(new ListMilestoneWarningsCommand(ScopeId, Center));

        Assert.Equal([ProcessMilestone.ValoracionMedica, ProcessMilestone.InformeDerivacion], result.Value!.Select(w => w.Milestone).Order());
        Assert.Equal(2, result.Value!.Count);
    }

    [Fact]
    public async Task UsaLosPlazosDelCentro_NoLosDeCJ()
    {
        var fact = Fact(ProcessMilestone.LecturaIndicacion, TimeSpan.FromHours(2));   // con el plazo de CJ (8 h) aún no avisa
        var custom = new Dictionary<ProcessMilestone, MilestoneDeadline>(ProcessMilestoneRules.Defaults)
        {
            [ProcessMilestone.LecturaIndicacion] = new(TimeSpan.FromHours(1), TimeSpan.FromMinutes(15)),
        };
        var command = new ListMilestoneWarningsCommand(ScopeId, Center);

        var byDefault = await new ListMilestoneWarnings(new Scopes(SystemProfile.Enfermeria), new Facts(fact), new Session(), new Deadlines()).ExecuteAsync(command);
        var byCenter = await new ListMilestoneWarnings(new Scopes(SystemProfile.Enfermeria), new Facts(fact), new Session(), new Deadlines(custom)).ExecuteAsync(command);

        Assert.Empty(byDefault.Value!);
        Assert.Equal(MilestoneStatus.FueraDePlazo, Assert.Single(byCenter.Value!).Status);
    }

    [Theory]
    [InlineData(SystemProfile.Auxiliar)]
    [InlineData(SystemProfile.Administracion)]
    [InlineData(SystemProfile.DireccionClinica)]
    public async Task OtrosPerfiles_NoVenAvisos(SystemProfile profile)
    {
        var result = await new ListMilestoneWarnings(new Scopes(profile), new Facts(), new Session(), new Deadlines())
            .ExecuteAsync(new ListMilestoneWarningsCommand(ScopeId, Center));

        Assert.Equal(ApplicationFailureCode.AccessDenied, result.Error!.Code);
    }

    [Fact]
    public async Task SinSesionOConUnAmbitoAjeno_EsAccesoDenegado()
    {
        var noSession = await new ListMilestoneWarnings(new Scopes(SystemProfile.Enfermeria), new Facts(), new Session(false), new Deadlines())
            .ExecuteAsync(new ListMilestoneWarningsCommand(ScopeId, Center));
        var foreign = await new ListMilestoneWarnings(new Scopes(SystemProfile.Enfermeria), new Facts(), new Session(), new Deadlines())
            .ExecuteAsync(new ListMilestoneWarningsCommand(Guid.NewGuid(), Center));

        Assert.Equal(ApplicationFailureCode.AccessDenied, noSession.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, foreign.Error!.Code);
    }
}
