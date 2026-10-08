using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using ResidApp.Infrastructure.Persistence;

namespace ResidApp.FunctionalTests;

/// <summary>Camino feliz a través de ResidApp.Web de verdad (Kestrel de pruebas + pipeline HTTP real):
/// identidad de desarrollo (DevAuth) y alta de residente (Residents/Create). Automatiza lo verificado
/// manualmente por curl durante el cableado inicial de la Web.</summary>
public class ResidentsFlowTests : IClassFixture<ResidentsFlowTests.WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public ResidentsFlowTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task LoginAndCreateResident_GoldenPath_Succeeds()
    {
        var seed = await SeedAsync();
        var client = _factory.CreateClient();

        var loginPage = await client.GetStringAsync("/DevAuth/Login");
        var loginResponse = await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(loginPage, "__RequestVerificationToken"),
            ["externalSubject"] = seed.ExternalSubject,
        }));
        loginResponse.EnsureSuccessStatusCode();

        var createPage = await client.GetStringAsync("/Residents/Create");

        var response = await client.PostAsync("/Residents/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(createPage, "__RequestVerificationToken"),
            ["OperacionId"] = ExtractValue(createPage, "OperacionId"),
            ["AmbitoPerfilId"] = seed.ProfileScopeId.ToString(),
            ["CentroId"] = seed.CenterId.ToString(),
            ["UnidadId"] = seed.UnitId.ToString(),
            ["NombreVisible"] = "Residente Funcional",
            ["FechaNacimiento"] = "1938-02-20",
            ["SexoDocumentadoCodigo"] = "Hombre",
            ["Familiares[0].NombreVisible"] = "Familiar Prueba", ["Familiares[0].Relacion"] = "Hija", ["Familiares[0].Telefono"] = "600123456", ["Familiares[0].ContactoPrioritario"] = "true",
        }));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Id del residente", body);
    }

    [Fact]
    public async Task Create_RegistraLosFamiliaresDeContacto_ConLaAppEntera_YRechazaUnaFilaAMedias()
    {
        // El alta con familiares por la app real y el usuario limitado: pasan por la seguridad por filas de familiares y contacto urgente.
        var seed = await SeedAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var partialName = $"Residente Familia A Medias {suffix}";
        var completeName = $"Residente Familia {suffix}";
        var admin = await LoginAsync(seed.ExternalSubject);
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        var page = await admin.GetStringAsync("/Residents/Create");
        Assert.Contains("Familiares de contacto", page);
        Assert.Contains("name=\"Familiares[2].NombreVisible\"", page);
        Dictionary<string, string> Form(string name) => new()
        {
            ["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken"),
            ["OperacionId"] = ExtractValue(page, "OperacionId"),
            ["AmbitoPerfilId"] = seed.ProfileScopeId.ToString(),
            ["CentroId"] = seed.CenterId.ToString(),
            ["UnidadId"] = seed.UnitId.ToString(),
            ["NombreVisible"] = name,
            ["FechaNacimiento"] = "1938-02-20",
            ["SexoDocumentadoCodigo"] = "Mujer",
        };

        var partial = Form(partialName);
        partial["Familiares[0].NombreVisible"] = "Ana Ruiz";
        var rejected = WebUtility.HtmlDecode(await (await admin.PostAsync("/Residents/Create", new FormUrlEncodedContent(partial))).Content.ReadAsStringAsync());

        var complete = Form(completeName);
        complete["Familiares[0].NombreVisible"] = "Ana Ruiz";
        complete["Familiares[0].Relacion"] = "Hija";
        complete["Familiares[0].Telefono"] = "600123456";
        complete["Familiares[0].Referente"] = "true";
        complete["Familiares[1].NombreVisible"] = "Luis Gil";
        complete["Familiares[1].Relacion"] = "Sobrino";
        complete["Familiares[1].Telefono"] = "600765432";
        complete["Familiares[1].TutorLegal"] = "true";
        complete["Familiares[0].ContactoPrioritario"] = "true";
        complete["Familiares[1].ContactoPrioritario"] = "true";
        var noPriority = Form($"Residente Familia Sin Prioritario {suffix}");
        noPriority["Familiares[0].NombreVisible"] = "Ana Ruiz";
        noPriority["Familiares[0].Relacion"] = "Hija";
        noPriority["Familiares[0].Telefono"] = "600123456";
        var rejectedNoPriority = WebUtility.HtmlDecode(await (await admin.PostAsync("/Residents/Create", new FormUrlEncodedContent(noPriority))).Content.ReadAsStringAsync());
        var created = await admin.PostAsync("/Residents/Create", new FormUrlEncodedContent(complete));

        Assert.Contains("Revisa los familiares", rejected);
        Assert.Contains("al menos un contacto prioritario", rejectedNoPriority);
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.residentes WHERE nombre_visible = @partialName", new { partialName }));
        Assert.Contains("Id del residente", await created.Content.ReadAsStringAsync());
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.residentes_familiares l JOIN dbo.residentes r ON r.id = l.residente_id
             WHERE r.nombre_visible = @completeName
            """, new { completeName }));
        Assert.Equal(new[] { "Ana Ruiz", "Luis Gil" }, (await connection.QueryAsync<string>("""
            SELECT f.nombre_visible FROM dbo.residentes_contacto_urgente d
              JOIN dbo.residentes_familiares l ON l.id = d.vinculo_id JOIN dbo.familiares f ON f.id = l.familiar_id
              JOIN dbo.residentes r ON r.id = d.residente_id WHERE r.nombre_visible = @completeName ORDER BY d.numero
            """, new { completeName })).ToArray());
    }

    [Fact]
    public async Task Create_LaUnidadSeEligeEntreLasDelAmbito_YSusErroresSalenEnEspañol()
    {
        var seed = await SeedAsync();
        var client = _factory.CreateClient();
        var loginPage = await client.GetStringAsync("/DevAuth/Login");
        (await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(loginPage, "__RequestVerificationToken"),
            ["externalSubject"] = seed.ExternalSubject,
        }))).EnsureSuccessStatusCode();

        // Con una sola unidad, ya viene elegida.
        var singleUnitPage = await client.GetStringAsync("/Residents/Create");
        var secondUnitId = await GrantUnitAsync(seed, "Planta funcional dos");
        var createPage = await client.GetStringAsync("/Residents/Create");
        async Task<string> PostWithUnitAsync(string unit) => WebUtility.HtmlDecode(await (await client.PostAsync(
            "/Residents/Create", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractValue(createPage, "__RequestVerificationToken"),
                ["OperacionId"] = ExtractValue(createPage, "OperacionId"),
                ["AmbitoPerfilId"] = seed.ProfileScopeId.ToString(),
                ["CentroId"] = seed.CenterId.ToString(),
                ["UnidadId"] = unit,
                ["NombreVisible"] = "Residente Funcional",
                ["FechaNacimiento"] = "1938-02-20",
                ["SexoDocumentadoCodigo"] = "Hombre",
            ["Familiares[0].NombreVisible"] = "Familiar Prueba", ["Familiares[0].Relacion"] = "Hija", ["Familiares[0].Telefono"] = "600123456", ["Familiares[0].ContactoPrioritario"] = "true",
            }))).Content.ReadAsStringAsync());
        var empty = await PostWithUnitAsync("");
        var malformed = await PostWithUnitAsync("no-es-un-guid");

        Assert.Contains($"<option selected=\"selected\" value=\"{seed.UnitId}\">", singleUnitPage);
        Assert.DoesNotContain("Elige una unidad", singleUnitPage);
        Assert.Contains("<option value=\"\">Elige una unidad</option>", createPage);
        Assert.Contains($"<option value=\"{secondUnitId}\">Planta funcional dos</option>", createPage);
        Assert.Contains($"<option value=\"{seed.UnitId}\">", createPage);
        Assert.DoesNotContain(Guid.Empty.ToString(), createPage);
        Assert.Contains("Elige la unidad.", WebUtility.HtmlDecode(createPage));
        Assert.Contains("Elige la unidad.", empty);
        Assert.Contains("«no-es-un-guid» no es un valor válido para Unidad.", malformed);
        // Los campos ocultos (ámbito, centro, operación) conservan su mensaje por defecto: no los rellena el usuario.
        foreach (var field in new[] { "Unidad", "Nombre completo", "Fecha de nacimiento", "Sexo documentado", "Referencia interna" })
        {
            Assert.DoesNotContain($"The {field} field", createPage + empty + malformed);
        }

        Assert.DoesNotContain("The field ", createPage);
        Assert.DoesNotContain("is not valid", empty + malformed);
    }

    [Fact]
    public async Task BaselineDirection_ElResidenteSeEligeDeLaLista_YElAmbitoSaleDelActivo()
    {
        // Administración da de alta un residente y Dirección Clínica, en el mismo centro y unidad, lo consulta.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var createPage = await admin.GetStringAsync("/Residents/Create");
        var created = await (await admin.PostAsync("/Residents/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(createPage, "__RequestVerificationToken"),
            ["OperacionId"] = ExtractValue(createPage, "OperacionId"),
            ["AmbitoPerfilId"] = seed.ProfileScopeId.ToString(),
            ["CentroId"] = seed.CenterId.ToString(),
            ["UnidadId"] = seed.UnitId.ToString(),
            ["NombreVisible"] = "Residente Consulta Dirección",
            ["FechaNacimiento"] = "1938-02-20",
            ["SexoDocumentadoCodigo"] = "Hombre",
            ["Familiares[0].NombreVisible"] = "Familiar Prueba", ["Familiares[0].Relacion"] = "Hija", ["Familiares[0].Telefono"] = "600123456", ["Familiares[0].ContactoPrioritario"] = "true",
        }))).Content.ReadAsStringAsync();
        var residentId = Regex.Match(created, "Id del residente: <code>([0-9a-f-]{36})</code>").Groups[1].Value;
        var direccion = await LoginAsync(await GrantDirectionAsync(seed));

        var queryPage = WebUtility.HtmlDecode(await direccion.GetStringAsync("/Baseline/Direction"));
        async Task<(HttpStatusCode Status, string Body)> QueryAsync(string resident)
        {
            var response = await direccion.PostAsync("/Baseline/Direction", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractValue(queryPage, "__RequestVerificationToken"),
                ["OperacionId"] = Guid.NewGuid().ToString(),
                ["ResidenteId"] = resident,
                ["TipoRecurso"] = "BASELINE_HISTORY",
                ["Proposito"] = "CONTINUIDAD_ASISTENCIAL",
                ["Justificacion"] = "Revisión de continuidad de un caso (prueba funcional).",
            }));
            return (response.StatusCode, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        }
        var empty = await QueryAsync("");
        var chosen = await QueryAsync(residentId);

        Assert.NotEmpty(residentId);
        Assert.Contains($"<option value=\"{residentId}\">Residente Consulta Dirección (", queryPage);
        Assert.Contains("<option value=\"\">Elige un residente</option>", queryPage);
        Assert.DoesNotContain("name=\"AmbitoPerfilId\"", queryPage);
        Assert.DoesNotContain("name=\"CentroId\"", queryPage);
        Assert.Equal(HttpStatusCode.OK, empty.Status);
        Assert.Contains("Elige el residente.", empty.Body);
        Assert.Equal(HttpStatusCode.OK, chosen.Status);
        // La consulta se autoriza con el ámbito activo y el residente elegido; como aún no tiene ningún basal firmado, el
        // resultado lo dice en vez de denegar.
        Assert.Contains("Historial de basal auditado", chosen.Body);
        Assert.Contains("Este residente todavía no tiene ningún basal firmado.", chosen.Body);
        Assert.DoesNotContain("No se puede acceder a esta operación.", chosen.Body);
    }

    [Fact]
    public async Task BaselineDirection_LaDeclaracionDeAcceso_ExigeFinalidadYJustificacion_ValeParaElResidente_YTerminaAlSalirOCambiarDeAmbito()
    {
        // CJ (2026-10-06): finalidad y justificación obligatorias, una declaración por residente que vale 1 hora, y termina al cambiar
        // de residente, de ámbito o al cerrar sesión. Por la app real, con el usuario limitado.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentOne = await CreateResidentAsync(admin, seed, "Residente Declaracion Uno Funcional");
        var residentTwo = await CreateResidentAsync(admin, seed, "Residente Declaracion Dos Funcional");
        var directionSubject = await GrantDirectionAsync(seed);
        var direccion = await LoginAsync(directionSubject);
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        var page = WebUtility.HtmlDecode(await direccion.GetStringAsync("/Baseline/Direction"));

        async Task<string> QueryAsync(string resident, string? purpose, string? justification)
        {
            var fields = new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken"),
                ["OperacionId"] = Guid.NewGuid().ToString(),
                ["ResidenteId"] = resident,
                ["TipoRecurso"] = "BASELINE_HISTORY",
            };
            if (purpose is not null) fields["Proposito"] = purpose;
            if (justification is not null) fields["Justificacion"] = justification;
            return WebUtility.HtmlDecode(await (await direccion.PostAsync("/Baseline/Direction", new FormUrlEncodedContent(fields))).Content.ReadAsStringAsync());
        }
        Task<int> OpenDeclarationsAsync() => connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.declaraciones_acceso_clinico d JOIN dbo.cuentas c ON c.id = d.cuenta_id
             WHERE c.sujeto_externo = @directionSubject AND d.terminada_en IS NULL AND d.caduca_en > SYSUTCDATETIME()
            """, new { directionSubject });

        var withoutJustification = await QueryAsync(residentOne, "CONTINUIDAD_ASISTENCIAL", "   ");
        var withoutPurpose = await QueryAsync(residentOne, null, "Revisión de un seguimiento vencido (prueba funcional).");
        var oldPurpose = await QueryAsync(residentOne, "SUPERVISION_CLINICA", "Una finalidad que ya no se ofrece.");
        var declaredNothing = await OpenDeclarationsAsync();

        var first = await QueryAsync(residentOne, "CONTINUIDAD_ASISTENCIAL", "Revisión de un seguimiento vencido (prueba funcional).");
        var openAfterFirst = await OpenDeclarationsAsync();
        var minutes = await connection.ExecuteScalarAsync<int>("""
            SELECT DATEDIFF(MINUTE, d.creada_en, d.caduca_en) FROM dbo.declaraciones_acceso_clinico d
             WHERE d.id = (SELECT TOP 1 id FROM dbo.declaraciones_acceso_clinico ORDER BY creada_en DESC)
               AND d.residente_id = @residentOne
            """, new { residentOne });
        var rememberedPage = WebUtility.HtmlDecode(await direccion.GetStringAsync($"/Baseline/Direction?residenteId={residentOne}"));
        var reused = await QueryAsync(residentOne, null, null);
        var openAfterReuse = await OpenDeclarationsAsync();
        var other = await QueryAsync(residentTwo, null, null);
        var otherDeclared = await QueryAsync(residentTwo, "INCIDENCIA_RECLAMACION", "Reclamación familiar registrada (prueba funcional).");
        var openAfterOther = await OpenDeclarationsAsync();
        var backToOne = await QueryAsync(residentOne, null, null);

        (await direccion.PostAsync("/DevAuth/Logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken"),
        }))).EnsureSuccessStatusCode();
        var openAfterLogout = await OpenDeclarationsAsync();

        Assert.Contains("Escribe una justificación breve del acceso.", withoutJustification);
        Assert.Contains("Elige la finalidad del acceso.", withoutPurpose);
        Assert.Contains("Elige la finalidad del acceso.", oldPurpose);
        Assert.Equal(0, declaredNothing);
        Assert.Contains("Historial de basal auditado", first);
        Assert.Contains("Finalidad declarada:", first);
        Assert.Equal(1, openAfterFirst);
        Assert.Equal(60, minutes);
        Assert.Contains("Ya has declarado la finalidad del acceso para este residente", rememberedPage);
        Assert.Contains("Historial de basal auditado", reused);
        Assert.Equal(1, openAfterReuse);
        // Otro residente: la declaración del primero no sirve; al declarar de nuevo, la anterior termina.
        Assert.Contains("Elige la finalidad del acceso.", other);
        Assert.Contains("Historial de basal auditado", otherDeclared);
        Assert.Equal(1, openAfterOther);
        Assert.Contains("Elige la finalidad del acceso.", backToOne);
        Assert.Equal(0, openAfterLogout);
    }

    [Fact]
    public async Task EnfermeriaBasal_CreaYCancelaUnBorrador_ConLaAppEntera()
    {
        // Recorrido por la app real con cookie y ámbito: con la app conectada como usuario limitado
        // (RESIDAPP_TEST_APP_CONNECTION_STRING) pasa por la seguridad por filas de las tablas del basal.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var createPage = await admin.GetStringAsync("/Residents/Create");
        var created = await (await admin.PostAsync("/Residents/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(createPage, "__RequestVerificationToken"),
            ["OperacionId"] = ExtractValue(createPage, "OperacionId"),
            ["AmbitoPerfilId"] = seed.ProfileScopeId.ToString(),
            ["CentroId"] = seed.CenterId.ToString(),
            ["UnidadId"] = seed.UnitId.ToString(),
            ["NombreVisible"] = "Residente Borrador Funcional",
            ["FechaNacimiento"] = "1938-02-20",
            ["SexoDocumentadoCodigo"] = "Hombre",
            ["Familiares[0].NombreVisible"] = "Familiar Prueba", ["Familiares[0].Relacion"] = "Hija", ["Familiares[0].Telefono"] = "600123456", ["Familiares[0].ContactoPrioritario"] = "true",
        }))).Content.ReadAsStringAsync();
        var residentId = Regex.Match(created, "Id del residente: <code>([0-9a-f-]{36})</code>").Groups[1].Value;
        var nurse = await LoginAsync(await GrantNursingAsync(seed));

        var hub = await nurse.GetStringAsync($"/EnfermeriaBasal/Draft?residenteId={residentId}");
        var afterCreate = WebUtility.HtmlDecode(await (await nurse.PostAsync("/EnfermeriaBasal/CrearBorrador", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(hub, "__RequestVerificationToken"),
            ["ResidenteId"] = residentId,
            ["OperacionId"] = Guid.NewGuid().ToString(),
            ["Motivo"] = "Alta",
            ["FuenteInformacionComun"] = "ValoracionDirecta",
            ["FechaInformacionComun"] = "2026-10-06",
        }))).Content.ReadAsStringAsync());
        var afterCancel = WebUtility.HtmlDecode(await (await nurse.PostAsync("/EnfermeriaBasal/CancelarBorrador", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(afterCreate, "__RequestVerificationToken"),
            ["ResidenteId"] = residentId,
            ["Motivo"] = "Prueba funcional de la seguridad por filas",
        }))).Content.ReadAsStringAsync());

        Assert.Contains("Borrador creado.", afterCreate);
        Assert.Contains("Borrador cancelado.", afterCancel);
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.basales_borrador WHERE residente_id = @residentId", new { residentId }));
    }

    [Fact]
    public async Task Enfermeria_RegistraUnEventoYGuardaSuValoracion_ConLaAppEntera()
    {
        // Flujo clínico por la app real con cookie y ámbito: con la app conectada como usuario limitado
        // (RESIDAPP_TEST_APP_CONNECTION_STRING) pasa por la seguridad por filas de los eventos y las valoraciones.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var createPage = await admin.GetStringAsync("/Residents/Create");
        var created = await (await admin.PostAsync("/Residents/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(createPage, "__RequestVerificationToken"),
            ["OperacionId"] = ExtractValue(createPage, "OperacionId"),
            ["AmbitoPerfilId"] = seed.ProfileScopeId.ToString(),
            ["CentroId"] = seed.CenterId.ToString(),
            ["UnidadId"] = seed.UnitId.ToString(),
            ["NombreVisible"] = "Residente Evento Funcional",
            ["FechaNacimiento"] = "1938-02-20",
            ["SexoDocumentadoCodigo"] = "Hombre",
            ["Familiares[0].NombreVisible"] = "Familiar Prueba", ["Familiares[0].Relacion"] = "Hija", ["Familiares[0].Telefono"] = "600123456", ["Familiares[0].ContactoPrioritario"] = "true",
        }))).Content.ReadAsStringAsync();
        var residentId = Regex.Match(created, "Id del residente: <code>([0-9a-f-]{36})</code>").Groups[1].Value;
        var nurse = await LoginAsync(await GrantNursingAsync(seed));
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();

        var registerPage = await nurse.GetStringAsync($"/Enfermeria/RegistrarEvento?residenteId={residentId}");
        var detailPage = await (await nurse.PostAsync("/Enfermeria/RegistrarEvento", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(registerPage, "__RequestVerificationToken"),
            ["ResidenteId"] = residentId,
            ["OperacionId"] = ExtractValue(registerPage, "OperacionId"),
            ["Observacion"] = "Tos productiva desde la mañana (prueba funcional).",
            ["Clasificacion"] = "Ordinario",
        }))).Content.ReadAsStringAsync();
        var eventId = await connection.ExecuteScalarAsync<Guid>(
            "SELECT id FROM dbo.eventos_asistenciales WHERE residente_id = @residentId", new { residentId });
        var revision = await connection.ExecuteScalarAsync<int>("SELECT revision FROM dbo.eventos_asistenciales WHERE id = @eventId", new { eventId });

        var started = WebUtility.HtmlDecode(await (await nurse.PostAsync("/Enfermeria/EmpezarValoracion", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(detailPage, "__RequestVerificationToken"),
            ["eventoId"] = eventId.ToString(),
            ["revision"] = revision.ToString(),
        }))).Content.ReadAsStringAsync());
        var revisionAfterStart = await connection.ExecuteScalarAsync<int>("SELECT revision FROM dbo.eventos_asistenciales WHERE id = @eventId", new { eventId });
        var assessmentPage = await nurse.GetStringAsync($"/Enfermeria/Valoracion?eventoId={eventId}");
        var saved = WebUtility.HtmlDecode(await (await nurse.PostAsync("/Enfermeria/Valoracion", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(assessmentPage, "__RequestVerificationToken"),
            ["Form.EventoId"] = eventId.ToString(),
            ["Form.Revision"] = revisionAfterStart.ToString(),
            ["Form.Hallazgos"] = "Crepitantes en base derecha.",
            ["Form.Actuaciones"] = "Se incorpora a 45 grados.",
        }))).Content.ReadAsStringAsync());

        Assert.Contains("Evento registrado.", WebUtility.HtmlDecode(detailPage));
        Assert.True(revisionAfterStart > revision, "Empezar la valoración debería subir la revisión del evento.");
        Assert.Contains("Valoración guardada.", saved);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.valoraciones_enfermeria WHERE evento_id = @eventId", new { eventId }));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.valoraciones_enfermeria_versiones WHERE evento_id = @eventId", new { eventId }));
    }

    [Fact]
    public async Task Enfermeria_DerivaAUrgenciasConComunicaciones_VistaPreviaFirmaYPdf_ConLaAppEntera()
    {
        // La pantalla de derivación de punta a punta: el apartado «Comunicaciones» se escribe, sale en la vista previa, viaja con la
        // huella al firmar y queda en el informe (con el usuario limitado, bajo la seguridad por filas).
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Derivacion Funcional");
        var nurse = await LoginAsync(await GrantNursingAsync(seed));
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        Task<int> RevisionAsync(Guid id) => connection.ExecuteScalarAsync<int>("SELECT revision FROM dbo.eventos_asistenciales WHERE id = @id", new { id });

        var registerPage = await nurse.GetStringAsync($"/Enfermeria/RegistrarEvento?residenteId={residentId}");
        var detailPage = await (await nurse.PostAsync("/Enfermeria/RegistrarEvento", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(registerPage, "__RequestVerificationToken"),
            ["ResidenteId"] = residentId,
            ["OperacionId"] = ExtractValue(registerPage, "OperacionId"),
            ["Observacion"] = "Disnea brusca (prueba funcional de derivación).",
            ["Clasificacion"] = "Ordinario",
        }))).Content.ReadAsStringAsync();
        var eventId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.eventos_asistenciales WHERE residente_id = @residentId", new { residentId });
        (await nurse.PostAsync("/Enfermeria/EmpezarValoracion", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(detailPage, "__RequestVerificationToken"),
            ["eventoId"] = eventId.ToString(),
            ["revision"] = (await RevisionAsync(eventId)).ToString(),
        }))).EnsureSuccessStatusCode();
        var assessmentPage = await nurse.GetStringAsync($"/Enfermeria/Valoracion?eventoId={eventId}");
        (await nurse.PostAsync("/Enfermeria/Valoracion", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(assessmentPage, "__RequestVerificationToken"),
            ["Form.EventoId"] = eventId.ToString(),
            ["Form.Revision"] = (await RevisionAsync(eventId)).ToString(),
            ["Form.Hallazgos"] = "Crepitantes bilaterales.",
        }))).EnsureSuccessStatusCode();
        var activatePage = await nurse.GetStringAsync($"/Enfermeria/ActivarProtocolo?eventoId={eventId}");
        (await nurse.PostAsync("/Enfermeria/ActivarProtocolo", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(activatePage, "__RequestVerificationToken"),
            ["Form.EventoId"] = eventId.ToString(),
            ["Form.Revision"] = (await RevisionAsync(eventId)).ToString(),
        }))).EnsureSuccessStatusCode();

        const string communications = "Contacto telefónico con SEM a las 18 h. Avisamos a la familia del traslado a Urgencias.";
        var referralPage = await nurse.GetStringAsync($"/Enfermeria/Derivar?eventoId={eventId}");
        var preview = WebUtility.HtmlDecode(await (await nurse.PostAsync("/Enfermeria/Derivar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(referralPage, "__RequestVerificationToken"),
            ["Form.EventoId"] = eventId.ToString(),
            ["Form.Revision"] = (await RevisionAsync(eventId)).ToString(),
            ["Form.OperacionId"] = ExtractValue(referralPage, "Form.OperacionId"),
            ["Form.Motivo"] = "Desaturación que no remonta con oxigenoterapia.",
            ["Form.Comunicaciones"] = communications,
            ["Form.Accion"] = "VistaPrevia",
        }))).Content.ReadAsStringAsync());
        var signed = WebUtility.HtmlDecode(await (await nurse.PostAsync("/Enfermeria/Derivar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(preview, "__RequestVerificationToken"),
            ["Form.EventoId"] = eventId.ToString(),
            ["Form.Revision"] = ExtractValue(preview, "Form.Revision"),
            ["Form.OperacionId"] = ExtractValue(preview, "Form.OperacionId"),
            ["Form.Motivo"] = ExtractValue(preview, "Form.Motivo"),
            ["Form.InformacionAdicional"] = "",
            ["Form.Comunicaciones"] = ExtractValue(preview, "Form.Comunicaciones"),
            ["Form.Huella"] = ExtractValue(preview, "Form.Huella"),
            ["Form.Accion"] = "Firmar",
        }))).Content.ReadAsStringAsync());

        Assert.Contains("Comunicaciones (opcional)", WebUtility.HtmlDecode(referralPage));
        Assert.Contains("Vista previa del informe", preview);
        Assert.Contains(communications, preview);
        Assert.Contains("Informe de derivación firmado.", signed);
        Assert.Contains(communications, signed);
        Assert.Equal(communications, await connection.ExecuteScalarAsync<string>(
            "SELECT comunicaciones FROM dbo.informes_derivacion WHERE evento_id = @eventId", new { eventId }));
    }

    [Fact]
    public async Task BaselineSign_SinPantallaPropia_RedirigeAResidentes()
    {
        var seed = await SeedAsync();
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var loginPage = await client.GetStringAsync("/DevAuth/Login");
        await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(loginPage, "__RequestVerificationToken"),
            ["externalSubject"] = seed.ExternalSubject,
        }));
        var tokenPage = await client.GetStringAsync("/DevAuth/Login");

        var get = await client.GetAsync("/Baseline/Sign");
        // Sin los campos que rellena la confirmación: antes acababa en un 500 al construir los identificadores.
        var post = await client.PostAsync("/Baseline/Sign", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(tokenPage, "__RequestVerificationToken"),
            ["OperacionId"] = Guid.NewGuid().ToString(),
            ["RevisionBorradorEsperada"] = "1",
        }));

        Assert.Equal(HttpStatusCode.Redirect, get.StatusCode);
        Assert.Equal("/Enfermeria/Residentes", get.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Equal("/Enfermeria/Residentes", post.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Administracion_CorrigeIdentidadYGestionaFamiliares_ConLaAppEntera()
    {
        // Identidad corregida, familiar añadido, su autorización abierta y activada y su designación como contacto urgente, por la
        // app real: con el usuario limitado pasan por la seguridad por filas de las cuatro tablas de familias y correcciones.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Familias Funcional");
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();

        var identityPage = await admin.GetStringAsync($"/Administracion/CorregirIdentidad?residenteId={residentId}");
        var corrected = WebUtility.HtmlDecode(await (await admin.PostAsync("/Administracion/CorregirIdentidad", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(identityPage, "__RequestVerificationToken"),
            ["Form.ResidenteId"] = residentId,
            ["Form.CorreccionesEsperadas"] = "0",
            ["Form.NombreVisible"] = "Residente Familias Corregido",
            ["Form.FechaNacimiento"] = "1938-02-21",
            ["Form.SexoDocumentado"] = "Hombre",
            ["Form.Motivo"] = "Error de transcripción (prueba funcional).",
        }))).Content.ReadAsStringAsync());

        var addPage = await admin.GetStringAsync($"/Administracion/AnadirFamiliar?residenteId={residentId}");
        var added = WebUtility.HtmlDecode(await (await admin.PostAsync("/Administracion/AnadirFamiliar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(addPage, "__RequestVerificationToken"),
            ["Form.ResidenteId"] = residentId,
            ["Form.OperacionId"] = ExtractValue(addPage, "Form.OperacionId"),
            ["Form.NombreVisible"] = "Familiar Funcional",
            ["Form.Relacion"] = "Hija",
            ["Form.Telefono"] = "600000000",
        }))).Content.ReadAsStringAsync());
        var linkId = await connection.ExecuteScalarAsync<Guid>(
            """
            SELECT l.id FROM dbo.residentes_familiares l JOIN dbo.familiares f ON f.id = l.familiar_id
             WHERE l.residente_id = @residentId AND f.nombre_visible = 'Familiar Funcional'
            """, new { residentId });

        var changes = 0;
        foreach (var change in new[] { "Abrir", "Activar" })
        {
            var authorizationPage = await admin.GetStringAsync($"/Administracion/AutorizacionFamiliar?residenteId={residentId}&vinculoId={linkId}");
            await admin.PostAsync("/Administracion/AutorizacionFamiliar", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractValue(authorizationPage, "__RequestVerificationToken"),
                ["Form.ResidenteId"] = residentId,
                ["Form.VinculoId"] = linkId.ToString(),
                ["Form.CambiosEsperados"] = changes.ToString(),
                ["Form.Cambio"] = change,
            }));
            changes++;
        }

        var contactPage = await admin.GetStringAsync($"/Administracion/ContactoUrgente?residenteId={residentId}");
        var designated = WebUtility.HtmlDecode(await (await admin.PostAsync("/Administracion/ContactoUrgente", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(contactPage, "__RequestVerificationToken"),
            ["Form.ResidenteId"] = residentId,
            ["Form.DesignacionesEsperadas"] = "1",
            ["Form.VinculosIds"] = linkId.ToString(),
        }))).Content.ReadAsStringAsync());

        Assert.Contains("Identidad corregida.", corrected);
        Assert.Contains("Familiar añadido.", added);
        Assert.Contains("Contactos urgentes guardados.", designated);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.residentes_identidad_correcciones WHERE residente_id = @residentId", new { residentId }));
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.familiares_autorizaciones_cambios WHERE vinculo_id = @linkId", new { linkId }));
        // El alta ya dejó un contacto prioritario; elegir otro lo quita y añade el nuevo: 1 + 2 designaciones.
        Assert.Equal(3, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.residentes_contacto_urgente WHERE residente_id = @residentId", new { residentId }));

        // Desvincular (script 0045): el único contacto urgente no se puede desvincular; el familiar del alta, que ya no lo es, sí.
        async Task<string> UnlinkAsync(string name, string reason)
        {
            var id = await connection.ExecuteScalarAsync<Guid>("""
                SELECT l.id FROM dbo.residentes_familiares l JOIN dbo.familiares f ON f.id = l.familiar_id
                 WHERE l.residente_id = @residentId AND f.nombre_visible = @name
                """, new { residentId, name });
            var page = await admin.GetStringAsync($"/Administracion/DesvincularFamiliar?residenteId={residentId}&vinculoId={id}");
            return WebUtility.HtmlDecode(await (await admin.PostAsync("/Administracion/DesvincularFamiliar", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken"),
                ["Form.ResidenteId"] = residentId,
                ["Form.VinculoId"] = id.ToString(),
                ["Form.Motivo"] = reason,
            }))).Content.ReadAsStringAsync());
        }

        var refusedUnlink = await UnlinkAsync("Familiar Funcional", "Prueba");
        var unlinkedPage = await UnlinkAsync("Familiar Prueba", "Ya no tiene relación con el residente.");

        Assert.Contains("único contacto urgente", refusedUnlink);
        Assert.Contains("Familiar desvinculado.", unlinkedPage);
        Assert.Contains("Familiares desvinculados", unlinkedPage);
        Assert.Contains("Ya no tiene relación con el residente.", unlinkedPage);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.residentes_familiares WHERE residente_id = @residentId AND desvinculado_en IS NOT NULL", new { residentId }));
    }

    [Fact]
    public async Task Administracion_TrasladaUnResidenteDeUnidad_ConLaAppEntera()
    {
        // El traslado por la app real con el usuario limitado: cierra y abre la ubicación, guarda el traslado (con seguridad por filas) y
        // audita. Un reenvío del mismo formulario no lo repite; sin permiso de unidad de destino, no se traslada.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Traslado Funcional");
        var destinationId = await GrantUnitAsync(seed, "Planta traslado funcional");
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();

        var page = await admin.GetStringAsync($"/Administracion/Trasladar?residenteId={residentId}");
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken"),
            ["Form.ResidenteId"] = residentId,
            ["Form.UnidadOrigenId"] = ExtractValue(page, "Form.UnidadOrigenId"),
            ["Form.OperacionId"] = ExtractValue(page, "Form.OperacionId"),
            ["Form.UnidadDestinoId"] = destinationId.ToString(),
        };
        var moved = WebUtility.HtmlDecode(await (await admin.PostAsync("/Administracion/Trasladar", new FormUrlEncodedContent(form))).Content.ReadAsStringAsync());
        var resent = WebUtility.HtmlDecode(await (await admin.PostAsync("/Administracion/Trasladar", new FormUrlEncodedContent(form))).Content.ReadAsStringAsync());

        Assert.Contains("Residente trasladado.", moved);
        Assert.Contains("Residente trasladado.", resent);
        Assert.Equal(destinationId, await connection.ExecuteScalarAsync<Guid>(
            "SELECT unidad_id FROM dbo.intervalos_ubicacion_residente WHERE residente_id = @residentId AND vigente_hasta IS NULL", new { residentId }));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.traslados_residente WHERE residente_id = @residentId", new { residentId }));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE residente_id = @residentId AND accion_codigo = 'RESIDENT_TRANSFER'", new { residentId }));
    }

    [Fact]
    public async Task Administracion_SuspendeDaDeBajaYReactivaUnResidente_ConLaAppEntera()
    {
        // Suspensión, reanudación, baja y reactivación por la app real con el usuario limitado: pasan por la seguridad por filas de las
        // tablas de bajas y suspensiones y por los disparadores de la base de datos.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Estado Funcional");
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();

        async Task<string> PostAsync(string path, string getPath, Dictionary<string, string> fields)
        {
            var page = await admin.GetStringAsync(getPath);
            fields["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken");
            if (fields.ContainsKey("Form.OperacionId"))
            {
                fields["Form.OperacionId"] = ExtractValue(page, "Form.OperacionId");
            }

            return WebUtility.HtmlDecode(await (await admin.PostAsync(path, new FormUrlEncodedContent(fields))).Content.ReadAsStringAsync());
        }

        var suspended = await PostAsync("/Administracion/Suspender", $"/Administracion/Suspender?residenteId={residentId}",
            new() { ["Form.ResidenteId"] = residentId, ["Form.OperacionId"] = "", ["Form.Nota"] = "Hospital de prueba." });
        var resumed = await PostAsync($"/Administracion/Reanudar?residenteId={residentId}", $"/Administracion/Residente?residenteId={residentId}", new());
        var discharged = await PostAsync("/Administracion/DarDeBaja", $"/Administracion/DarDeBaja?residenteId={residentId}",
            new() { ["Form.ResidenteId"] = residentId, ["Form.OperacionId"] = "", ["Form.Motivo"] = "TrasladoOtroCentro" });
        var reactivated = await PostAsync("/Administracion/Reactivar", $"/Administracion/Reactivar?residenteId={residentId}",
            new() { ["Form.ResidenteId"] = residentId, ["Form.OperacionId"] = "", ["Form.UnidadId"] = seed.UnitId.ToString() });

        Assert.Contains("Residente suspendido", suspended);
        Assert.Contains("Atención reanudada", resumed);
        Assert.Contains("se ha dado de baja", discharged);
        Assert.Contains("Residente Estado Funcional", discharged);
        Assert.Contains("Residente reactivado.", reactivated);
        Assert.Equal("ACTIVE", await connection.ExecuteScalarAsync<string>("SELECT estado FROM dbo.residentes WHERE id = @residentId", new { residentId }));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.bajas_residente WHERE residente_id = @residentId AND reactivada_en IS NOT NULL", new { residentId }));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.suspensiones_residente WHERE residente_id = @residentId AND finalizada_en IS NOT NULL", new { residentId }));
    }

    [Fact]
    public async Task Auxiliar_RegistraSinCambiosYUnCambioConOpciones_ConLaAppEntera()
    {
        // Los tres cierres cotidianos por la app real: el «sin cambios» (cierre del residente) y un cambio con un área de texto
        // y otra con opción rápida (cierre, áreas y opciones), con el usuario limitado.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var quietResident = await CreateResidentAsync(admin, seed, "Residente Sin Cambios Funcional");
        var changedResident = await CreateResidentAsync(admin, seed, "Residente Con Cambio Funcional");
        var auxiliar = await LoginAsync(await GrantAuxiliarAsync(seed, quietResident, changedResident));
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();

        var quietPage = await auxiliar.GetStringAsync($"/Auxiliar/Registro?residenteId={quietResident}");
        var quiet = WebUtility.HtmlDecode(await (await auxiliar.PostAsync("/Auxiliar/SinCambios", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(quietPage, "__RequestVerificationToken"),
            ["residenteId"] = quietResident,
            ["operacionId"] = Guid.NewGuid().ToString(),
        }))).Content.ReadAsStringAsync());

        var changePage = await auxiliar.GetStringAsync($"/Auxiliar/RegistrarCambio?residenteId={changedResident}");
        var changed = WebUtility.HtmlDecode(await (await auxiliar.PostAsync("/Auxiliar/ConfirmarCambio", new FormUrlEncodedContent(new[]
        {
            KeyValuePair.Create("__RequestVerificationToken", ExtractValue(changePage, "__RequestVerificationToken")),
            KeyValuePair.Create("ResidenteId", changedResident),
            KeyValuePair.Create("OperacionId", ExtractValue(changePage, "OperacionId")),
            KeyValuePair.Create("AreaOpciones", "ALIMENTACION_HIDRATACION:NULA_INGESTA"),
            KeyValuePair.Create("AreaTexto[ESTADO_CONCIENCIA]", "Más somnoliento de lo habitual (prueba funcional)."),
            KeyValuePair.Create("Clasificacion", "Ordinario"),
        }))).Content.ReadAsStringAsync());

        Assert.Contains("Cierre registrado: sin cambios.", quiet);
        Assert.Contains("Cambio registrado", changed);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.cierres_cotidianos_residente WHERE residente_id = @quietResident", new { quietResident }));
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.cierres_cotidianos_cambio_areas area
              JOIN dbo.cierres_cotidianos_residente closure ON closure.id = area.cierre_id
             WHERE closure.residente_id = @changedResident
            """, new { changedResident }));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.cierres_cotidianos_cambio_area_opciones option_row
              JOIN dbo.cierres_cotidianos_cambio_areas area ON area.id = option_row.area_id
              JOIN dbo.cierres_cotidianos_residente closure ON closure.id = area.cierre_id
             WHERE closure.residente_id = @changedResident
            """, new { changedResident }));
    }

    [Fact]
    public async Task Administracion_ConcedeYRevocaUnPermisoYAsignaUnResidente_ConLaAppEntera()
    {
        // Los permisos y las asignaciones de residente se escriben por la app real con el usuario limitado, y la autorización de la
        // cuenta afectada (que lee esas mismas tablas en cada petición) refleja el cambio: el Auxiliar ve al residente mientras está asignado.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Asignado Funcional");
        var auxiliarSubject = await GrantAuxiliarAsync(seed);
        var nurseSubject = await GrantNursingAsync(seed);
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        var (auxiliarAccount, auxiliarScope) = await connection.QuerySingleAsync<(Guid, Guid)>(
            "SELECT c.id, a.id FROM dbo.cuentas c JOIN dbo.ambitos_perfil a ON a.cuenta_id = c.id WHERE c.sujeto_externo = @auxiliarSubject",
            new { auxiliarSubject });
        var (nurseAccount, nurseScope) = await connection.QuerySingleAsync<(Guid, Guid)>(
            "SELECT c.id, a.id FROM dbo.cuentas c JOIN dbo.ambitos_perfil a ON a.cuenta_id = c.id WHERE c.sujeto_externo = @nurseSubject",
            new { nurseSubject });
        var auxiliar = await LoginAsync(auxiliarSubject);

        async Task<string> ChangeAsync(string action, Guid account, Guid scope, Dictionary<string, string> fields)
        {
            var page = await admin.GetStringAsync($"/Administracion/PerfilUsuario?cuentaId={account}&ambitoId={scope}");
            fields["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken");
            fields["cuentaId"] = account.ToString();
            fields["ambitoId"] = scope.ToString();
            return WebUtility.HtmlDecode(await (await admin.PostAsync($"/Administracion/{action}", new FormUrlEncodedContent(fields))).Content.ReadAsStringAsync());
        }

        var before = await auxiliar.GetStringAsync("/Auxiliar");
        var assigned = await ChangeAsync("ResidentePerfil", auxiliarAccount, auxiliarScope,
            new() { ["residenteId"] = residentId, ["asignar"] = "true" });
        var whileAssigned = await auxiliar.GetStringAsync("/Auxiliar");
        var retired = await ChangeAsync("ResidentePerfil", auxiliarAccount, auxiliarScope,
            new() { ["residenteId"] = residentId, ["asignar"] = "false" });
        var afterRetired = await auxiliar.GetStringAsync("/Auxiliar");

        var granted = await ChangeAsync("PermisoPerfil", nurseAccount, nurseScope,
            new() { ["permiso"] = "BASELINE_REEVALUATE", ["conceder"] = "true" });
        var revoked = await ChangeAsync("PermisoPerfil", nurseAccount, nurseScope,
            new() { ["permiso"] = "BASELINE_REEVALUATE", ["conceder"] = "false" });

        Assert.DoesNotContain("Residente Asignado Funcional", WebUtility.HtmlDecode(before));
        Assert.Contains("Residente asignado.", assigned);
        Assert.Contains("Residente Asignado Funcional", WebUtility.HtmlDecode(whileAssigned));
        Assert.Contains("Residente retirado.", retired);
        Assert.DoesNotContain("Residente Asignado Funcional", WebUtility.HtmlDecode(afterRetired));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ambitos_perfil_residente WHERE ambito_perfil_id = @auxiliarScope AND revocado_en IS NOT NULL", new { auxiliarScope }));
        Assert.Contains("Permiso concedido.", granted);
        Assert.Contains("Permiso revocado.", revoked);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.permisos_perfil WHERE ambito_perfil_id = @nurseScope AND permiso_codigo = 'BASELINE_REEVALUATE' AND revocado_en IS NOT NULL",
            new { nurseScope }));
    }

    private async Task<string> CreateResidentAsync(
        HttpClient admin, (string ExternalSubject, Guid ProfileScopeId, Guid CenterId, Guid UnitId) seed, string name)
    {
        var createPage = await admin.GetStringAsync("/Residents/Create");
        var created = await (await admin.PostAsync("/Residents/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(createPage, "__RequestVerificationToken"),
            ["OperacionId"] = ExtractValue(createPage, "OperacionId"),
            ["AmbitoPerfilId"] = seed.ProfileScopeId.ToString(),
            ["CentroId"] = seed.CenterId.ToString(),
            ["UnidadId"] = seed.UnitId.ToString(),
            ["NombreVisible"] = name,
            ["FechaNacimiento"] = "1938-02-20",
            ["SexoDocumentadoCodigo"] = "Hombre",
            ["Familiares[0].NombreVisible"] = "Familiar Prueba", ["Familiares[0].Relacion"] = "Hija", ["Familiares[0].Telefono"] = "600123456", ["Familiares[0].ContactoPrioritario"] = "true",
        }))).Content.ReadAsStringAsync();
        return Regex.Match(created, "Id del residente: <code>([0-9a-f-]{36})</code>").Groups[1].Value;
    }

    /// <summary>Una cuenta de Auxiliar con los residentes dados asignados, en el centro y la unidad de la semilla.</summary>
    private static async Task<string> GrantAuxiliarAsync(
        (string ExternalSubject, Guid ProfileScopeId, Guid CenterId, Guid UnitId) seed, params string[] residentIds)
    {
        var accountId = Guid.NewGuid();
        var profileScopeId = Guid.NewGuid();
        var externalSubject = $"functional-aux-{Guid.NewGuid().ToString("N")[..12]}";
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@accountId, @externalSubject, 'ACTIVE', @now);
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (@profileScopeId, @accountId, @centerId, 'AUXILIAR', 'ACTIVE', @now, @accountId);
            INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @profileScopeId, @centerId, @unitId, @now, @accountId);
            """, new { accountId, externalSubject, now, profileScopeId, centerId = seed.CenterId, unitId = seed.UnitId });
        foreach (var residentId in residentIds)
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.ambitos_perfil_residente (id, ambito_perfil_id, centro_id, residente_id, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @profileScopeId, @centerId, @residentId, @now, @accountId)
                """, new { profileScopeId, centerId = seed.CenterId, residentId = Guid.Parse(residentId), now, accountId });
        }
        return externalSubject;
    }

    private async Task<HttpClient> LoginAsync(string externalSubject)
    {
        var client = _factory.CreateClient();
        var loginPage = await client.GetStringAsync("/DevAuth/Login");
        (await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(loginPage, "__RequestVerificationToken"),
            ["externalSubject"] = externalSubject,
        }))).EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>Una cuenta de Dirección Clínica con permiso de lectura del detalle clínico, en el centro y la unidad de la semilla.</summary>
    private static async Task<string> GrantDirectionAsync((string ExternalSubject, Guid ProfileScopeId, Guid CenterId, Guid UnitId) seed)
    {
        var accountId = Guid.NewGuid();
        var profileScopeId = Guid.NewGuid();
        var externalSubject = $"functional-dir-{Guid.NewGuid().ToString("N")[..12]}";
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@accountId, @externalSubject, 'ACTIVE', @now);
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (@profileScopeId, @accountId, @centerId, 'DIRECCION_CLINICA', 'ACTIVE', @now, @accountId);
            INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @profileScopeId, @centerId, @unitId, @now, @accountId);
            INSERT INTO dbo.permisos_perfil (id, ambito_perfil_id, centro_id, permiso_codigo, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @profileScopeId, @centerId, 'CLINICAL_DETAIL_READ', @now, @accountId);
            """, new { accountId, externalSubject, now, profileScopeId, centerId = seed.CenterId, unitId = seed.UnitId });
        return externalSubject;
    }

    /// <summary>Una cuenta de Enfermería con permiso de basal inicial, en el centro y la unidad de la semilla.</summary>
    private static async Task<string> GrantNursingAsync((string ExternalSubject, Guid ProfileScopeId, Guid CenterId, Guid UnitId) seed)
    {
        var accountId = Guid.NewGuid();
        var profileScopeId = Guid.NewGuid();
        var externalSubject = $"functional-enf-{Guid.NewGuid().ToString("N")[..12]}";
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@accountId, @externalSubject, 'ACTIVE', @now);
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (@profileScopeId, @accountId, @centerId, 'ENFERMERIA', 'ACTIVE', @now, @accountId);
            INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @profileScopeId, @centerId, @unitId, @now, @accountId);
            INSERT INTO dbo.permisos_perfil (id, ambito_perfil_id, centro_id, permiso_codigo, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @profileScopeId, @centerId, 'BASELINE_INITIAL_COMPLETE', @now, @accountId);
            """, new { accountId, externalSubject, now, profileScopeId, centerId = seed.CenterId, unitId = seed.UnitId });
        return externalSubject;
    }

    private static async Task<Guid> GrantUnitAsync((string ExternalSubject, Guid ProfileScopeId, Guid CenterId, Guid UnitId) seed, string name)
    {
        var unitId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en) VALUES (@unitId, @centerId, @code, @name, 'ACTIVE', @now)",
            new { unitId, centerId = seed.CenterId, code = $"FUNC-UNIT-{unitId:N}"[..30], name, now });
        await connection.ExecuteAsync("""
            INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
            SELECT NEWID(), id, centro_id, @unitId, @now, cuenta_id FROM dbo.ambitos_perfil WHERE id = @profileScopeId
            """, new { unitId, now, profileScopeId = seed.ProfileScopeId });
        return unitId;
    }

    private static string ExtractValue(string html, string inputName) =>
        Regex.Match(html, $"name=\"{inputName}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;

    private async Task<(string ExternalSubject, Guid ProfileScopeId, Guid CenterId, Guid UnitId)> SeedAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var accountId = Guid.NewGuid();
        var centerId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var profileScopeId = Guid.NewGuid();
        var externalSubject = $"functional-{suffix}";
        var now = DateTimeOffset.UtcNow.UtcDateTime;

        var connections = new SqlConnectionFactory(WebAppFactory.TestConnectionString);
        using var connection = await connections.OpenAsync();

        await connection.ExecuteAsync(
            "INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@accountId, @externalSubject, 'ACTIVE', @now)",
            new { accountId, externalSubject, now });
        await connection.ExecuteAsync(
            "INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en) VALUES (@centerId, @code, @code, 'ACTIVE', @now)",
            new { centerId, code = $"FUNC-CENTER-{suffix}", now });
        await connection.ExecuteAsync(
            "INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en) VALUES (@unitId, @centerId, @code, @code, 'ACTIVE', @now)",
            new { unitId, centerId, code = $"FUNC-UNIT-{suffix}", now });
        await connection.ExecuteAsync("""
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (@profileScopeId, @accountId, @centerId, 'ADMINISTRACION', 'ACTIVE', @now, @accountId)
            """, new { profileScopeId, accountId, centerId, now });
        await connection.ExecuteAsync("""
            INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
            VALUES (@id, @profileScopeId, @centerId, @unitId, @now, @accountId)
            """, new { id = Guid.NewGuid(), profileScopeId, centerId, unitId, now, accountId });

        return (externalSubject, profileScopeId, centerId, unitId);
    }

    public class WebAppFactory : WebApplicationFactory<Program>
    {
        public static string TestConnectionString =>
            Environment.GetEnvironmentVariable("RESIDAPP_TEST_CONNECTION_STRING")
            ?? "Server=ACER-ORLANDO;Database=ResidApp;Integrated Security=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

        // Cadena con la que se conecta la aplicación. Por defecto es la de las pruebas (db_owner, para la que la seguridad por
        // filas es transparente). Con RESIDAPP_TEST_APP_CONNECTION_STRING la aplicación se conecta con el usuario limitado
        // (database/seguridad/crear_usuario_aplicacion.sql) mientras el sembrado y las comprobaciones siguen con TestConnectionString.
        public static string AppConnectionString =>
            Environment.GetEnvironmentVariable("RESIDAPP_TEST_APP_CONNECTION_STRING") ?? TestConnectionString;

        // Program.cs lee ConnectionStrings:ResidApp directamente sobre builder.Configuration antes de
        // builder.Build() (falla rápido si falta). Las sobrescrituras de WebApplicationFactory vía
        // ConfigureWebHost/ConfigureAppConfiguration solo se aplican en el momento de Build(), demasiado
        // tarde para ese chequeo — por eso se fija aquí como variable de entorno del proceso, que
        // CreateBuilder(args) sí incorpora desde el arranque, sea cual sea el entorno (Development o no).
        public WebAppFactory() =>
            Environment.SetEnvironmentVariable("ConnectionStrings__ResidApp", AppConnectionString);
    }
}
