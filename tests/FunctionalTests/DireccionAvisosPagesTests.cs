using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Enfermeria;
using ResidApp.Infrastructure.Persistence;
using ResidApp.Shared;

namespace ResidApp.FunctionalTests;

/// <summary>«Requiere atención» del Inicio de Dirección Clínica. Las cifras contra la base y su coincidencia con las listas se prueban en
/// la integración; aquí, la pantalla: textos, orden, enlaces, nada pendiente y fallo de lectura.</summary>
public class DireccionAvisosPagesTests(ResidentsFlowTests.WebAppFactory factory) : IClassFixture<ResidentsFlowTests.WebAppFactory>
{
    [Fact]
    public async Task SinEpisodios_DiceQueNoHayNadaPendiente()
    {
        var page = await StartPageAsync(factory);

        Assert.Contains("Requiere atención", page);
        Assert.Contains("No hay nada pendiente.", page);
        Assert.DoesNotContain("href=\"/Direccion/Pendientes?tipo=", page);
    }

    [Fact]
    public async Task ConPendientes_EnseñaLosCuatroAvisosEnSuOrden_CadaUnoConSuListaFiltrada()
    {
        var page = await StartPageAsync(With(inner => new FakeEpisodes(inner, unit =>
        [
            Urgent(unit),
            Priority(unit), Priority(unit),
            Overdue(unit), Overdue(unit), Overdue(unit),
            NotDone(unit), NotDone(unit), NotDone(unit), NotDone(unit),
            Episode(unit, ClinicalEventStatus.Pendiente),
        ])));

        string[] expected =
        [
            "href=\"/Direccion/Pendientes?tipo=ProtocoloUrgente\">1 episodio con protocolo urgente activo<",
            "href=\"/Direccion/Pendientes?tipo=PrioritarioSinValorar\">2 episodios prioritarios sin valorar<",
            "href=\"/Direccion/Pendientes?tipo=SeguimientoVencido\">3 episodios con el seguimiento pendiente (pasada su fecha prevista)<",
            "href=\"/Direccion/Pendientes?tipo=IndicacionConIncidencia\">4 episodios con alguna indicación no realizada<",
        ];
        var positions = expected.Select(text => page.IndexOf(text, StringComparison.Ordinal)).ToList();
        Assert.All(positions, position => Assert.True(position >= 0));
        Assert.Equal(positions.Order(), positions);
        Assert.DoesNotContain("No hay nada pendiente.", page);
    }

    [Theory]
    [InlineData("ProtocoloUrgente")]
    [InlineData("PrioritarioSinValorar")]
    [InlineData("SeguimientoVencido")]
    [InlineData("IndicacionConIncidencia")]
    public async Task ConUnSoloTipoDePendiente_SoloSaleSuLinea(string type)
    {
        var page = await StartPageAsync(With(inner => new FakeEpisodes(inner, unit =>
        [
            type switch
            {
                "ProtocoloUrgente" => Urgent(unit),
                "PrioritarioSinValorar" => Priority(unit),
                "SeguimientoVencido" => Overdue(unit),
                _ => NotDone(unit),
            },
        ])));

        var link = Assert.Single(Regex.Matches(page, "href=\"/Direccion/Pendientes\\?tipo=([A-Za-z]+)\""));
        Assert.Equal(type, link.Groups[1].Value);
        Assert.DoesNotContain("No hay nada pendiente.", page);
    }

    [Fact]
    public async Task ElFiltroNuevo_EstaEnLaListaDePendientes()
    {
        var client = await ClientAsync(factory);

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/Direccion/Pendientes?tipo=PrioritarioSinValorar"));

        Assert.Contains("<option value=\"PrioritarioSinValorar\"", page);
        Assert.Contains(">Prioritario sin valorar</option>", page);
    }

    [Fact]
    public async Task SiFallaLaLectura_NoDiceQueNoHayNadaPendiente_NiPintaCeros_NiQueNoHayUnidades()
    {
        var page = await StartPageAsync(With(inner => new FakeEpisodes(inner, _ => throw new InvalidOperationException("Fallo de prueba."))));

        Assert.Contains("Ahora mismo no se pueden mostrar las cifras de lo que requiere atención.", page);
        Assert.DoesNotContain("No hay nada pendiente.", page);
        Assert.Contains("Ahora mismo no se puede mostrar la cifra.", page);
        Assert.Contains("Ahora mismo no se pueden mostrar las cifras por unidad.", page);
        Assert.DoesNotContain("class=\"cifra\"", page);
        Assert.DoesNotContain("Tu ámbito de supervisión no tiene unidades.", page);
    }

