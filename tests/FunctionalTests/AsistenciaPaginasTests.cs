using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Attendance;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Persistence;
using ResidApp.Shared;
using ResidApp.Web.Models;
using static ResidApp.FunctionalTests.ResidentsFlowTests;

namespace ResidApp.FunctionalTests;

/// <summary>
/// Asistencia diaria del Centro de Día (ADR 0019, script 0115), pantallas por HTTP y con SQL real: solo existe en un Centro de Día, está apagada hasta que Administración la activa con las dos
/// confirmaciones y, una vez activa, enseña la lista del día. Los casos de uso y el almacén tienen sus propias pruebas de integración.
/// </summary>
public class AsistenciaPaginasTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private const string Token = "__RequestVerificationToken";
    private const string PlatformCenterId = "5F3A1C00-0000-4000-8000-000000000001";

    private static SqlConnectionFactory Db => new(WebAppFactory.TestConnectionString);

    private static string ExtractValue(string html, string inputName) =>
        Regex.Match(html, $"name=\"{Regex.Escape(inputName)}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;

    private HttpClient NewClient() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<HttpClient> LoginPlatformAsync()
    {
        var accountId = Guid.NewGuid();
        var subject = $"asis-plat-{Guid.NewGuid():N}"[..28];
        using (var connection = await Db.OpenAsync())
        {
            await connection.ExecuteAsync($"""
                INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@accountId, @subject, 'ACTIVE', SYSUTCDATETIME());
                INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @accountId, '{PlatformCenterId}', 'PLATAFORMA', 'ACTIVE', SYSUTCDATETIME(), @accountId)
                """, new { accountId, subject });
        }

        return await LoginAsync(subject);
    }

    private async Task<HttpClient> LoginAsync(string subject)
    {
        var client = NewClient();
        Assert.Equal(HttpStatusCode.Redirect, (await SesionTests.PostLoginAsync(client, subject)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/ProfileScope/Select")).StatusCode);
        return client;
    }

    /// <summary>Crea un centro del tipo pedido por el alta real de Plataforma y entra con una Administración propia de ese centro.</summary>
    private Task<HttpClient> LoginAdminOfNewCenterAsync(string type) => LoginOfNewCenterAsync(type, "ADMINISTRACION");

    /// <summary>El centro que creó la última llamada a <see cref="LoginOfNewCenterAsync"/>.</summary>
    private Guid _centerId;

    private async Task<HttpClient> LoginOfNewCenterAsync(string type, string profile)
    {
        var platform = await LoginPlatformAsync();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var name = $"Centro Asistencia {suffix} (ficticio)";
        var page = await platform.GetStringAsync("/Plataforma/NuevoCentro");
        var response = await platform.PostAsync("/Plataforma/NuevoCentro", new FormUrlEncodedContent(
        [
            new(Token, ExtractValue(page, Token)), new("Form.OperacionId", Guid.NewGuid().ToString()), new("Form.NombreCentro", name), new("Form.NombreUnidad", "Sala 1"),
            new("Form.IdentificadorAdministrador", $"asis-admin-{suffix}"), new("Form.NombreAdministrador", "Admin Asistencia (ficticio)"),
            new("Form.TipoDeServicio", type), new("Form.ConfirmoTipo", "true"),
        ]));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var accountId = Guid.NewGuid();
        var subject = $"asis-adm-{Guid.NewGuid():N}"[..28];
        using (var connection = await Db.OpenAsync())
        {
            var centerId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.centros WITH (NOLOCK) WHERE nombre_visible = @name", new { name });
            Assert.NotEqual(Guid.Empty, centerId);
            _centerId = centerId;
            await connection.ExecuteAsync("""
                INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@accountId, @subject, 'ACTIVE', SYSUTCDATETIME());
                INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @accountId, @centerId, @profile, 'ACTIVE', SYSUTCDATETIME(), @accountId)
                """, new { accountId, subject, centerId, profile });
        }

        return await LoginAsync(subject);
    }

    [Fact]
    public async Task Una_Residencia_no_tiene_asistencia_y_vuelve_al_inicio_con_un_aviso()
    {
        var admin = await LoginAdminOfNewCenterAsync("Residencia");

        var response = await admin.GetAsync("/Asistencia");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
        Assert.DoesNotContain("href=\"/Asistencia\"", await admin.GetStringAsync("/Administracion"));
    }

    [Fact]
    public async Task En_un_Centro_de_Dia_la_asistencia_viene_apagada_y_Administracion_la_activa_con_las_dos_confirmaciones()
    {
        var admin = await LoginAdminOfNewCenterAsync("CentroDeDia");
        Assert.Contains("href=\"/Asistencia\"", await admin.GetStringAsync("/Administracion"));

        // Apagada: Administración va directa a los ajustes.
        var first = await admin.GetAsync("/Asistencia");
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal("/Asistencia/Ajustes", first.Headers.Location!.OriginalString);
        var settings = WebUtility.HtmlDecode(await admin.GetStringAsync("/Asistencia/Ajustes"));
        Assert.Contains("Ahora la asistencia está desactivada.", settings);
        Assert.Contains("Antes de activarla, confirma estas dos cosas", settings);

        // Sin las dos confirmaciones no se activa.
        var rejected = await admin.PostAsync("/Asistencia/Ajustes", new FormUrlEncodedContent(
            [new(Token, ExtractValue(settings, Token)), new("version", ExtractValue(settings, "version")), new("encender", "true"), new("informada", "true")]));
        Assert.Contains("tienes que marcar las dos confirmaciones", WebUtility.HtmlDecode(await rejected.Content.ReadAsStringAsync()));

        var activated = await admin.PostAsync("/Asistencia/Ajustes", new FormUrlEncodedContent(
            [new(Token, ExtractValue(settings, Token)), new("version", ExtractValue(settings, "version")), new("encender", "true"), new("informada", "true"), new("eipd", "true")]));
        Assert.Equal(HttpStatusCode.Redirect, activated.StatusCode);
        Assert.Contains("Ahora la asistencia está activada.", WebUtility.HtmlDecode(await admin.GetStringAsync("/Asistencia/Ajustes")));

        // Activa: la lista del día se abre; Administración la lee pero no ve botones para anotar.
        var list = WebUtility.HtmlDecode(await admin.GetStringAsync("/Asistencia"));
        Assert.Contains("Asistencia del", list);
        Assert.DoesNotContain("name=\"estado\"", list);
        Assert.Contains("Ausencias programadas", WebUtility.HtmlDecode(await admin.GetStringAsync("/Asistencia/Ausencias")));

        // La lista de seguimiento: solo una consulta, con el texto fijo que dice lo que no es, sin caché y sin contador ni lenguaje de riesgo.
        Assert.Contains("href=\"/Asistencia/Seguimiento\"", list);
        var followUp = await admin.GetAsync("/Asistencia/Seguimiento");
        Assert.Equal(HttpStatusCode.OK, followUp.StatusCode);
        Assert.Contains("no-store", followUp.Headers.CacheControl!.ToString());
        var followUpPage = WebUtility.HtmlDecode(await followUp.Content.ReadAsStringAsync());
        Assert.Contains("Esto no es un aviso de seguridad.", followUpPage);
        Assert.Contains("Esta pantalla no se imprime.", followUpPage);
        Assert.Contains("Nadie lleva 3 o más días seguidos", followUpPage);
        Assert.Contains("Todas las personas tienen pauta.", followUpPage);
        Assert.DoesNotContain("riesgo", followUpPage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task La_lista_de_seguimiento_pinta_la_fila_de_quien_lleva_tres_dias_sin_venir_con_sus_causas_y_sin_la_nota()
    {
        var admin = await LoginAdminOfNewCenterAsync("CentroDeDia");
        var today = CenterTime.TodayAt(DateTimeOffset.UtcNow);
        using (var connection = await Db.OpenAsync())
        {
            var (accountId, unitId) = await connection.QuerySingleAsync<(Guid, Guid)>("""
                SELECT (SELECT TOP (1) cuenta_id FROM dbo.ambitos_perfil WHERE centro_id = @centerId AND perfil_codigo = 'ADMINISTRACION'),
                       (SELECT TOP (1) id FROM dbo.unidades WHERE centro_id = @centerId)
                """, new { centerId = _centerId });
            var resident = (await new SqlResidentRepository(Db).CreateWithInitialLocationAsync(new CreateResidentInput(
                AccountId.From(accountId), SystemProfile.Administracion, CenterId.From(_centerId), UnitId.From(unitId), "Persona en seguimiento (ficticia)",
                new DateOnly(1940, 5, 20), DocumentedSexCode.NoConsta, null, null, null, null, null, Guid.NewGuid(), LocationPending: true))).ResidentId.Value;

            // La ficha es de hace tres días y en los tres consta «No vino».
            await connection.ExecuteAsync("UPDATE dbo.residentes SET creado_en = DATEADD(DAY, -3, creado_en) WHERE id = @resident", new { resident });
            foreach (var (back, cause, note) in new[] { (3, "AVISO_FAMILIA", null), (2, "SIN_AVISO", null), (1, "OTRO", "nota que no sale") })
            {
                await connection.ExecuteAsync("""
                    INSERT INTO dbo.asistencia_dia (id, centro_id, residente_id, fecha, numero, estado_codigo, causa_codigo, nota, anotado_por_cuenta_id, anotado_por_perfil, anotado_en)
                    VALUES (NEWID(), @centerId, @resident, @date, 1, 'NO_ASISTIO', @cause, @note, @accountId, 'ENFERMERIA', SYSUTCDATETIME())
                    """, new { centerId = _centerId, resident, date = today.AddDays(-back).ToDateTime(TimeOnly.MinValue), cause, note, accountId });
            }
        }

        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/Asistencia/Seguimiento"));

        var table = Regex.Match(page, "aria-labelledby=\"faltas-titulo\".*?</table>", RegexOptions.Singleline).Value;
        var cells = Regex.Matches(table, "<t[hd][^>]*>(.*?)</t[hd]>", RegexOptions.Singleline)
            .Select(cell => Regex.Replace(Regex.Replace(cell.Groups[1].Value, "<[^>]+>", " "), @"\s+", " ").Trim()).Skip(4).ToList();
        Assert.Equal(4, cells.Count);
        Assert.Equal(["Persona en seguimiento (ficticia) Sala 1", "3", "No consta en los últimos 60 días"], cells.Take(3));
        foreach (var cause in new[] { AttendanceCause.AvisoFamilia, AttendanceCause.SinAviso, AttendanceCause.Otro })
        {
            Assert.Contains(EnumDisplay.Label(cause), cells[3]);
        }

        Assert.Contains("No hay personas con días recientes sin anotar.", page);
        Assert.DoesNotContain("nota que no sale", page);
    }

    [Theory]
    [InlineData("AUXILIAR")]
    [InlineData("ENFERMERIA")]
    public async Task Auxiliar_y_Enfermeria_ven_la_asistencia_pero_no_la_lista_de_seguimiento_ni_los_ajustes(string profile)
    {
        var client = await LoginOfNewCenterAsync("CentroDeDia", profile);

        // Nunca activada: se lee la lista (vacía) con el aviso, sin botones para anotar.
        var list = WebUtility.HtmlDecode(await client.GetStringAsync("/Asistencia"));
        Assert.Contains("La asistencia está desactivada", list);
        Assert.DoesNotContain("name=\"estado\"", list);
        Assert.DoesNotContain("Lista de seguimiento", list);

        foreach (var forbidden in new[] { "/Asistencia/Seguimiento", "/Asistencia/Ajustes" })
        {
            var response = await client.GetAsync(forbidden);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/Asistencia", response.Headers.Location!.OriginalString);
        }
    }
}
