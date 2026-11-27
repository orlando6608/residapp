using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Persistence;
using ResidApp.Shared;
using ResidApp.Web.Models;

namespace ResidApp.FunctionalTests;

/// <summary>El «Panel de gestión» de Administración y los dos avisos nuevos de su Inicio, vistos en las páginas.</summary>
public class AdministracionPanelPagesTests(ResidentsFlowTests.WebAppFactory factory) : IClassFixture<ResidentsFlowTests.WebAppFactory>
{
    private static SqlConnectionFactory Connections => new(ResidentsFlowTests.WebAppFactory.TestConnectionString);

    private sealed record Seed(Guid AccountId, Guid CenterId, Guid UnitId, Guid ScopeId);

    // Con un único ámbito, abrir el selector lo deja elegido.
    private async Task<(HttpClient Client, Seed Seed)> EnterAsync(string profile = "ADMINISTRACION")
    {
        var (subject, accountId) = await SesionTests.SeedAccountAsync(profile);
        using var connection = await Connections.OpenAsync();
        var seed = await connection.QuerySingleAsync<Seed>("""
            SELECT ambito.cuenta_id AS AccountId, ambito.centro_id AS CenterId, unidad.unidad_id AS UnitId, ambito.id AS ScopeId
              FROM dbo.ambitos_perfil ambito JOIN dbo.ambitos_perfil_unidad unidad ON unidad.ambito_perfil_id = ambito.id
             WHERE ambito.cuenta_id = @accountId
            """, new { accountId });
        var client = await SesionTests.LoginAsync(factory, subject);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/ProfileScope/Select")).StatusCode);
        return (client, seed);
    }

