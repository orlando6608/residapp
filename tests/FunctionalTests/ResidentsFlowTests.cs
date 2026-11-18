using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ResidApp.Infrastructure.Persistence;

namespace ResidApp.FunctionalTests;

/// <summary>Camino feliz a través de ResidApp.Web de verdad (Kestrel de pruebas + pipeline HTTP real):
/// sesión abierta con la entrada de pruebas (EntradaDePruebas) y alta de residente (Residents/Create). Automatiza lo verificado
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

        (await SesionTests.PostLoginAsync(client, seed.ExternalSubject)).EnsureSuccessStatusCode();

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
        (await SesionTests.PostLoginAsync(client, seed.ExternalSubject)).EnsureSuccessStatusCode();

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
            SELECT TOP 1 DATEDIFF(MINUTE, d.creada_en, d.caduca_en) FROM dbo.declaraciones_acceso_clinico d
             WHERE d.residente_id = @residentOne
             ORDER BY d.creada_en DESC
            """, new { residentOne });
        var rememberedPage = WebUtility.HtmlDecode(await direccion.GetStringAsync($"/Baseline/Direction?residenteId={residentOne}"));
        var reused = await QueryAsync(residentOne, null, null);
        var openAfterReuse = await OpenDeclarationsAsync();
        var other = await QueryAsync(residentTwo, null, null);
        var otherDeclared = await QueryAsync(residentTwo, "INCIDENCIA_RECLAMACION", "Reclamación familiar registrada (prueba funcional).");
        var openAfterOther = await OpenDeclarationsAsync();
        var backToOne = await QueryAsync(residentOne, null, null);

        (await direccion.PostAsync("/Acceso/Salir", new FormUrlEncodedContent(new Dictionary<string, string>
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
        var nurseSubject = await GrantNursingAsync(seed);
        var nurse = await LoginAsync(nurseSubject);

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
        var nurseSubject = await GrantNursingAsync(seed);
        var nurse = await LoginAsync(nurseSubject);
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
    public async Task Indicaciones_MedicinaNoCierraSinCumplirlas_YEnfermeriaCierraElEpisodio_ConLaAppEntera()
    {
        // CJ, 4.2 por la app real: Medicina indica algo a Enfermería y no puede cerrar mientras esté sin cumplir; al cumplirse, Enfermería
        // lo encuentra en «Para cerrar» y cierra el episodio sobre la valoración médica (el escalado ya cerró la suya).
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Cierre Indicaciones Funcional");
        var nurseSubject = await GrantNursingAsync(seed);
        var nurse = await LoginAsync(nurseSubject);
        var doctor = await LoginAsync(await GrantMedicineAsync(seed));
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();

        var registerPage = await nurse.GetStringAsync($"/Enfermeria/RegistrarEvento?residenteId={residentId}");
        var detailPage = await (await nurse.PostAsync("/Enfermeria/RegistrarEvento", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(registerPage, "__RequestVerificationToken"),
            ["ResidenteId"] = residentId,
            ["OperacionId"] = ExtractValue(registerPage, "OperacionId"),
            ["Observacion"] = "Disnea progresiva (prueba funcional).",
            ["Clasificacion"] = "Ordinario",
        }))).Content.ReadAsStringAsync();
        var eventId = await connection.ExecuteScalarAsync<Guid>(
            "SELECT id FROM dbo.eventos_asistenciales WHERE residente_id = @residentId", new { residentId });
        Task<int> RevisionAsync() => connection.ExecuteScalarAsync<int>("SELECT revision FROM dbo.eventos_asistenciales WHERE id = @eventId", new { eventId });
        Task<string> PostAsync(HttpClient client, string url, string page, Dictionary<string, string> fields)
        {
            fields["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken");
            return client.PostAsync(url, new FormUrlEncodedContent(fields)).ContinueWith(async r => WebUtility.HtmlDecode(await r.Result.Content.ReadAsStringAsync())).Unwrap();
        }

        await PostAsync(nurse, "/Enfermeria/EmpezarValoracion", detailPage, new() { ["eventoId"] = eventId.ToString(), ["revision"] = (await RevisionAsync()).ToString() });
        var assessmentPage = await nurse.GetStringAsync($"/Enfermeria/Valoracion?eventoId={eventId}");
        await PostAsync(nurse, "/Enfermeria/Valoracion", assessmentPage, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.Hallazgos"] = "Crepitantes en base derecha.",
        });
        var escalatePage = await nurse.GetStringAsync($"/Enfermeria/Escalar?eventoId={eventId}");
        await PostAsync(nurse, "/Enfermeria/Escalar", escalatePage, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.Motivo"] = "Disnea progresiva pese a oxigenoterapia.",
        });
        Assert.Equal("ESCALADO_MEDICINA", await connection.ExecuteScalarAsync<string>("SELECT estado_codigo FROM dbo.eventos_asistenciales WHERE id = @eventId", new { eventId }));

        var medicalDetail = await doctor.GetStringAsync($"/Medicina/Escalado?eventoId={eventId}");
        await PostAsync(doctor, "/Medicina/EmpezarValoracion", medicalDetail, new() { ["eventoId"] = eventId.ToString(), ["revision"] = (await RevisionAsync()).ToString() });
        var medicalAssessmentPage = await doctor.GetStringAsync($"/Medicina/Valoracion?eventoId={eventId}");
        await PostAsync(doctor, "/Medicina/Valoracion", medicalAssessmentPage, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.HallazgosExploracion"] = "Crepitantes bibasales.",
            ["Form.Valoracion"] = "Posible infección respiratoria.", ["Form.Actuaciones"] = "Se solicita radiografía.",
        });
        var indicationPage = await doctor.GetStringAsync($"/Medicina/Indicacion?eventoId={eventId}");
        await PostAsync(doctor, "/Medicina/Indicacion", indicationPage, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.Texto"] = "Control de SpO2 cada 4 horas.",
            ["Form.Criterio"] = "Si baja de 90 %, avisar.",
        });
        Assert.Equal("CON_INDICACION_PENDIENTE", await connection.ExecuteScalarAsync<string>("SELECT estado_codigo FROM dbo.eventos_asistenciales WHERE id = @eventId", new { eventId }));
        // Guía de pruebas 2, prueba 5: junto al residente consta el estado del evento, aparte del de cada indicación («Pendiente de lectura»).
        var afterIndication = WebUtility.HtmlDecode(await doctor.GetStringAsync($"/Medicina/Escalado?eventoId={eventId}"));
        Assert.Contains("Con indicación pendiente</span>", afterIndication);

        // Medicina ve el bloqueo y, si fuerza el envío, el mensaje.
        var blockedPage = WebUtility.HtmlDecode(await doctor.GetStringAsync($"/Medicina/Cerrar?eventoId={eventId}"));
        Assert.Contains("No se puede cerrar el evento hasta que Enfermería haya leído las indicaciones", blockedPage);
        Assert.Matches("<button[^>]*disabled[^>]*>Cerrar evento</button>", blockedPage);
        var forced = await PostAsync(doctor, "/Medicina/Cerrar", blockedPage, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.OperacionId"] = Guid.NewGuid().ToString(),
            ["Form.Comunicacion"] = "NoComunicar",
        });
        Assert.Contains("No se puede cerrar el evento mientras queden indicaciones sin cumplir", forced);
        Assert.Equal("CON_INDICACION_PENDIENTE", await connection.ExecuteScalarAsync<string>("SELECT estado_codigo FROM dbo.eventos_asistenciales WHERE id = @eventId", new { eventId }));

        // Enfermería no lo tiene aún en «Para cerrar»; lee y realiza la indicación, y la pantalla le ofrece cerrar.
        Assert.DoesNotContain("Residente Cierre Indicaciones Funcional", WebUtility.HtmlDecode(await nurse.GetStringAsync("/Enfermeria/ParaCerrar")));
        var indicationId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.indicaciones_medicas WHERE evento_id = @eventId", new { eventId });
        Task<int> IndicationRevisionAsync() => connection.ExecuteScalarAsync<int>("SELECT revision FROM dbo.indicaciones_medicas WHERE id = @indicationId", new { indicationId });
        var inbox = await nurse.GetStringAsync("/Enfermeria/Indicaciones");
        await PostAsync(nurse, "/Enfermeria/ProgresoIndicacion", inbox, new()
        {
            ["eventoId"] = eventId.ToString(), ["indicacionId"] = indicationId.ToString(), ["revision"] = (await IndicationRevisionAsync()).ToString(),
        });
        var afterDone = await PostAsync(nurse, "/Enfermeria/ProgresoIndicacion", inbox, new()
        {
            ["eventoId"] = eventId.ToString(), ["indicacionId"] = indicationId.ToString(), ["revision"] = (await IndicationRevisionAsync()).ToString(),
            ["realizada"] = "true",
        });
        Assert.Contains("Ya están cumplidas todas las indicaciones de este episodio: puedes cerrarlo.", afterDone);
        Assert.Contains("Cerrar episodio", afterDone);

        var forNurse = WebUtility.HtmlDecode(await nurse.GetStringAsync("/Enfermeria/ParaCerrar"));
        Assert.Contains("Residente Cierre Indicaciones Funcional", forNurse);
        var closePage = WebUtility.HtmlDecode(await nurse.GetStringAsync($"/Enfermeria/Cerrar?eventoId={eventId}"));
        Assert.Contains("Resumen de la valoración médica", closePage);
        Assert.Contains("Control de SpO2 cada 4 horas.", closePage);
        var closed = await PostAsync(nurse, "/Enfermeria/Cerrar", closePage, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.OperacionId"] = Guid.NewGuid().ToString(),
            ["Form.Comunicacion"] = "NoComunicar",
        });

        Assert.Contains("Evento cerrado.", closed);
        Assert.Contains("Cerrado por ti", closed);
        Assert.Contains("Cerrado por otro profesional de Enfermería", WebUtility.HtmlDecode(await doctor.GetStringAsync($"/Medicina/Escalado?eventoId={eventId}")));
        Assert.Equal("CERRADO", await connection.ExecuteScalarAsync<string>("SELECT estado_codigo FROM dbo.eventos_asistenciales WHERE id = @eventId", new { eventId }));
        Assert.Equal("CERRADA", await connection.ExecuteScalarAsync<string>("SELECT estado_codigo FROM dbo.valoraciones_medicas WHERE evento_id = @eventId", new { eventId }));
        Assert.DoesNotContain("Residente Cierre Indicaciones Funcional", WebUtility.HtmlDecode(await nurse.GetStringAsync("/Enfermeria/ParaCerrar")));
    }

    [Fact]
    public async Task Indicaciones_MedicinaRegistraVariasSeguidasSinSalirDelFormulario_ConLaAppEntera()
    {
        // Guía de pruebas 2, prueba 5 (CJ): «Registrar y añadir otra» guarda y devuelve el formulario en blanco, sin pasar por el detalle.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Varias Indicaciones Funcional");
        var nurse = await LoginAsync(await GrantNursingAsync(seed));
        var doctor = await LoginAsync(await GrantMedicineAsync(seed));
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();

        var registerPage = await nurse.GetStringAsync($"/Enfermeria/RegistrarEvento?residenteId={residentId}");
        var detailPage = await (await nurse.PostAsync("/Enfermeria/RegistrarEvento", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(registerPage, "__RequestVerificationToken"),
            ["ResidenteId"] = residentId,
            ["OperacionId"] = ExtractValue(registerPage, "OperacionId"),
            ["Observacion"] = "Disnea progresiva (prueba funcional).",
            ["Clasificacion"] = "Ordinario",
        }))).Content.ReadAsStringAsync();
        var eventId = await connection.ExecuteScalarAsync<Guid>(
            "SELECT id FROM dbo.eventos_asistenciales WHERE residente_id = @residentId", new { residentId });
        Task<int> RevisionAsync() => connection.ExecuteScalarAsync<int>("SELECT revision FROM dbo.eventos_asistenciales WHERE id = @eventId", new { eventId });
        Task<string> PostAsync(HttpClient client, string url, string page, Dictionary<string, string> fields)
        {
            fields["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken");
            return client.PostAsync(url, new FormUrlEncodedContent(fields)).ContinueWith(async r => WebUtility.HtmlDecode(await r.Result.Content.ReadAsStringAsync())).Unwrap();
        }

        await PostAsync(nurse, "/Enfermeria/EmpezarValoracion", detailPage, new() { ["eventoId"] = eventId.ToString(), ["revision"] = (await RevisionAsync()).ToString() });
        var assessmentPage = await nurse.GetStringAsync($"/Enfermeria/Valoracion?eventoId={eventId}");
        await PostAsync(nurse, "/Enfermeria/Valoracion", assessmentPage, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.Hallazgos"] = "Crepitantes en base derecha.",
        });
        var escalatePage = await nurse.GetStringAsync($"/Enfermeria/Escalar?eventoId={eventId}");
        await PostAsync(nurse, "/Enfermeria/Escalar", escalatePage, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.Motivo"] = "Disnea progresiva pese a oxigenoterapia.",
        });
        var medicalDetail = await doctor.GetStringAsync($"/Medicina/Escalado?eventoId={eventId}");
        await PostAsync(doctor, "/Medicina/EmpezarValoracion", medicalDetail, new() { ["eventoId"] = eventId.ToString(), ["revision"] = (await RevisionAsync()).ToString() });
        var medicalAssessmentPage = await doctor.GetStringAsync($"/Medicina/Valoracion?eventoId={eventId}");
        await PostAsync(doctor, "/Medicina/Valoracion", medicalAssessmentPage, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.HallazgosExploracion"] = "Crepitantes bibasales.",
            ["Form.Valoracion"] = "Posible infección respiratoria.", ["Form.Actuaciones"] = "Se solicita radiografía.",
        });

        var indicationPage = WebUtility.HtmlDecode(await doctor.GetStringAsync($"/Medicina/Indicacion?eventoId={eventId}"));
        Assert.Contains("Registrar y añadir otra", indicationPage);
        var afterFirst = await PostAsync(doctor, "/Medicina/Indicacion", indicationPage, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.Texto"] = "Control de SpO2 cada 4 horas.",
            ["Form.Criterio"] = "Si baja de 90 %, avisar.", ["otra"] = "true",
        });
        // Vuelve al formulario en blanco, con el aviso, y la indicación ya guardada.
        Assert.Contains("Indicación registrada.", afterFirst);
        Assert.Contains("Puedes añadir otra.", afterFirst);
        Assert.Contains("Registrar y añadir otra", afterFirst);
        Assert.DoesNotContain("Control de SpO2 cada 4 horas.", afterFirst);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.indicaciones_medicas WHERE evento_id = @eventId", new { eventId }));

        // La segunda, con el botón normal, vuelve al detalle.
        var afterSecond = await PostAsync(doctor, "/Medicina/Indicacion", afterFirst, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.Texto"] = "Vigilar la ingesta de líquidos.",
            ["Form.Criterio"] = "Cada turno.",
        });
        Assert.DoesNotContain("Puedes añadir otra.", afterSecond);
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.indicaciones_medicas WHERE evento_id = @eventId", new { eventId }));
    }

    private static Task<int> EventRevisionAsync(SqlConnection connection, Guid eventId) =>
        connection.ExecuteScalarAsync<int>("SELECT revision FROM dbo.eventos_asistenciales WHERE id = @eventId", new { eventId });

    private static async Task<string> PostFormPageAsync(HttpClient client, string url, string page, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken");
        return WebUtility.HtmlDecode(await (await client.PostAsync(url, new FormUrlEncodedContent(fields))).Content.ReadAsStringAsync());
    }

    /// <summary>Un escalado de Enfermería con la valoración médica ya empezada (estado «En valoración médica») y sin ninguna indicación: el punto de partida de las pruebas
    /// del botón «Guardar e indicar a Enfermería». El llamador cierra la conexión.</summary>
    private async Task<(HttpClient Doctor, Guid EventId, SqlConnection Connection)> EventInMedicalValuationAsync(string residentName)
    {
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, seed, residentName);
        var nurse = await LoginAsync(await GrantNursingAsync(seed));
        var doctor = await LoginAsync(await GrantMedicineAsync(seed));
        var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();

        var registerPage = await nurse.GetStringAsync($"/Enfermeria/RegistrarEvento?residenteId={residentId}");
        var detailPage = await (await nurse.PostAsync("/Enfermeria/RegistrarEvento", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(registerPage, "__RequestVerificationToken"),
            ["ResidenteId"] = residentId,
            ["OperacionId"] = ExtractValue(registerPage, "OperacionId"),
            ["Observacion"] = "Disnea progresiva (prueba funcional).",
            ["Clasificacion"] = "Ordinario",
        }))).Content.ReadAsStringAsync();
        var eventId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.eventos_asistenciales WHERE residente_id = @residentId", new { residentId });

        await PostFormPageAsync(nurse, "/Enfermeria/EmpezarValoracion", detailPage, new() { ["eventoId"] = eventId.ToString(), ["revision"] = (await EventRevisionAsync(connection, eventId)).ToString() });
        var assessmentPage = await nurse.GetStringAsync($"/Enfermeria/Valoracion?eventoId={eventId}");
        await PostFormPageAsync(nurse, "/Enfermeria/Valoracion", assessmentPage, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await EventRevisionAsync(connection, eventId)).ToString(), ["Form.Hallazgos"] = "Crepitantes en base derecha.",
        });
        var escalatePage = await nurse.GetStringAsync($"/Enfermeria/Escalar?eventoId={eventId}");
        await PostFormPageAsync(nurse, "/Enfermeria/Escalar", escalatePage, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await EventRevisionAsync(connection, eventId)).ToString(), ["Form.Motivo"] = "Disnea progresiva pese a oxigenoterapia.",
        });
        var medicalDetail = await doctor.GetStringAsync($"/Medicina/Escalado?eventoId={eventId}");
        await PostFormPageAsync(doctor, "/Medicina/EmpezarValoracion", medicalDetail, new() { ["eventoId"] = eventId.ToString(), ["revision"] = (await EventRevisionAsync(connection, eventId)).ToString() });
        return (doctor, eventId, connection);
    }

    [Fact]
    public async Task Valoracion_GuardarEIndicar_GuardaYSaltaAlFormularioDeIndicaciones_ConLaAppEntera()
    {
        // Guía de pruebas 2, prueba 9 (CJ): desde la valoración, «Guardar e indicar a Enfermería» guarda y abre el formulario de indicaciones, sin pasar por el
        // detalle ni la conducta. «Guardar borrador» sigue yendo al detalle (y es el envío por defecto: va antes en el código).
        var (doctor, eventId, connection) = await EventInMedicalValuationAsync("Residente Guardar E Indicar Funcional");
        using var _ = connection;
        Task<int> VersionsAsync() => connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.valoraciones_medicas_versiones WHERE evento_id = @eventId", new { eventId });

        var medicalAssessmentPage = WebUtility.HtmlDecode(await doctor.GetStringAsync($"/Medicina/Valoracion?eventoId={eventId}"));
        Assert.Contains("Guardar e indicar a Enfermería", medicalAssessmentPage);
        Assert.True(medicalAssessmentPage.IndexOf("Guardar borrador", StringComparison.Ordinal) < medicalAssessmentPage.IndexOf("Guardar e indicar a Enfermería", StringComparison.Ordinal),
            "«Guardar borrador» debe ir antes en el código para ser el envío por defecto.");

        // Sin «indicar» no cambia nada: vuelve al detalle del escalado.
        var afterDraft = await PostFormPageAsync(doctor, "/Medicina/Valoracion", medicalAssessmentPage, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await EventRevisionAsync(connection, eventId)).ToString(), ["Form.HallazgosExploracion"] = "Crepitantes bibasales.",
            ["Form.Valoracion"] = "Posible infección respiratoria.", ["Form.Actuaciones"] = "Se solicita radiografía.",
        });
        Assert.Contains("Valoración médica guardada.", afterDraft);
        Assert.DoesNotContain("Escribe ahora la indicación", afterDraft);
        Assert.Contains("Pasar a conducta médica", afterDraft);
        var versionsAfterDraft = await VersionsAsync();

        // Con «indicar» guarda (otra versión, con el contenido nuevo) y salta al formulario de indicaciones, con el aviso de la valoración y sin el «Puedes añadir otra».
        var indicationPage = await PostFormPageAsync(doctor, "/Medicina/Valoracion", medicalAssessmentPage, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await EventRevisionAsync(connection, eventId)).ToString(), ["Form.HallazgosExploracion"] = "Crepitantes bibasales y sibilancias.",
            ["Form.Valoracion"] = "Posible infección respiratoria.", ["Form.Actuaciones"] = "Se solicita radiografía.", ["indicar"] = "true",
        });
        Assert.Contains("Valoración médica guardada. Escribe ahora la indicación para Enfermería.", indicationPage);
        Assert.Contains("Indicación a Enfermería —", indicationPage);
        Assert.Contains("Registrar y añadir otra", indicationPage);
        Assert.DoesNotContain("Puedes añadir otra.", indicationPage);
        Assert.Equal(versionsAfterDraft + 1, await VersionsAsync());
        Assert.Equal("EN_VALORACION_MEDICA", await connection.ExecuteScalarAsync<string>("SELECT estado_codigo FROM dbo.eventos_asistenciales WHERE id = @eventId", new { eventId }));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.indicaciones_medicas WHERE evento_id = @eventId", new { eventId }));

        // El aviso se muestra una sola vez: al recargar el formulario ya no está.
        Assert.DoesNotContain("Escribe ahora la indicación para Enfermería.", WebUtility.HtmlDecode(await doctor.GetStringAsync($"/Medicina/Indicacion?eventoId={eventId}")));

        // Desde ahí se registra la indicación como siempre y el evento pasa a «Con indicación pendiente».
        var afterIndication = await PostFormPageAsync(doctor, "/Medicina/Indicacion", indicationPage, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await EventRevisionAsync(connection, eventId)).ToString(), ["Form.Texto"] = "Control de SpO2 cada 4 horas.",
            ["Form.Criterio"] = "Si baja de 90 %, avisar.",
        });
        Assert.Contains("Con indicación pendiente", afterIndication);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.indicaciones_medicas WHERE evento_id = @eventId", new { eventId }));

        // Con el evento ya con indicaciones la valoración no se abre (no se ofrece el botón) y un formulario viejo no puede guardar ni saltar.
        Assert.DoesNotContain("Guardar e indicar a Enfermería", WebUtility.HtmlDecode(await doctor.GetStringAsync($"/Medicina/Valoracion?eventoId={eventId}")));
        var versionsBeforeStale = await VersionsAsync();
        var stale = await PostFormPageAsync(doctor, "/Medicina/Valoracion", medicalAssessmentPage, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await EventRevisionAsync(connection, eventId)).ToString(), ["Form.HallazgosExploracion"] = "Un formulario viejo.",
            ["Form.Valoracion"] = "Un formulario viejo.", ["indicar"] = "true",
        });
        Assert.DoesNotContain("Escribe ahora la indicación para Enfermería.", stale);
        Assert.Equal(versionsBeforeStale, await VersionsAsync());
    }

    [Fact]
    public async Task Valoracion_GuardarEIndicar_ConRevisionAntiguaOSinDatos_MuestraElErrorYNoSalta_ConLaAppEntera()
    {
        var (doctor, eventId, connection) = await EventInMedicalValuationAsync("Residente Indicar Con Error Funcional");
        using var _ = connection;
        Task<int> VersionsAsync() => connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.valoraciones_medicas_versiones WHERE evento_id = @eventId", new { eventId });
        var page = WebUtility.HtmlDecode(await doctor.GetStringAsync($"/Medicina/Valoracion?eventoId={eventId}"));
        var revision = await EventRevisionAsync(connection, eventId);

        // Sin ningún dato: «Revisa los datos…», se vuelve a pintar la valoración, no se salta y no se guarda nada.
        var empty = await PostFormPageAsync(doctor, "/Medicina/Valoracion", page, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = revision.ToString(), ["indicar"] = "true",
        });
        Assert.Contains("Revisa los datos: la valoración necesita al menos un dato", empty);
        Assert.DoesNotContain("Indicación a Enfermería —", empty);
        Assert.DoesNotContain("Escribe ahora la indicación para Enfermería.", empty);
        Assert.Equal(0, await VersionsAsync());

        // Con una revisión antigua (otro profesional cambió el evento): se pide recargar, lo escrito sigue en el formulario y no se salta.
        var stale = await PostFormPageAsync(doctor, "/Medicina/Valoracion", page, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (revision - 1).ToString(), ["Form.HallazgosExploracion"] = "Hallazgos que no se pierden.", ["indicar"] = "true",
        });
        Assert.Contains("Lo que has escrito sigue aquí para que puedas copiarlo.", stale);
        Assert.Contains("Hallazgos que no se pierden.", stale);
        Assert.DoesNotContain("Indicación a Enfermería —", stale);
        Assert.Equal(0, await VersionsAsync());

        // Y el aviso no queda guardado para otra pantalla: la indicación se abre sin él.
        Assert.DoesNotContain("Escribe ahora la indicación para Enfermería.", WebUtility.HtmlDecode(await doctor.GetStringAsync($"/Medicina/Indicacion?eventoId={eventId}")));
    }

    [Fact]
    public async Task Direccion_CierraUnEpisodioConIndicacionesSinCumplir_ConMotivo_ConLaAppEntera()
    {
        // CJ, tercera 5.1 (script 0075): con una indicación sin cumplir nadie más cierra el episodio; Dirección Clínica sí, con motivo y confirmación.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Cierre Excepcional Funcional");
        var nurse = await LoginAsync(await GrantNursingAsync(seed));
        var doctor = await LoginAsync(await GrantMedicineAsync(seed));
        var direction = await LoginAsync(await GrantDirectionAsync(seed));
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();

        var registerPage = await nurse.GetStringAsync($"/Enfermeria/RegistrarEvento?residenteId={residentId}");
        var detailPage = await (await nurse.PostAsync("/Enfermeria/RegistrarEvento", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(registerPage, "__RequestVerificationToken"),
            ["ResidenteId"] = residentId,
            ["OperacionId"] = ExtractValue(registerPage, "OperacionId"),
            ["Observacion"] = "Disnea progresiva (prueba funcional).",
            ["Clasificacion"] = "Ordinario",
        }))).Content.ReadAsStringAsync();
        var eventId = await connection.ExecuteScalarAsync<Guid>(
            "SELECT id FROM dbo.eventos_asistenciales WHERE residente_id = @residentId", new { residentId });
        Task<int> RevisionAsync() => connection.ExecuteScalarAsync<int>("SELECT revision FROM dbo.eventos_asistenciales WHERE id = @eventId", new { eventId });
        Task<string> PostAsync(HttpClient client, string url, string page, Dictionary<string, string> fields)
        {
            fields["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken");
            return client.PostAsync(url, new FormUrlEncodedContent(fields)).ContinueWith(async r => WebUtility.HtmlDecode(await r.Result.Content.ReadAsStringAsync())).Unwrap();
        }

        await PostAsync(nurse, "/Enfermeria/EmpezarValoracion", detailPage, new() { ["eventoId"] = eventId.ToString(), ["revision"] = (await RevisionAsync()).ToString() });
        var assessmentPage = await nurse.GetStringAsync($"/Enfermeria/Valoracion?eventoId={eventId}");
        await PostAsync(nurse, "/Enfermeria/Valoracion", assessmentPage, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.Hallazgos"] = "Crepitantes en base derecha.",
        });
        var escalatePage = await nurse.GetStringAsync($"/Enfermeria/Escalar?eventoId={eventId}");
        await PostAsync(nurse, "/Enfermeria/Escalar", escalatePage, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.Motivo"] = "Disnea progresiva pese a oxigenoterapia.",
        });
        var medicalDetail = await doctor.GetStringAsync($"/Medicina/Escalado?eventoId={eventId}");
        await PostAsync(doctor, "/Medicina/EmpezarValoracion", medicalDetail, new() { ["eventoId"] = eventId.ToString(), ["revision"] = (await RevisionAsync()).ToString() });
        var medicalAssessmentPage = await doctor.GetStringAsync($"/Medicina/Valoracion?eventoId={eventId}");
        await PostAsync(doctor, "/Medicina/Valoracion", medicalAssessmentPage, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.HallazgosExploracion"] = "Crepitantes bibasales.",
            ["Form.Valoracion"] = "Posible infección respiratoria.", ["Form.Actuaciones"] = "Se solicita radiografía.",
        });
        var indicationPage = await doctor.GetStringAsync($"/Medicina/Indicacion?eventoId={eventId}");
        await PostAsync(doctor, "/Medicina/Indicacion", indicationPage, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.Texto"] = "Control de SpO2 cada 4 horas.",
            ["Form.Criterio"] = "Si baja de 90 %, avisar.",
        });
        Assert.Equal("CON_INDICACION_PENDIENTE", await connection.ExecuteScalarAsync<string>("SELECT estado_codigo FROM dbo.eventos_asistenciales WHERE id = @eventId", new { eventId }));

        // Dirección ve el episodio con el aviso y el formulario (solo hay uno si hay indicaciones abiertas); Enfermería no puede enviarlo.
        var episodeUrl = $"/Direccion/Episodio?eventoId={eventId}";
        var episode = WebUtility.HtmlDecode(await direction.GetStringAsync(episodeUrl));
        Assert.Contains("Cerrar el episodio con indicaciones sin cumplir", episode);
        Assert.Contains("1 indicación médica abierta", episode);
        Assert.Contains("no se tocan", episode);
        Assert.DoesNotContain("Control de SpO2 cada 4 horas.", episode);
        var byNurse = await PostAsync(nurse, "/Direccion/CerrarEpisodio", await nurse.GetStringAsync("/Enfermeria"), new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Motivo"] = "Intento de Enfermería.", ["Form.Confirmo"] = "true",
        });
        Assert.DoesNotContain("Episodio cerrado", byNurse);
        Assert.Equal("CON_INDICACION_PENDIENTE", await connection.ExecuteScalarAsync<string>("SELECT estado_codigo FROM dbo.eventos_asistenciales WHERE id = @eventId", new { eventId }));

        // Sin la casilla de confirmación o sin motivo, no se cierra.
        var withoutConfirmation = await PostAsync(direction, "/Direccion/CerrarEpisodio", episode, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Motivo"] = "Nadie va a cumplirla.",
        });
        var withoutReason = await PostAsync(direction, "/Direccion/CerrarEpisodio", episode, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Motivo"] = "  ", ["Form.Confirmo"] = "true",
        });
        Assert.Contains("Marca la casilla", withoutConfirmation);
        Assert.Contains("Escribe el motivo", withoutReason);
        Assert.Equal("CON_INDICACION_PENDIENTE", await connection.ExecuteScalarAsync<string>("SELECT estado_codigo FROM dbo.eventos_asistenciales WHERE id = @eventId", new { eventId }));

        var closed = await PostAsync(direction, "/Direccion/CerrarEpisodio", episode, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Motivo"] = "La residente fue trasladada a otro hospital.", ["Form.Confirmo"] = "true",
        });

        Assert.Contains("Episodio cerrado. Las indicaciones médicas se quedan sin resolver", closed);
        Assert.Equal(("CERRADO", "DIRECCION_EXCEPCIONAL", "NO_COMUNICAR"), await connection.QuerySingleAsync<(string, string, string)>(
            "SELECT estado_codigo, cierre_sistema_codigo, comunicacion_familiar_codigo FROM dbo.eventos_asistenciales WHERE id = @eventId", new { eventId }));
        Assert.Equal("PENDIENTE_LECTURA", await connection.ExecuteScalarAsync<string>("SELECT estado_codigo FROM dbo.indicaciones_medicas WHERE evento_id = @eventId", new { eventId }));
        Assert.Equal("La residente fue trasladada a otro hospital.", await connection.ExecuteScalarAsync<string>(
            "SELECT motivo FROM dbo.cierres_excepcionales_direccion WHERE evento_id = @eventId", new { eventId }));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE recurso_id = @eventId AND accion_codigo = 'CLINICAL_EVENT_CLOSE_EXCEPTIONAL' AND perfil_activo = 'DIRECCION_CLINICA'", new { eventId }));
        // Ya cerrado, el episodio no se abre desde Dirección: vuelve a los pendientes sin distinguir el motivo.
        var reopened = await direction.GetAsync(episodeUrl);
        Assert.DoesNotContain("Cerrar el episodio con indicaciones sin cumplir", WebUtility.HtmlDecode(await reopened.Content.ReadAsStringAsync()));
    }
    [Fact]
    public async Task Enfermeria_DerivaAUrgenciasConComunicaciones_VistaPreviaFirmaYPdf_ConLaAppEntera()
    {
        // La pantalla de derivación de punta a punta: el apartado «Comunicaciones» se escribe, sale en la vista previa, viaja con la
        // huella al firmar y queda en el informe (con el usuario limitado, bajo la seguridad por filas).
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Derivacion Funcional");
        var nurseSubject = await GrantNursingAsync(seed);
        var nurse = await LoginAsync(nurseSubject);
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
        Assert.Contains("Las alergias a medicamentos, las", WebUtility.HtmlDecode(referralPage));
        Assert.Contains("enfermedades e intervenciones y la medicación habitual salen solas de la ficha del residente", WebUtility.HtmlDecode(referralPage));
        Assert.Contains("aria-describedby=\"derivar-informacion-ayuda\"", referralPage);
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
        Assert.Contains("Alergias e intolerancias a medicamentos", preview);
        Assert.Contains("Ficha pendiente de rellenar en este centro", preview);
        Assert.Contains("Enfermedades e intervenciones", preview);
        Assert.Contains("No equivale a «sin enfermedades»", preview);
        Assert.Contains("Medicación habitual", preview);
        Assert.Contains("No equivale a «no toma medicación»", preview);
        Assert.Contains(communications, preview);
        Assert.Contains("Informe de derivación firmado.", signed);
        Assert.Contains(communications, signed);
        Assert.Equal(communications, await connection.ExecuteScalarAsync<string>(
            "SELECT comunicaciones FROM dbo.informes_derivacion WHERE evento_id = @eventId", new { eventId }));

        // Guía de pruebas 1, prueba 12: tras derivar no hay nada que decidir; «Preparar comunicación» no es un botón que parezca no hacer nada.
        var closePage = await nurse.GetStringAsync($"/Enfermeria/Cerrar?eventoId={eventId}");
        var decisionInput = System.Text.RegularExpressions.Regex.Match(closePage, "<input[^>]*name=\"Form.Comunicacion\"[^>]*>").Value;
        Assert.Contains("type=\"hidden\"", decisionInput);
        Assert.Contains("value=\"Preparar\"", decisionInput);
        Assert.DoesNotContain("type=\"radio\"", closePage);
    }

    [Fact]
    public async Task BaselineSign_SinPantallaPropia_RedirigeAResidentes()
    {
        var seed = await SeedAsync();
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SesionTests.PostLoginAsync(client, seed.ExternalSubject);
        var tokenPage = await client.GetStringAsync("/Acceso/Solicitar");

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
    public async Task Administracion_ConfirmaLaDireccionDelFamiliar_LaQuitaYLaPierdeAlCambiarla_ConLaAppEntera()
    {
        // Script 0084: con el usuario limitado de la aplicación y la seguridad por filas, la casilla se guarda, se enseña, se quita y se pierde al cambiar la dirección.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Dirección Funcional");
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();

        var addPage = WebUtility.HtmlDecode(await admin.GetStringAsync($"/Administracion/AnadirFamiliar?residenteId={residentId}"));
        Assert.Contains("Correo para avisos de ResidApp (opcional)", addPage);
        Assert.Contains("Dirección confirmada con la familia: se la he leído a la persona a quien pertenece y me ha dicho que es correcta", addPage);
        Assert.DoesNotContain("name=\"Form.ConfirmarCorreo\" checked", addPage);
        Assert.Contains(ResidApp.Infrastructure.Correo.CorreoOptions.RemitenteDefecto, addPage);
        var added = WebUtility.HtmlDecode(await (await admin.PostAsync("/Administracion/AnadirFamiliar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(addPage, "__RequestVerificationToken"),
            ["Form.ResidenteId"] = residentId,
            ["Form.OperacionId"] = ExtractValue(addPage, "Form.OperacionId"),
            ["Form.NombreVisible"] = "Familiar Dirección",
            ["Form.Relacion"] = "Hija",
            ["Form.Telefono"] = "600000000",
            ["Form.Correo"] = "familiar.direccion@example.test",
        }))).Content.ReadAsStringAsync());
        var linkId = await connection.ExecuteScalarAsync<Guid>(
            """
            SELECT l.id FROM dbo.residentes_familiares l JOIN dbo.familiares f ON f.id = l.familiar_id
             WHERE l.residente_id = @residentId AND f.nombre_visible = 'Familiar Dirección'
            """, new { residentId });
        var fichaSinConfirmar = WebUtility.HtmlDecode(await admin.GetStringAsync($"/Administracion/Residente?residenteId={residentId}"));

        async Task<string> EditAsync(string email, bool confirm)
        {
            var page = await admin.GetStringAsync($"/Administracion/EditarFamiliar?residenteId={residentId}&vinculoId={linkId}");
            var form = new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken"),
                ["Form.ResidenteId"] = residentId,
                ["Form.VinculoId"] = linkId.ToString(),
                ["Form.Version"] = ExtractValue(page, "Form.Version"),
                ["Form.NombreVisible"] = "Familiar Dirección",
                ["Form.Relacion"] = "Hija",
                ["Form.Telefono"] = "600000000",
                ["Form.Correo"] = email,
            };
            if (confirm)
            {
                form["Form.ConfirmarCorreo"] = "true";
            }

            return WebUtility.HtmlDecode(await (await admin.PostAsync("/Administracion/EditarFamiliar", new FormUrlEncodedContent(form))).Content.ReadAsStringAsync());
        }

        var confirmed = await EditAsync("familiar.direccion@example.test", confirm: true);
        var fichaConfirmada = WebUtility.HtmlDecode(await admin.GetStringAsync($"/Administracion/Residente?residenteId={residentId}"));
        var editPage = WebUtility.HtmlDecode(await admin.GetStringAsync($"/Administracion/EditarFamiliar?residenteId={residentId}&vinculoId={linkId}"));
        var removed = WebUtility.HtmlDecode(await (await admin.PostAsync("/Administracion/QuitarConfirmacionCorreo", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(editPage.Replace("&#x2B;", "+"), "__RequestVerificationToken"),
            ["residenteId"] = residentId,
            ["vinculoId"] = linkId.ToString(),
            ["version"] = ExtractValue(editPage, "Form.Version"),
        }))).Content.ReadAsStringAsync());
        var withoutAddress = await EditAsync("", confirm: true);

        Assert.Contains("Familiar añadido.", added);
        Assert.Contains("Correo sin confirmar: ResidApp no enviará avisos a esta dirección.", fichaSinConfirmar);
        Assert.Contains("Datos del familiar guardados.", confirmed);
        Assert.Contains("Dirección confirmada el ", fichaConfirmada);
        Assert.DoesNotContain("Correo sin confirmar", fichaConfirmada);
        Assert.Contains("Quitar la confirmación de la dirección", editPage);
        Assert.Contains("Confirmación de la dirección quitada", removed);
        Assert.Contains("Correo sin confirmar: ResidApp no enviará avisos a esta dirección.", removed);
        Assert.Contains("Escribe el correo para poder confirmarlo.", withoutAddress);
        Assert.Equal(
            ["FAMILY_EMAIL_CONFIRM", "FAMILY_EMAIL_UNCONFIRM"],
            (await connection.QueryAsync<string>(
                "SELECT accion_codigo FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE residente_id = @residentId AND accion_codigo LIKE 'FAMILY_EMAIL_%' ORDER BY accion_codigo",
                new { residentId })).ToArray());
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

        // Desvincular (script 0045): el único contacto urgente sí se puede desvincular (CJ, 2026-10-10), con aviso: el residente se queda sin contacto
        // urgente y el motivo del desvínculo consta también como motivo de ello; el familiar del alta, que ya no lo es, también.
        async Task<(string Screen, string Result)> UnlinkAsync(string name, string reason)
        {
            var id = await connection.ExecuteScalarAsync<Guid>("""
                SELECT l.id FROM dbo.residentes_familiares l JOIN dbo.familiares f ON f.id = l.familiar_id
                 WHERE l.residente_id = @residentId AND f.nombre_visible = @name
                """, new { residentId, name });
            var page = await admin.GetStringAsync($"/Administracion/DesvincularFamiliar?residenteId={residentId}&vinculoId={id}");
            return (WebUtility.HtmlDecode(page), WebUtility.HtmlDecode(await (await admin.PostAsync("/Administracion/DesvincularFamiliar", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken"),
                ["Form.ResidenteId"] = residentId,
                ["Form.VinculoId"] = id.ToString(),
                ["Form.Motivo"] = reason,
            }))).Content.ReadAsStringAsync()));
        }

        var onlyContact = await UnlinkAsync("Familiar Funcional", "Se ha ido a vivir fuera.");
        var unlinkedPage = (await UnlinkAsync("Familiar Prueba", "Ya no tiene relación con el residente.")).Result;

        Assert.Contains("Es el único contacto urgente del residente", onlyContact.Screen);
        Assert.Contains("Familiar desvinculado.", onlyContact.Result);
        Assert.Matches(@"Sin contacto urgente:\s*(<span[^>]*>)?Único contacto urgente desvinculado: Se ha ido a vivir fuera\.", onlyContact.Result);
        Assert.Contains("Familiar desvinculado.", unlinkedPage);
        Assert.Contains("Familiares desvinculados", unlinkedPage);
        Assert.Contains("Ya no tiene relación con el residente.", unlinkedPage);
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>(
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
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE residente_id = @residentId AND accion_codigo = 'RESIDENT_TRANSFER'", new { residentId }));
    }

    [Fact]
    public async Task UnidadDeDestino_PideLosEpisodiosCerradosDeLaAnterior_ConMotivo_ConLaAppEntera()
    {
        // CJ, tercera 5.2 C (script 0077): tras un traslado, la unidad de destino no ve los episodios cerrados de la anterior; los abre (historial, línea temporal y
        // detalle, solo lectura) con una petición con motivo que vale 1 hora y se audita. Enfermería y Medicina, con la app entera y el usuario limitado.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Historial Traslado Funcional");
        var destinationId = await GrantUnitAsync(seed, "Planta destino historial");
        var nurseOrigin = await LoginAsync(await GrantNursingAsync(seed));
        var doctorOrigin = await LoginAsync(await GrantMedicineAsync(seed));
        var destinationSeed = (seed.ExternalSubject, seed.ProfileScopeId, seed.CenterId, destinationId);
        var nurse = await LoginAsync(await GrantNursingAsync(destinationSeed));
        var doctor = await LoginAsync(await GrantMedicineAsync(destinationSeed));
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        async Task<string> PostAsync(HttpClient client, string url, string page, Dictionary<string, string> fields)
        {
            fields["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken");
            return WebUtility.HtmlDecode(await (await client.PostAsync(url, new FormUrlEncodedContent(fields))).Content.ReadAsStringAsync());
        }

        // Un episodio escalado a Medicina y cerrado por Medicina en la unidad de origen.
        var registerPage = await nurseOrigin.GetStringAsync($"/Enfermeria/RegistrarEvento?residenteId={residentId}");
        var detailPage = await PostAsync(nurseOrigin, "/Enfermeria/RegistrarEvento", registerPage, new()
        {
            ["ResidenteId"] = residentId, ["OperacionId"] = ExtractValue(registerPage, "OperacionId"),
            ["Observacion"] = "Fiebre desde la tarde (historial funcional).", ["Clasificacion"] = "Ordinario",
        });
        var eventId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.eventos_asistenciales WHERE residente_id = @residentId", new { residentId });
        Task<int> RevisionAsync() => connection.ExecuteScalarAsync<int>("SELECT revision FROM dbo.eventos_asistenciales WHERE id = @eventId", new { eventId });
        await PostAsync(nurseOrigin, "/Enfermeria/EmpezarValoracion", detailPage, new() { ["eventoId"] = eventId.ToString(), ["revision"] = (await RevisionAsync()).ToString() });
        await PostAsync(nurseOrigin, "/Enfermeria/Valoracion", await nurseOrigin.GetStringAsync($"/Enfermeria/Valoracion?eventoId={eventId}"), new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.Hallazgos"] = "Febrícula.",
        });
        await PostAsync(nurseOrigin, "/Enfermeria/Escalar", await nurseOrigin.GetStringAsync($"/Enfermeria/Escalar?eventoId={eventId}"), new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.Motivo"] = "Fiebre persistente.",
        });
        await PostAsync(doctorOrigin, "/Medicina/EmpezarValoracion", await doctorOrigin.GetStringAsync($"/Medicina/Escalado?eventoId={eventId}"), new()
        {
            ["eventoId"] = eventId.ToString(), ["revision"] = (await RevisionAsync()).ToString(),
        });
        await PostAsync(doctorOrigin, "/Medicina/Valoracion", await doctorOrigin.GetStringAsync($"/Medicina/Valoracion?eventoId={eventId}"), new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.HallazgosExploracion"] = "Febrícula sin foco.",
        });
        Assert.Contains("Evento cerrado.", await PostAsync(doctorOrigin, "/Medicina/Cerrar", await doctorOrigin.GetStringAsync($"/Medicina/Cerrar?eventoId={eventId}"), new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.OperacionId"] = Guid.NewGuid().ToString(),
            ["Form.Comunicacion"] = "NoComunicar",
        }));

        // El traslado a la unidad de destino.
        var transferPage = await admin.GetStringAsync($"/Administracion/Trasladar?residenteId={residentId}");
        Assert.Contains("Residente trasladado.", await PostAsync(admin, "/Administracion/Trasladar", transferPage, new()
        {
            ["Form.ResidenteId"] = residentId, ["Form.UnidadOrigenId"] = ExtractValue(transferPage, "Form.UnidadOrigenId"),
            ["Form.OperacionId"] = ExtractValue(transferPage, "Form.OperacionId"), ["Form.UnidadDestinoId"] = destinationId.ToString(),
        }));

        foreach (var (client, profile, detail, home) in new[]
                 {
                     (nurse, "Enfermeria", $"/Enfermeria/DetalleCambio?eventoId={eventId}", "Enfermería"),
                     (doctor, "Medicina", $"/Medicina/Escalado?eventoId={eventId}", "Medicina"),
                 })
        {
            // Sin petición: ni el episodio en el historial, ni en el detalle; solo se ofrece pedirlo.
            var history = WebUtility.HtmlDecode(await client.GetStringAsync($"/{profile}/Historial?residenteId={residentId}"));
            Assert.Contains("Episodios cerrados de la unidad anterior", history);
            Assert.Contains("Pedir los episodios de la unidad anterior", history);
            Assert.Contains("No escribas datos de salud ni nombres de personas", history);
            Assert.Contains("Revisión de continuidad asistencial", history);
            Assert.Contains("Otro motivo", history);
            Assert.DoesNotContain("Fiebre desde la tarde", history);
            Assert.DoesNotContain("Fiebre desde la tarde", WebUtility.HtmlDecode(await client.GetStringAsync(detail)));

            // Con «Otro motivo» y sin texto, no se abre.
            var rejected = await PostAsync(client, $"/{profile}/PedirHistorialAnterior", history, new()
            {
                ["ResidenteId"] = residentId, ["Retorno"] = "Historial", ["Finalidad"] = "Otro", ["Motivo"] = "   ",
            });
            Assert.Contains("con «Otro motivo», escribe cuál", rejected);
            Assert.DoesNotContain("Fiebre desde la tarde", rejected);

            // Con motivo: el historial, la línea temporal y el detalle (solo lectura) lo enseñan, marcado.
            var opened = await PostAsync(client, $"/{profile}/PedirHistorialAnterior", history, new()
            {
                ["ResidenteId"] = residentId, ["Retorno"] = "Historial", ["Finalidad"] = "ContinuidadAsistencial", ["Motivo"] = "Tras el traslado.",
            });
            Assert.Contains("Fiebre desde la tarde", opened);
            Assert.Contains("De una unidad anterior", opened);
            Assert.Contains("Tu petición vale 1 hora", opened);
            Assert.DoesNotContain("Pedir los episodios de la unidad anterior", opened);
            var timeline = WebUtility.HtmlDecode(await client.GetStringAsync($"/{profile}/LineaTemporal?residenteId={residentId}"));
            Assert.Contains("De una unidad anterior", timeline);
            Assert.DoesNotContain("Descargar PDF", timeline);
            var opened2 = WebUtility.HtmlDecode(await client.GetStringAsync(detail));
            Assert.Contains("Episodio cerrado de una unidad anterior del residente", opened2);
            Assert.Contains("Fiebre desde la tarde", opened2);
            Assert.DoesNotContain("Rectificar", opened2);
        }

        // Cada petición y cada lectura quedan auditadas con el motivo (2 peticiones, y por cada perfil: historial, línea temporal y detalle).
        Assert.Equal(8, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE residente_id = @residentId AND accion_codigo = 'TRANSFER_HISTORY_READ' AND justificacion = 'Revisión de continuidad asistencial — Tras el traslado.'",
            new { residentId }));
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
    public async Task Administracion_AnonimizaAUnFamiliar_ConLaAppEntera_SoloSiEsLaPrincipal_YLaFichaLoMuestraAnonimizado()
    {
        // Script 0087 con el usuario limitado de la aplicación y la seguridad por filas: pantalla de confirmación, validación, anonimización y ficha.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Anonimizar Funcional");
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        var addPage = WebUtility.HtmlDecode(await admin.GetStringAsync($"/Administracion/AnadirFamiliar?residenteId={residentId}"));
        await admin.PostAsync("/Administracion/AnadirFamiliar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(addPage, "__RequestVerificationToken"),
            ["Form.ResidenteId"] = residentId,
            ["Form.OperacionId"] = ExtractValue(addPage, "Form.OperacionId"),
            ["Form.NombreVisible"] = "Familiar Anonimizar Funcional",
            ["Form.Relacion"] = "Hija",
            ["Form.Telefono"] = "600000001",
            ["Form.Correo"] = "anonimizar.funcional@example.test",
        }));
        var linkId = await connection.ExecuteScalarAsync<Guid>(
            """
            SELECT l.id FROM dbo.residentes_familiares l JOIN dbo.familiares f ON f.id = l.familiar_id
             WHERE l.residente_id = @residentId AND f.nombre_visible = 'Familiar Anonimizar Funcional'
            """, new { residentId });
        var url = $"/Administracion/AnonimizarFamiliar?residenteId={residentId}&vinculoId={linkId}";

        async Task<string> PostAsync(Dictionary<string, string> fields)
        {
            var page = await admin.GetStringAsync(url);
            fields["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken");
            fields["Form.ResidenteId"] = residentId;
            fields["Form.VinculoId"] = linkId.ToString();
            fields["Form.Version"] = ExtractValue(page, "Form.Version");
            return WebUtility.HtmlDecode(await (await admin.PostAsync("/Administracion/AnonimizarFamiliar", new FormUrlEncodedContent(fields))).Content.ReadAsStringAsync());
        }

        // Sin ser la principal, la pantalla lo explica y no ofrece nada que enviar.
        var notPrincipal = WebUtility.HtmlDecode(await admin.GetStringAsync(url));
        Assert.Contains("del centro puede anonimizar a un familiar", notPrincipal);
        Assert.DoesNotContain("name=\"Form.Confirmado\"", notPrincipal);
        Assert.Contains("Anonimizar a este familiar", WebUtility.HtmlDecode(await admin.GetStringAsync($"/Administracion/EditarFamiliar?residenteId={residentId}&vinculoId={linkId}")));

        // Con la marca de principal: avisa de lo que pasa y de lo que no, y pide quién lo pide y la confirmación.
        await connection.ExecuteAsync("""
            INSERT INTO dbo.administraciones_principales_cambios (id, centro_id, ambito_perfil_id, numero, principal, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            SELECT NEWID(), centro_id, id, 1, 1, cuenta_id, 'ADMINISTRACION', SYSUTCDATETIME()
              FROM dbo.ambitos_perfil WHERE id = @scope
            """, new { scope = seed.ProfileScopeId });
        var confirmPage = WebUtility.HtmlDecode(await admin.GetStringAsync(url));
        Assert.Contains("Esto no se puede deshacer", confirmPage);
        Assert.Contains("Qué no cambia", confirmPage);
        Assert.Contains("La propia persona", confirmPage);
        Assert.Contains("Elige…", confirmPage);

        var withoutChoice = await PostAsync(new() { ["Form.Confirmado"] = "true" });
        Assert.Contains("Elige quién lo pide.", withoutChoice);
        var withoutConfirmation = await PostAsync(new() { ["Form.Motivo"] = "PeticionDelInteresado" });
        Assert.Contains("hay que comprobar la identidad", withoutConfirmation);
        Assert.Equal("Familiar Anonimizar Funcional", await connection.ExecuteScalarAsync<string>(
            "SELECT f.nombre_visible FROM dbo.familiares f JOIN dbo.residentes_familiares l ON l.familiar_id = f.id WHERE l.id = @linkId", new { linkId }));

        var done = await PostAsync(new() { ["Form.Motivo"] = "PeticionDelInteresado", ["Form.Confirmado"] = "true" });

        Assert.Contains("Familiar anonimizado. Anótalo en el registro de derechos de tu centro.", done);
        Assert.Contains("Persona anonimizada", done);
        Assert.Contains("Anonimizado", done);
        Assert.DoesNotContain("Familiar Anonimizar Funcional", done);
        Assert.DoesNotContain("anonimizar.funcional@example.test", done);
        Assert.Equal("-", await connection.ExecuteScalarAsync<string>(
            "SELECT f.telefono FROM dbo.familiares f JOIN dbo.residentes_familiares l ON l.familiar_id = f.id WHERE l.id = @linkId", new { linkId }));
        Assert.Contains("ya no se puede cambiar", WebUtility.HtmlDecode(await admin.GetStringAsync($"/Administracion/EditarFamiliar?residenteId={residentId}&vinculoId={linkId}")));
        Assert.Contains("ya está anonimizado", WebUtility.HtmlDecode(await admin.GetStringAsync(url)));
    }

    [Fact]
    public async Task Administracion_ConElInterruptorDeAnonimizarApagado_NoOfreceElBoton_YLaRutaResponde404_SinTocarAlFamiliar()
    {
        // Docs/privacidad/antes-de-datos-reales.md, apartado 8: apagado por defecto y en todo entorno con centros reales; solo se enciende en Development y en Azure dev.
        var seed = await SeedAsync();
        var off = _factory.WithWebHostBuilder(builder => builder.UseSetting("Privacidad:AnonimizarFamiliarActivo", "false"));
        var admin = off.CreateClient();
        (await SesionTests.PostLoginAsync(admin, seed.ExternalSubject)).EnsureSuccessStatusCode();
        var residentId = await CreateResidentAsync(admin, seed, "Residente Anonimizar Apagado");
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        var addPage = WebUtility.HtmlDecode(await admin.GetStringAsync($"/Administracion/AnadirFamiliar?residenteId={residentId}"));
        await admin.PostAsync("/Administracion/AnadirFamiliar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(addPage, "__RequestVerificationToken"),
            ["Form.ResidenteId"] = residentId,
            ["Form.OperacionId"] = ExtractValue(addPage, "Form.OperacionId"),
            ["Form.NombreVisible"] = "Familiar Anonimizar Apagado",
            ["Form.Relacion"] = "Hija",
            ["Form.Telefono"] = "600000002",
        }));
        var linkId = await connection.ExecuteScalarAsync<Guid>(
            """
            SELECT l.id FROM dbo.residentes_familiares l JOIN dbo.familiares f ON f.id = l.familiar_id
             WHERE l.residente_id = @residentId AND f.nombre_visible = 'Familiar Anonimizar Apagado'
            """, new { residentId });
        await connection.ExecuteAsync("""
            INSERT INTO dbo.administraciones_principales_cambios (id, centro_id, ambito_perfil_id, numero, principal, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            SELECT NEWID(), centro_id, id, 1, 1, cuenta_id, 'ADMINISTRACION', SYSUTCDATETIME()
              FROM dbo.ambitos_perfil WHERE id = @scope
            """, new { scope = seed.ProfileScopeId });

        var editPage = WebUtility.HtmlDecode(await admin.GetStringAsync($"/Administracion/EditarFamiliar?residenteId={residentId}&vinculoId={linkId}"));
        Assert.DoesNotContain("Anonimizar a este familiar", editPage);

        var url = $"/Administracion/AnonimizarFamiliar?residenteId={residentId}&vinculoId={linkId}";
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(url)).StatusCode);
        var post = await admin.PostAsync("/Administracion/AnonimizarFamiliar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(editPage, "__RequestVerificationToken"),
            ["Form.ResidenteId"] = residentId,
            ["Form.VinculoId"] = linkId.ToString(),
            ["Form.Motivo"] = "PeticionDelInteresado",
            ["Form.Confirmado"] = "true",
        }));
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Equal("Familiar Anonimizar Apagado", await connection.ExecuteScalarAsync<string>(
            "SELECT f.nombre_visible FROM dbo.familiares f JOIN dbo.residentes_familiares l ON l.familiar_id = f.id WHERE l.id = @linkId", new { linkId }));
    }

    [Fact]
    public async Task Administracion_VeElPlazoDeConservacionDeLasBajas_Con15Anos_YAvisaDeLosVencidosYLosProximos_ConLaAppEntera()
    {
        // Art. 12 de la Llei 21/2000 (Ley 16/2010): 15 años desde la baja. La lista de bajas avisa de lo vencido y de lo que vence en 12 meses, y dice que no se borra nada.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var vigente = await CreateResidentAsync(admin, seed, "Residente Plazo Vigente");
        var vencido = await CreateResidentAsync(admin, seed, "Residente Plazo Vencido");
        var proximo = await CreateResidentAsync(admin, seed, "Residente Plazo Proximo");
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();

        foreach (var residentId in new[] { vigente, vencido, proximo })
        {
            var page = await admin.GetStringAsync($"/Administracion/DarDeBaja?residenteId={residentId}");
            Assert.Contains("se conservan 15 años desde la baja", WebUtility.HtmlDecode(page));
            var response = await admin.PostAsync("/Administracion/DarDeBaja", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken"),
                ["Form.ResidenteId"] = residentId,
                ["Form.OperacionId"] = ExtractValue(page, "Form.OperacionId"),
                ["Form.Motivo"] = "AltaVoluntaria",
            }));
            Assert.Contains("Sus datos se conservan 15 años", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        }

        var expected = DateOnly.FromDateTime(await connection.ExecuteScalarAsync<DateTime>(
            "SELECT dada_en FROM dbo.bajas_residente WHERE residente_id = @residentId", new { residentId = vigente })).AddYears(15);
        Assert.Equal(expected, DateOnly.FromDateTime(await connection.ExecuteScalarAsync<DateTime>(
            "SELECT conservar_hasta FROM dbo.bajas_residente WHERE residente_id = @residentId", new { residentId = vigente })));

        // El disparador de la baja no deja cambiar conservar_hasta: se desactiva solo para colocar dos fechas de prueba y se vuelve a activar.
        await connection.ExecuteAsync("DISABLE TRIGGER dbo.TR_bjr_guard ON dbo.bajas_residente");
        try
        {
            await connection.ExecuteAsync("UPDATE dbo.bajas_residente SET conservar_hasta = DATEADD(DAY, -1, CAST(SYSUTCDATETIME() AS DATE)) WHERE residente_id = @residentId", new { residentId = vencido });
            await connection.ExecuteAsync("UPDATE dbo.bajas_residente SET conservar_hasta = DATEADD(MONTH, 6, CAST(SYSUTCDATETIME() AS DATE)) WHERE residente_id = @residentId", new { residentId = proximo });
        }
        finally
        {
            await connection.ExecuteAsync("ENABLE TRIGGER dbo.TR_bjr_guard ON dbo.bajas_residente");
        }

        var list = WebUtility.HtmlDecode(await admin.GetStringAsync("/Administracion/Bajas"));
        Assert.Contains("1 con el plazo vencido y 1 que vence en los próximos 12 meses", list);
        Assert.Contains("no borra nada", list);
        Assert.Single(Regex.Matches(list, "Plazo vencido</span>"));
        Assert.Single(Regex.Matches(list, "Vence en menos de 12 meses</span>"));
    }

    [Fact]
    public async Task Auxiliar_RegistraSoloLaTemperatura_ConAviso_YFueraDeRangoNoPasa()
    {
        // CJ, 2026-10-25 (dictado-por-voz-confirmaciones-cj 2.5, 3.1 y 3.2): un cambio con solo la temperatura se puede registrar, con un aviso
        // en la confirmación; fuera de 34,0–42,0 °C el formulario no deja seguir.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var resident = await CreateResidentAsync(admin, seed, "Residente Solo Temperatura Funcional");
        var auxiliar = await LoginAsync(await GrantAuxiliarAsync(seed, resident));
        var page = await auxiliar.GetStringAsync($"/Auxiliar/RegistrarCambio?residenteId={resident}");
        async Task<string> PostAsync(string path, string temperatura)
        {
            var response = await auxiliar.PostAsync(path, new FormUrlEncodedContent(new[]
            {
                KeyValuePair.Create("__RequestVerificationToken", ExtractValue(page, "__RequestVerificationToken")),
                KeyValuePair.Create("ResidenteId", resident),
                KeyValuePair.Create("OperacionId", ExtractValue(page, "OperacionId")),
                KeyValuePair.Create("Temperatura", temperatura),
                KeyValuePair.Create("__Invariant", "Temperatura"),
                KeyValuePair.Create("Clasificacion", "Ordinario"),
            }));
            return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        }

        var fueraDeRango = await PostAsync("/Auxiliar/RegistrarCambio", "378");
        var confirmacion = await PostAsync("/Auxiliar/RegistrarCambio", "38.5");
        var registrado = await PostAsync("/Auxiliar/ConfirmarCambio", "38.5");

        Assert.Contains("La temperatura tiene que estar entre 34,0 y 42,0 °C", fueraDeRango);
        Assert.Contains("Solo has anotado la temperatura", confirmacion);
        Assert.Contains("Cambio registrado", registrado);
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        Assert.Equal(38.5m, await connection.ExecuteScalarAsync<decimal>(
            "SELECT temperatura_celsius FROM dbo.cierres_cotidianos_residente WHERE residente_id = @resident", new { resident }));
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

        // Guía de pruebas 1, prueba 4: un prioritario sin el aviso directo vuelve a la confirmación, y el campo obligatorio avisa en español
        // (jQuery Validate lo hacía en inglés: «This field is required.»).
        var withoutNotice = WebUtility.HtmlDecode(await (await auxiliar.PostAsync("/Auxiliar/ConfirmarCambio", new FormUrlEncodedContent(new[]
        {
            KeyValuePair.Create("__RequestVerificationToken", ExtractValue(changePage, "__RequestVerificationToken")),
            KeyValuePair.Create("ResidenteId", changedResident),
            KeyValuePair.Create("OperacionId", Guid.NewGuid().ToString()),
            KeyValuePair.Create("AreaOpciones", "ALIMENTACION_HIDRATACION:NULA_INGESTA"),
            KeyValuePair.Create("Clasificacion", "Prioritario"),
            KeyValuePair.Create("MotivoPrioritario", "AlteracionConcienciaEstadoGeneral"),
        }))).Content.ReadAsStringAsync());
        Assert.Contains("Documenta el aviso directo antes de confirmar", withoutNotice);
        Assert.Contains("Este campo es obligatorio.", withoutNotice);

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

    [Fact]
    public async Task FotoDelResidente_AdministracionLaPone_EnfermeriaLaVeYLaQuita_ConLaAppEntera()
    {
        // Estudio de mercado R2 (scripts 0059 y 0071): el primer formulario con fichero de la app. La foto se guarda con quién firmó el
        // consentimiento y cuándo, llega sin EXIF a la lista de Enfermería, la imagen se sirve como JPEG sin que el navegador la reinterprete, y
        // una Auxiliar sin el residente asignado recibe un 404. Enfermería solo la cambia con el permiso que Administración da en su perfil.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Foto Funcional");
        var otherResident = await CreateResidentAsync(admin, seed, "Residente Sin Foto Funcional");
        var nurseSubject = await GrantNursingAsync(seed);
        var nurse = await LoginAsync(nurseSubject);
        var auxiliar = await LoginAsync(await GrantAuxiliarAsync(seed, otherResident));
        static byte[] Segment(byte marker, byte[] payload) => [0xFF, marker, 0, (byte)(payload.Length + 2), .. payload];
        byte[] image = [.. Segment(0xDA, [1, 1, 0, 0, 63, 0]), 0x12, 0x34, 0xFF, 0xD9];

        async Task<string> SaveAsync(bool consent, string signedOn = "")
        {
            var record = await admin.GetStringAsync($"/Administracion/Residente?residenteId={residentId}");
            using var form = new MultipartFormDataContent
            {
                { new StringContent(ExtractValue(record, "__RequestVerificationToken")), "__RequestVerificationToken" },
                { new StringContent(residentId), "Form.ResidenteId" },
            };
            if (consent)
            {
                form.Add(new StringContent("Representante"), "Form.Firmante");
                form.Add(new StringContent(signedOn.Length > 0 ? signedOn : DateTime.Today.ToString("yyyy-MM-dd")), "Form.FechaConsentimiento");
            }
            var file = new ByteArrayContent([0xFF, 0xD8, .. Segment(0xE1, [.. "Exif\0\0GPS 40.4168 N"u8]), .. image]);
            file.Headers.ContentType = new("image/jpeg");
            form.Add(file, "Form.Foto", "foto.jpg");
            return WebUtility.HtmlDecode(await (await admin.PostAsync("/FotoResidente/Guardar", form)).Content.ReadAsStringAsync());
        }

        var withoutConsent = await SaveAsync(consent: false);
        var futureConsent = await SaveAsync(consent: true, signedOn: DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"));
        var saved = await SaveAsync(consent: true);
        var list = WebUtility.HtmlDecode(await nurse.GetStringAsync("/Enfermeria/Residentes"));
        var photoUrl = Regex.Match(list, $"/FotoResidente/Ver\\?residenteId={residentId}&v=\\d+").Value;
        var photo = await nurse.GetAsync(photoUrl);
        var byOutsider = await auxiliar.GetAsync(photoUrl);
        var nurseRecordWithoutPermission = WebUtility.HtmlDecode(await nurse.GetStringAsync($"/Enfermeria/Residente?residenteId={residentId}"));
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        var (nurseAccount, nurseScope) = await connection.QuerySingleAsync<(Guid, Guid)>(
            "SELECT c.id, a.id FROM dbo.cuentas c JOIN dbo.ambitos_perfil a ON a.cuenta_id = c.id WHERE c.sujeto_externo = @nurseSubject",
            new { nurseSubject });
        var profilePage = await admin.GetStringAsync($"/Administracion/PerfilUsuario?cuentaId={nurseAccount}&ambitoId={nurseScope}");
        var granted = WebUtility.HtmlDecode(await (await admin.PostAsync("/Administracion/PermisoPerfil", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(profilePage, "__RequestVerificationToken"), ["cuentaId"] = nurseAccount.ToString(),
            ["ambitoId"] = nurseScope.ToString(), ["permiso"] = "RESIDENT_PHOTO_MANAGE", ["conceder"] = "true",
        }))).Content.ReadAsStringAsync());
        var nurseRecord = await nurse.GetStringAsync($"/Enfermeria/Residente?residenteId={residentId}");
        var removed = WebUtility.HtmlDecode(await (await nurse.PostAsync($"/FotoResidente/Quitar?residenteId={residentId}", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = ExtractValue(nurseRecord, "__RequestVerificationToken") })))
            .Content.ReadAsStringAsync());

        Assert.Contains("Indica quién firmó el consentimiento y la fecha de la firma para guardar la foto.", withoutConsent);
        Assert.Contains("La fecha de la firma no puede ser posterior a hoy.", futureConsent);
        Assert.Contains("Foto guardada.", saved);
        Assert.Contains($"Consentimiento firmado por su representante el {DateTime.Today:dd/MM/yyyy}.", saved);
        Assert.Contains("Poner, cambiar y quitar la foto del residente", WebUtility.HtmlDecode(profilePage));
        Assert.Contains("pídeselo a Administración", nurseRecordWithoutPermission);
        Assert.DoesNotContain("Guardar foto", nurseRecordWithoutPermission);
        Assert.Contains("Permiso concedido.", granted);
        Assert.Contains("Ubicación actual", saved);
        Assert.Contains("Sin habitación asignada", saved);
        Assert.NotEmpty(photoUrl);
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        Assert.Equal("image/jpeg", photo.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", Assert.Single(photo.Headers.GetValues("X-Content-Type-Options")));
        Assert.True(photo.Headers.CacheControl?.NoStore);
        Assert.Equal([0xFF, 0xD8, .. image], await photo.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, byOutsider.StatusCode);
        Assert.Contains("Foto de identificación", nurseRecord);
        Assert.Contains("Foto quitada.", removed);
        Assert.Equal(HttpStatusCode.NotFound, (await nurse.GetAsync(photoUrl)).StatusCode);
    }

    [Fact]
    public async Task FichaDeAntecedentes_EnfermeriaLaRellenaConPermiso_MedicinaLaVe_AuxiliarNo_ConLaAppEntera()
    {
        // Ficha de antecedentes de salud, fase 1 (script 0094): sin el permiso HEALTH_BACKGROUND_EDIT que da Administración, Enfermería la ve «pendiente» pero no la
        // cambia; con él, la guarda; otra pantalla abierta antes no la pisa; Medicina la ve; una Auxiliar no llega al formulario. Los nombres de fármacos son inventados.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Ficha Funcional");
        var nurseSubject = await GrantNursingAsync(seed);
        var nurse = await LoginAsync(nurseSubject);
        var doctor = await LoginAsync(await GrantMedicineAsync(seed));
        var auxiliar = await LoginAsync(await GrantAuxiliarAsync(seed, residentId));
        var recordUrl = $"/Enfermeria/Residente?residenteId={residentId}";
        var formUrl = $"/FichaAntecedentes/Alergias?residenteId={residentId}";

        var pending = WebUtility.HtmlDecode(await nurse.GetStringAsync(recordUrl));
        var withoutPermission = WebUtility.HtmlDecode(await nurse.GetStringAsync(formUrl));

        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        var (nurseAccount, nurseScope) = await connection.QuerySingleAsync<(Guid, Guid)>(
            "SELECT c.id, a.id FROM dbo.cuentas c JOIN dbo.ambitos_perfil a ON a.cuenta_id = c.id WHERE c.sujeto_externo = @nurseSubject", new { nurseSubject });
        var profilePage = await admin.GetStringAsync($"/Administracion/PerfilUsuario?cuentaId={nurseAccount}&ambitoId={nurseScope}");
        var granted = WebUtility.HtmlDecode(await (await admin.PostAsync("/Administracion/PermisoPerfil", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(profilePage, "__RequestVerificationToken"), ["cuentaId"] = nurseAccount.ToString(),
            ["ambitoId"] = nurseScope.ToString(), ["permiso"] = "HEALTH_BACKGROUND_EDIT", ["conceder"] = "true",
        }))).Content.ReadAsStringAsync());
        var form = WebUtility.HtmlDecode(await nurse.GetStringAsync(formUrl));
        var token = ExtractValue(form, "__RequestVerificationToken");

        Task<string> PostAsync(Dictionary<string, string> fields) => PostFichaAsync(nurse, token, residentId, fields);
        var withoutChoice = await PostAsync(new() { ["Form.VersionVista"] = "0" });
        var saved = await PostAsync(new()
        {
            ["Form.VersionVista"] = "0", ["Form.Estado"] = "NO_REFIERE", ["Form.Fuente"] = "RESIDENTE",
            ["Form.Filas[0].Tipo"] = "INTOLERANCIA", ["Form.Filas[0].Nombre"] = "Fármaco Alfa", ["Form.Filas[0].Reaccion"] = "náuseas",
        });
        var stale = await PostAsync(new()
        {
            ["Form.VersionVista"] = "0", ["Form.Estado"] = "NO_CONSTA",
        });
        var changed = await PostAsync(new()
        {
            ["Form.VersionVista"] = "1", ["Form.Estado"] = "CON_ENTRADAS", ["Form.Motivo"] = "ACTUALIZACION",
            ["Form.Filas[0].Tipo"] = "ALERGIA", ["Form.Filas[0].Nombre"] = "Fármaco Beta", ["Form.Filas[0].Reaccion"] = "",
        });
        var byDoctor = WebUtility.HtmlDecode(await doctor.GetStringAsync($"/Medicina/Residente?residenteId={residentId}"));
        var byDoctorWithoutPermission = WebUtility.HtmlDecode(await doctor.GetStringAsync(formUrl));
        var byAuxiliar = WebUtility.HtmlDecode(await auxiliar.GetStringAsync(formUrl));

        Assert.Contains("Antecedentes de salud", pending);
        Assert.Contains("Ficha pendiente de rellenar en este centro", pending);
        Assert.Contains("pídeselo a Administración", pending);
        Assert.DoesNotContain("Rellenar las alergias", pending);
        Assert.Contains("hace falta el permiso «Rellenar y cambiar la ficha de antecedentes de salud»", withoutPermission);
        Assert.Contains("Rellenar y cambiar la ficha de antecedentes de salud", WebUtility.HtmlDecode(profilePage));
        Assert.Contains("Permiso concedido.", granted);
        Assert.Contains("Revisa que sea la persona correcta.", form);
        Assert.Contains("Residente Ficha Funcional", form);
        Assert.Contains("autocomplete=\"off\"", form);
        var stateRadios = Regex.Matches(form, "<input[^>]*name=\"Form.Estado\"[^>]*>").Select(m => m.Value).ToList();
        Assert.Equal(3, stateRadios.Count);
        Assert.All(stateRadios, radio => Assert.DoesNotContain("checked", radio));
        Assert.Contains("Revisa la ficha:", withoutChoice);
        Assert.Contains("Ficha de alergias guardada.", saved);
        Assert.Contains("No refiere alergias a medicamentos.", saved);
        Assert.Contains("Intolerancia o efecto adverso: Fármaco Alfa (náuseas)", saved);
        Assert.Contains("Fuente: el propio residente.", saved);
        Assert.Contains("Esta ficha no se revisa de forma periódica", saved);
        Assert.DoesNotContain("revisad", saved, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cambiar las alergias", saved);
        Assert.Contains("Otra persona ha guardado esta ficha mientras la editabas", stale);
        Assert.Contains("Ficha de alergias guardada.", changed);
        Assert.Contains("Alergia a: Fármaco Beta", changed);
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.fichas_antecedentes_versiones WHERE residente_id = @residentId", new { residentId }));
        Assert.Contains("Alergia a: Fármaco Beta", byDoctor);
        Assert.Contains("por Enfermería", byDoctor);
        Assert.Contains("pídeselo a Administración", byDoctor);
        Assert.DoesNotContain("Revisa que sea la persona correcta.", byDoctorWithoutPermission);
        Assert.DoesNotContain("Revisa que sea la persona correcta.", byAuxiliar);
        Assert.DoesNotContain("Fármaco Beta", byAuxiliar);
    }

    [Fact]
    public async Task FichaDeAntecedentes_EnfermedadesEIntervenciones_SeRellenanConPermiso_ConAnioOpcional_ConLaAppEntera()
    {
        // Ficha de antecedentes de salud, fase 2 (script 0096): con el permiso HEALTH_BACKGROUND_EDIT, Enfermería guarda la lista con el año opcional; un año futuro o que no es un
        // número se rechaza sin guardar; «sin enfermedades» no admite filas; la ayuda de la pantalla está y no hay cuadros de texto largos; Medicina la ve; una Auxiliar no llega
        // al formulario. Las enfermedades son inventadas.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Enfermedades Funcional");
        var nurseSubject = await GrantNursingAsync(seed);
        var nurse = await LoginAsync(nurseSubject);
        var doctor = await LoginAsync(await GrantMedicineAsync(seed));
        var auxiliar = await LoginAsync(await GrantAuxiliarAsync(seed, residentId));
        var formUrl = $"/FichaAntecedentes/Enfermedades?residenteId={residentId}";
        var withoutPermission = WebUtility.HtmlDecode(await nurse.GetStringAsync(formUrl));
        var pending = WebUtility.HtmlDecode(await nurse.GetStringAsync($"/Enfermeria/Residente?residenteId={residentId}"));

        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        var (nurseAccount, nurseScope) = await connection.QuerySingleAsync<(Guid, Guid)>(
            "SELECT c.id, a.id FROM dbo.cuentas c JOIN dbo.ambitos_perfil a ON a.cuenta_id = c.id WHERE c.sujeto_externo = @nurseSubject", new { nurseSubject });
        var profilePage = await admin.GetStringAsync($"/Administracion/PerfilUsuario?cuentaId={nurseAccount}&ambitoId={nurseScope}");
        await admin.PostAsync("/Administracion/PermisoPerfil", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(profilePage, "__RequestVerificationToken"), ["cuentaId"] = nurseAccount.ToString(),
            ["ambitoId"] = nurseScope.ToString(), ["permiso"] = "HEALTH_BACKGROUND_EDIT", ["conceder"] = "true",
        }));
        var form = WebUtility.HtmlDecode(await nurse.GetStringAsync(formUrl));
        var token = ExtractValue(form, "__RequestVerificationToken");

        Task<string> PostAsync(Dictionary<string, string> fields) => PostFichaAsync(nurse, token, residentId, fields, "Enfermedades");
        var withoutChoice = await PostAsync(new() { ["Form.VersionVista"] = "0" });
        var futureYear = await PostAsync(new()
        {
            ["Form.VersionVista"] = "0", ["Form.Estado"] = "CON_ENTRADAS",
            ["Form.Filas[0].Tipo"] = "ENFERMEDAD", ["Form.Filas[0].Nombre"] = "Enfermedad Alfa", ["Form.Filas[0].Anio"] = (DateTime.Now.Year + 1).ToString(),
        });
        var wordYear = await PostAsync(new()
        {
            ["Form.VersionVista"] = "0", ["Form.Estado"] = "CON_ENTRADAS",
            ["Form.Filas[0].Tipo"] = "ENFERMEDAD", ["Form.Filas[0].Nombre"] = "Enfermedad Alfa", ["Form.Filas[0].Anio"] = "hace años",
        });
        var noneWithRows = await PostAsync(new()
        {
            ["Form.VersionVista"] = "0", ["Form.Estado"] = "NO_REFIERE",
            ["Form.Filas[0].Tipo"] = "ENFERMEDAD", ["Form.Filas[0].Nombre"] = "Enfermedad Alfa",
        });
        var rejectedCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.fichas_antecedentes_versiones WHERE residente_id = @residentId", new { residentId });
        var saved = await PostAsync(new()
        {
            ["Form.VersionVista"] = "0", ["Form.Estado"] = "CON_ENTRADAS", ["Form.Fuente"] = "INFORME_MEDICO",
            ["Form.Filas[0].Tipo"] = "ENFERMEDAD", ["Form.Filas[0].Nombre"] = "Enfermedad Alfa", ["Form.Filas[0].Anio"] = "",
            ["Form.Filas[1].Tipo"] = "INTERVENCION", ["Form.Filas[1].Nombre"] = "Intervención Beta", ["Form.Filas[1].Anio"] = "1999",
        });
        var stale = await PostAsync(new() { ["Form.VersionVista"] = "0", ["Form.Estado"] = "NO_CONSTA" });
        var byDoctor = WebUtility.HtmlDecode(await doctor.GetStringAsync($"/Medicina/Residente?residenteId={residentId}"));
        var byDoctorForm = WebUtility.HtmlDecode(await doctor.GetStringAsync(formUrl));
        var byAuxiliar = WebUtility.HtmlDecode(await auxiliar.GetStringAsync(formUrl));

        Assert.Contains("Enfermedades e intervenciones", pending);
        Assert.Contains("No equivale a «sin enfermedades»", pending);
        Assert.DoesNotContain("Rellenar las enfermedades e intervenciones", pending);
        Assert.Contains("hace falta el permiso «Rellenar y cambiar la ficha de antecedentes de salud»", withoutPermission);
        // La ayuda del DPO está visible y no hay cuadros de texto largos; el año tiene su casilla.
        Assert.Contains("Solo enfermedades e intervenciones de esta persona.", form);
        Assert.Contains("No escribas antecedentes de familiares, nombres de otras personas, explicaciones ni opiniones.", form);
        Assert.DoesNotContain("<textarea", form);
        Assert.Contains("Año (si se sabe)", form);
        Assert.Contains("Revisa que sea la persona correcta.", form);
        Assert.Contains("Residente Enfermedades Funcional", form);
        Assert.Contains("autocomplete=\"off\"", form);
        var stateRadios = Regex.Matches(form, "<input[^>]*name=\"Form.Estado\"[^>]*>").Select(m => m.Value).ToList();
        Assert.Equal(3, stateRadios.Count);
        Assert.All(stateRadios, radio => Assert.DoesNotContain("checked", radio));
        Assert.Contains("Revisa la ficha:", withoutChoice);
        Assert.Contains("Revisa la ficha:", futureYear);
        Assert.Contains("Revisa la ficha:", wordYear);
        Assert.Contains("Revisa la ficha:", noneWithRows);
        Assert.Equal(0, rejectedCount);
        Assert.Contains("Ficha de enfermedades e intervenciones guardada.", saved);
        Assert.Contains("Enfermedad: Enfermedad Alfa", saved);
        Assert.DoesNotContain("Enfermedad: Enfermedad Alfa (", saved);
        Assert.Contains("Intervención: Intervención Beta (1999)", saved);
        Assert.Contains("Fuente: informe médico.", saved);
        Assert.Contains("Cambiar las enfermedades e intervenciones", saved);
        Assert.DoesNotContain("revisad", saved, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Otra persona ha guardado esta ficha mientras la editabas", stale);
        // El formulario se vuelve a abrir precargado con lo guardado (si no, el siguiente guardado vaciaría la lista sin avisar), con el año en su casilla y el motivo a elegir.
        Assert.Contains("value=\"Enfermedad Alfa\"", stale);
        Assert.Contains("value=\"Intervención Beta\"", stale);
        Assert.Contains("value=\"1999\"", stale);
        Assert.Matches("<input[^>]*name=\"Form.Estado\"[^>]*value=\"CON_ENTRADAS\"[^>]*checked", stale);
        Assert.Contains("¿Por qué guardas una versión nueva?", stale);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.fichas_antecedentes_versiones WHERE residente_id = @residentId AND apartado_codigo = 'ENFERMEDADES'", new { residentId }));
        Assert.Contains("Intervención: Intervención Beta (1999)", byDoctor);
        Assert.Contains("pídeselo a Administración", byDoctor);
        Assert.DoesNotContain("Revisa que sea la persona correcta.", byDoctorForm);
        Assert.DoesNotContain("Revisa que sea la persona correcta.", byAuxiliar);
        Assert.DoesNotContain("Intervención Beta", byAuxiliar);
    }

    [Fact]
    public async Task FichaDeAntecedentes_MedicacionHabitual_SeRellenaConPermiso_ConDosisYPautaObligatorias_ConLaAppEntera()
    {
        // Ficha de antecedentes de salud, fase 3 (script 0097): con el permiso HEALTH_BACKGROUND_EDIT, Enfermería guarda la lista con nombre, dosis y pauta (obligatorias; «no consta» vale);
        // una fila sin dosis, una pauta que es un guion, o «No refiere» con filas se rechazan sin guardar; la ayuda del DPO está visible y no hay cuadros de texto largos; Medicina la ve con la
        // cautela de que es una copia; una Auxiliar no llega al formulario. Los fármacos son inventados.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Medicacion Funcional");
        var nurseSubject = await GrantNursingAsync(seed);
        var nurse = await LoginAsync(nurseSubject);
        var doctor = await LoginAsync(await GrantMedicineAsync(seed));
        var auxiliar = await LoginAsync(await GrantAuxiliarAsync(seed, residentId));
        var formUrl = $"/FichaAntecedentes/Medicacion?residenteId={residentId}";
        var withoutPermission = WebUtility.HtmlDecode(await nurse.GetStringAsync(formUrl));
        var pending = WebUtility.HtmlDecode(await nurse.GetStringAsync($"/Enfermeria/Residente?residenteId={residentId}"));

        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        var (nurseAccount, nurseScope) = await connection.QuerySingleAsync<(Guid, Guid)>(
            "SELECT c.id, a.id FROM dbo.cuentas c JOIN dbo.ambitos_perfil a ON a.cuenta_id = c.id WHERE c.sujeto_externo = @nurseSubject", new { nurseSubject });
        var profilePage = await admin.GetStringAsync($"/Administracion/PerfilUsuario?cuentaId={nurseAccount}&ambitoId={nurseScope}");
        await admin.PostAsync("/Administracion/PermisoPerfil", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(profilePage, "__RequestVerificationToken"), ["cuentaId"] = nurseAccount.ToString(),
            ["ambitoId"] = nurseScope.ToString(), ["permiso"] = "HEALTH_BACKGROUND_EDIT", ["conceder"] = "true",
        }));
        var form = WebUtility.HtmlDecode(await nurse.GetStringAsync(formUrl));
        var token = ExtractValue(form, "__RequestVerificationToken");

        Task<string> PostAsync(Dictionary<string, string> fields) => PostFichaAsync(nurse, token, residentId, fields, "Medicacion");
        var withoutChoice = await PostAsync(new() { ["Form.VersionVista"] = "0" });
        var withoutDose = await PostAsync(new()
        {
            ["Form.VersionVista"] = "0", ["Form.Estado"] = "CON_ENTRADAS",
            ["Form.Filas[0].Nombre"] = "Fármaco Alfa", ["Form.Filas[0].Dosis"] = "", ["Form.Filas[0].Pauta"] = "cada 8 h",
        });
        var dashSchedule = await PostAsync(new()
        {
            ["Form.VersionVista"] = "0", ["Form.Estado"] = "CON_ENTRADAS",
            ["Form.Filas[0].Nombre"] = "Fármaco Alfa", ["Form.Filas[0].Dosis"] = "1 g", ["Form.Filas[0].Pauta"] = "-",
        });
        var noneWithRows = await PostAsync(new()
        {
            ["Form.VersionVista"] = "0", ["Form.Estado"] = "NO_REFIERE",
            ["Form.Filas[0].Nombre"] = "Fármaco Alfa", ["Form.Filas[0].Dosis"] = "1 g", ["Form.Filas[0].Pauta"] = "cada 8 h",
        });
        var rejectedCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.fichas_antecedentes_versiones WHERE residente_id = @residentId", new { residentId });
        var saved = await PostAsync(new()
        {
            ["Form.VersionVista"] = "0", ["Form.Estado"] = "CON_ENTRADAS", ["Form.Fuente"] = "RECETA_ELECTRONICA",
            ["Form.Filas[0].Nombre"] = "Fármaco Alfa", ["Form.Filas[0].Dosis"] = "1 g", ["Form.Filas[0].Pauta"] = "cada 8 h",
            ["Form.Filas[1].Nombre"] = "Fármaco Beta", ["Form.Filas[1].Dosis"] = "no consta", ["Form.Filas[1].Pauta"] = "si precisa",
        });
        var stale = await PostAsync(new() { ["Form.VersionVista"] = "0", ["Form.Estado"] = "NO_CONSTA" });
        var byDoctor = WebUtility.HtmlDecode(await doctor.GetStringAsync($"/Medicina/Residente?residenteId={residentId}"));
        var byDoctorForm = WebUtility.HtmlDecode(await doctor.GetStringAsync(formUrl));
        var byAuxiliar = WebUtility.HtmlDecode(await auxiliar.GetStringAsync(formUrl));

        Assert.Contains("Medicación habitual", pending);
        Assert.Contains("No equivale a «no toma medicación»", pending);
        Assert.DoesNotContain("Rellenar la medicación habitual", pending);
        Assert.Contains("hace falta el permiso «Rellenar y cambiar la ficha de antecedentes de salud»", withoutPermission);
        // La ayuda del DPO está visible, con la cautela de que es una copia; sin cuadros de texto largos; nada de observaciones.
        Assert.Contains("Copia informativa: la fuente es la receta electrónica o la hoja de tratamiento oficial del centro; si no coinciden, manda la oficial.", form);
        Assert.Contains("No anotes si la toma o no", form);
        Assert.DoesNotContain("<textarea", form);
        Assert.Contains("Revisa que sea la persona correcta.", form);
        Assert.Contains("autocomplete=\"off\"", form);
        var stateRadios = Regex.Matches(form, "<input[^>]*name=\"Form.Estado\"[^>]*>").Select(m => m.Value).ToList();
        Assert.Equal(3, stateRadios.Count);
        Assert.All(stateRadios, radio => Assert.DoesNotContain("checked", radio));
        foreach (var rejected in new[] { withoutChoice, withoutDose, dashSchedule, noneWithRows })
        {
            Assert.Contains("Revisa la ficha:", rejected);
        }
        Assert.Equal(0, rejectedCount);
        Assert.Contains("Ficha de medicación habitual guardada.", saved);
        Assert.Contains("Fármaco Alfa, 1 g, cada 8 h", saved);
        Assert.Contains("Fármaco Beta, no consta, si precisa", saved);
        Assert.Contains("Fuente: receta electrónica.", saved);
        Assert.Contains("Cambiar la medicación habitual", saved);
        Assert.DoesNotContain("revisad", saved, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Otra persona ha guardado esta ficha mientras la editabas", stale);
        Assert.Contains("value=\"Fármaco Alfa\"", stale);
        Assert.Contains("value=\"no consta\"", stale);
        Assert.Matches("<input[^>]*name=\"Form.Estado\"[^>]*value=\"CON_ENTRADAS\"[^>]*checked", stale);
        Assert.Contains("Última modificación el", stale);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.fichas_antecedentes_versiones WHERE residente_id = @residentId AND apartado_codigo = 'MEDICACION'", new { residentId }));
        Assert.Contains("Fármaco Beta, no consta, si precisa", byDoctor);
        Assert.Contains(ResidApp.Web.Models.HealthBackgroundDisplay.MedicationCopyNotice, byDoctor);
        Assert.DoesNotContain("Revisa que sea la persona correcta.", byDoctorForm);
        Assert.DoesNotContain("Revisa que sea la persona correcta.", byAuxiliar);
        Assert.DoesNotContain("Fármaco Beta", byAuxiliar);
    }

    [Fact]
    public async Task CitasExternas_AdministracionRegistra_EnfermeriaConPermiso_MedicinaSoloVe_AuxiliarNo_ConLaAppEntera()
    {
        // Citas externas, PR 2 (script 0101): Administración registra y cambia siempre; Enfermería, solo con el permiso EXTERNAL_APPOINTMENT_MANAGE que le da Administración; Medicina
        // las ve; una Auxiliar no entra. El aviso del inicio de Administración no lleva ni la especialidad ni el lugar. Las ayudas del DPO están junto a los campos y no hay
        // cuadros de texto largos. Nombres, lugares y motivos inventados.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Cita Funcional");
        var nurseSubject = await GrantNursingAsync(seed);
        var nurse = await LoginAsync(nurseSubject);
        var doctor = await LoginAsync(await GrantMedicineAsync(seed));
        var auxiliar = await LoginAsync(await GrantAuxiliarAsync(seed, residentId));
        var day = ResidApp.Domain.Modules.CenterModuleRules.TodayInMadrid(DateTimeOffset.UtcNow).AddDays(3).ToString("yyyy-MM-dd");
        var farDay = ResidApp.Domain.Modules.CenterModuleRules.TodayInMadrid(DateTimeOffset.UtcNow).AddDays(40).ToString("yyyy-MM-dd");

        async Task<(string Html, string? Id)> PostAsync(HttpClient client, string path, string formPath, Dictionary<string, string> fields)
        {
            var page = await client.GetStringAsync(formPath);
            fields["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken");
            var response = await client.PostAsync(path, new FormUrlEncodedContent(fields));
            var query = response.RequestMessage!.RequestUri!.PathAndQuery;
            return (WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()), Regex.Match(query, "([0-9a-f]{8}-[0-9a-f-]{27})").Groups[1].Value is { Length: > 0 } id ? id : null);
        }

        Dictionary<string, string> Fields(string date, string place = "Hospital de Prueba") => new()
        {
            ["Form.ResidenteId"] = residentId, ["Form.Fecha"] = date, ["Form.Tipo"] = "CONSULTA", ["Form.Especialidad"] = "CARDIOLOGIA", ["Form.Lugar"] = place,
            ["Form.TransporteModo"] = "SIN_DETERMINAR", ["Form.AcompananteModo"] = "SIN_DETERMINAR", ["Form.Visibilidad"] = "VISIBLE",
        };

        var nueva = $"/CitasExternas/Nueva?residenteId={residentId}";
        var form = WebUtility.HtmlDecode(await admin.GetStringAsync(nueva));
        var withoutType = await PostAsync(admin, "/CitasExternas/Nueva", nueva, new() { ["Form.ResidenteId"] = residentId, ["Form.Fecha"] = day });
        var registered = await PostAsync(admin, "/CitasExternas/Nueva", nueva, Fields(day));
        var citaId = registered.Id;
        var adminHome = WebUtility.HtmlDecode(await admin.GetStringAsync("/Administracion"));
        var nurseWithoutPermission = WebUtility.HtmlDecode(await nurse.GetStringAsync(nueva));
        var nurseList = WebUtility.HtmlDecode(await nurse.GetStringAsync("/CitasExternas"));
        var doctorNew = WebUtility.HtmlDecode(await doctor.GetStringAsync(nueva));
        var doctorList = WebUtility.HtmlDecode(await doctor.GetStringAsync("/CitasExternas"));
        var doctorView = WebUtility.HtmlDecode(await doctor.GetStringAsync($"/CitasExternas/Ver?id={citaId}"));
        var auxiliarList = WebUtility.HtmlDecode(await auxiliar.GetStringAsync("/CitasExternas"));
        var extremeDates = await admin.GetAsync("/CitasExternas?desde=9999-12-31&hasta=0001-01-01");

        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        var (nurseAccount, nurseScope) = await connection.QuerySingleAsync<(Guid, Guid)>(
            "SELECT c.id, a.id FROM dbo.cuentas c JOIN dbo.ambitos_perfil a ON a.cuenta_id = c.id WHERE c.sujeto_externo = @nurseSubject", new { nurseSubject });
        var profilePage = await admin.GetStringAsync($"/Administracion/PerfilUsuario?cuentaId={nurseAccount}&ambitoId={nurseScope}");
        var granted = WebUtility.HtmlDecode(await (await admin.PostAsync("/Administracion/PermisoPerfil", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(profilePage, "__RequestVerificationToken"), ["cuentaId"] = nurseAccount.ToString(),
            ["ambitoId"] = nurseScope.ToString(), ["permiso"] = "EXTERNAL_APPOINTMENT_MANAGE", ["conceder"] = "true",
        }))).Content.ReadAsStringAsync());
        var nurseForm = WebUtility.HtmlDecode(await nurse.GetStringAsync(nueva));
        var nurseRecord = WebUtility.HtmlDecode(await nurse.GetStringAsync($"/Enfermeria/Residente?residenteId={residentId}"));

        var editar = $"/CitasExternas/Editar?id={citaId}";
        var editPage = WebUtility.HtmlDecode(await nurse.GetStringAsync(editar));
        Dictionary<string, string> Edit(string date, string version, string transport, string motive = "") => new(Fields(date))
        {
            ["Form.CitaId"] = citaId!, ["Form.VersionVista"] = version, ["Form.TransporteModo"] = transport, ["Form.TransporteConfirmado"] = "true",
            ["Form.AcompananteModo"] = "NO_NECESITA", ["Form.MotivoCodigo"] = motive,
        };
        var coveredByNurse = await PostAsync(nurse, "/CitasExternas/Editar", editar, Edit(day, "1", "FAMILIA"));
        var adminHomeCovered = WebUtility.HtmlDecode(await admin.GetStringAsync("/Administracion"));
        var rescheduleWithoutReason = await PostAsync(nurse, "/CitasExternas/Editar", editar, Edit(farDay, "2", "FAMILIA"));
        var staleEdit = await PostAsync(nurse, "/CitasExternas/Editar", editar, Edit(day, "1", "CENTRO"));
        var rescheduled = await PostAsync(nurse, "/CitasExternas/Editar", editar, Edit(farDay, "2", "FAMILIA", "CAMBIO_DEL_HOSPITAL"));

        var ver = $"/CitasExternas/Ver?id={citaId}";
        // Una cita futura no se puede dar por realizada; el día de la cita sí (decisión de Orlando, 2026-11-18): se mueve a hoy por SQL.
        var tooEarly = await PostAsync(nurse, "/CitasExternas/Estado", ver, new() { ["id"] = citaId!, ["version"] = "3", ["estado"] = "REALIZADA" });
        var futureView = WebUtility.HtmlDecode(await nurse.GetStringAsync(ver));
        await connection.ExecuteAsync("UPDATE dbo.citas_externas SET fecha = @date, version = version + 1 WHERE id = @id",
            new { id = citaId, date = ResidApp.Domain.Modules.CenterModuleRules.TodayInMadrid(DateTimeOffset.UtcNow).ToDateTime(TimeOnly.MinValue) });
        var todayView = WebUtility.HtmlDecode(await nurse.GetStringAsync(ver));
        var done = await PostAsync(nurse, "/CitasExternas/Estado", ver, new() { ["id"] = citaId!, ["version"] = "4", ["estado"] = "REALIZADA" });
        var afterDone = WebUtility.HtmlDecode(await nurse.GetStringAsync(editar));

        var second = await PostAsync(admin, "/CitasExternas/Nueva", nueva, Fields(day, "Centro de Salud de Prueba"));
        var cancelar = $"/CitasExternas/Cancelar?id={second.Id}";
        var withoutReason = await PostAsync(admin, "/CitasExternas/Cancelar", cancelar, new() { ["Form.CitaId"] = second.Id!, ["Form.VersionVista"] = "1" });
        var dischargePreview = WebUtility.HtmlDecode(await admin.GetStringAsync($"/Administracion/DarDeBaja?residenteId={residentId}"));
        var cancelled = await PostAsync(admin, "/CitasExternas/Cancelar", cancelar,
            new() { ["Form.CitaId"] = second.Id!, ["Form.VersionVista"] = "1", ["Form.MotivoCodigo"] = "CAMBIO_EN_EL_CENTRO" });

        Assert.Contains("Revisa que sea la persona correcta.", form);
        Assert.Contains("Residente Cita Funcional", form);
        Assert.Contains("autocomplete=\"off\"", form);
        Assert.Contains("Solo el nombre del hospital o centro. No escribas el servicio, la unidad ni el motivo.", form);
        Assert.Contains("Solo lo que hay que llevar o hacer para salir. Las indicaciones clínicas van por Medicina, no aquí.", form);
        Assert.Contains("maxlength=\"80\"", form);
        Assert.Contains("maxlength=\"100\"", form);
        Assert.DoesNotContain("<textarea", form);
        Assert.Contains("Revisa la cita:", withoutType.Html);
        Assert.NotNull(citaId);
        Assert.Contains("Cita externa registrada.", registered.Html);
        Assert.Contains("Por confirmar", registered.Html);
        Assert.Contains("falta transporte y acompañante", registered.Html);
        Assert.Contains("Cardiología", registered.Html);
        Assert.Contains("Citas externas con algo sin cubrir", adminHome);
        Assert.Contains("Residente Cita Funcional", adminHome);
        Assert.Contains("falta transporte y acompañante", adminHome);
        Assert.DoesNotContain("Cardiología", adminHome);
        Assert.DoesNotContain("Hospital de Prueba", adminHome);
        Assert.Contains("hace falta el permiso «Registrar y cambiar las citas externas de los residentes»", nurseWithoutPermission);
        Assert.DoesNotContain("Registrar una cita externa", nurseList);
        Assert.Contains("Residente Cita Funcional", nurseList);
        Assert.Contains("Las registra Administración", doctorNew);
        Assert.Contains("Residente Cita Funcional", doctorList);
        Assert.DoesNotContain("Registrar una cita externa", doctorList);
        Assert.DoesNotContain("Cambiar la cita", doctorView);
        Assert.Contains("Solo puedes ver esta cita.", doctorView);
        Assert.DoesNotContain("Pruebas, consultas y tratamientos de los residentes fuera del centro", auxiliarList);
        Assert.Equal(HttpStatusCode.OK, extremeDates.StatusCode);
        Assert.Contains("Permiso concedido.", granted);
        Assert.Contains("Revisa que sea la persona correcta.", nurseForm);
        Assert.Contains("Registrar cita externa", nurseRecord);
        Assert.Contains("Cambiar la cita externa", editPage);
        Assert.Contains("Cita externa guardada.", coveredByNurse.Html);
        Assert.Contains("Cubierta", coveredByNurse.Html);
        Assert.DoesNotContain("Citas externas con algo sin cubrir", adminHomeCovered);
        Assert.Contains("Revisa la cita:", rescheduleWithoutReason.Html);
        Assert.Contains("Otra persona ha cambiado esta cita", staleEdit.Html);
        Assert.Contains("Cita externa guardada.", rescheduled.Html);
        Assert.Contains("Ese cambio de estado no es posible", tooEarly.Html);
        Assert.DoesNotContain(">Dar por realizada</button>", futureView);
        Assert.Contains("se podrán marcar desde el día de la cita", futureView);
        Assert.Contains(">Dar por realizada</button>", todayView);
        Assert.Contains("Estado de la cita guardado.", done.Html);
        Assert.Contains("La cita está cerrada y no se puede cambiar.", done.Html);
        Assert.Contains("La cita ya está cerrada", afterDone);
        Assert.Contains("Elige el motivo de la cancelación.", withoutReason.Html);
        Assert.Contains("Al dar de baja se cancelarán solas las citas externas abiertas", dischargePreview);
        Assert.Contains("Cita externa cancelada.", cancelled.Html);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.citas_externas_cambios WHERE cita_id = @id AND accion_codigo = 'REPROGRAMADA'", new { id = citaId }));
        Assert.Equal("CAMBIO_DEL_HOSPITAL", await connection.ExecuteScalarAsync<string>("SELECT motivo_codigo FROM dbo.citas_externas_cambios WHERE cita_id = @id AND accion_codigo = 'REPROGRAMADA'", new { id = citaId }));

        // Por cerrar: una cita abierta cuyo día ya pasó (se mueve por SQL, la pantalla no deja registrar una pasada) sale en el inicio de Administración, en la lista y en la ficha.
        var third = await PostAsync(admin, "/CitasExternas/Nueva", nueva, Fields(day, "Clínica Ficticia"));
        await connection.ExecuteAsync("UPDATE dbo.citas_externas SET fecha = @date, version = version + 1 WHERE id = @id",
            new { id = third.Id, date = ResidApp.Domain.Modules.CenterModuleRules.TodayInMadrid(DateTimeOffset.UtcNow).AddDays(-2).ToDateTime(TimeOnly.MinValue) });
        var overdueHome = WebUtility.HtmlDecode(await admin.GetStringAsync("/Administracion"));
        var overdueList = WebUtility.HtmlDecode(await doctor.GetStringAsync("/CitasExternas"));
        var overdueRecord = WebUtility.HtmlDecode(await nurse.GetStringAsync($"/Enfermeria/Residente?residenteId={residentId}"));
        Assert.Contains("Citas externas por cerrar", overdueHome);
        Assert.DoesNotContain("Clínica Ficticia", overdueHome);
        Assert.Contains("Citas externas por cerrar", overdueList);
        Assert.Contains("Las cierran Administración y las Enfermerías con permiso.", overdueList);
        Assert.Contains("Por cerrar:", overdueRecord);

        // Doble envío del formulario de registrar (script 0102): el mismo formulario, enviado dos veces, deja una sola cita.
        var doublePage = WebUtility.HtmlDecode(await admin.GetStringAsync(nueva));
        var operation = ExtractValue(doublePage, "Form.OperacionId");
        Assert.NotEqual(Guid.Empty.ToString(), operation);
        var doublePlace = $"Hospital Doble Envío {Guid.NewGuid():N}"[..40];
        var doubleFields = Fields(day, doublePlace);
        doubleFields["Form.OperacionId"] = operation;
        doubleFields["__RequestVerificationToken"] = ExtractValue(doublePage, "__RequestVerificationToken");
        var firstSend = await admin.PostAsync("/CitasExternas/Nueva", new FormUrlEncodedContent(doubleFields));
        var secondSend = await admin.PostAsync("/CitasExternas/Nueva", new FormUrlEncodedContent(doubleFields));
        Assert.Equal(firstSend.RequestMessage!.RequestUri!.AbsolutePath, secondSend.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.citas_externas WHERE lugar = @doublePlace", new { doublePlace }));
    }

    [Fact]
    public async Task CitasExternas_CalendarioDelPersonal_YVistaDelAuxiliar_SinEspecialidadNiLugar_ConLaAppEntera()
    {
        // PR 3: calendario mensual y semanal del personal y las citas de hoy y mañana del Auxiliar. El Auxiliar no recibe la especialidad ni el lugar (el servidor no los devuelve). Datos inventados.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Calendario Funcional");
        var doctor = await LoginAsync(await GrantMedicineAsync(seed));
        var auxiliar = await LoginAsync(await GrantAuxiliarAsync(seed, residentId));
        var tomorrow = ResidApp.Domain.Modules.CenterModuleRules.TodayInMadrid(DateTimeOffset.UtcNow).AddDays(1);
        var secretPlace = $"Hospital Calendario {Guid.NewGuid():N}"[..40];

        var nueva = $"/CitasExternas/Nueva?residenteId={residentId}";
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync(nueva));
        var registered = await admin.PostAsync("/CitasExternas/Nueva", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken"), ["Form.ResidenteId"] = residentId, ["Form.OperacionId"] = ExtractValue(page, "Form.OperacionId"),
            ["Form.Fecha"] = tomorrow.ToString("yyyy-MM-dd"), ["Form.Hora"] = "10:30", ["Form.Tipo"] = "CONSULTA", ["Form.Especialidad"] = "ONCOLOGIA", ["Form.Lugar"] = secretPlace,
            ["Form.TransporteModo"] = "AMBULANCIA", ["Form.AcompananteModo"] = "PERSONAL_CENTRO", ["Form.PrepAyuno"] = "true", ["Form.PrepTexto"] = "Llevar gafas", ["Form.Visibilidad"] = "VISIBLE",
        }));
        Assert.True(registered.IsSuccessStatusCode);

        var month = WebUtility.HtmlDecode(await admin.GetStringAsync($"/CitasExternas/Mes?anio={tomorrow.Year}&mes={tomorrow.Month}"));
        var week = WebUtility.HtmlDecode(await doctor.GetStringAsync($"/CitasExternas/Semana?dia={tomorrow:yyyy-MM-dd}"));
        var onlyUncovered = WebUtility.HtmlDecode(await admin.GetStringAsync($"/CitasExternas/Mes?anio={tomorrow.Year}&mes={tomorrow.Month}&sinCubrir=true"));
        var otherUnit = WebUtility.HtmlDecode(await admin.GetStringAsync($"/CitasExternas/Semana?dia={tomorrow:yyyy-MM-dd}&unidad={Guid.NewGuid()}"));
        var farMonth = await admin.GetAsync("/CitasExternas/Mes?anio=9999&mes=13");
        var farWeek = await admin.GetAsync("/CitasExternas/Semana?dia=9999-12-31");
        var badValues = await admin.GetAsync("/CitasExternas/Mes?anio=abc&mes=xyz&unidad=nope");
        var auxiliarView = WebUtility.HtmlDecode(await auxiliar.GetStringAsync("/Auxiliar/CitasExternas"));
        var auxiliarHome = WebUtility.HtmlDecode(await auxiliar.GetStringAsync("/Auxiliar"));
        var auxiliarCalendar = WebUtility.HtmlDecode(await auxiliar.GetStringAsync("/CitasExternas/Mes"));
        var staffOnAuxiliarView = WebUtility.HtmlDecode(await admin.GetStringAsync("/Auxiliar/CitasExternas"));

        Assert.Contains(ResidApp.Web.Models.ExternalAppointmentCalendarDisplay.MonthTitle(new DateOnly(tomorrow.Year, tomorrow.Month, 1)), month);
        Assert.Contains("Calendario de citas externas", month);
        Assert.Contains("R. C. F.", month);
        Assert.Contains("Falta transporte y acompañante", month);
        Assert.Contains("Falta transporte y acompañante", onlyUncovered);
        Assert.Contains("Residente Calendario Funcional", week);
        Assert.Contains("Oncología", week);
        Assert.Contains("Sin citas", week);
        Assert.DoesNotContain("Residente Calendario Funcional", otherUnit);
        Assert.Equal(HttpStatusCode.OK, farMonth.StatusCode);
        Assert.True(farMonth.Headers.CacheControl?.NoStore == true, "El calendario no se guarda en el dispositivo (DPO, 5.1).");
        Assert.Equal(HttpStatusCode.OK, farWeek.StatusCode);
        Assert.Equal(HttpStatusCode.OK, badValues.StatusCode);
        Assert.Contains("Citas externas de hoy y mañana", auxiliarView);
        Assert.Contains("Residente Calendario Funcional", auxiliarView);
        Assert.Contains("Mañana", auxiliarView);
        Assert.Contains("10:30", auxiliarView);
        Assert.Contains("Qué preparar: ayuno, Llevar gafas", auxiliarView);
        Assert.Contains("Acompaña: Personal del centro", auxiliarView);
        Assert.Contains("Transporte: Ambulancia", auxiliarView);
        Assert.DoesNotContain(secretPlace, auxiliarView);
        Assert.DoesNotContain("Oncología", auxiliarView);
        Assert.DoesNotContain(">Consulta<", auxiliarView);
        Assert.Contains("Citas externas de hoy y mañana", auxiliarHome);
        Assert.DoesNotContain("Calendario de citas externas", auxiliarCalendar);
        Assert.DoesNotContain("Residente Calendario Funcional", staffOnAuxiliarView);
    }

    [Fact]
    public async Task CitasExternas_Calendario_LimitesDeMes_FiltroDeUnidad_PorCerrar_YPerfilesSinAcceso_ConLaAppEntera()
    {
        // PR 3, revisión de calidad: meses de 28 y 31 días con sus semanas completas, el cambio de año, la unidad elegida que se conserva aunque no tenga citas, la marca «Por cerrar» (y su ausencia en
        // «solo las que tienen algo sin cubrir») y los perfiles que no entran por URL. Datos inventados.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Calendario Limites");
        var direccion = await LoginAsync(await GrantDirectionAsync(seed));
        var anonymous = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var today = ResidApp.Domain.Modules.CenterModuleRules.TodayInMadrid(DateTimeOffset.UtcNow);
        var nueva = $"/CitasExternas/Nueva?residenteId={residentId}";
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync(nueva));
        var registered = await admin.PostAsync("/CitasExternas/Nueva", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken"), ["Form.ResidenteId"] = residentId, ["Form.OperacionId"] = ExtractValue(page, "Form.OperacionId"),
            ["Form.Fecha"] = today.AddDays(2).ToString("yyyy-MM-dd"), ["Form.Tipo"] = "PRUEBA", ["Form.TransporteModo"] = "SIN_DETERMINAR", ["Form.AcompananteModo"] = "SIN_DETERMINAR",
            ["Form.Visibilidad"] = "VISIBLE",
        }));
        var citaId = Regex.Match(registered.RequestMessage!.RequestUri!.PathAndQuery, "([0-9a-f]{8}-[0-9a-f-]{27})").Groups[1].Value;
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();

        static int Rows(string html) => Regex.Matches(html, "<tr>").Count - 1; // menos la fila de los días
        var february = WebUtility.HtmlDecode(await admin.GetStringAsync("/CitasExternas/Mes?anio=2027&mes=2"));
        var august = WebUtility.HtmlDecode(await admin.GetStringAsync("/CitasExternas/Mes?anio=2027&mes=8"));
        var december = WebUtility.HtmlDecode(await admin.GetStringAsync("/CitasExternas/Mes?anio=2026&mes=12"));
        var withUnit = WebUtility.HtmlDecode(await admin.GetStringAsync($"/CitasExternas/Mes?anio={today.AddDays(2).Year}&mes={today.AddDays(2).Month}"));
        var unitId = Regex.Match(withUnit, "<option value=\"([0-9a-f-]{36})\"").Groups[1].Value;
        var realUnit = WebUtility.HtmlDecode(await admin.GetStringAsync($"/CitasExternas/Semana?dia={today.AddDays(2):yyyy-MM-dd}&unidad={unitId}"));
        var otherUnit = await admin.GetAsync($"/CitasExternas/Semana?dia={today.AddDays(2):yyyy-MM-dd}&unidad={Guid.NewGuid()}");
        var otherUnitHtml = WebUtility.HtmlDecode(await otherUnit.Content.ReadAsStringAsync());
        var uncoveredFilter = WebUtility.HtmlDecode(await admin.GetStringAsync($"/CitasExternas/Semana?dia={today.AddDays(2):yyyy-MM-dd}&sinCubrir=true"));
        await connection.ExecuteAsync("UPDATE dbo.citas_externas SET fecha = @date, version = version + 1 WHERE id = @id", new { id = citaId, date = today.AddDays(-1).ToDateTime(TimeOnly.MinValue) });
        var pastWeek = WebUtility.HtmlDecode(await admin.GetStringAsync($"/CitasExternas/Semana?dia={today.AddDays(-1):yyyy-MM-dd}"));
        var pastUncovered = WebUtility.HtmlDecode(await admin.GetStringAsync($"/CitasExternas/Semana?dia={today.AddDays(-1):yyyy-MM-dd}&sinCubrir=true"));
        var direccionMonth = WebUtility.HtmlDecode(await direccion.GetStringAsync("/CitasExternas/Mes"));
        var direccionWeek = WebUtility.HtmlDecode(await direccion.GetStringAsync("/CitasExternas/Semana"));
        var anonymousMonth = await anonymous.GetAsync("/CitasExternas/Mes");

        Assert.Equal(4, Rows(february));
        Assert.Equal(6, Rows(august));
        Assert.Contains("anio=2027&mes=1", december);
        Assert.Contains("anio=2026&mes=11", december);
        Assert.Contains("Diciembre de 2026", december);
        Assert.NotEmpty(unitId);
        Assert.Contains("Residente Calendario Limites", realUnit);
        Assert.Equal(HttpStatusCode.OK, otherUnit.StatusCode);
        Assert.DoesNotContain("Residente Calendario Limites", otherUnitHtml);
        Assert.Contains("La unidad elegida (sin citas en estas fechas)", otherUnitHtml);
        Assert.Contains("Residente Calendario Limites", uncoveredFilter);
        Assert.Contains("Falta transporte y acompañante", uncoveredFilter);
        Assert.Contains("Por cerrar", pastWeek);
        Assert.DoesNotContain("Falta transporte y acompañante", pastWeek);
        Assert.DoesNotContain("Residente Calendario Limites", pastUncovered);
        Assert.DoesNotContain("Calendario de citas externas", direccionMonth);
        Assert.DoesNotContain("Calendario de citas externas", direccionWeek);
        Assert.Equal(HttpStatusCode.Redirect, anonymousMonth.StatusCode);
        Assert.Contains("/Acceso/Entrar", anonymousMonth.Headers.Location?.OriginalString ?? "");
    }

    [Fact]
    public async Task CitasExternas_NotaAlTerminar_LaEscribeEnfermeria_LaLeenEnfermeriaYMedicina_AdministracionSoloVeLaSiguienteCita_ConLaAppEntera()
    {
        // Citas externas, PR 4 (script 0103): tras una cita realizada, Enfermería (aunque no tenga el permiso de gestionar citas) anota qué cambia para el centro; la leen Enfermería y Medicina;
        // Administración no la lee y solo ve la siguiente cita prevista, con el botón de registrarla (que rellena el formulario y guarda de cuál viene). Retirar no borra. Textos inventados.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Nota Funcional");
        var nurse = await LoginAsync(await GrantNursingAsync(seed));
        var doctor = await LoginAsync(await GrantMedicineAsync(seed));
        var today = ResidApp.Domain.Modules.CenterModuleRules.TodayInMadrid(DateTimeOffset.UtcNow);

        async Task<(string Html, string? Id)> PostAsync(HttpClient client, string path, string formPath, Dictionary<string, string> fields)
        {
            var page = await client.GetStringAsync(formPath);
            fields["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken");
            var response = await client.PostAsync(path, new FormUrlEncodedContent(fields));
            var query = response.RequestMessage!.RequestUri!.PathAndQuery;
            return (WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()), Regex.Match(query, "([0-9a-f]{8}-[0-9a-f-]{27})").Groups[1].Value is { Length: > 0 } id ? id : null);
        }

        var nueva = $"/CitasExternas/Nueva?residenteId={residentId}";
        var registered = await PostAsync(admin, "/CitasExternas/Nueva", nueva, new()
        {
            ["Form.ResidenteId"] = residentId, ["Form.Fecha"] = today.ToString("yyyy-MM-dd"), ["Form.Tipo"] = "CONSULTA", ["Form.Especialidad"] = "CARDIOLOGIA", ["Form.Lugar"] = "Hospital de Prueba",
            ["Form.TransporteModo"] = "FAMILIA", ["Form.AcompananteModo"] = "FAMILIAR", ["Form.Visibilidad"] = "SIN_ESPECIALIDAD", ["Form.PrepAyuno"] = "true", ["Form.PrepSilla"] = "true",
        });
        var citaId = registered.Id!;
        var ver = $"/CitasExternas/Ver?id={citaId}";
        var openView = WebUtility.HtmlDecode(await nurse.GetStringAsync(ver));
        Assert.DoesNotContain("Anotar al terminar", openView); // Todavía abierta: nada de nota.
        var done = await PostAsync(admin, "/CitasExternas/Estado", ver, new() { ["id"] = citaId, ["version"] = "1", ["estado"] = "REALIZADA" });
        Assert.Contains("Estado de la cita guardado", done.Html);

        var adminBefore = WebUtility.HtmlDecode(await admin.GetStringAsync(ver));
        var nurseBefore = WebUtility.HtmlDecode(await nurse.GetStringAsync(ver));
        var anotar = $"/CitasExternas/Anotar?id={citaId}";
        var noteForm = WebUtility.HtmlDecode(await nurse.GetStringAsync(anotar));
        var adminGetAnotar = WebUtility.HtmlDecode(await admin.GetStringAsync(anotar));

        Assert.Contains("Registrar la siguiente cita", adminBefore);
        Assert.DoesNotContain("Al terminar", adminBefore);
        Assert.DoesNotContain("Anotar al terminar", adminBefore);
        Assert.Contains("Todavía no hay nota", nurseBefore);
        Assert.Contains("Anotar al terminar", nurseBefore);
        Assert.Contains("No escribas diagnósticos, resultados de pruebas, dosis ni nombres de personas", noteForm);
        Assert.Contains("maxlength=\"150\"", noteForm);
        Assert.DoesNotContain("<textarea", noteForm);
        Assert.Contains("Administración verá este dato, y solo este", noteForm);
        Assert.DoesNotContain("name=\"Form.Resultado\"", adminGetAnotar); // Administración no recibe el formulario de la nota.
        Assert.Contains("La nota «al terminar» la escribe Enfermería", adminGetAnotar);

        // Sin el resultado, la nota no vale; con él, se guarda con la siguiente cita en seis meses.
        var withoutResult = await PostAsync(nurse, "/CitasExternas/Anotar", anotar, new() { ["Form.CitaId"] = citaId, ["Form.VersionVista"] = "0", ["Form.OperacionId"] = Guid.NewGuid().ToString(), ["Form.Siguiente"] = "ninguna" });
        Assert.Contains("Revisa la nota", withoutResult.Html);
        var operation = Guid.NewGuid().ToString();
        Dictionary<string, string> Note(string version, string result, string text, string next, string months = "", string day = "") => new()
        {
            ["Form.CitaId"] = citaId, ["Form.VersionVista"] = version, ["Form.OperacionId"] = operation, ["Form.Resultado"] = result, ["Form.Texto"] = text,
            ["Form.Siguiente"] = next, ["Form.SiguienteMeses"] = months, ["Form.SiguienteFecha"] = day,
        };
        var saved = await PostAsync(nurse, "/CitasExternas/Anotar", anotar, Note("0", "PENDIENTE_INFORME", "Recoger el informe el viernes", "meses", "6"));
        var resubmitted = await PostAsync(nurse, "/CitasExternas/Anotar", anotar, Note("0", "PENDIENTE_INFORME", "Recoger el informe el viernes", "meses", "6"));

        Assert.Contains("Nota guardada", saved.Html);
        Assert.Contains("Recoger el informe el viernes", saved.Html);
        Assert.Contains("Pendiente de informe", saved.Html);
        Assert.Contains("en 6 meses", saved.Html);
        Assert.Contains("Anotada por", saved.Html);
        Assert.Contains("Nota guardada", resubmitted.Html); // El reenvío del mismo formulario no crea otra versión.

        var doctorView = WebUtility.HtmlDecode(await doctor.GetStringAsync(ver));
        var adminView = WebUtility.HtmlDecode(await admin.GetStringAsync(ver));
        var doctorAnotar = WebUtility.HtmlDecode(await doctor.GetStringAsync(anotar));
        var adminVersions = WebUtility.HtmlDecode(await admin.GetStringAsync($"/CitasExternas/Versiones?id={citaId}"));
        var nurseVersions = WebUtility.HtmlDecode(await nurse.GetStringAsync($"/CitasExternas/Versiones?id={citaId}"));

        Assert.Contains("Recoger el informe el viernes", doctorView);
        Assert.DoesNotContain("Anotar al terminar", doctorView);
        Assert.DoesNotContain("Cambiar la nota", doctorView);
        Assert.DoesNotContain("name=\"Form.Resultado\"", doctorAnotar);
        // Administración: ni el resultado, ni la línea, ni quién la escribió; solo la siguiente cita prevista.
        Assert.Contains("Siguiente cita prevista", adminView);
        Assert.Contains("en 6 meses", adminView);
        Assert.DoesNotContain("Recoger el informe", adminView);
        Assert.DoesNotContain("Pendiente de informe", adminView);
        Assert.DoesNotContain("Anotada por", adminView);
        Assert.DoesNotContain("Al terminar", adminView);
        Assert.DoesNotContain("Recoger el informe", adminVersions);
        Assert.DoesNotContain("<table", adminVersions);
        Assert.Contains("Recoger el informe el viernes", nurseVersions);

        // «Registrar la siguiente cita»: por POST, con el tipo, el lugar y la visibilidad de la de origen, y la fecha sugerida a seis meses; al confirmar guarda de cuál viene.
        var next = await PostAsync(admin, "/CitasExternas/SiguienteCita", ver, new() { ["id"] = citaId });
        Assert.Contains("Es la siguiente cita de la del", next.Html);
        // Copia el tipo, la especialidad, el lugar y qué ve la familia; NO qué preparar, el transporte ni el acompañante (son de cada salida).
        Assert.Contains("value=\"CONSULTA\" selected", next.Html);
        Assert.Contains("value=\"CARDIOLOGIA\" selected", next.Html);
        Assert.DoesNotMatch("name=\"Form.PrepAyuno\"[^>]*checked", next.Html);
        Assert.DoesNotMatch("name=\"Form.PrepSilla\"[^>]*checked", next.Html);
        Assert.DoesNotContain("value=\"FAMILIA\" selected", next.Html);
        Assert.DoesNotContain("value=\"FAMILIAR\" selected", next.Html);
        Assert.Contains("value=\"SIN_DETERMINAR\" selected", next.Html);
        Assert.Contains($"name=\"Form.CitaOrigenId\" value=\"{citaId}\"", next.Html);
        Assert.Contains("Hospital de Prueba", next.Html);
        Assert.Contains(today.AddMonths(6).ToString("yyyy-MM-dd"), next.Html);
        Assert.Matches("id=\"visibilidad-SIN_ESPECIALIDAD\"[^>]*checked", next.Html);
        var nextDay = today.AddMonths(6).ToString("yyyy-MM-dd");
        var created = await PostAsync(admin, "/CitasExternas/Nueva", nueva, new()
        {
            ["Form.ResidenteId"] = residentId, ["Form.Fecha"] = nextDay, ["Form.Tipo"] = "CONSULTA", ["Form.Especialidad"] = "CARDIOLOGIA", ["Form.Lugar"] = "Hospital de Prueba",
            ["Form.TransporteModo"] = "SIN_DETERMINAR", ["Form.AcompananteModo"] = "SIN_DETERMINAR", ["Form.Visibilidad"] = "SIN_ESPECIALIDAD", ["Form.CitaOrigenId"] = citaId,
        });
        var adminAfter = WebUtility.HtmlDecode(await admin.GetStringAsync(ver));
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();

        Assert.Contains("Cita externa registrada", created.Html);
        Assert.Equal(Guid.Parse(citaId), await connection.ExecuteScalarAsync<Guid>("SELECT cita_origen_id FROM dbo.citas_externas WHERE id = @id", new { id = created.Id }));
        Assert.Contains("Siguiente cita registrada", adminAfter);
        Assert.DoesNotContain("Registrar la siguiente cita", adminAfter);

        // Retirar no borra: la nota deja de ser la vigente y las versiones se conservan.
        var retired = await PostAsync(nurse, "/CitasExternas/RetirarNota", anotar, new() { ["Form.CitaId"] = citaId, ["Form.VersionVista"] = "1", ["Form.MotivoRetirada"] = "ERROR_DE_REGISTRO" });
        var nurseAfter = WebUtility.HtmlDecode(await nurse.GetStringAsync(ver));
        var versionsAfter = WebUtility.HtmlDecode(await nurse.GetStringAsync($"/CitasExternas/Versiones?id={citaId}"));

        Assert.Contains("Nota retirada", retired.Html);
        Assert.Contains("Todavía no hay nota", nurseAfter);
        Assert.Contains("Nota retirada", versionsAfter);
        Assert.Contains("Recoger el informe el viernes", versionsAfter);
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.citas_externas_notas WHERE cita_id = @id", new { id = citaId }));

        // Tras retirar, la pantalla deja volver a anotar: la versión vista es la de la retirada (2), no 0, y se guarda como tercera versión.
        var againForm = WebUtility.HtmlDecode(await nurse.GetStringAsync(anotar));
        Assert.Contains("name=\"Form.VersionVista\" value=\"2\"", againForm);
        Assert.DoesNotContain("id=\"retirar-titulo\"", againForm); // sin nota vigente no hay nada que retirar
        var again = await PostAsync(nurse, "/CitasExternas/Anotar", anotar, new()
        {
            ["Form.CitaId"] = citaId, ["Form.VersionVista"] = "2", ["Form.OperacionId"] = Guid.NewGuid().ToString(), ["Form.Resultado"] = "SIN_CAMBIOS", ["Form.Siguiente"] = "ninguna",
        });
        Assert.Contains("Nota guardada", again.Html);
        Assert.Equal(3, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.citas_externas_notas WHERE cita_id = @id", new { id = citaId }));
    }

    private async Task<string> PostFichaAsync(HttpClient client, string token, string residentId, Dictionary<string, string> fields, string action = "Alergias")
    {
        fields["__RequestVerificationToken"] = token;
        fields["Form.ResidenteId"] = residentId;
        return WebUtility.HtmlDecode(await (await client.PostAsync($"/FichaAntecedentes/{action}", new FormUrlEncodedContent(fields))).Content.ReadAsStringAsync());
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

    [Fact]
    public async Task Administracion_CambiaElHorarioDeLosComunicados_ConLaAppEntera()
    {
        // CJ, 5.1 (2026-10-10): cada centro elige la hora de publicación y el margen. Con el usuario limitado: lee y escribe bajo la seguridad por filas (0051).
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var nurseSubject = await GrantNursingAsync(seed);
        var nurse = await LoginAsync(nurseSubject);
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        async Task<string> PostAsync(string time, string margin, string version, string frequency = "Semanal")
        {
            var form = await admin.GetStringAsync("/Administracion/HorarioComunicados");
            return WebUtility.HtmlDecode(await (await admin.PostAsync("/Administracion/HorarioComunicados", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractValue(form, "__RequestVerificationToken"), ["Form.Version"] = version,
                ["Form.HoraPublicacion"] = time, ["Form.MargenMinutos"] = margin, ["Form.ResumenFrecuencia"] = frequency,
            }))).Content.ReadAsStringAsync());
        }

        var initial = WebUtility.HtmlDecode(await admin.GetStringAsync("/Administracion/HorarioComunicados"));
        var invalid = await PostAsync("20:30", "5000", "0");
        var saved = await PostAsync("20:30", "120", "0");
        var stale = await PostAsync("21:00", "30", "0");
        var daily = await PostAsync("20:30", "120", "1", "Diaria");
        var comunicados = WebUtility.HtmlDecode(await admin.GetStringAsync("/Administracion/Comunicados"));
        var nurseDenied = await nurse.GetAsync("/Administracion/HorarioComunicados");

        Assert.Contains("Este centro sigue con el horario sugerido.", initial);
        Assert.Contains("El margen va de 0 a 1440 minutos.", invalid);
        Assert.Contains("Horario de los comunicados guardado.", saved);
        Assert.Matches(@"19:00, 60 min y resumen semanal\s*→ 20:30, 120 min y resumen semanal", saved);
        // La frecuencia del resumen (por defecto semanal) se elige en el mismo formulario, con las opciones en español.
        Assert.Contains("Frecuencia del resumen", initial);
        Assert.Contains(">Semanal</option>", initial);
        Assert.Contains(">Diaria</option>", initial);
        Assert.Matches(@"20:30, 120 min y resumen semanal\s*→ 20:30, 120 min y resumen diario", daily);
        Assert.Equal("DIARIA", await connection.ExecuteScalarAsync<string>(
            "SELECT resumen_frecuencia_codigo FROM dbo.comunicados_horario_centro WHERE centro_id = @c", new { c = seed.CenterId }));
        Assert.Contains("Otra persona ha cambiado el horario", stale);
        Assert.Contains("Cambiar la hora de publicación y el margen del centro", comunicados);
        Assert.Equal(("20:30:00", 120), (
            await connection.ExecuteScalarAsync<string>("SELECT CONVERT(VARCHAR(8), hora_publicacion) FROM dbo.comunicados_horario_centro WHERE centro_id = @c", new { c = seed.CenterId }),
            await connection.ExecuteScalarAsync<int>("SELECT margen_minutos FROM dbo.comunicados_horario_centro WHERE centro_id = @c", new { c = seed.CenterId })));
        var deniedPage = WebUtility.HtmlDecode(await nurseDenied.Content.ReadAsStringAsync());
        Assert.Contains(">Inicio</h1>", deniedPage);
        Assert.DoesNotContain("Form.HoraPublicacion", deniedPage);
    }

    [Fact]
    public async Task Comunicados_MedicinaLosVeYCorrige_DireccionLosPublicaAntes_ConLaAppEntera()
    {
        // CJ, 5.3 y 5.4 (2026-10-10): Medicina tiene su pantalla de comunicados, los corrige hasta que se publican, y Dirección Clínica puede
        // publicarlos antes de su hora. Con el usuario limitado: lee y escribe bajo la seguridad por filas.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Comunicados Medicina Funcional");
        var nurseSubject = await GrantNursingAsync(seed);
        var nurse = await LoginAsync(nurseSubject);
        var doctor = await LoginAsync(await GrantMedicineAsync(seed));
        var direction = await LoginAsync(await GrantDirectionAsync(seed));
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        async Task<string> PostAsync(HttpClient client, string url, string page, Dictionary<string, string> fields)
        {
            fields["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken");
            return WebUtility.HtmlDecode(await (await client.PostAsync(url, new FormUrlEncodedContent(fields))).Content.ReadAsStringAsync());
        }

        var registerPage = await nurse.GetStringAsync($"/Enfermeria/RegistrarEvento?residenteId={residentId}");
        var detailPage = await PostAsync(nurse, "/Enfermeria/RegistrarEvento", registerPage, new()
        {
            ["ResidenteId"] = residentId, ["OperacionId"] = ExtractValue(registerPage, "OperacionId"),
            ["Observacion"] = "Fiebre desde la tarde (prueba funcional).", ["Clasificacion"] = "Ordinario",
        });
        var eventId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.eventos_asistenciales WHERE residente_id = @residentId", new { residentId });
        Task<int> RevisionAsync() => connection.ExecuteScalarAsync<int>("SELECT revision FROM dbo.eventos_asistenciales WHERE id = @eventId", new { eventId });
        await PostAsync(nurse, "/Enfermeria/EmpezarValoracion", detailPage, new() { ["eventoId"] = eventId.ToString(), ["revision"] = (await RevisionAsync()).ToString() });
        await PostAsync(nurse, "/Enfermeria/Valoracion", await nurse.GetStringAsync($"/Enfermeria/Valoracion?eventoId={eventId}"), new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.Hallazgos"] = "Febrícula.",
        });
        await PostAsync(nurse, "/Enfermeria/Escalar", await nurse.GetStringAsync($"/Enfermeria/Escalar?eventoId={eventId}"), new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.Motivo"] = "Fiebre persistente.",
        });
        await PostAsync(doctor, "/Medicina/EmpezarValoracion", await doctor.GetStringAsync($"/Medicina/Escalado?eventoId={eventId}"), new()
        {
            ["eventoId"] = eventId.ToString(), ["revision"] = (await RevisionAsync()).ToString(),
        });
        await PostAsync(doctor, "/Medicina/Valoracion", await doctor.GetStringAsync($"/Medicina/Valoracion?eventoId={eventId}"), new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.HallazgosExploracion"] = "Febrícula sin foco.",
        });
        var closed = await PostAsync(doctor, "/Medicina/Cerrar", await doctor.GetStringAsync($"/Medicina/Cerrar?eventoId={eventId}"), new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.OperacionId"] = Guid.NewGuid().ToString(),
            ["Form.Comunicacion"] = "Preparar", ["Form.TipoComunicacion"] = "Ordinaria", ["Form.TextoComunicacion"] = "Ha tenido algo de fiebre; la vigilamos.",
        });
        Assert.Contains("Evento cerrado.", closed);

        var home = WebUtility.HtmlDecode(await doctor.GetStringAsync("/Medicina"));
        var list = WebUtility.HtmlDecode(await doctor.GetStringAsync("/Medicina/Comunicaciones"));
        var communicationId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.comunicaciones_familiares WHERE evento_id = @eventId", new { eventId });
        var formPage = WebUtility.HtmlDecode(await doctor.GetStringAsync($"/Medicina/CorregirComunicacion?comunicadoId={communicationId}"));
        var corrected = await PostAsync(doctor, "/Medicina/CorregirComunicacion", formPage, new()
        {
            ["Form.ComunicadoId"] = communicationId.ToString(), ["Form.Version"] = "0", ["Form.Tipo"] = "Relevante", ["Form.Texto"] = "Ha tenido fiebre; el equipo la vigila.",
        });
        var directionPage = WebUtility.HtmlDecode(await direction.GetStringAsync("/Direccion/Comunicados"));
        var published = await PostAsync(direction, "/Direccion/PublicarComunicado", directionPage, new() { ["comunicadoId"] = communicationId.ToString() });
        var afterPublication = WebUtility.HtmlDecode(await doctor.GetStringAsync("/Medicina/Comunicaciones"));
        var closedForm = WebUtility.HtmlDecode(await (await doctor.GetAsync($"/Medicina/CorregirComunicacion?comunicadoId={communicationId}")).Content.ReadAsStringAsync());

        Assert.Matches(@"<span class=""cifra"">1</span>\s*<span class=""text-body-secondary"">comunicación familiar pendiente de publicar", home);
        Assert.Contains("Ha tenido algo de fiebre; la vigilamos.", list);
        Assert.Contains("Se publica el", list);
        Assert.Contains("Corregir", list);
        Assert.Contains("Comunicado corregido.", corrected);
        Assert.Contains("Ha tenido fiebre; el equipo la vigila.", corrected);
        Assert.Contains("Publicar ahora", directionPage);
        // La frecuencia por unidad: una fila con un comunicado programado; solo cifras (ni el texto ni el residente salen en esa tabla).
        var frequency = Regex.Match(directionPage, "id=\"frecuencia\".*?</section>", RegexOptions.Singleline).Value;
        Assert.Contains("Frecuencia y estado por unidad", frequency);
        Assert.Matches("<td class=\"text-end\">1</td>", frequency);
        Assert.DoesNotContain("fiebre", frequency);
        Assert.DoesNotContain("Residente", frequency);
        Assert.Contains("Frecuencia y estado por unidad", published);
        Assert.Contains("Comunicado publicado antes de su hora.", published);
        Assert.Contains("Publicado antes de su hora", published);
        Assert.Contains("Publicada antes de su hora", afterPublication);
        Assert.DoesNotContain("CorregirComunicacion?comunicadoId", afterPublication);
        Assert.Contains("ya no se puede corregir", closedForm);
        Assert.Equal("DIRECCION_CLINICA", await connection.ExecuteScalarAsync<string>(
            "SELECT perfil_activo FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE accion_codigo = 'FAMILY_COMMUNICATION_PUBLISH_NOW' AND recurso_id = @communicationId", new { communicationId }));
        Assert.Equal("MEDICINA", await connection.ExecuteScalarAsync<string>(
            "SELECT perfil_activo FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE accion_codigo = 'FAMILY_COMMUNICATION_CORRECT' AND recurso_id = @communicationId", new { communicationId }));
    }

    [Fact]
    public async Task Alta_SinFamiliaConocida_ExigeElMotivo_YLoMuestranAdministracionEnfermeriaYElProtocolo_ConLaAppEntera()
    {
        // CJ, 2.1 (2026-10-10): un residente puede entrar sin contacto urgente si se indica el motivo; la ficha de Administración y la de
        // Enfermería lo dicen, y la ficha deja volver a designar contactos cuando haya familia.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var nurseSubject = await GrantNursingAsync(seed);
        var nurse = await LoginAsync(nurseSubject);
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        var name = $"Residente Sin Familia Funcional {Guid.NewGuid().ToString("N")[..8]}";

        async Task<string> CreateAsync(string? reason)
        {
            var page = await admin.GetStringAsync("/Residents/Create");
            var fields = new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken"), ["OperacionId"] = ExtractValue(page, "OperacionId"),
                ["AmbitoPerfilId"] = seed.ProfileScopeId.ToString(), ["CentroId"] = seed.CenterId.ToString(), ["UnidadId"] = seed.UnitId.ToString(),
                ["NombreVisible"] = name, ["FechaNacimiento"] = "1938-02-20", ["SexoDocumentadoCodigo"] = "Hombre",
            };
            if (reason is not null)
            {
                fields["MotivoSinContacto"] = reason;
            }

            return WebUtility.HtmlDecode(await (await admin.PostAsync("/Residents/Create", new FormUrlEncodedContent(fields))).Content.ReadAsStringAsync());
        }

        var rejected = await CreateAsync(null);
        var created = await CreateAsync("Sin familia conocida");
        var residentId = Regex.Match(created, "Id del residente: <code>([0-9a-f-]{36})</code>").Groups[1].Value;

        Assert.Contains("escribe el motivo", rejected);
        Assert.Contains("Id del residente", created);
        Assert.Equal(("SIN_CONTACTO", "Sin familia conocida"), (
            await connection.ExecuteScalarAsync<string>("SELECT accion_codigo FROM dbo.residentes_contacto_urgente WHERE residente_id = @residentId", new { residentId }),
            await connection.ExecuteScalarAsync<string>("SELECT motivo FROM dbo.residentes_contacto_urgente WHERE residente_id = @residentId", new { residentId })));
        var adminFile = WebUtility.HtmlDecode(await admin.GetStringAsync($"/Administracion/Residente?residenteId={residentId}"));
        Assert.Matches(@"Sin contacto urgente:\s*(<span[^>]*>)?Sin familia conocida", adminFile);
        Assert.DoesNotContain("no tiene familiares registrados", adminFile);
        var nurseFile = WebUtility.HtmlDecode(await nurse.GetStringAsync($"/Enfermeria/Residente?residenteId={residentId}"));
        Assert.Matches(@"Sin contacto urgente:\s*(<span[^>]*>)?Sin familia conocida", nurseFile);

        // Con la derivación firmada y sin contacto urgente, la tarjeta de cierre no exige el intento de llamada (el cierre ya lo exime).
        var registerPage = await nurse.GetStringAsync($"/Enfermeria/RegistrarEvento?residenteId={residentId}");
        var detailPage = await (await nurse.PostAsync("/Enfermeria/RegistrarEvento", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(registerPage, "__RequestVerificationToken"), ["ResidenteId"] = residentId,
            ["OperacionId"] = ExtractValue(registerPage, "OperacionId"), ["Observacion"] = "Disnea brusca (prueba funcional).", ["Clasificacion"] = "Ordinario",
        }))).Content.ReadAsStringAsync();
        var eventId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.eventos_asistenciales WHERE residente_id = @residentId", new { residentId });
        Task<int> RevisionAsync() => connection.ExecuteScalarAsync<int>("SELECT revision FROM dbo.eventos_asistenciales WHERE id = @eventId", new { eventId });
        async Task<string> PostAsync(string url, string page, Dictionary<string, string> fields)
        {
            fields["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken");
            return WebUtility.HtmlDecode(await (await nurse.PostAsync(url, new FormUrlEncodedContent(fields))).Content.ReadAsStringAsync());
        }

        await PostAsync("/Enfermeria/EmpezarValoracion", detailPage, new() { ["eventoId"] = eventId.ToString(), ["revision"] = (await RevisionAsync()).ToString() });
        await PostAsync("/Enfermeria/Valoracion", await nurse.GetStringAsync($"/Enfermeria/Valoracion?eventoId={eventId}"), new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.Hallazgos"] = "Crepitantes bilaterales.",
        });
        await PostAsync("/Enfermeria/ActivarProtocolo", await nurse.GetStringAsync($"/Enfermeria/ActivarProtocolo?eventoId={eventId}"), new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(),
        });
        var referralPage = await nurse.GetStringAsync($"/Enfermeria/Derivar?eventoId={eventId}");
        var preview = await PostAsync("/Enfermeria/Derivar", referralPage, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = (await RevisionAsync()).ToString(), ["Form.OperacionId"] = ExtractValue(referralPage, "Form.OperacionId"),
            ["Form.Motivo"] = "Desaturación que no remonta.", ["Form.Comunicaciones"] = "Aviso al SEM.", ["Form.Accion"] = "VistaPrevia",
        });
        var signed = await PostAsync("/Enfermeria/Derivar", preview, new()
        {
            ["Form.EventoId"] = eventId.ToString(), ["Form.Revision"] = ExtractValue(preview, "Form.Revision"), ["Form.OperacionId"] = ExtractValue(preview, "Form.OperacionId"),
            ["Form.Motivo"] = ExtractValue(preview, "Form.Motivo"), ["Form.InformacionAdicional"] = "", ["Form.Comunicaciones"] = ExtractValue(preview, "Form.Comunicaciones"),
            ["Form.Huella"] = ExtractValue(preview, "Form.Huella"), ["Form.Accion"] = "Firmar",
        });
        Assert.Contains("Informe de derivación firmado.", signed);
        Assert.Contains("el residente no tiene contacto urgente, así que no hace falta ningún intento de llamada", signed);
        Assert.DoesNotContain("exige al menos un intento de llamada", signed);

        // Se vuelve a designar contacto en cuanto hay familia, y el motivo deja de verse.
        var addPage = await admin.GetStringAsync($"/Administracion/AnadirFamiliar?residenteId={residentId}");
        await admin.PostAsync("/Administracion/AnadirFamiliar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(addPage, "__RequestVerificationToken"), ["Form.ResidenteId"] = residentId,
            ["Form.OperacionId"] = ExtractValue(addPage, "Form.OperacionId"), ["Form.NombreVisible"] = "Hija Aparecida", ["Form.Relacion"] = "Hija",
            ["Form.Telefono"] = "600123456",
        }));
        var linkId = await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.residentes_familiares WHERE residente_id = @residentId", new { residentId });
        var contactPage = await admin.GetStringAsync($"/Administracion/ContactoUrgente?residenteId={residentId}");
        var designated = WebUtility.HtmlDecode(await (await admin.PostAsync("/Administracion/ContactoUrgente", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(contactPage, "__RequestVerificationToken"), ["Form.ResidenteId"] = residentId,
            ["Form.DesignacionesEsperadas"] = "1", ["Form.VinculosIds"] = linkId.ToString(),
        }))).Content.ReadAsStringAsync());

        Assert.Contains("Contactos urgentes guardados.", designated);
        Assert.DoesNotContain("Sin contacto urgente: Sin familia conocida\n", WebUtility.HtmlDecode(await nurse.GetStringAsync($"/Enfermeria/Residente?residenteId={residentId}")));
    }

    [Fact]
    public async Task Administracion_BuscaYVinculaUnFamiliarDelCentro_ConLaAppEntera()
    {
        // CJ, 1.1: vincular un familiar que ya existe se busca por nombre o teléfono entre los de todo el centro. Con el usuario limitado
        // lee familiares y vínculos bajo la seguridad por filas.
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var first = await CreateResidentAsync(admin, seed, "Residente Vincular Primero Funcional");
        var second = await CreateResidentAsync(admin, seed, "Residente Vincular Segundo Funcional");
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();

        var empty = WebUtility.HtmlDecode(await admin.GetStringAsync($"/Administracion/VincularFamiliar?residenteId={second}"));
        var tooShort = WebUtility.HtmlDecode(await admin.GetStringAsync($"/Administracion/VincularFamiliar?residenteId={second}&q=Fa"));
        var noMatch = WebUtility.HtmlDecode(await admin.GetStringAsync($"/Administracion/VincularFamiliar?residenteId={second}&q=Zacarias"));
        var found = WebUtility.HtmlDecode(await admin.GetStringAsync($"/Administracion/VincularFamiliar?residenteId={second}&q=familiar%20PRUEBA"));
        var byPhone = WebUtility.HtmlDecode(await admin.GetStringAsync($"/Administracion/VincularFamiliar?residenteId={second}&q=600%20123"));

        Assert.Contains("Busca al familiar para vincularlo.", empty);
        Assert.Contains("Busca al familiar para vincularlo.", tooShort);
        Assert.Contains("Ningún familiar del centro coincide con «Zacarias»", noMatch);
        Assert.Contains("Familiar Prueba · 600123456 · vinculado a 1 residente", found);
        Assert.Contains("Familiar Prueba · 600123456 · vinculado a 1 residente", byPhone);

        var familyId = await connection.ExecuteScalarAsync<Guid>(
            "SELECT familiar_id FROM dbo.residentes_familiares WHERE residente_id = @first", new { first });
        var linked = WebUtility.HtmlDecode(await (await admin.PostAsync("/Administracion/VincularFamiliar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(found, "__RequestVerificationToken"),
            ["Form.ResidenteId"] = second, ["Form.OperacionId"] = ExtractValue(found, "Form.OperacionId"), ["Form.Q"] = "familiar PRUEBA",
            ["Form.FamiliarId"] = familyId.ToString(), ["Form.Relacion"] = "Sobrina",
        }))).Content.ReadAsStringAsync());

        Assert.Contains("Familiar vinculado.", linked);
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.residentes_familiares WHERE familiar_id = @familyId AND desvinculado_en IS NULL", new { familyId }));
        var again = WebUtility.HtmlDecode(await admin.GetStringAsync($"/Administracion/VincularFamiliar?residenteId={second}&q=familiar%20PRUEBA"));
        Assert.DoesNotContain("vinculado a 1 residente", again);
    }

    [Fact]
    public async Task Administracion_VeLosComunicadosAFamiliasYLosPublicaAntes_ConLaAppEntera()
    {
        // CJ (2026-10-07): el comunicado se aprueba al guardarlo, sale a una hora fija tras 1 h de margen y Administración puede publicarlo
        // antes. Con el usuario limitado: lee y escribe bajo la seguridad por filas (script 0048).
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Comunicado Funcional");
        var nurseSubject = await GrantNursingAsync(seed);
        var nurse = await LoginAsync(nurseSubject);
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        var registerPage = await nurse.GetStringAsync($"/Enfermeria/RegistrarEvento?residenteId={residentId}");
        (await nurse.PostAsync("/Enfermeria/RegistrarEvento", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(registerPage, "__RequestVerificationToken"),
            ["ResidenteId"] = residentId,
            ["OperacionId"] = ExtractValue(registerPage, "OperacionId"),
            ["Observacion"] = "Evento para un comunicado (prueba funcional).",
            ["Clasificacion"] = "Ordinario",
        }))).EnsureSuccessStatusCode();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.comunicaciones_familiares (id, evento_id, residente_id, centro_id, tipo_codigo, texto, preparado_por_cuenta_id, preparado_en)
            SELECT NEWID(), ea.id, ea.residente_id, ea.centro_id, 'ORDINARIA', 'Hoy ha estado tranquila y ha comido bien.', account.id, SYSUTCDATETIME()
              FROM dbo.eventos_asistenciales ea JOIN dbo.cuentas account ON account.sujeto_externo = @subject
             WHERE ea.residente_id = @residentId
            """, new { subject = seed.ExternalSubject, residentId });

        var home = WebUtility.HtmlDecode(await admin.GetStringAsync("/Administracion"));
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/Administracion/Comunicados"));
        var communicationId = Regex.Match(page, "name=\"comunicadoId\" value=\"([0-9a-f-]{36})\"").Groups[1].Value;
        var published = WebUtility.HtmlDecode(await (await admin.PostAsync("/Administracion/PublicarComunicado", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken"),
            ["comunicadoId"] = communicationId,
        }))).Content.ReadAsStringAsync());
        var nursePage = WebUtility.HtmlDecode(await nurse.GetStringAsync("/Enfermeria/Comunicaciones"));

        Assert.Contains("Comunicados a la familia", home);
        Assert.Contains("Hoy ha estado tranquila y ha comido bien.", page);
        Assert.Contains("Se publica el", page);
        Assert.Contains("Publicar ahora", page);
        Assert.Contains("Comunicado publicado antes de su hora.", published);
        Assert.Contains("Publicado antes de su hora", published);
        Assert.DoesNotContain("Publicar ahora", published);
        Assert.Contains("Publicada antes de su hora", nursePage);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.comunicaciones_familiares_publicacion_anticipada WHERE comunicacion_id = @communicationId", new { communicationId }));
    }

    [Fact]
    public async Task Enfermeria_CorrigeUnComunicadoDentroDelMargen_YDejaDePoderHacerloSiSePublicaAntes_ConLaAppEntera()
    {
        // CJ (2026-10-07): 1 hora de margen para corregir. Con el usuario limitado: la corrección escribe bajo la seguridad por filas (script 0049).
        var seed = await SeedAsync();
        var admin = await LoginAsync(seed.ExternalSubject);
        var residentId = await CreateResidentAsync(admin, seed, "Residente Correccion Funcional");
        var nurseSubject = await GrantNursingAsync(seed);
        var nurse = await LoginAsync(nurseSubject);
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        var registerPage = await nurse.GetStringAsync($"/Enfermeria/RegistrarEvento?residenteId={residentId}");
        (await nurse.PostAsync("/Enfermeria/RegistrarEvento", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(registerPage, "__RequestVerificationToken"),
            ["ResidenteId"] = residentId,
            ["OperacionId"] = ExtractValue(registerPage, "OperacionId"),
            ["Observacion"] = "Evento para corregir un comunicado (prueba funcional).",
            ["Clasificacion"] = "Ordinario",
        }))).EnsureSuccessStatusCode();
        var communicationId = Guid.NewGuid();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.comunicaciones_familiares (id, evento_id, residente_id, centro_id, tipo_codigo, texto, preparado_por_cuenta_id, preparado_en)
            SELECT @communicationId, ea.id, ea.residente_id, ea.centro_id, 'ORDINARIA', 'Texto con una errata.', account.id, SYSUTCDATETIME()
              FROM dbo.eventos_asistenciales ea JOIN dbo.cuentas account ON account.sujeto_externo = @subject
             WHERE ea.residente_id = @residentId
            """, new { communicationId, subject = seed.ExternalSubject, residentId });

        var list = WebUtility.HtmlDecode(await nurse.GetStringAsync("/Enfermeria/Comunicaciones"));
        var formPage = WebUtility.HtmlDecode(await nurse.GetStringAsync($"/Enfermeria/CorregirComunicacion?comunicadoId={communicationId}"));
        Task<string> PostAsync(string text, string version) => nurse.PostAsync("/Enfermeria/CorregirComunicacion", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(formPage, "__RequestVerificationToken"),
            ["Form.ComunicadoId"] = communicationId.ToString(),
            ["Form.Version"] = version,
            ["Form.Tipo"] = "Relevante",
            ["Form.Texto"] = text,
        })).ContinueWith(t => t.Result.Content.ReadAsStringAsync()).Unwrap().ContinueWith(t => WebUtility.HtmlDecode(t.Result));
        var empty = await PostAsync("   ", "0");
        var corrected = await PostAsync("Texto sin errata.", "0");
        var stale = await PostAsync("Otro intento con la versión antigua.", "0");
        var publicationPage = WebUtility.HtmlDecode(await admin.GetStringAsync("/Administracion/Comunicados"));
        var adminList = WebUtility.HtmlDecode(await (await admin.PostAsync("/Administracion/PublicarComunicado", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(publicationPage, "__RequestVerificationToken"),
            ["comunicadoId"] = communicationId.ToString(),
        }))).Content.ReadAsStringAsync());
        var afterPublication = WebUtility.HtmlDecode(await nurse.GetStringAsync("/Enfermeria/Comunicaciones"));
        var closedForm = WebUtility.HtmlDecode(await (await nurse.GetAsync($"/Enfermeria/CorregirComunicacion?comunicadoId={communicationId}")).Content.ReadAsStringAsync());

        Assert.Contains("Texto con una errata.", list);
        Assert.Contains("Corregir", list);
        Assert.Contains("Guardar la corrección", formPage);
        Assert.Contains("Escribe el texto para la familia.", empty);
        Assert.Contains("Comunicado corregido.", corrected);
        Assert.Contains("Texto sin errata.", corrected);
        Assert.Contains("Corregido una vez", corrected);
        Assert.Contains("No se ha guardado: alguien lo corrigió antes", stale);
        Assert.Contains("Texto sin errata.", adminList);
        Assert.Contains("Comunicado publicado antes de su hora.", adminList);
        Assert.DoesNotContain("btn btn-outline-primary me-2\">Corregir", afterPublication);
        Assert.Contains("Ese comunicado ya no se puede corregir", closedForm);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.comunicaciones_familiares_correcciones WHERE comunicacion_id = @communicationId", new { communicationId }));
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
        (await SesionTests.PostLoginAsync(client, externalSubject)).EnsureSuccessStatusCode();
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

    /// <summary>Una cuenta de Medicina en el centro y la unidad de la semilla.</summary>
    private static async Task<string> GrantMedicineAsync((string ExternalSubject, Guid ProfileScopeId, Guid CenterId, Guid UnitId) seed)
    {
        var accountId = Guid.NewGuid();
        var profileScopeId = Guid.NewGuid();
        var externalSubject = $"functional-med-{Guid.NewGuid().ToString("N")[..12]}";
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@accountId, @externalSubject, 'ACTIVE', @now);
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (@profileScopeId, @accountId, @centerId, 'MEDICINA', 'ACTIVE', @now, @accountId);
            INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @profileScopeId, @centerId, @unitId, @now, @accountId);
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
            ?? "Server=ACER-ORLANDO;Database=ResidAppPruebas;Integrated Security=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

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

        // En CreateHost y no en ConfigureWebHost, que sobrescriben las fábricas derivadas; WithWebHostBuilder también pasa por aquí.
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureServices(services => services.AddTransient<IStartupFilter, EntradaDePruebas>());
            return base.CreateHost(builder);
        }
    }
}
