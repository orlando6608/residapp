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
        string? module = CenterModules.DictadoVoz, string? annex = "ANX-2026-A", string? version = "1", bool conditions = true,
        DateOnly? from = null, DateOnly? until = null, DateTimeOffset? received = null, ModuleRequestChannel? channel = ModuleRequestChannel.Correo, CenterId? center = null) =>
        NewCenterModuleChangeRules.Validate(
            center ?? Center, PlatformId, module, true, from ?? Today, until ?? Today.AddDays(30), annex, version, conditions, null, received ?? Now.AddHours(-1), channel, Now);

    [Fact]
    public void ActivarGuardaLaFichaYLasCondicionesDelCatalogo_NoLasQueDiceQuienOpera()
    {
        var change = Enable();

        Assert.True(change.Enable);
        Assert.Equal(CenterModules.All[0].PrivacySheetVersion, change.PrivacySheetVersion);
        Assert.Equal(string.Join(',', CenterModules.All[0].PrivacyConditions), change.ConditionCodes);
    }

    [Theory]
    [InlineData("Ana Pérez")]
    [InlineData("solicitud de Ana")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("0123456789012345678901234567890123456789X")]
    public void LasReferenciasDelAnexoSonCodigosOpacos_NuncaNombresNiTextoLibre(string? reference)
    {
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
        Assert.Throws<DomainValidationException>(() => Enable(from: Today.AddDays(-30), until: Today.AddDays(-1)));
        Assert.Throws<DomainValidationException>(() => Enable(received: new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.Throws<DomainValidationException>(() => Enable(received: Now.AddDays(-367)));
        Assert.Equal(Today, Enable(from: Today.AddDays(-30), until: Today).ValidUntil);
        Assert.Equal("1234567890123456", Enable(version: "1234567890123456").AnnexVersion);
    }

    [Fact]
    public void LaReferenciaDelAnexoAdmiteComoMaximo40Caracteres_YNingunSaltoDeLinea()
    {
        Assert.Equal(new string('A', 40), Enable(annex: new string('A', 40)).AnnexReference);
        Assert.Throws<DomainValidationException>(() => Enable(annex: new string('A', 41)));
        Assert.Throws<DomainValidationException>(() => Enable(annex: "ANX\n1"));
    }

    [Fact]
    public void LaReferenciaDeLaPeticionLaAsignaResidApp_ModuloYNumeroDelCambio_SinTexto()
    {
        Assert.Equal("DICTADO_VOZ-0001", NewCenterModuleChangeRules.RequestReferenceFor(CenterModules.DictadoVoz, 1));
        Assert.Equal("DICTADO_VOZ-0123", NewCenterModuleChangeRules.RequestReferenceFor(CenterModules.DictadoVoz, 123));
        Assert.True(NewCenterModuleChangeRules.RequestReferenceFor(CenterModules.DictadoVoz, 99999).Length <= 40);
    }

    [Fact]
    public void ApagarExigeMotivoDeLaListaCerrada_YNoLlevaNadaContractual()
    {
        var change = NewCenterModuleChangeRules.Validate(
            Center, PlatformId, CenterModules.DictadoVoz, false, Today, Today, "ANX", "1", true, ModuleDisableReason.PeticionCentro, Now.AddHours(-1), ModuleRequestChannel.Telefono, Now);

        Assert.False(change.Enable);
        Assert.Null(change.ValidFrom);
        Assert.Null(change.AnnexReference);
        Assert.Null(change.ConditionCodes);
        Assert.Equal(ModuleDisableReason.PeticionCentro, change.DisableReason);
        Assert.Throws<DomainValidationException>(() => NewCenterModuleChangeRules.Validate(
            Center, PlatformId, CenterModules.DictadoVoz, false, null, null, null, null, false, null, Now.AddHours(-1), ModuleRequestChannel.Telefono, Now));
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
    private static readonly DateOnly Today = CenterModuleRules.TodayInMadrid(DateTimeOffset.UtcNow);

    private static readonly ResidApp.Domain.Modules.CenterModule WithData = new(
        "CON_DATOS", "Módulo con datos propios", [], "1", "docs/privacidad/x.md", ["C"], HasOwnData: true, UsesThirdParty: false, GlobalCut: "n/a");

    private sealed class Repository(ResidApp.Domain.Modules.CenterModuleChange? last) : ResidApp.Application.Ports.ICenterModuleRepository
    {
        public Task<ResidApp.Domain.Modules.CenterModuleChange?> FindLastAsync(CenterId centerId, string moduleCode, CancellationToken ct = default) => Task.FromResult(last);

        public Task<IReadOnlyList<ResidApp.Application.Ports.PlatformModuleChange>> ListChangesAsync(ResidApp.Application.Ports.PlatformAccess access, CenterId centerId, CancellationToken ct = default) => throw new NotSupportedException();


        public Task<IReadOnlyList<ResidApp.Application.Ports.PlatformExpiringModule>> ListExpiringAsync(ResidApp.Application.Ports.PlatformAccess access, DateOnly from, DateOnly until, CancellationToken ct = default) => throw new NotSupportedException();


        public Task<string> AppendAsync(ResidApp.Application.Ports.PlatformAccess access, ResidApp.Domain.Modules.NewCenterModuleChange change, DateOnly today, CancellationToken ct = default) =>
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

/// <summary>El día de Madrid (decidido por Orlando, 2026-11-16): la vigencia cambia a medianoche en España, no en UTC.</summary>
public class MadridDayTests
{
    [Theory]
    [InlineData("2026-07-15T21:59:59Z", "2026-07-15")]
    [InlineData("2026-07-15T22:00:00Z", "2026-07-16")]
    [InlineData("2026-01-15T22:59:59Z", "2026-01-15")]
    [InlineData("2026-01-15T23:00:00Z", "2026-01-16")]
    [InlineData("2026-03-29T00:30:00Z", "2026-03-29")]
    [InlineData("2026-10-25T22:59:59Z", "2026-10-25")]
    [InlineData("2026-10-25T23:00:00Z", "2026-10-26")]
    public void ElDiaDeMadrid_CambiaALaMedianocheDeEspana_EnVeranoEInvierno(string utc, string madrid) =>
        Assert.Equal(DateOnly.Parse(madrid), ResidApp.Domain.Modules.CenterModuleRules.TodayInMadrid(DateTimeOffset.Parse(utc)));

    [Fact]
    public void ElUltimoDiaDeVigenciaSigueActivoHastaLasCeroHorasDeMadrid()
    {
        var module = new ResidApp.Domain.Modules.CenterModule("X", "X", [], "1", "docs/privacidad/x.md", ["C"], HasOwnData: false, UsesThirdParty: false, GlobalCut: "n/a");
        var change = new ResidApp.Domain.Modules.CenterModuleChange(1, true, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 15));

        // 23:59 en Madrid del 15 = 21:59 UTC; las 00:00 del 16 = 22:00 UTC.
        var beforeMidnight = ResidApp.Domain.Modules.CenterModuleRules.TodayInMadrid(DateTimeOffset.Parse("2026-07-15T21:59:59Z"));
        var afterMidnight = ResidApp.Domain.Modules.CenterModuleRules.TodayInMadrid(DateTimeOffset.Parse("2026-07-15T22:00:00Z"));

        Assert.Equal(ResidApp.Domain.Modules.ModuleState.Active, ResidApp.Domain.Modules.CenterModuleRules.Resolve(module, change, beforeMidnight));
        Assert.Equal(ResidApp.Domain.Modules.ModuleState.NotAvailable, ResidApp.Domain.Modules.CenterModuleRules.Resolve(module, change, afterMidnight));
    }

    [Fact]
    public void ActivarConVigenciaDeHoyEnMadrid_SeAcepta_AunqueEnUtcSeaElDiaAnterior()
    {
        // 00:30 del 16 en Madrid (verano) = 22:30 UTC del 15.
        var now = DateTimeOffset.Parse("2026-07-15T22:30:00Z");
        var change = ResidApp.Domain.Modules.NewCenterModuleChangeRules.Validate(
            CenterId.From(Guid.NewGuid()), Guid.NewGuid(), ResidApp.Domain.Modules.CenterModules.DictadoVoz, true, new DateOnly(2026, 7, 16), new DateOnly(2026, 8, 16), "ANX", "1", true, null,
            now.AddHours(-1), ResidApp.Domain.Modules.ModuleRequestChannel.Correo, now);

        Assert.Equal(new DateOnly(2026, 7, 16), change.ValidFrom);
        Assert.Throws<DomainValidationException>(() => ResidApp.Domain.Modules.NewCenterModuleChangeRules.Validate(
            CenterId.From(Guid.NewGuid()), Guid.NewGuid(), ResidApp.Domain.Modules.CenterModules.DictadoVoz, true, new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 15), "ANX", "1", true, null,
            now.AddHours(-1), ResidApp.Domain.Modules.ModuleRequestChannel.Correo, now));
    }

    [Fact]
    public void LaHoraDeLaSolicitud_Admite366DiasYAhoraMismo_PeroNoMas()
    {
        var now = DateTimeOffset.Parse("2026-11-16T10:00:00Z");
        ResidApp.Domain.Modules.NewCenterModuleChange Build(DateTimeOffset received) => ResidApp.Domain.Modules.NewCenterModuleChangeRules.Validate(
            CenterId.From(Guid.NewGuid()), Guid.NewGuid(), ResidApp.Domain.Modules.CenterModules.DictadoVoz, false, null, null, null, null, false,
            ResidApp.Domain.Modules.ModuleDisableReason.Otro, received, ResidApp.Domain.Modules.ModuleRequestChannel.Ticket, now);

        Assert.Equal(now, Build(now).RequestReceivedAt);
        Assert.Equal(now.AddDays(-366), Build(now.AddDays(-366)).RequestReceivedAt);
        Assert.Throws<DomainValidationException>(() => Build(now.AddDays(-366).AddSeconds(-1)));
        Assert.Throws<DomainValidationException>(() => Build(now.AddSeconds(1)));
    }
}
