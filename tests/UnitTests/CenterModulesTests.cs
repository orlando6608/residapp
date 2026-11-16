using ResidApp.Domain.Modules;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>Módulos opcionales por centro (ADR 0015): el estado según el último cambio y la vigencia, y las reglas de entrada de un cambio.</summary>
public class CenterModulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 16);
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid PlatformId = Guid.NewGuid();
    private static readonly CenterId Center = CenterId.From(Guid.NewGuid());

    private static CenterModule Module(bool hasOwnData) =>
        new("X", "Módulo X", [], "1", "docs/privacidad/x.md", ["C"], HasOwnData: hasOwnData, UsesThirdParty: false, GlobalCut: "n/a");

    private static CenterModuleChange Change(bool enabled, DateOnly? from = null, DateOnly? until = null) => new(1, enabled, from, until);

    [Fact]
    public void SinNingunCambio_NoEstaDisponible_EnCualquierModulo()
    {
        Assert.Equal(ModuleState.NotAvailable, CenterModuleRules.Resolve(Module(true), null, Today));
        Assert.Equal(ModuleState.NotAvailable, CenterModuleRules.Resolve(Module(false), null, Today));
    }

    [Theory]
    [InlineData(-5, 5)]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 1)]
    public void DentroDeLaVigencia_EstaActivo_ConLosDosExtremosIncluidos(int fromDays, int untilDays) =>
        Assert.Equal(ModuleState.Active, CenterModuleRules.Resolve(Module(true), Change(true, Today.AddDays(fromDays), Today.AddDays(untilDays)), Today));

    [Theory]
    [InlineData(-10, -1)]
    [InlineData(1, 10)]
    public void FueraDeLaVigencia_PasaASoloLectura_SiGuardaDatosPropios_YANoDisponible_SiNo(int fromDays, int untilDays)
    {
        var change = Change(true, Today.AddDays(fromDays), Today.AddDays(untilDays));

        Assert.Equal(ModuleState.ReadOnly, CenterModuleRules.Resolve(Module(true), change, Today));
        Assert.Equal(ModuleState.NotAvailable, CenterModuleRules.Resolve(Module(false), change, Today));
    }

    [Fact]
    public void UnaBaja_ApagaElModulo_AunqueLaVigenciaSiguieraAbierta()
    {
        Assert.Equal(ModuleState.ReadOnly, CenterModuleRules.Resolve(Module(true), Change(false), Today));
        Assert.Equal(ModuleState.NotAvailable, CenterModuleRules.Resolve(Module(false), Change(false), Today));
    }

    private static NewCenterModuleChange Enable(
        string? module = CenterModules.DictadoVoz, string? annex = "ANX-2026-A", string? version = "1", string? reference = "SOL-1", bool conditions = true,
        DateOnly? from = null, DateOnly? until = null, DateTimeOffset? received = null, ModuleRequestChannel? channel = ModuleRequestChannel.Correo, CenterId? center = null) =>
        NewCenterModuleChangeRules.Validate(
            center ?? Center, PlatformId, module, true, from ?? Today, until ?? Today.AddDays(30), annex, version, conditions, null, received ?? Now.AddHours(-1), channel, reference, Now);

    [Fact]
    public void ActivarGuardaLaFichaYLasCondicionesDelCatalogo_NoLasQueDiceQuienOpera()
    {
        var change = Enable(reference: "  SOL-1  ");

        Assert.True(change.Enable);
        Assert.Equal("SOL-1", change.RequestReference);
        Assert.Equal(CenterModules.All[0].PrivacySheetVersion, change.PrivacySheetVersion);
        Assert.Equal(string.Join(',', CenterModules.All[0].PrivacyConditions), change.ConditionCodes);
    }

    [Theory]
    [InlineData("Ana Pérez")]
    [InlineData("solicitud de Ana")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("0123456789012345678901234567890123456789X")]
    public void LasReferenciasSonCodigosOpacos_NuncaNombresNiTextoLibre(string? reference)
    {
        Assert.Throws<DomainValidationException>(() => Enable(reference: reference));
        Assert.Throws<DomainValidationException>(() => Enable(annex: reference));
    }

    [Fact]
    public void ActivarExigeVigenciaCoherente_CondicionesComprobadas_Y_UnaSolicitudYaRecibida()
    {
        Assert.Throws<DomainValidationException>(() => Enable(conditions: false));
        Assert.Throws<DomainValidationException>(() => Enable(from: Today, until: Today.AddDays(-1)));
        Assert.Throws<DomainValidationException>(() => Enable(received: Now.AddMinutes(1)));
        Assert.Throws<DomainValidationException>(() => Enable(channel: null));
        Assert.Throws<DomainValidationException>(() => Enable(module: "OTRO"));
        Assert.Throws<DomainValidationException>(() => Enable(center: CenterId.From(PlatformId)));
        Assert.Throws<DomainValidationException>(() => Enable(version: "12345678901234567"));
        Assert.Throws<DomainValidationException>(() => Enable(from: Today.AddDays(1), until: Today.AddDays(30)));
        Assert.Equal("1234567890123456", Enable(version: "1234567890123456").AnnexVersion);
    }

    [Fact]
    public void LaReferenciaAdmiteComoMaximo40Caracteres_YNingunSaltoDeLinea()
    {
        Assert.Equal(new string('A', 40), Enable(reference: new string('A', 40)).RequestReference);
        Assert.Throws<DomainValidationException>(() => Enable(reference: new string('A', 41)));
        Assert.Throws<DomainValidationException>(() => Enable(reference: "SOL\n1"));
    }

    [Fact]
    public void ApagarExigeMotivoDeLaListaCerrada_YNoLlevaNadaContractual()
    {
        var change = NewCenterModuleChangeRules.Validate(
            Center, PlatformId, CenterModules.DictadoVoz, false, Today, Today, "ANX", "1", true, ModuleDisableReason.PeticionCentro, Now.AddHours(-1), ModuleRequestChannel.Telefono, "SOL-2", Now);

        Assert.False(change.Enable);
        Assert.Null(change.ValidFrom);
        Assert.Null(change.AnnexReference);
        Assert.Null(change.ConditionCodes);
        Assert.Equal(ModuleDisableReason.PeticionCentro, change.DisableReason);
        Assert.Throws<DomainValidationException>(() => NewCenterModuleChangeRules.Validate(
            Center, PlatformId, CenterModules.DictadoVoz, false, null, null, null, null, false, null, Now.AddHours(-1), ModuleRequestChannel.Telefono, "SOL-2", Now));
    }

    [Fact]
    public void LosCodigosDeLasListasCerradas_CoincidenConLosDelScript0098()
    {
        Assert.Equal(["CORREO", "TELEFONO", "TICKET", "OTRO"], Enum.GetValues<ModuleRequestChannel>().Select(c => c.ToCode()));
        Assert.Equal(["PETICION_CENTRO", "FIN_CONTRATO", "IMPAGO", "OTRO"], Enum.GetValues<ModuleDisableReason>().Select(c => c.ToCode()));
    }
}

