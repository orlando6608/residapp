using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
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
        var page = await client.GetStringAsync("/DevAuth/Login");
        (await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken"), ["externalSubject"] = subject,
        }))).EnsureSuccessStatusCode();
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
        Assert.Contains("No tienes ninguna autorización activa", WebUtility.HtmlDecode(await withoutAuthorization.Content.ReadAsStringAsync()));
        Assert.Contains("Autorización abierta", opened);
        Assert.Contains("No tienes ninguna autorización activa", pending);
        Assert.Contains("Autorización activada.", activated);
        Assert.Contains("Residente Portal Funcional", home);
        Assert.Contains("Hoy ha merendado bien y ha paseado por el jardín.", list);
        Assert.Contains("Equipo asistencial del centro", list);
        Assert.DoesNotContain("Esto aún no se ha publicado.", list);
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
        Assert.Contains("No tienes ninguna autorización activa", afterRevocation);
        Assert.Equal(HttpStatusCode.Forbidden, detailAfterRevocation.StatusCode);
        // Entrar (que ya lleva a su único residente), la portada, el historial y el detalle: cuatro lecturas; las denegadas no dejan rastro.
        Assert.Equal(4, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE residente_id = @residentId AND perfil_activo = 'FAMILIAR' AND accion_codigo = 'FAMILY_PORTAL_READ'",
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
        var skipped = new[] { "PortalFamiliar", "Home", "ProfileScope", "DevAuth", "AccesoCj" };
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