    private static async Task<string> MainAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        return WebUtility.HtmlDecode(Regex.Match(body, "<main.*?</main>", RegexOptions.Singleline).Value);
    }

    private static async Task<ResidentId> CreateResidentAsync(Seed seed, string name, Guid? room = null, Guid? place = null) =>
        (await new SqlResidentRepository(Connections).CreateWithInitialLocationAsync(new CreateResidentInput(
            AccountId.From(seed.AccountId), SystemProfile.Administracion, CenterId.From(seed.CenterId), UnitId.From(seed.UnitId), name,
            new DateOnly(1940, 5, 20), DocumentedSexCode.NoConsta, null, null, null, room, place, Guid.NewGuid(), LocationPending: true))).ResidentId;

    [Fact]
    public async Task ElInicio_EnlazaAlPanel_YElPanelVacio_NoInventaCifras()
    {
        var (client, _) = await EnterAsync();

        var home = await MainAsync(client, "/");
        var administration = await MainAsync(client, "/Administracion");
        var panel = await MainAsync(client, "/Administracion/Panel");

        // La tarjeta está en Inicio, al lado de «Administración», y no se repite en la pantalla de Administración.
        Assert.Matches("(?s)href=\"/Administracion\".*href=\"/Administracion/Panel\"", home);
        Assert.DoesNotContain("href=\"/Administracion/Panel\"", administration);
        foreach (var title in new[]
                 {
                     "Panel de gestión", "Residentes activos", "Ocupación actual", "Ocupación por unidad y edificio", "Altas y bajas", "Citas con la familia",
                     "Comunicados a la familia", "Autorizaciones de familiares", "Datos agregados, no reidentificar.",
                 })
        {
            Assert.Contains(title, panel);
        }

        Assert.Contains("Sin plazas", panel);
        Assert.Contains("No hay datos en este periodo.", panel);
        Assert.DoesNotContain("Ahora mismo no se pued", panel);
    }

    [Theory]
    [InlineData("", "30 días", "Semana")]
    [InlineData("?periodo=7d", "7 días", "Día")]
    [InlineData("?periodo=12m", "12 meses", "Mes")]
    [InlineData("?periodo=otra-cosa", "30 días", "Semana")]
    public async Task ElPeriodoViajaEnLaDireccion(string query, string selected, string header)
    {
        var (client, seed) = await EnterAsync();
        await CreateResidentAsync(seed, "Para que haya un alta");

        var panel = await MainAsync(client, "/Administracion/Panel" + query);

        Assert.Matches($"aria-current=\"page\"[^>]*>{selected}</a>", panel);
        Assert.Contains($"<th scope=\"col\">{header}</th>", panel);
    }

    [Fact]
    public async Task ConDatos_DibujaEnElServidor_ConPuntoDecimal_YSinEstilosNiGuionesEnLinea()
    {
        var (client, seed) = await EnterAsync();
        var room = Guid.NewGuid();
        var bed = Guid.NewGuid();
        using (var connection = await Connections.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.habitaciones (id, centro_id, unidad_id, nombre_visible, creado_en, creado_por_cuenta_id)
                VALUES (@room, @CenterId, @UnitId, N'Habitación 1', SYSUTCDATETIME(), @AccountId);
                INSERT INTO dbo.plazas (id, centro_id, unidad_id, habitacion_id, nombre_visible, creado_en, creado_por_cuenta_id)
                VALUES (@bed, @CenterId, @UnitId, @room, N'Cama A', SYSUTCDATETIME(), @AccountId),
                       (NEWID(), @CenterId, @UnitId, @room, N'Cama B', SYSUTCDATETIME(), @AccountId),
                       (NEWID(), @CenterId, @UnitId, @room, N'Cama C', SYSUTCDATETIME(), @AccountId);
                """, new { room, bed, seed.CenterId, seed.UnitId, seed.AccountId });
        }

        await CreateResidentAsync(seed, "Residente del panel", room, bed);

        var panel = await MainAsync(client, "/Administracion/Panel?periodo=7d");

        Assert.Contains("33 % (1 de 3 plazas)", panel);
        Assert.Contains("stroke-dasharray=\"33 67\"", panel);
        Assert.Contains("width=\"33.33%\"", panel);
        Assert.Contains("Altas 1 · Bajas 0", panel);
        // El centro empieza hoy: no hay con qué comparar. Y se avisa de que una baja anulada sigue contando.
        Assert.Equal(2, Regex.Matches(panel, AdministrationPanelDisplay.NothingToCompare).Count);
        Assert.DoesNotContain("más que al empezar", panel);
        Assert.Contains("reactivación sigue contando.", panel);
        Assert.Contains("<table", panel);
        Assert.DoesNotContain("style=", panel);
        Assert.DoesNotContain("<script", panel);
        // Cifras de gestión: ningún nombre de residente.
        Assert.DoesNotContain("Residente del panel", panel);
    }

    [Fact]
    public async Task OtroPerfil_NoAbreElPanel()
    {
        var (client, _) = await EnterAsync("ENFERMERIA");

        var response = await client.GetAsync("/Administracion/Panel");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ElInicio_AvisaDeLasInvitacionesSinUsar_YDeLasBajasConElPlazoPorVencer_YSusEnlacesEnsenanLoMismo()
    {
        var (client, seed) = await EnterAsync();
        var resident = await CreateResidentAsync(seed, "Baja antigua");
        // Otra baja con el plazo lejano: no es un aviso.
        var recent = await CreateResidentAsync(seed, "Baja reciente");
        await DischargeAsync(seed, recent, monthsLeft: 180);
        var before = await MainAsync(client, "/Administracion");
        var invited = $"invitada-{Guid.NewGuid():N}"[..20];
        using (var connection = await Connections.OpenAsync())
        {
            await connection.ExecuteAsync("""
                DECLARE @now DATETIME2(3) = SYSUTCDATETIME(), @cuenta UNIQUEIDENTIFIER = NEWID(), @ambito UNIQUEIDENTIFIER = NEWID();
                INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@cuenta, @invited, 'ACTIVE', @now);
                INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
                VALUES (@ambito, @cuenta, @CenterId, 'ENFERMERIA', 'ACTIVE', @now, @AccountId);
                INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @ambito, @CenterId, @UnitId, @now, @AccountId);
                INSERT INTO dbo.invitaciones_acceso (id, cuenta_id, centro_id, tipo, resumen_token, emitida_en, emitida_por_cuenta_id, perfil_emisor, caduca_en)
                VALUES (NEWID(), @cuenta, @CenterId, 'ALTA', @token, DATEADD(DAY, -5, @now), @AccountId, 'ADMINISTRACION', DATEADD(DAY, -2, @now));
                """, new { invited, token = $"{Guid.NewGuid():N}{Guid.NewGuid():N}", seed.CenterId, seed.UnitId, seed.AccountId });
        }

        await DischargeAsync(seed, resident, monthsLeft: 6);

        var after = await MainAsync(client, "/Administracion");
        var accounts = await MainAsync(client, "/Administracion/Usuarios?invitacion=true");
        var discharged = await MainAsync(client, "/Administracion/Bajas");

        Assert.DoesNotContain("invitación de acceso sin usar", before);
        Assert.DoesNotContain("plazo de conservación vencido", before);
        Assert.Contains("1 cuenta con la invitación de acceso sin usar (1 caducada)", after);
        Assert.Contains("href=\"/Administracion/Usuarios?invitacion=true\"", after);
        Assert.Contains("1 baja con el plazo de conservación vencido o que vence en 12 meses o menos", after);
        Assert.DoesNotContain("No hay nada pendiente.", after);
        Assert.Contains("Solo las cuentas con la invitación de acceso sin usar.", accounts);
        Assert.Contains(invited, accounts);
        Assert.Contains("Invitación caducada el ", accounts);
        Assert.DoesNotContain("sesion-", accounts);
        Assert.Contains("Baja antigua", discharged);
    }

    /// <summary>Una baja con el plazo de conservación que se quiera: la aplicación siempre pone quince años.</summary>
    private static async Task DischargeAsync(Seed seed, ResidentId resident, int monthsLeft)
    {
        using var connection = await Connections.OpenAsync();
        await connection.ExecuteAsync("""
            DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
            UPDATE dbo.intervalos_ubicacion_residente SET vigente_hasta = DATEADD(SECOND, 1, @now) WHERE residente_id = @resident AND vigente_hasta IS NULL;
            UPDATE dbo.residentes SET estado = 'INACTIVE', motivo_inactivacion = N'Alta voluntaria', inactivado_en = @now,
                   inactivado_por_cuenta_id = @AccountId, inactivado_por_perfil = 'ADMINISTRACION' WHERE id = @resident;
            INSERT INTO dbo.bajas_residente (id, residente_id, centro_id, episodio_id, unidad_id, motivo_codigo, conservar_hasta, dada_por_cuenta_id, dada_en)
            SELECT NEWID(), @resident, @CenterId, episodio.id, @UnitId, 'ALTA_VOLUNTARIA', DATEADD(MONTH, @monthsLeft, CAST(@now AS DATE)), @AccountId, @now
              FROM dbo.episodios_residente_centro episodio WHERE episodio.residente_id = @resident;
            """, new { resident = resident.Value, monthsLeft, seed.CenterId, seed.UnitId, seed.AccountId });
    }

    [Fact]
    public async Task UnCentroDeDia_NoVeOcupacionNiPlazas()
    {
        var subject = $"panel-dia-{Guid.NewGuid():N}"[..22];
        using (var connection = await Connections.OpenAsync())
        {
            await connection.ExecuteAsync("""
                DECLARE @now DATETIME2(3) = SYSUTCDATETIME(), @account UNIQUEIDENTIFIER = NEWID(), @center UNIQUEIDENTIFIER = NEWID(),
                        @unit UNIQUEIDENTIFIER = NEWID(), @scope UNIQUEIDENTIFIER = NEWID();
                INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@account, @subject, 'ACTIVE', @now);
                INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en, tipo_de_servicio)
                VALUES (@center, @subject, N'Centro de prueba', 'ACTIVE', @now, 'CENTRO_DE_DIA');
                INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en) VALUES (@unit, @center, 'SALA', N'Sala', 'ACTIVE', @now);
                INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
                VALUES (@scope, @account, @center, 'ADMINISTRACION', 'ACTIVE', @now, @account);
                INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @scope, @center, @unit, @now, @account);
                """, new { subject });
        }

        var client = await SesionTests.LoginAsync(factory, subject);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/ProfileScope/Select")).StatusCode);

        var home = await MainAsync(client, "/");
        var panel = await MainAsync(client, "/Administracion/Panel");

        // La misma tarjeta en Inicio que en una Residencia, sin hablar de ocupación.
        Assert.Matches("(?s)href=\"/Administracion\".*href=\"/Administracion/Panel\"", home);
        Assert.Contains("Cifras y gráficos de altas y bajas", home);
        Assert.Contains("Residentes activos", panel);
        Assert.DoesNotContain("Ocupación", panel);
        Assert.DoesNotContain("plaza", panel);
    }

    [Fact]
    public async Task SiUnBloqueFalla_LaPaginaLoDice_YEnsenaLosDemas()
    {
        var failing = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<IAdministrationDashboardReader>(provider =>
                new MovementsFail(new SqlAdministrationDashboard(provider.GetRequiredService<SqlConnectionFactory>())))));
        var (subject, _) = await SesionTests.SeedAccountAsync("ADMINISTRACION");
        var client = await SesionTests.LoginAsync(failing, subject);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/ProfileScope/Select")).StatusCode);

        var panel = await MainAsync(client, "/Administracion/Panel");

        Assert.Contains("Ahora mismo no se pueden mostrar las cifras de este bloque.", panel);
        Assert.Contains("Ahora mismo no se puede mostrar.", panel);
        Assert.Contains("Residentes activos", panel);
        Assert.Contains("No hay plazas definidas en tus unidades", panel);
        Assert.DoesNotContain("altas en el periodo", panel);
    }

    private sealed class MovementsFail(IAdministrationDashboardReader inner) : IAdministrationDashboardReader
    {
        public Task<DashboardOccupancy> ReadOccupancyAsync(AccountAdministrationAccess access, DateTime beforeUtc, CancellationToken ct = default) =>
            inner.ReadOccupancyAsync(access, beforeUtc, ct);

        public Task<DashboardMovementFacts> ReadMovementsAsync(
            AccountAdministrationAccess access, DateOnly fromDate, DateTime fromUtc, CancellationToken ct = default) =>
            throw new InvalidOperationException("Fallo de prueba.");

        public Task<IReadOnlyList<DashboardAppointmentFact>> ReadAppointmentsAsync(
            AccountAdministrationAccess access, DateTime fromUtc, DateTime toUtcExclusive, CancellationToken ct = default) =>
            inner.ReadAppointmentsAsync(access, fromUtc, toUtcExclusive, ct);

        public Task<IReadOnlyList<DashboardLinkFact>> ReadAuthorizationsAsync(AccountAdministrationAccess access, CancellationToken ct = default) =>
            inner.ReadAuthorizationsAsync(access, ct);
    }

    [Fact]
    public void UnBloqueQueNoSePudoLeer_SeDice_YNoSePintaComoCero()
    {
        var period = DashboardPeriod.At(DashboardRange.SieteDias, DateTimeOffset.UtcNow, CenterTime.Madrid);

        var cards = AdministrationPanelDisplay.Cards(new AdministrationPanelViewModel(period, DateTime.Now, null, null, null, null, null, null));

        Assert.Equal(6, cards.Count);
        Assert.All(cards, card => Assert.Null(card.Value));
        Assert.False(new AdministracionInicioViewModel(0, 0, new AdministrationOverview(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), null, 0, null,
            OpenInvitations: null, RetentionDue: 0).NothingPending);
    }

    [Fact]
    public void LaVariacion_SoloSeDiceSiAlEmpezarHabiaAlgo()
    {
        var period = DashboardPeriod.At(DashboardRange.SieteDias, DateTimeOffset.UtcNow, CenterTime.Madrid);
        IReadOnlyList<string> Details(int residentsBefore, int occupiedBefore) => AdministrationPanelDisplay.Cards(new AdministrationPanelViewModel(
                period, DateTime.Now, new DashboardOccupancy([new(Guid.NewGuid(), "A", null, 5, 10, 5, residentsBefore, occupiedBefore)]),
                null, null, null, null, null))
            .Take(2).Select(card => card.Details[0]).ToList();
        var since = period.From.ToString("dd/MM/yyyy");

        Assert.Equal([AdministrationPanelDisplay.NothingToCompare, AdministrationPanelDisplay.NothingToCompare], Details(0, 0));
        Assert.Equal([$"2 más que al empezar el {since}", $"20 puntos más que al empezar el {since}, con las plazas de hoy"], Details(3, 3));
        Assert.Equal([$"1 menos que al empezar el {since}", $"1 punto menos que al empezar el {since}, con las plazas de hoy"],
            AdministrationPanelDisplay.Cards(new AdministrationPanelViewModel(period, DateTime.Now,
                    new DashboardOccupancy([new(Guid.NewGuid(), "A", null, 50, 100, 50, 51, 51)]), null, null, null, null, null))
                .Take(2).Select(card => card.Details[0]));
    }

    [Fact]
    public void LasBarras_AgrupadasSeComparanConElMayor_ApiladasSeSuman_YSinDatosSeAvisa()
    {
        var table = new ChartTable("t", "c", [], []);
        var grouped = new BarChart(["a", "b"], false, [new ChartRow("x", [2, 4], ""), new ChartRow("y", [1, 0], "")], table);
        var stacked = new BarChart(["a", "b"], true, [new ChartRow("x", [2, 4], ""), new ChartRow("y", [3, 0], "")], table);
        var empty = new BarChart(["a"], false, [new ChartRow("x", [0], "")], table);

        Assert.Equal([new BarSegment(0, 0, 50, 0), new BarSegment(1, 0, 100, 1)], grouped.Segments(grouped.Rows[0]));
        Assert.Equal([new BarSegment(0, 0, 25, 0)], grouped.Segments(grouped.Rows[1]));
        Assert.Equal(2, stacked.Segments(stacked.Rows[0]).Count);
        Assert.Equal(50, stacked.Segments(stacked.Rows[1])[0].Width);
        Assert.True(empty.Empty);
        Assert.Equal("12.5%", BarChart.Svg(12.5));
    }

    [Fact]
    public void LaOcupacionPorEdificio_VaPorNombre_ConElTotalDeLasUnidadesDelAmbito_YSinPorcentajeDondeNoHayPlazas()
    {
        var chart = AdministrationPanelDisplay.OccupancyByUnit(new DashboardOccupancy(
        [
            // La más llena va la última por nombre: el orden no es por cifras.
            new(Guid.NewGuid(), "Verde", "Norte", 9, 10, 9, 0, 0),
            new(Guid.NewGuid(), "Roja", null, 3, 0, 0, 0, 0),
            new(Guid.NewGuid(), "Azul", "Norte", 1, 10, 1, 0, 0),
            new(Guid.NewGuid(), "Cerrada", "Norte", 0, 0, 0, 4, 0, Active: false),
        ]));

        Assert.Equal(["Norte: total de tus unidades", "Azul", "Verde", "Roja"], chart.Rows.Select(r => r.Label));
        Assert.Equal("10 de 20 plazas (50 %) · 10 residentes", chart.Rows[0].Text);
        Assert.Equal("3 residentes (sin plazas definidas)", chart.Rows[^1].Text);
        Assert.Empty(chart.Segments(chart.Rows[^1]));
    }
}
