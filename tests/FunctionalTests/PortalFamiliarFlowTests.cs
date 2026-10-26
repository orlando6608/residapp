using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using ResidApp.Domain.Enfermeria;
using ResidApp.Infrastructure.Persistence;
using static ResidApp.FunctionalTests.ResidentsFlowTests;

namespace ResidApp.FunctionalTests;

/// <summary>Portal Familiar de punta a punta por HTTP (script 0052): Administración da el acceso y activa la autorización, la familia entra y lee lo
/// publicado, y no ve nada más. Con el usuario limitado (RESIDAPP_TEST_APP_CONNECTION_STRING) pasa por la seguridad por filas.</summary>
public class PortalFamiliarFlowTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private const string Observation = "Observación clínica confidencial del barrido.";

    private static string ExtractValue(string html, string inputName) =>
        Regex.Match(html, $"name=\"{inputName}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;

    private static async Task<string> BodyAsync(HttpResponseMessage response) => WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    private async Task<HttpClient> LoginAsync(string subject, bool followRedirects = true)
    {
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = followRedirects });
        (await SesionTests.PostLoginAsync(client, subject)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<string> PostAsync(HttpClient client, string url, string pageUrl, Dictionary<string, string> fields)
    {
        var page = await client.GetStringAsync(pageUrl);
        fields["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken");
        return WebUtility.HtmlDecode(await (await client.PostAsync(url, new FormUrlEncodedContent(fields))).Content.ReadAsStringAsync());
    }

    private sealed record Seed(string Admin, Guid ScopeId, Guid CenterId, Guid UnitId, string Nurse);

    private static async Task<Seed> SeedAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var adminId = Guid.NewGuid();
        var nurseId = Guid.NewGuid();
        var centerId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var adminScope = Guid.NewGuid();
        var nurseScope = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@adminId, @admin, 'ACTIVE', @now), (@nurseId, @nurse, 'ACTIVE', @now);
            INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en) VALUES (@centerId, @code, @code, 'ACTIVE', @now);
            INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en) VALUES (@unitId, @centerId, @unitCode, @unitCode, 'ACTIVE', @now);
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (@adminScope, @adminId, @centerId, 'ADMINISTRACION', 'ACTIVE', @now, @adminId),
                   (@nurseScope, @nurseId, @centerId, 'ENFERMERIA', 'ACTIVE', @now, @adminId);
            INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @adminScope, @centerId, @unitId, @now, @adminId), (NEWID(), @nurseScope, @centerId, @unitId, @now, @adminId);
            """, new
        {
            adminId, nurseId, admin = $"portal-adm-{suffix}", nurse = $"portal-enf-{suffix}", centerId, unitId, adminScope, nurseScope, now,
            code = $"PORTAL-CENTER-{suffix}", unitCode = $"PORTAL-UNIT-{suffix}",
        });
        return new Seed($"portal-adm-{suffix}", adminScope, centerId, unitId, $"portal-enf-{suffix}");
    }

    private static async Task<string> CreateResidentAsync(HttpClient admin, Seed seed, string name, string familyName)
    {
        var page = await admin.GetStringAsync("/Residents/Create");
        var created = await (await admin.PostAsync("/Residents/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken"), ["OperacionId"] = ExtractValue(page, "OperacionId"),
            ["AmbitoPerfilId"] = seed.ScopeId.ToString(), ["CentroId"] = seed.CenterId.ToString(), ["UnidadId"] = seed.UnitId.ToString(),
            ["NombreVisible"] = name, ["FechaNacimiento"] = "1938-02-20", ["SexoDocumentadoCodigo"] = "Hombre",
            ["Familiares[0].NombreVisible"] = familyName, ["Familiares[0].Relacion"] = "Hija", ["Familiares[0].Telefono"] = "600123456",
            ["Familiares[0].ContactoPrioritario"] = "true",
        }))).Content.ReadAsStringAsync();
        return Regex.Match(created, "Id del residente: <code>([0-9a-f-]{36})</code>").Groups[1].Value;
    }

    /// <summary>Un evento del residente registrado por Enfermería por la app, con un comunicado ya hecho a mano (las horas se eligen).</summary>
    private static async Task<Guid> AddCommunicationAsync(
        HttpClient nurse, string nurseSubject, string residentId, string text, DateTime preparedAt, DateTime scheduledAt)
    {
        var page = await nurse.GetStringAsync($"/Enfermeria/RegistrarEvento?residenteId={residentId}");
        (await nurse.PostAsync("/Enfermeria/RegistrarEvento", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken"), ["ResidenteId"] = residentId,
            ["OperacionId"] = ExtractValue(page, "OperacionId"), ["Observacion"] = Observation, ["Clasificacion"] = "Ordinario",
        }))).EnsureSuccessStatusCode();
        var id = Guid.NewGuid();
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.comunicaciones_familiares
                (id, evento_id, residente_id, centro_id, tipo_codigo, texto, preparado_por_cuenta_id, preparado_en, publicacion_programada_en)
            SELECT TOP (1) @id, ea.id, ea.residente_id, ea.centro_id, 'ORDINARIA', @text, (SELECT id FROM dbo.cuentas WHERE sujeto_externo = @nurseSubject), @preparedAt, @scheduledAt
              FROM dbo.eventos_asistenciales ea
             WHERE ea.residente_id = @residentId AND NOT EXISTS (SELECT 1 FROM dbo.comunicaciones_familiares cf WHERE cf.evento_id = ea.id)
             ORDER BY ea.recibido_en DESC
            """, new { id, text, preparedAt, scheduledAt, nurseSubject, residentId = Guid.Parse(residentId) });
        return id;
    }

    [Fact]
    public async Task Familia_DaAccesoAdministracion_LaFamiliaLeeLoPublicado_YNadaMas_ConLaAppEntera()
    {
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.Admin);
        var nurse = await LoginAsync(seed.Nurse);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Portal Funcional", "Madre Portal Funcional");
        var foreignResidentId = await CreateResidentAsync(admin, seed, "Residente Ajeno Barrido", "Otra Familia Funcional");
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        var linkId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.residentes_familiares WHERE residente_id = @residentId", new { residentId = Guid.Parse(residentId) });
        var foreignLinkId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.residentes_familiares WHERE residente_id = @residentId", new { residentId = Guid.Parse(foreignResidentId) });
        var now = DateTime.UtcNow;
        var published = await AddCommunicationAsync(nurse, seed.Nurse, residentId, "Hoy ha merendado bien y ha paseado por el jardín.", now.AddHours(-30), now.AddHours(-5));
        await AddCommunicationAsync(nurse, seed.Nurse, residentId, "Esto aún no se ha publicado.", now.AddHours(-1), now.AddHours(20));
        // Uno publicado dentro del último periodo cerrado del resumen semanal (el centro está en el horario de CJ: semanal, corte el domingo a las 19:00).
        var summaryEnd = FamilyCommunicationSchedule.LastClosedSummaryPeriod(
            FamilyCommunicationSummaryFrequency.Semanal, FamilyCommunicationScheduleSettings.Default.PublicationTime, TimeZoneInfo.Local, DateTimeOffset.UtcNow).End;
        await AddCommunicationAsync(nurse, seed.Nurse, residentId, "Contado en el resumen de la semana.", summaryEnd.UtcDateTime.AddHours(-30), summaryEnd.UtcDateTime.AddHours(-2));
        var foreign = await AddCommunicationAsync(nurse, seed.Nurse, foreignResidentId, "Comunicado de un residente ajeno.", now.AddHours(-30), now.AddHours(-5));

        var authorizationUrl = $"/Administracion/AutorizacionFamiliar?residenteId={residentId}&vinculoId={linkId}";
        var subject = $"familia-{Guid.NewGuid():N}"[..20];
        async Task<string> ChangeAsync(string change, int expected) => await PostAsync(admin, "/Administracion/AutorizacionFamiliar", authorizationUrl, new()
        {
            ["Form.ResidenteId"] = residentId, ["Form.VinculoId"] = linkId.ToString(), ["Form.CambiosEsperados"] = expected.ToString(), ["Form.Cambio"] = change,
        });

        var granted = await PostAsync(admin, "/Administracion/AccesoPortalFamiliar", authorizationUrl, new()
        {
            ["residenteId"] = residentId, ["vinculoId"] = linkId.ToString(), ["usuario"] = subject,
        });
        var withoutAuthorization = await (await LoginAsync(subject)).GetAsync("/Familia");
        var opened = await ChangeAsync("Abrir", 0);
        var pending = await BodyAsync(await (await LoginAsync(subject)).GetAsync("/Familia"));
        var activated = await ChangeAsync("Activar", 1);

        var family = await LoginAsync(subject);
        // Entrar ya abre su único residente (primera visita); lo publicado después es nuevo en la siguiente.
        await AddCommunicationAsync(nurse, seed.Nurse, residentId, "Publicada después de entrar.", now.AddHours(-2), DateTime.UtcNow);
        var home = await BodyAsync(await family.GetAsync("/Familia"));
        var list = await BodyAsync(await family.GetAsync($"/Familia/Residente/{linkId}"));
        var detail = await BodyAsync(await family.GetAsync($"/Familia/Residente/{linkId}/Comunicado/{published}"));
        var foreignLink = await family.GetAsync($"/Familia/Residente/{foreignLinkId}");
        var foreignDetail = await family.GetAsync($"/Familia/Residente/{linkId}/Comunicado/{foreign}");
        var staffScreen = await family.GetAsync("/Enfermeria");
        var users = await BodyAsync(await admin.GetAsync("/Administracion/Usuarios"));
        var revoked = await PostAsync(admin, "/Administracion/AutorizacionFamiliar", authorizationUrl, new()
        {
            ["Form.ResidenteId"] = residentId, ["Form.VinculoId"] = linkId.ToString(), ["Form.CambiosEsperados"] = "2", ["Form.Cambio"] = "Revocar",
            ["Form.Motivo"] = "Deja de ser familiar autorizado (prueba funcional).",
        });
        var afterRevocation = await BodyAsync(await family.GetAsync("/Familia"));
        var detailAfterRevocation = await family.GetAsync($"/Familia/Residente/{linkId}/Comunicado/{published}");

        Assert.Contains("Acceso al Portal Familiar creado.", granted);
        Assert.Contains("Ahora no tienes acceso a ningún residente", WebUtility.HtmlDecode(await withoutAuthorization.Content.ReadAsStringAsync()));
        Assert.Contains("Autorización abierta", opened);
        Assert.Contains("Ahora no tienes acceso a ningún residente", pending);
        Assert.Contains("Autorización activada.", activated);
        Assert.Contains("Residente Portal Funcional", home);
        // Lo publicado desde la visita anterior es nuevo; en la siguiente visita ya no (CJ, tercera 6.3).
        Assert.Contains("badge text-bg-primary\">Nueva</span>", home);
        Assert.Contains("Hay 1 novedad nueva desde tu última visita.", home);
        Assert.DoesNotContain(">Nueva</span>", list);
        Assert.DoesNotContain("novedad nueva", list);
        // Al imprimir, el portal lleva la marca del centro arriba y ResidApp al pie, como el resto de la app (guía de diseño, sección 4).
        var centerName = WebUtility.HtmlDecode(Regex.Replace(
            Regex.Match(home, "<a class=\"navbar-brand[^>]*>(.*?)</a>", RegexOptions.Singleline).Groups[1].Value, "<[^>]+>", "")).Replace("· Familias", "").Trim();
        var printHeader = Regex.Match(home, "impresion-marca.*?impresion-pie", RegexOptions.Singleline).Value;
        Assert.NotEmpty(centerName);
        Assert.Contains("impresion-pagina", home);
        Assert.Contains($"<strong>{WebUtility.HtmlEncode(centerName)}</strong>", printHeader);
        Assert.Contains("Generado con ResidApp", home);
        Assert.Contains("Hoy ha merendado bien y ha paseado por el jardín.", list);
        Assert.Contains("Equipo asistencial del centro", list);
        Assert.DoesNotContain("Esto aún no se ha publicado.", list);
        // El resumen semanal (CJ, tercera 1.1 a 1.5) recopila lo ya publicado en el último periodo cerrado y se suma a los sueltos: sale dos veces.
        Assert.Contains("Resumen semanal", list);
        Assert.Equal(2, Regex.Matches(list, "Contado en el resumen de la semana\\.").Count);
        Assert.DoesNotContain("Resumen diario", list);
        Assert.DoesNotContain("Publicada después de entrar.", Regex.Match(list, "id=\"resumen-titulo\".*?</section>", RegexOptions.Singleline).Value);
        Assert.Contains("Hoy ha merendado bien y ha paseado por el jardín.", detail);
        Assert.Contains("Este portal no es un canal urgente", detail);
        // Nada del evento ni del personal, ni de otros residentes o familiares.
        foreach (var page in new[] { home, list, detail })
        {
            Assert.DoesNotContain(Observation, page);
            Assert.DoesNotContain(seed.Nurse, page);
            Assert.DoesNotContain(subject, page);
            Assert.DoesNotContain("Residente Ajeno Barrido", page);
            Assert.DoesNotContain("Otra Familia Funcional", page);
            Assert.DoesNotContain("600123456", page);
            Assert.DoesNotContain("Documentos de CJ", page);
        }

        Assert.Equal(HttpStatusCode.Forbidden, foreignLink.StatusCode);
        Assert.Contains("No tienes acceso a esto", WebUtility.HtmlDecode(await foreignLink.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.Forbidden, foreignDetail.StatusCode);
        Assert.DoesNotContain("Comunicado de un residente ajeno.", WebUtility.HtmlDecode(await foreignDetail.Content.ReadAsStringAsync()));
        Assert.DoesNotContain(Observation, WebUtility.HtmlDecode(await staffScreen.Content.ReadAsStringAsync()));
        Assert.DoesNotContain(subject, users);
        Assert.Contains("Autorización revocada.", revoked);
        Assert.Contains("Ahora no tienes acceso a ningún residente", afterRevocation);
        Assert.Equal(HttpStatusCode.Forbidden, detailAfterRevocation.StatusCode);
        // Entrar (que ya lleva a su único residente), la portada, el historial y el detalle: cuatro lecturas; las denegadas no dejan rastro.
        Assert.Equal(4, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE residente_id = @residentId AND perfil_activo = 'FAMILIAR' AND accion_codigo = 'FAMILY_PORTAL_READ'",
            new { residentId = Guid.Parse(residentId) }));
    }

    [Fact]
    public async Task Correctora_YRetirada_LaFamiliaVeLaCorreccionJuntoAlOriginal_YDespuesQueYaNoEstaDisponible_ConLaAppEntera()
    {
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.Admin);
        var nurse = await LoginAsync(seed.Nurse);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Correctora Funcional", "Madre Correctora Funcional");
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        var linkId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.residentes_familiares WHERE residente_id = @residentId", new { residentId = Guid.Parse(residentId) });
        var now = DateTime.UtcNow;
        var published = await AddCommunicationAsync(nurse, seed.Nurse, residentId, "Ha tenido fiebre esta tarde.", now.AddHours(-30), now.AddHours(-5));
        var authorizationUrl = $"/Administracion/AutorizacionFamiliar?residenteId={residentId}&vinculoId={linkId}";
        var subject = $"familia-{Guid.NewGuid():N}"[..20];
        await PostAsync(admin, "/Administracion/AccesoPortalFamiliar", authorizationUrl, new() { ["residenteId"] = residentId, ["vinculoId"] = linkId.ToString(), ["usuario"] = subject });
        foreach (var (change, expected) in new[] { ("Abrir", 0), ("Activar", 1) })
        {
            await PostAsync(admin, "/Administracion/AutorizacionFamiliar", authorizationUrl, new()
            {
                ["Form.ResidenteId"] = residentId, ["Form.VinculoId"] = linkId.ToString(), ["Form.CambiosEsperados"] = expected.ToString(), ["Form.Cambio"] = change,
            });
        }

        var family = await LoginAsync(subject);
        var staffList = await BodyAsync(await admin.GetAsync("/Administracion/Comunicados"));
        var nurseForm = await nurse.GetAsync($"/Administracion/PublicarCorreccion?comunicadoId={published}");
        var invalid = await PostAsync(admin, "/Administracion/PublicarCorreccion", $"/Administracion/PublicarCorreccion?comunicadoId={published}", new()
        {
            ["Form.ComunicadoId"] = published.ToString(), ["Form.Tipo"] = "Relevante", ["Form.Texto"] = "   ",
        });
        var corrected = await PostAsync(admin, "/Administracion/PublicarCorreccion", $"/Administracion/PublicarCorreccion?comunicadoId={published}", new()
        {
            ["Form.ComunicadoId"] = published.ToString(), ["Form.Tipo"] = "Relevante", ["Form.Texto"] = "Corrección: fue febrícula, no fiebre.",
        });
        var correctedList = await BodyAsync(await family.GetAsync($"/Familia/Residente/{linkId}"));
        var originalDetail = await BodyAsync(await family.GetAsync($"/Familia/Residente/{linkId}/Comunicado/{published}"));
        var correctiveId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.comunicaciones_familiares_correctoras WHERE comunicacion_id = @published", new { published });
        var correctiveDetail = await BodyAsync(await family.GetAsync($"/Familia/Residente/{linkId}/Comunicado/{correctiveId}"));
        var staffAfterCorrection = await BodyAsync(await admin.GetAsync("/Administracion/Comunicados"));
        var withoutReason = await PostAsync(admin, "/Administracion/RetirarComunicado", $"/Administracion/RetirarComunicado?comunicadoId={published}", new()
        {
            ["Form.ComunicadoId"] = published.ToString(), ["Form.Motivo"] = "",
        });
        var withdrawn = await PostAsync(admin, "/Administracion/RetirarComunicado", $"/Administracion/RetirarComunicado?comunicadoId={published}", new()
        {
            ["Form.ComunicadoId"] = published.ToString(), ["Form.Motivo"] = "Motivo interno de la retirada.",
        });
        var withdrawnList = await BodyAsync(await family.GetAsync($"/Familia/Residente/{linkId}"));
        var withdrawnDetail = await BodyAsync(await family.GetAsync($"/Familia/Residente/{linkId}/Comunicado/{published}"));
        var correctiveAfterWithdrawal = await family.GetAsync($"/Familia/Residente/{linkId}/Comunicado/{correctiveId}");
        var staffAfterWithdrawal = await BodyAsync(await admin.GetAsync("/Administracion/Comunicados"));
        var formAfterWithdrawal = await BodyAsync(await admin.GetAsync($"/Administracion/RetirarComunicado?comunicadoId={published}"));

        Assert.Contains("Publicar una corrección", staffList);
        Assert.Contains("Retirar", staffList);
        Assert.Contains("No se puede acceder", WebUtility.HtmlDecode(await nurseForm.Content.ReadAsStringAsync()) + nurseForm.StatusCode);
        Assert.Contains("Escribe el texto de la corrección.", invalid);
        Assert.Contains("Corrección publicada.", corrected);
        Assert.Contains("Corrección publicada", staffAfterCorrection);
        Assert.Contains("Corrección: fue febrícula, no fiebre.", correctedList);
        Assert.Contains("Ha tenido fiebre esta tarde.", correctedList);
        Assert.Contains("Tiene una corrección", correctedList);
        Assert.Contains("Corrige la novedad publicada el", correctedList);
        Assert.Contains("Esta novedad tiene una corrección", originalDetail);
        Assert.Contains("Ver la novedad original", correctiveDetail);
        Assert.Contains("Escribe el motivo de la retirada.", withoutReason);
        Assert.Contains("Comunicado retirado.", withdrawn);
        Assert.Contains("Esta novedad ya no está disponible", withdrawnList);
        Assert.Contains("Esta novedad ya no está disponible", withdrawnDetail);
        foreach (var page in new[] { withdrawnList, withdrawnDetail })
        {
            Assert.DoesNotContain("Ha tenido fiebre esta tarde.", page);
            Assert.DoesNotContain("Corrección: fue febrícula, no fiebre.", page);
            Assert.DoesNotContain("Motivo interno de la retirada.", page);
        }

        Assert.Equal(HttpStatusCode.Forbidden, correctiveAfterWithdrawal.StatusCode);
        Assert.Contains("Motivo interno de la retirada.", staffAfterWithdrawal);
        Assert.DoesNotContain("Publicar una corrección", staffAfterWithdrawal);
        Assert.Contains("Ese comunicado no se puede retirar", formAfterWithdrawal);
    }

    [Fact]
    public async Task Citas_AdministracionConfigura_LaFamiliaReservaSinDuplicar_AdministracionCancela_ConLaAppEntera()
    {
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.Admin);
        var nurse = await LoginAsync(seed.Nurse);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Citas Funcional", "Madre Citas Funcional");
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        var linkId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.residentes_familiares WHERE residente_id = @residentId", new { residentId = Guid.Parse(residentId) });
        var authorizationUrl = $"/Administracion/AutorizacionFamiliar?residenteId={residentId}&vinculoId={linkId}";
        var subject = $"familia-{Guid.NewGuid():N}"[..20];
        await PostAsync(admin, "/Administracion/AccesoPortalFamiliar", authorizationUrl, new() { ["residenteId"] = residentId, ["vinculoId"] = linkId.ToString(), ["usuario"] = subject });
        foreach (var (change, expected) in new[] { ("Abrir", 0), ("Activar", 1) })
        {
            await PostAsync(admin, "/Administracion/AutorizacionFamiliar", authorizationUrl, new()
            {
                ["Form.ResidenteId"] = residentId, ["Form.VinculoId"] = linkId.ToString(), ["Form.CambiosEsperados"] = expected.ToString(), ["Form.Cambio"] = change,
            });
        }

        var family = await LoginAsync(subject);
        var citasUrl = $"/Familia/Residente/{linkId}/Citas";

        // Con el servicio sin configurar, la familia no ve formularios.
        var beforeConfiguration = await BodyAsync(await family.GetAsync(citasUrl));
        var newBefore = await BodyAsync(await family.GetAsync($"{citasUrl}/Nueva"));

        // Administración configura: reserva directa, solo Presencial para el equipo médico, y una franja todos los días.
        var saved = await PostAsync(admin, "/Administracion/Citas", "/Administracion/Citas", new()
        {
            ["Servicio.Version"] = "0", ["Servicio.Modo"] = "ReservaDirecta", ["Servicio.AntelacionHoras"] = "1", ["Servicio.HorizonteDias"] = "30",
            ["Servicio.Modalidades"] = "MEDICO:PRESENCIAL",
        });
        for (var day = 1; day <= 7; day++)
        {
            Assert.Contains("Franja añadida.", await PostAsync(admin, "/Administracion/CitasFranja", "/Administracion/Citas", new()
            {
                ["Franja.Equipo"] = "Medico", ["Franja.DiaSemana"] = day.ToString(), ["Franja.Inicio"] = "08:00", ["Franja.Fin"] = "20:00", ["Franja.DuracionMinutos"] = "30",
            }));
        }

        var invalidWindow = await PostAsync(admin, "/Administracion/CitasFranja", "/Administracion/Citas", new()
        {
            ["Franja.Equipo"] = "Medico", ["Franja.DiaSemana"] = "1", ["Franja.Inicio"] = "10:00", ["Franja.Fin"] = "11:00", ["Franja.DuracionMinutos"] = "30",
        });
        var configuration = await BodyAsync(await admin.GetAsync("/Administracion/Citas"));
        var nurseConfiguration = await BodyAsync(await nurse.GetAsync("/Administracion/Citas"));

        var home = await BodyAsync(await family.GetAsync(citasUrl));
        var chooser = await BodyAsync(await family.GetAsync($"{citasUrl}/Nueva"));
        var slotsPage = await BodyAsync(await family.GetAsync($"{citasUrl}/Nueva?equipo=Medico&modalidad=Presencial"));
        var review = Regex.Match(slotsPage, "href=\"(/Familia/Residente/[^\"]+/Citas/Revisar[^\"]*)\"").Groups[1].Value;
        var reviewPage = await BodyAsync(await family.GetAsync(review));
        var fields = new Dictionary<string, string>
        {
            ["equipo"] = ExtractValue(reviewPage, "equipo"), ["modalidad"] = ExtractValue(reviewPage, "modalidad"), ["inicio"] = ExtractValue(reviewPage, "inicio"),
            ["operacionId"] = ExtractValue(reviewPage, "operacionId"),
        };

        // Doble pulsación: la misma operación dos veces no duplica la cita.
        var booked = await PostAsync(family, $"{citasUrl}/Reservar", review, new Dictionary<string, string>(fields));
        var bookedAgain = await PostAsync(family, $"{citasUrl}/Reservar", review, new Dictionary<string, string>(fields));
        var appointmentId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.citas WHERE residente_id = @residentId", new { residentId = Guid.Parse(residentId) });
        var homeAfter = await BodyAsync(await family.GetAsync(citasUrl));
        var otherSlots = await BodyAsync(await family.GetAsync($"{citasUrl}/Nueva?equipo=Medico&modalidad=Presencial"));
        var start = DateTimeOffset.Parse(fields["inicio"]);

        var agenda = await BodyAsync(await admin.GetAsync($"/Administracion/AgendaCitas?semana={start.ToLocalTime():yyyy-MM-dd}"));
        var cancelWithoutReason = await PostAsync(admin, "/Administracion/CancelarCita", $"/Administracion/CancelarCita?citaId={appointmentId}", new()
        {
            ["Form.CitaId"] = appointmentId.ToString(), ["Form.Motivo"] = "",
        });
        var cancelled = await PostAsync(admin, "/Administracion/CancelarCita", $"/Administracion/CancelarCita?citaId={appointmentId}", new()
        {
            ["Form.CitaId"] = appointmentId.ToString(), ["Form.Motivo"] = "Motivo interno de la cancelación.",
        });
        var cancelledFamily = await BodyAsync(await family.GetAsync($"{citasUrl}/{appointmentId}"));
        var freeAgain = await BodyAsync(await family.GetAsync($"{citasUrl}/Nueva?equipo=Medico&modalidad=Presencial"));

        // Desactivar el servicio: la familia ya no ve formularios de reserva.
        var disabled = await PostAsync(admin, "/Administracion/Citas", "/Administracion/Citas", new()
        {
            ["Servicio.Version"] = "1", ["Servicio.Modo"] = "Desactivado", ["Servicio.AntelacionHoras"] = "1", ["Servicio.HorizonteDias"] = "30",
            ["Servicio.Modalidades"] = "MEDICO:PRESENCIAL",
        });
        var homeDisabled = await BodyAsync(await family.GetAsync(citasUrl));
        var slotsDisabled = await BodyAsync(await family.GetAsync($"{citasUrl}/Nueva?equipo=Medico&modalidad=Presencial"));
        var foreignAppointment = await family.GetAsync($"/Familia/Residente/{Guid.NewGuid()}/Citas/{appointmentId}");

        Assert.Contains("no ofrece reservar citas", beforeConfiguration);
        Assert.DoesNotContain("Reservar una cita", beforeConfiguration);
        Assert.Contains("no ofrece reservar citas", newBefore);
        Assert.Contains("Configuración del servicio de citas guardada", saved);
        Assert.Contains("se solapa con otra", invalidWindow);
        Assert.Contains("Equipo médico", configuration);
        Assert.Contains("Lunes", configuration);
        Assert.DoesNotContain("Guardar el servicio", nurseConfiguration);
        Assert.Contains("Reservar una cita", home);
        Assert.Contains("Las citas no son un canal urgente", home);
        Assert.Contains("No tienes citas.", home);
        Assert.Contains("Equipo médico", chooser);
        Assert.DoesNotContain("Equipo de Enfermería", chooser);
        Assert.DoesNotContain("Telefónica", chooser);
        Assert.Contains("Huecos libres", slotsPage);
        Assert.Contains("Confirmar la reserva", reviewPage);
        Assert.Contains("Cita confirmada.", booked);
        Assert.Contains("Cita confirmada.", bookedAgain);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.citas WHERE residente_id = @residentId", new { residentId = Guid.Parse(residentId) }));
        Assert.Contains("Confirmada", homeAfter);
        Assert.DoesNotContain($"inicio={fields["inicio"]}", Uri.UnescapeDataString(otherSlots)); // el hueco reservado ya no se ofrece
        Assert.Contains("Residente Citas Funcional", agenda);
        Assert.Contains("Madre Citas Funcional", agenda);
        Assert.Contains("Cancelar", agenda);
        Assert.Contains("Escribe el motivo de la cancelación.", cancelWithoutReason);
        Assert.Contains("Cita cancelada.", cancelled);
        Assert.Contains("Cancelada", cancelledFamily);
        Assert.Contains("por el centro", cancelledFamily);
        Assert.DoesNotContain("Motivo interno de la cancelación.", cancelledFamily);
        Assert.Contains($"inicio={fields["inicio"]}", Uri.UnescapeDataString(freeAgain)); // cancelada, el hueco vuelve a ofrecerse
        Assert.Contains("Configuración del servicio de citas guardada", disabled);
        Assert.Contains("no ofrece reservar citas", homeDisabled);
        Assert.DoesNotContain("Reservar una cita", homeDisabled);
        Assert.DoesNotContain("Revisar", slotsDisabled);
        Assert.Equal(HttpStatusCode.Forbidden, foreignAppointment.StatusCode);
    }

    [Fact]
    public async Task Solicitudes_LaFamiliaSolicita_AdministracionPropone_LaFamiliaPideOtroHorarioYAcepta_ConLaAppEntera()
    {
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.Admin);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Solicitud Funcional", "Madre Solicitud Funcional");
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        var linkId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.residentes_familiares WHERE residente_id = @residentId", new { residentId = Guid.Parse(residentId) });
        var authorizationUrl = $"/Administracion/AutorizacionFamiliar?residenteId={residentId}&vinculoId={linkId}";
        var subject = $"familia-{Guid.NewGuid():N}"[..20];
        await PostAsync(admin, "/Administracion/AccesoPortalFamiliar", authorizationUrl, new() { ["residenteId"] = residentId, ["vinculoId"] = linkId.ToString(), ["usuario"] = subject });
        foreach (var (change, expected) in new[] { ("Abrir", 0), ("Activar", 1) })
        {
            await PostAsync(admin, "/Administracion/AutorizacionFamiliar", authorizationUrl, new()
            {
                ["Form.ResidenteId"] = residentId, ["Form.VinculoId"] = linkId.ToString(), ["Form.CambiosEsperados"] = expected.ToString(), ["Form.Cambio"] = change,
            });
        }

        var family = await LoginAsync(subject);
        var citasUrl = $"/Familia/Residente/{linkId}/Citas";
        var requestsUrl = $"/Familia/Residente/{linkId}/Solicitudes";

        // En reserva directa o sin configurar, la familia no ve «Solicitar una cita» y el formulario no se abre.
        var directMode = await PostAsync(admin, "/Administracion/Citas", "/Administracion/Citas", new()
        {
            ["Servicio.Version"] = "0", ["Servicio.Modo"] = "ReservaDirecta", ["Servicio.AntelacionHoras"] = "1", ["Servicio.HorizonteDias"] = "30",
            ["Servicio.Modalidades"] = "MEDICO:PRESENCIAL",
        });
        var homeInDirect = await BodyAsync(await family.GetAsync(citasUrl));
        var formInDirect = await BodyAsync(await family.GetAsync($"{requestsUrl}/Nueva"));

        // Administración pasa a solicitud previa: ahora sí, y el modo se ve en su pantalla.
        var requestMode = await PostAsync(admin, "/Administracion/Citas", "/Administracion/Citas", new()
        {
            ["Servicio.Version"] = "1", ["Servicio.Modo"] = "SolicitudPrevia", ["Servicio.AntelacionHoras"] = "1", ["Servicio.HorizonteDias"] = "30",
            ["Servicio.Modalidades"] = "MEDICO:PRESENCIAL",
        });
        for (var day = 1; day <= 7; day++)
        {
            Assert.Contains("Franja añadida.", await PostAsync(admin, "/Administracion/CitasFranja", "/Administracion/Citas", new()
            {
                ["Franja.Equipo"] = "Medico", ["Franja.DiaSemana"] = day.ToString(), ["Franja.Inicio"] = "08:00", ["Franja.Fin"] = "20:00", ["Franja.DuracionMinutos"] = "30",
            }));
        }

        var configuration = await BodyAsync(await admin.GetAsync("/Administracion/Citas"));
        var homeInRequests = await BodyAsync(await family.GetAsync(citasUrl));
        var form = await BodyAsync(await family.GetAsync($"{requestsUrl}/Nueva"));
        var fields = new Dictionary<string, string>
        {
            ["Form.OperacionId"] = ExtractValue(form, "Form.OperacionId"), ["Form.Equipo"] = "Medico", ["Form.Modalidad"] = "Presencial",
            ["Form.Motivo"] = "Resultados", ["Form.Comentario"] = "Quiero hablar de los análisis (ficticio).", ["Form.Dias"] = "2", ["Form.ParteDelDia"] = "Tarde",
        };

        // Un envío sin motivo no se guarda; el mismo envío dos veces (doble pulsación) crea una sola solicitud.
        var withoutReason = await PostAsync(family, requestsUrl, $"{requestsUrl}/Nueva", new Dictionary<string, string>(fields) { ["Form.Motivo"] = "" });
        var created = await PostAsync(family, requestsUrl, $"{requestsUrl}/Nueva", new Dictionary<string, string>(fields));
        var createdAgain = await PostAsync(family, requestsUrl, $"{requestsUrl}/Nueva", new Dictionary<string, string>(fields));
        var requestId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.citas_solicitudes WHERE residente_id = @residentId", new { residentId = Guid.Parse(residentId) });
        var secondOpen = await PostAsync(family, requestsUrl, $"{requestsUrl}/Nueva", new Dictionary<string, string>(fields) { ["Form.OperacionId"] = Guid.NewGuid().ToString() });

        // Administración la ve en la bandeja con lo que pidió la familia y propone el primer hueco libre, con un profesional interno.
        var inbox = await BodyAsync(await admin.GetAsync("/Administracion/SolicitudesCitas"));
        var proposePageUrl = $"/Administracion/ProponerCita?solicitudId={requestId}";
        var proposePage = await BodyAsync(await admin.GetAsync(proposePageUrl));
        var firstSlot = Regex.Match(proposePage, "name=\"Form\\.Inicio\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var proposedWithoutSlot = await PostAsync(admin, "/Administracion/ProponerCita", proposePageUrl, new() { ["Form.SolicitudId"] = requestId.ToString(), ["Form.Inicio"] = "" });
        var proposed = await PostAsync(admin, "/Administracion/ProponerCita", proposePageUrl, new()
        {
            ["Form.SolicitudId"] = requestId.ToString(), ["Form.Inicio"] = firstSlot, ["Form.Profesional"] = "Dra. Ficticia Interna",
        });
        var requestUrl = $"{requestsUrl}/{requestId}";
        var proposalPage = await BodyAsync(await family.GetAsync(requestUrl));
        var inboxProposed = await BodyAsync(await admin.GetAsync("/Administracion/SolicitudesCitas"));
        var residentWithProposal = await BodyAsync(await family.GetAsync($"/Familia/Residente/{linkId}"));

        // Pasan 49 horas sin respuesta: la propuesta caduca (CJ, portal 6.4) y Administración puede volver a proponer.
        await connection.ExecuteAsync(
            "UPDATE dbo.citas_solicitudes SET propuesta_en = DATEADD(HOUR, -49, SYSUTCDATETIME()) WHERE id = @requestId", new { requestId });
        var expiredPage = await BodyAsync(await family.GetAsync(requestUrl));
        var inboxExpired = await BodyAsync(await admin.GetAsync("/Administracion/SolicitudesCitas"));
        var residentAfterExpiry = await BodyAsync(await family.GetAsync($"/Familia/Residente/{linkId}"));

        // La familia pide otro horario: vuelve a estar pendiente, sin propuesta; Administración la cierra con motivo y la familia lo ve cerrado sin el motivo.
        var anotherTime = await PostAsync(family, $"{requestUrl}/OtroHorario", requestUrl, new());
        var closePageUrl = $"/Administracion/CerrarSolicitudCita?solicitudId={requestId}";
        var closeWithoutReason = await PostAsync(admin, "/Administracion/CerrarSolicitudCita", closePageUrl, new() { ["Form.SolicitudId"] = requestId.ToString(), ["Form.Motivo"] = "" });
        var closed = await PostAsync(admin, "/Administracion/CerrarSolicitudCita", closePageUrl, new()
        {
            ["Form.SolicitudId"] = requestId.ToString(), ["Form.Motivo"] = "Motivo interno del cierre.",
        });
        var closedFamily = await BodyAsync(await family.GetAsync(requestUrl));

        // Una solicitud nueva: Administración propone, la familia acepta (dos veces seguidas) y nace una sola cita.
        var secondForm = await BodyAsync(await family.GetAsync($"{requestsUrl}/Nueva"));
        await PostAsync(family, requestsUrl, $"{requestsUrl}/Nueva", new Dictionary<string, string>(fields) { ["Form.OperacionId"] = ExtractValue(secondForm, "Form.OperacionId") });
        var secondId = await connection.ExecuteScalarAsync<Guid>(
            "SELECT id FROM dbo.citas_solicitudes WHERE residente_id = @residentId AND estado_codigo = 'PENDIENTE'", new { residentId = Guid.Parse(residentId) });
        var secondProposePage = $"/Administracion/ProponerCita?solicitudId={secondId}";
        var secondSlot = Regex.Match(await BodyAsync(await admin.GetAsync(secondProposePage)), "name=\"Form\\.Inicio\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        await PostAsync(admin, "/Administracion/ProponerCita", secondProposePage, new() { ["Form.SolicitudId"] = secondId.ToString(), ["Form.Inicio"] = secondSlot });
        var secondUrl = $"{requestsUrl}/{secondId}";
        var secondProposal = await BodyAsync(await family.GetAsync(secondUrl));
        var accepted = await PostAsync(family, $"{secondUrl}/Aceptar", secondUrl, new());
        var acceptedAgain = await PostAsync(family, $"{secondUrl}/Aceptar", secondUrl, new());
        var appointmentId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.citas WHERE residente_id = @residentId", new { residentId = Guid.Parse(residentId) });
        var start = DateTimeOffset.Parse(secondSlot);
        var agenda = await BodyAsync(await admin.GetAsync($"/Administracion/AgendaCitas?semana={start.ToLocalTime():yyyy-MM-dd}"));
        var requestAfter = await BodyAsync(await family.GetAsync(secondUrl));

        Assert.DoesNotContain("Solicitar una cita", homeInDirect);
        Assert.Contains("Ahora mismo el centro no admite solicitudes", formInDirect);
        Assert.Contains("Solicitud previa", configuration);
        Assert.Contains("Solicitar una cita", homeInRequests);
        Assert.DoesNotContain("Reservar una cita", homeInRequests);
        Assert.Contains("Elige el motivo de la cita.", withoutReason);
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.citas_solicitudes WHERE motivo_codigo = 'OTRO' AND residente_id = @residentId", new { residentId = Guid.Parse(residentId) }));
        Assert.Contains("Solicitud enviada.", created);
        Assert.Contains("Pendiente de gestión", created);
        Assert.Contains("Solicitud enviada.", createdAgain);
        Assert.Contains("ya tengas una solicitud abierta", secondOpen);
        Assert.Contains("Madre Solicitud Funcional", inbox);
        Assert.Contains("Residente Solicitud Funcional", inbox);
        Assert.Contains("Proponer un horario", inbox);
        Assert.Contains("Quiero hablar de los análisis (ficticio).", inbox);
        Assert.Contains("Martes · por la tarde", inbox);
        Assert.Contains("Elige el hueco que propones.", proposedWithoutSlot);
        Assert.Contains("Propuesta enviada.", proposed);
        Assert.Contains("profesional (interno): Dra. Ficticia Interna", inboxProposed);
        Assert.Contains("El centro te propone", proposalPage);
        Assert.Contains("Aceptar y confirmar la cita", proposalPage);
        Assert.Contains("Pedir otro horario", proposalPage);
        Assert.DoesNotContain("Dra. Ficticia Interna", proposalPage);
        Assert.Contains("No escribas datos clínicos detallados; el equipo te llamará.", form);
        Assert.Contains("Responde antes del", proposalPage);
        Assert.Contains("si la familia no responde", inboxProposed);
        Assert.Contains("Tienes una propuesta de cita esperando tu respuesta.", residentWithProposal);
        Assert.Contains(requestUrl, residentWithProposal);
        Assert.Contains("Esta propuesta caducó el", expiredPage);
        Assert.DoesNotContain("Aceptar y confirmar la cita", expiredPage);
        Assert.Contains("Pedir otro horario", expiredPage);
        Assert.Contains("Caducó sin respuesta el", inboxExpired);
        Assert.Contains("Proponer un horario", inboxExpired);
        Assert.DoesNotContain("Tienes una propuesta de cita esperando tu respuesta.", residentAfterExpiry);
        Assert.Contains("Hemos avisado al centro de que necesitas otro horario", anotherTime);
        Assert.Contains("Pendiente de gestión", anotherTime);
        Assert.DoesNotContain("Aceptar y confirmar la cita", anotherTime);
        Assert.Contains("Escribe el motivo del cierre.", closeWithoutReason);
        Assert.Contains("Solicitud cerrada.", closed);
        Assert.Contains("Cerrada por el centro", closedFamily);
        Assert.Contains("El centro ha cerrado esta solicitud", closedFamily);
        Assert.DoesNotContain("Motivo interno del cierre.", closedFamily);
        Assert.Contains("El centro te propone", secondProposal);
        Assert.Contains("Cita confirmada.", accepted);
        Assert.Contains("Cita confirmada.", acceptedAgain);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.citas WHERE residente_id = @residentId", new { residentId = Guid.Parse(residentId) }));
        Assert.Contains("Madre Solicitud Funcional", agenda);
        Assert.Contains(appointmentId.ToString(), agenda);
        Assert.Contains("Aceptaste la propuesta: la cita está confirmada.", requestAfter);
        Assert.DoesNotContain("Aceptar y confirmar la cita", requestAfter);
        Assert.DoesNotContain("Pedir otro horario", requestAfter);
    }

    [Fact]
    public async Task InicioDeAdministracion_MuestraLoQueRequiereAtencion_SoloCifras_ConLaAppEntera()
    {
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.Admin);
        var nurse = await LoginAsync(seed.Nurse);
        var emptyHome = await BodyAsync(await admin.GetAsync("/Administracion"));
        var residentId = await CreateResidentAsync(admin, seed, "Residente Inicio Funcional", "Madre Inicio Funcional");

        // Un residente sin habitación y un familiar vinculado sin autorización abierta: dos cosas pendientes.
        var beforeAuthorization = await BodyAsync(await admin.GetAsync("/Administracion"));
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        var linkId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.residentes_familiares WHERE residente_id = @residentId", new { residentId = Guid.Parse(residentId) });
        var authorizationUrl = $"/Administracion/AutorizacionFamiliar?residenteId={residentId}&vinculoId={linkId}";
        var subject = $"familia-{Guid.NewGuid():N}"[..20];
        await PostAsync(admin, "/Administracion/AccesoPortalFamiliar", authorizationUrl, new() { ["residenteId"] = residentId, ["vinculoId"] = linkId.ToString(), ["usuario"] = subject });
        foreach (var (change, expected) in new[] { ("Abrir", 0), ("Activar", 1) })
        {
            await PostAsync(admin, "/Administracion/AutorizacionFamiliar", authorizationUrl, new()
            {
                ["Form.ResidenteId"] = residentId, ["Form.VinculoId"] = linkId.ToString(), ["Form.CambiosEsperados"] = expected.ToString(), ["Form.Cambio"] = change,
            });
        }

        // En solicitud previa, la familia envía una solicitud: pasa a ser una solicitud de cita por gestionar.
        await PostAsync(admin, "/Administracion/Citas", "/Administracion/Citas", new()
        {
            ["Servicio.Version"] = "0", ["Servicio.Modo"] = "SolicitudPrevia", ["Servicio.AntelacionHoras"] = "1", ["Servicio.HorizonteDias"] = "30",
            ["Servicio.Modalidades"] = "MEDICO:PRESENCIAL",
        });
        var family = await LoginAsync(subject);
        var requestsUrl = $"/Familia/Residente/{linkId}/Solicitudes";
        var form = await BodyAsync(await family.GetAsync($"{requestsUrl}/Nueva"));
        await PostAsync(family, requestsUrl, $"{requestsUrl}/Nueva", new()
        {
            ["Form.OperacionId"] = ExtractValue(form, "Form.OperacionId"), ["Form.Equipo"] = "Medico", ["Form.Modalidad"] = "Presencial",
            ["Form.Motivo"] = "Resultados", ["Form.Comentario"] = "Texto de la familia que el inicio no debe enseñar.", ["Form.ParteDelDia"] = "Indiferente",
        });
        var afterRequest = await BodyAsync(await admin.GetAsync("/Administracion"));
        var pastAppointment = Guid.NewGuid();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.citas
                (id, centro_id, residente_id, vinculo_id, cuenta_id, equipo_codigo, modalidad_codigo, inicio_en, fin_en, estado_codigo, operacion_id, creada_en)
            SELECT @id, link.centro_id, link.residente_id, link.id, account.id, 'MEDICO', 'PRESENCIAL', DATEADD(DAY, -2, SYSUTCDATETIME()),
                   DATEADD(MINUTE, 30, DATEADD(DAY, -2, SYSUTCDATETIME())), 'CONFIRMADA', NEWID(), DATEADD(DAY, -3, SYSUTCDATETIME())
              FROM dbo.residentes_familiares link CROSS JOIN dbo.cuentas account
             WHERE link.id = @linkId AND account.sujeto_externo = @subject
            """, new { id = pastAppointment, linkId, subject });
        var withPastAppointment = await BodyAsync(await admin.GetAsync("/Administracion"));
        var agenda = await BodyAsync(await admin.GetAsync("/Administracion/AgendaCitas"));
        var finished = await PostAsync(admin, "/Administracion/FinalizarCita", "/Administracion/AgendaCitas", new() { ["citaId"] = pastAppointment.ToString() });
        var noRoomList = await BodyAsync(await admin.GetAsync("/Administracion/Residentes?atencion=UbicacionPendiente"));
        var expiredList = await BodyAsync(await admin.GetAsync("/Administracion/Residentes?atencion=AutorizacionCaducada"));
        var pendingList = await BodyAsync(await admin.GetAsync("/Administracion/Residentes?atencion=AutorizacionPendiente"));
        var nurseHome = await nurse.GetAsync("/Administracion");

        Assert.Contains("Requiere atención", emptyHome);
        Assert.Contains("No hay nada pendiente.", emptyHome);
        // Las cuentas que conviene revisar (CJ, tercera 4.4): la Enfermería de la semilla tiene su unidad, así que no sale.
        var review = await BodyAsync(await admin.GetAsync("/Administracion/Usuarios?revisar=true"));
        Assert.Contains("Solo las cuentas que conviene revisar", review);
        Assert.DoesNotContain(seed.Nurse, review);
        Assert.Contains("1 residente con la ubicación pendiente (sin habitación o sin cama)", beforeAuthorization);
        Assert.Contains("1 autorización de familiar pendiente de activar", beforeAuthorization);
        Assert.DoesNotContain("No hay nada pendiente.", beforeAuthorization);
        Assert.DoesNotContain("solicitud de cita por gestionar", beforeAuthorization);
        Assert.Contains("1 residente con la ubicación pendiente (sin habitación o sin cama)", afterRequest);
        Assert.Contains("atencion=UbicacionPendiente", WebUtility.HtmlDecode(await admin.GetStringAsync("/Administracion")));
        Assert.Contains("Residente Inicio Funcional", noRoomList);
        Assert.Contains("Sin habitación asignada", noRoomList);
        Assert.Contains("Ningún residente coincide", expiredList);
        Assert.Contains("Ningún residente coincide", pendingList);
        Assert.DoesNotContain("pendiente de activar", afterRequest);
        Assert.Contains("1 solicitud de cita por gestionar", afterRequest);
        Assert.Contains("Citas confirmadas: 0 hoy y 0 en los próximos 7 días", afterRequest);
        // Solo cifras: ni el nombre del residente ni lo que escribió la familia salen en el inicio.
        Assert.DoesNotContain("Residente Inicio Funcional", afterRequest);
        Assert.DoesNotContain("Texto de la familia", afterRequest);
        // CJ, portal 5.8: la cita pasada sin finalizar sale en el inicio y arriba de la agenda, de cualquier semana, hasta que se finaliza.
        Assert.DoesNotContain("cita pasada sin cerrar", afterRequest);
        Assert.Contains("1 cita pasada sin cerrar", withPastAppointment);
        Assert.Contains("AgendaCitas#sin-cerrar", withPastAppointment);
        var unclosed = Regex.Match(agenda, "id=\"sin-cerrar\".*?</section>", RegexOptions.Singleline).Value;
        Assert.Contains("Citas pasadas sin cerrar", unclosed);
        Assert.Contains("Residente Inicio Funcional", unclosed);
        Assert.Contains(pastAppointment.ToString(), unclosed);
        Assert.DoesNotContain("Citas pasadas sin cerrar", finished);
        Assert.Equal("FINALIZADA", await connection.ExecuteScalarAsync<string>("SELECT estado_codigo FROM dbo.citas WHERE id = @pastAppointment", new { pastAppointment }));
        Assert.Contains("No se puede acceder", await BodyAsync(nurseHome) + nurseHome.StatusCode);
        Assert.DoesNotContain("Requiere atención", await BodyAsync(nurseHome));
    }

    [Fact]
    public async Task PortalFamiliar_ConUnPerfilProfesional_NoEntraEnBucleDeRedirecciones_ConLaAppEntera()
    {
        // Un perfil que no es familiar y tiene un solo ámbito: ProfileScope/Select lo vuelve a seleccionar y lo devolvía a la misma ruta de /Familia, en bucle.
        var seed = await SeedAsync();
        var linkId = Guid.NewGuid();
        foreach (var subject in new[] { seed.Nurse, seed.Admin })
        {
            var professional = await LoginAsync(subject);
            foreach (var url in new[] { "/Familia", $"/Familia/Residente/{linkId}", $"/Familia/Residente/{linkId}/Citas", $"/Familia/Residente/{linkId}/Citas/Nueva" })
            {
                var response = await professional.GetAsync(url);
                var body = await BodyAsync(response);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Contains("Esta sección es del Portal Familiar", body);
                Assert.Contains("Cambiar de ámbito", body);
                Assert.DoesNotContain("Volver a mis residentes", body);
            }
        }
    }

    [Fact]
    public async Task Familia_NoAlcanzaNingunaPantallaDelPersonal_ConLaAppEntera()
    {
        // Barrido: con la sesión de una familiar (con acceso a su residente), ninguna ruta GET de los demás controladores devuelve el residente
        // ajeno ni el texto clínico de los eventos. El personal comprueba el perfil en cada caso de uso; esto cubre que ninguna lo olvide.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.Admin);
        var nurse = await LoginAsync(seed.Nurse);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Propio Barrido", "Familiar Barrido");
        var foreignResidentId = await CreateResidentAsync(admin, seed, "Residente Ajeno Barrido", "Familiar Ajeno Barrido");
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        var linkId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.residentes_familiares WHERE residente_id = @residentId", new { residentId = Guid.Parse(residentId) });
        var now = DateTime.UtcNow;
        var communicationId = await AddCommunicationAsync(nurse, seed.Nurse, foreignResidentId, "Comunicado ajeno del barrido.", now.AddHours(-30), now.AddHours(-5));
        var eventId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.eventos_asistenciales WHERE residente_id = @residentId", new { residentId = Guid.Parse(foreignResidentId) });
        var subject = $"barrido-{Guid.NewGuid():N}"[..20];
        var url = $"/Administracion/AutorizacionFamiliar?residenteId={residentId}&vinculoId={linkId}";
        await PostAsync(admin, "/Administracion/AccesoPortalFamiliar", url, new() { ["residenteId"] = residentId, ["vinculoId"] = linkId.ToString(), ["usuario"] = subject });
        await PostAsync(admin, "/Administracion/AutorizacionFamiliar", url, new()
        {
            ["Form.ResidenteId"] = residentId, ["Form.VinculoId"] = linkId.ToString(), ["Form.CambiosEsperados"] = "0", ["Form.Cambio"] = "Abrir",
        });
        await PostAsync(admin, "/Administracion/AutorizacionFamiliar", url, new()
        {
            ["Form.ResidenteId"] = residentId, ["Form.VinculoId"] = linkId.ToString(), ["Form.CambiosEsperados"] = "1", ["Form.Cambio"] = "Activar",
        });

        var family = await LoginAsync(subject);
        Assert.Contains("Residente Propio Barrido", await BodyAsync(await family.GetAsync("/Familia")));
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["residenteId"] = foreignResidentId, ["eventoId"] = eventId.ToString(), ["comunicadoId"] = communicationId.ToString(), ["vinculoId"] = linkId.ToString(),
            ["id"] = foreignResidentId, ["residentId"] = foreignResidentId,
        };
        var skipped = new[] { "PortalFamiliar", "Home", "ProfileScope", "AccesoCj" };
        var routes = factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Where(a => !skipped.Contains(a.ControllerName))
            .Where(a => a.ActionConstraints?.OfType<HttpMethodActionConstraint>().All(c => c.HttpMethods.Contains("GET")) ?? true)
            .Select(a => (a.ControllerName, a.ActionName, Parameters: a.Parameters.Where(p => p.ParameterType == typeof(Guid) || p.ParameterType == typeof(Guid?)).Select(p => p.Name).ToList()))
            .ToList();

        var visited = 0;
        foreach (var (controller, action, parameters) in routes)
        {
            var query = string.Join("&", parameters.Select(p => $"{p}={(values.TryGetValue(p, out var value) ? value : Guid.NewGuid().ToString())}"));
            var response = await family.GetAsync($"/{controller}/{action}" + (query.Length > 0 ? "?" + query : ""));
            var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            visited++;
            Assert.DoesNotContain("Residente Ajeno Barrido", body);
            Assert.DoesNotContain("Comunicado ajeno del barrido.", body);
            Assert.DoesNotContain(Observation, body);
            Assert.False(response.StatusCode == HttpStatusCode.InternalServerError, $"{controller}/{action}?{query} dio un 500 a la familia: {body[..Math.Min(body.Length, 600)]}");
        }

        Assert.True(visited > 50, $"El barrido solo visitó {visited} rutas.");
    }
}