/// <summary>Los casos de uso que exigen el módulo, con un módulo que sí guarda datos propios (el único real, el dictado, no los guarda y nunca llega a solo lectura).</summary>
public class CenterModuleAccessTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static readonly ResidApp.Domain.Modules.CenterModule WithData = new(
        "CON_DATOS", "Módulo con datos propios", [], "1", "docs/privacidad/x.md", ["C"], HasOwnData: true, UsesThirdParty: false, GlobalCut: "n/a");

    private sealed class Repository(ResidApp.Domain.Modules.CenterModuleChange? last) : ResidApp.Application.Ports.ICenterModuleRepository
    {
        public Task<ResidApp.Domain.Modules.CenterModuleChange?> FindLastAsync(CenterId centerId, string moduleCode, CancellationToken ct = default) => Task.FromResult(last);

        public Task AppendAsync(ResidApp.Application.Ports.PlatformAccess access, ResidApp.Domain.Modules.NewCenterModuleChange change, DateOnly today, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class Settings : ResidApp.Application.Ports.IModuleSettings
    {
        public bool ActivateAll => false;

        public bool IsThirdPartyConfigured(string moduleCode) => true;
    }

    private static ResidApp.Application.UseCases.CenterModuleAccess Access(ResidApp.Domain.Modules.CenterModuleChange? last) =>
        new(new Repository(last), new Settings(), null!, null!, TimeProvider.System, [WithData]);

    [Fact]
    public async Task EnSoloLectura_ConsultarPasa_YCrearOEditarLanzaMODULE_READ_ONLY()
    {
        var access = Access(new ResidApp.Domain.Modules.CenterModuleChange(1, true, Today.AddDays(-10), Today.AddDays(-1)));
        var center = CenterId.From(Guid.NewGuid());

        await access.RequireReadableAsync(center, "CON_DATOS");
        var error = await Assert.ThrowsAsync<DomainStateException>(() => access.RequireWritableAsync(center, "CON_DATOS"));

        Assert.Equal(ResidApp.Application.UseCases.CenterModuleAccess.ReadOnly, error.Message);
    }

    [Fact]
    public async Task SinElModulo_NiConsultarNiEscribir_LanzanMODULE_NOT_AVAILABLE()
    {
        var access = Access(null);
        var center = CenterId.From(Guid.NewGuid());

        var read = await Assert.ThrowsAsync<DomainStateException>(() => access.RequireReadableAsync(center, "CON_DATOS"));
        var write = await Assert.ThrowsAsync<DomainStateException>(() => access.RequireWritableAsync(center, "CON_DATOS"));

        Assert.Equal(ResidApp.Application.UseCases.CenterModuleAccess.NotAvailable, read.Message);
        Assert.Equal(ResidApp.Application.UseCases.CenterModuleAccess.NotAvailable, write.Message);
    }
}