    [Theory]
    // Hoy con la hora ya pasada: pendiente en la lista y en el detalle. Hoy sin hora: todavía no.
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task ElSeguimientoDeHoy_SeMarcaEnLaListaYEnElDetalle_SoloSiSuHoraYaPaso(bool withTime, bool marked)
    {
        SupervisionEpisode? seeded = null;
        var client = await ClientAsync(With(inner => new FakeEpisodes(inner, unit =>
        [
            seeded ??= Episode(unit, ClinicalEventStatus.EnSeguimiento) with
            {
                HasFollowUp = true, FollowUpDue = CenterTime.Today, FollowUpDueTime = withTime ? TimeOnly.MinValue : null,
            },
        ])));

        var list = WebUtility.HtmlDecode(await client.GetStringAsync("/Direccion/Pendientes"));
        var detail = WebUtility.HtmlDecode(await client.GetStringAsync($"/Direccion/Episodio?eventoId={seeded!.EventId}"));

        // La marca sale dos veces en la lista: en la tarjeta del móvil y en la tabla.
        Assert.Equal(marked ? 2 : 0, Regex.Matches(list, "<span class=\"badge text-bg-warning\">Seguimiento pendiente</span>").Count);
        Assert.Equal(marked, detail.Contains("<span class=\"badge text-bg-warning\">Pendiente</span>", StringComparison.Ordinal));
        Assert.Equal(withTime, detail.Contains(" a las 00:00", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ElResidenteSuspendido_CuentaIgual_YSaleMarcadoEnLaListaYEnElDetalle(bool suspended)
    {
        SupervisionEpisode? seeded = null;
        var client = await ClientAsync(With(inner => new FakeEpisodes(inner, unit =>
            [seeded ??= Priority(unit) with { ResidentSuspended = suspended }])));

        var start = WebUtility.HtmlDecode(await client.GetStringAsync("/Direccion"));
        var list = WebUtility.HtmlDecode(await client.GetStringAsync("/Direccion/Pendientes"));
        var detail = WebUtility.HtmlDecode(await client.GetStringAsync($"/Direccion/Episodio?eventoId={seeded!.EventId}"));

        const string Mark = "<span class=\"badge text-bg-warning\">Residente suspendido</span>";
        Assert.Contains("1 episodio prioritario sin valorar", start);
        Assert.Equal(suspended ? 2 : 0, Regex.Matches(list, Mark).Count);
        Assert.Equal(suspended ? 1 : 0, Regex.Matches(detail, Mark).Count);
    }

    private WebApplicationFactory<Program> With(Func<ISupervisionDirectory, ISupervisionDirectory> wrap) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddScoped(provider =>
            wrap(new SqlSupervisionDirectory(provider.GetRequiredService<SqlConnectionFactory>())))));

    private static async Task<HttpClient> ClientAsync(WebApplicationFactory<Program> app)
    {
        var (subject, _) = await SesionTests.SeedAccountAsync("DIRECCION_CLINICA");
        var client = await SesionTests.LoginAsync(app, subject);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/ProfileScope/Select")).StatusCode);
        return client;
    }

    private static async Task<string> StartPageAsync(WebApplicationFactory<Program> app) =>
        WebUtility.HtmlDecode(await (await ClientAsync(app)).GetStringAsync("/Direccion"));

    private static SupervisionEpisode Episode(
        UnitId unit, ClinicalEventStatus status, DailyChangeClassification classification = DailyChangeClassification.Ordinario) => new(
        Guid.NewGuid(), ResidentId.From(Guid.NewGuid()), "Residente de prueba", unit, "Unidad", ClinicalEventOrigin.EventoEnfermeria,
        classification, status, DateTimeOffset.UtcNow, false, false, null, false, 0, 0);

    private static SupervisionEpisode Urgent(UnitId unit) => Episode(unit, ClinicalEventStatus.EnSeguimiento) with { HasUrgentProtocol = true };

    private static SupervisionEpisode Priority(UnitId unit) => Episode(unit, ClinicalEventStatus.Pendiente, DailyChangeClassification.Prioritario);

    private static SupervisionEpisode Overdue(UnitId unit) =>
        Episode(unit, ClinicalEventStatus.EnSeguimiento) with { HasFollowUp = true, FollowUpDue = CenterTime.Today.AddDays(-2) };

    private static SupervisionEpisode NotDone(UnitId unit) => Episode(unit, ClinicalEventStatus.EnSeguimiento) with { IndicationsNotDone = 3 };

    /// <summary>La lectura real salvo los episodios abiertos, que se inventan en la primera unidad del ámbito.</summary>
    private sealed class FakeEpisodes(ISupervisionDirectory inner, Func<UnitId, IReadOnlyList<SupervisionEpisode>> episodes) : ISupervisionDirectory
    {
        public async Task<IReadOnlyList<SupervisionEpisode>> ListOpenEpisodesAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
            episodes((await inner.FindScopeAsync(profileScopeId, centerId, ct))!.Units[0].Id);

        public Task<SupervisionScopeInfo?> FindScopeAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
            inner.FindScopeAsync(profileScopeId, centerId, ct);

        public async Task<SupervisionEpisodeDetail?> FindEpisodeAsync(Guid profileScopeId, CenterId centerId, Guid eventId, CancellationToken ct = default) =>
            (await ListOpenEpisodesAsync(profileScopeId, centerId, ct))
            .Where(e => e.EventId == eventId).Select(e => new SupervisionEpisodeDetail(e, [])).SingleOrDefault();

        public Task<IReadOnlyList<SupervisionReferral>> ListReferralsAsync(
            Guid profileScopeId, CenterId centerId, DateTime? closedFrom = null, DateTime? closedToExclusive = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MilestoneFact>> ListMilestoneFactsAsync(
            Guid profileScopeId, CenterId centerId, DateTime from, DateTime toExclusive, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AuditDirectionViewAsync(Guid profileScopeId, CenterId centerId, string action, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TransferHistoryRequestPage> ListTransferHistoryRequestsAsync(
            Guid profileScopeId, CenterId centerId, DateTime from, DateTime toExclusive, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SupervisionIndicatorFacts> ListIndicatorFactsAsync(
            Guid profileScopeId, CenterId centerId, DateTime from, DateTime toExclusive, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task CloseExceptionallyAsync(
            Guid profileScopeId, CenterId centerId, AccountId accountId, Guid eventId, string reason, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
