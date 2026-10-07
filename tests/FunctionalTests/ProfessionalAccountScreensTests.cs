using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Infrastructure.Persistence;
using ResidApp.Shared;
using ResidApp.Web.Models;

namespace ResidApp.FunctionalTests;

/// <summary>ADM-12/ADM-13 (0023) a través de ResidApp.Web de verdad: alta de usuario, ficha y lista, en español, y la
/// propia cuenta sin acciones.</summary>
public class ProfessionalAccountScreensTests : IClassFixture<ResidentsFlowTests.WebAppFactory>
{
    private readonly ResidentsFlowTests.WebAppFactory _factory;

    public ProfessionalAccountScreensTests(ResidentsFlowTests.WebAppFactory factory) => _factory = factory;

    [Fact]
    public void Estado_SeMuestraEnEspañol()
    {
        Assert.Equal("Activa", ProfessionalAccountDisplay.Status(AccountStatus.Active));
        Assert.Equal("Suspendida", ProfessionalAccountDisplay.Status(AccountStatus.Suspended));
        Assert.Equal(["Auxiliar", "Enfermería", "Medicina", "Administración", "Dirección Clínica"],
            ProfessionalAccountDisplay.ProfileOptions(ProfessionalAccount.GrantableProfiles).Select(o => o.Text));
    }

    [Fact]
    public async Task Alta_LlevaALaFichaDeLaCuenta_YLaPropiaCuentaNoTieneAcciones()
    {
        var admin = await SeedAdministratorAsync();
        var client = _factory.CreateClient();
        var loginPage = await client.GetStringAsync("/DevAuth/Login");
        (await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(loginPage, "__RequestVerificationToken"),
            ["externalSubject"] = admin.ExternalSubject,
        }))).EnsureSuccessStatusCode();

        var ownPage = WebUtility.HtmlDecode(await client.GetStringAsync($"/Administracion/Usuario?cuentaId={admin.AccountId}"));
        var newPage = await client.GetStringAsync("/Administracion/NuevoUsuario");
        var subject = $"func-alta-{Guid.NewGuid():N}"[..30];
        async Task<HttpResponseMessage> PostAsync(Dictionary<string, string> fields) => await client.PostAsync(
            "/Administracion/NuevoUsuario", new FormUrlEncodedContent(new Dictionary<string, string>(fields)
            {
                ["__RequestVerificationToken"] = ExtractValue(newPage, "__RequestVerificationToken"),
                ["Form.OperacionId"] = ExtractValue(newPage, "Form.OperacionId"),
            }));
        var empty = WebUtility.HtmlDecode(await (await PostAsync([])).Content.ReadAsStringAsync());
        var created = WebUtility.HtmlDecode(await (await PostAsync(new()
        {
            ["Form.Identificador"] = subject,
            ["Form.NombreVisible"] = "Ana Ruiz (ficticia)",
            ["Form.Perfil"] = "Enfermeria",
            ["Form.Unidades"] = admin.UnitId.ToString(),
        })).Content.ReadAsStringAsync());
        var list = WebUtility.HtmlDecode(await client.GetStringAsync("/Administracion/Usuarios"));

        Assert.Contains("Es tu propia cuenta", ownPage);
        Assert.DoesNotContain("Suspender cuenta", ownPage);
        Assert.DoesNotContain("Conceder perfil", ownPage);
        Assert.Contains("<option value=\"Enfermeria\">Enfermería</option>", WebUtility.HtmlDecode(newPage));
        Assert.DoesNotContain("Familiar", newPage);
        Assert.Matches($"value=\"{admin.UnitId}\"[^>]*checked=\"checked\"", newPage);
        Assert.Contains("Escribe el identificador de acceso.", empty);
        Assert.Contains("Escribe el nombre.", empty);
        Assert.Contains("Elige el perfil.", empty);
        Assert.Contains("Elige al menos una unidad.", empty);
        Assert.DoesNotContain("The ", empty);
        Assert.Contains("Usuario dado de alta.", created);
        Assert.Contains(subject, created);
        Assert.Contains("Suspender cuenta", created);
        Assert.Contains("Conceder perfil", created);
        Assert.Matches("<th scope=\"row\">Enfermería</th>", created);
        Assert.Contains("Ana Ruiz (ficticia)", list);
        Assert.Contains("Enfermería · ", list);
    }

    [Fact]
    public async Task Estructura_AdministracionCreaYRenombraUnaUnidad_YOtroPerfilNoEntra()
    {
        var admin = await SeedAdministratorAsync();
        var client = _factory.CreateClient();
        var loginPage = await client.GetStringAsync("/DevAuth/Login");
        (await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(loginPage, "__RequestVerificationToken"),
            ["externalSubject"] = admin.ExternalSubject,
        }))).EnsureSuccessStatusCode();

        var newPage = await client.GetStringAsync("/Administracion/NuevaUnidad");
        async Task<string> PostAsync(string path, string page, Dictionary<string, string> fields) =>
            WebUtility.HtmlDecode(await (await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>(fields)
            {
                ["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken"),
            }))).Content.ReadAsStringAsync());
        var code = $"func-{Guid.NewGuid():N}"[..16];
        var empty = await PostAsync("/Administracion/NuevaUnidad", newPage, new() { ["Form.OperacionId"] = ExtractValue(newPage, "Form.OperacionId") });
        var created = await PostAsync("/Administracion/NuevaUnidad", newPage, new()
        {
            ["Form.OperacionId"] = ExtractValue(newPage, "Form.OperacionId"),
            ["Form.Codigo"] = code,
            ["Form.Nombre"] = "Planta Norte (ficticia)",
        });
        var listPage = await client.GetStringAsync("/Administracion/Estructura");
        var unitId = Regex.Match(listPage, "name=\"unidadId\" value=\"([0-9a-f-]{36})\"").Groups[1].Value;
        var renamePage = await client.GetStringAsync($"/Administracion/NombreUnidad?unidadId={unitId}");
        var renamed = await PostAsync("/Administracion/NombreUnidad", renamePage, new()
        {
            ["Form.UnidadId"] = unitId,
            ["Form.Nombre"] = "Planta Norte renombrada (ficticia)",
        });
        var inactivated = await PostAsync("/Administracion/EstadoUnidad", renamed, new() { ["unidadId"] = unitId, ["activa"] = "false" });
        var again = await PostAsync("/Administracion/EstadoUnidad", inactivated, new() { ["unidadId"] = unitId, ["activa"] = "false" });
        var nursePage = await PageAsync("ENFERMERIA", null, "/Administracion/Estructura");

        Assert.Contains("Escribe el código.", empty);
        Assert.Contains("Escribe el nombre.", empty);
        Assert.DoesNotContain("The ", empty);
        Assert.Contains("Unidad creada.", created);
        Assert.Contains("Planta Norte (ficticia)", created);
        Assert.Contains(code.ToUpperInvariant(), created);
        Assert.Contains("Activa", created);
        Assert.Contains("Nombre guardado.", renamed);
        Assert.Contains("Planta Norte renombrada (ficticia)", renamed);
        Assert.Contains("Unidad inactivada", inactivated);
        Assert.Contains("Inactiva", inactivated);
        Assert.Contains("Reactivar", inactivated);
        Assert.Contains("La unidad ya estaba en ese estado.", again);
        Assert.Contains("No se puede acceder a esta operación", nursePage);
        Assert.DoesNotContain("Nueva unidad", nursePage);
    }

    [Fact]
    public async Task Edificios_CreaEdificioYPlanta_ColocaLaUnidad_YOtroPerfilNoEntra()
    {
        var admin = await SeedAdministratorAsync();
        var client = _factory.CreateClient();
        var loginPage = await client.GetStringAsync("/DevAuth/Login");
        (await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(loginPage, "__RequestVerificationToken"),
            ["externalSubject"] = admin.ExternalSubject,
        }))).EnsureSuccessStatusCode();
        async Task<string> PostAsync(string path, string page, Dictionary<string, string> fields) =>
            WebUtility.HtmlDecode(await (await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>(fields)
            {
                ["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken"),
            }))).Content.ReadAsStringAsync());

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var listPage = await client.GetStringAsync("/Administracion/Edificios");
        var empty = await PostAsync("/Administracion/NuevoEdificio", listPage, new() { ["Form.OperacionId"] = Guid.NewGuid().ToString(), ["Form.Nombre"] = "" });
        var created = await PostAsync("/Administracion/NuevoEdificio", listPage, new()
        {
            ["Form.OperacionId"] = Guid.NewGuid().ToString(), ["Form.Nombre"] = $"Edificio {suffix} (ficticio)",
        });
        var buildingId = Regex.Match(created, "name=\"edificioId\" value=\"([0-9a-f-]{36})\"").Groups[1].Value;
        var duplicated = await PostAsync("/Administracion/NuevoEdificio", created, new()
        {
            ["Form.OperacionId"] = Guid.NewGuid().ToString(), ["Form.Nombre"] = $"edificio {suffix} (ficticio)",
        });
        var floorCreated = await PostAsync("/Administracion/NuevaPlanta", created, new()
        {
            ["Form.OperacionId"] = Guid.NewGuid().ToString(), ["Form.EdificioId"] = buildingId, ["Form.Nombre"] = "Planta 1",
        });
        var floorId = Regex.Match(floorCreated, "name=\"plantaId\" value=\"([0-9a-f-]{36})\"").Groups[1].Value;
        var renamePage = await client.GetStringAsync($"/Administracion/NombrePlanta?plantaId={floorId}");
        var renamed = await PostAsync("/Administracion/NombrePlanta", renamePage, new() { ["Form.Id"] = floorId, ["Form.Nombre"] = "Planta baja" });
        var locationPage = await client.GetStringAsync($"/Administracion/UbicacionUnidad?unidadId={admin.UnitId}");
        var badLocation = await PostAsync("/Administracion/UbicacionUnidad", locationPage, new() { ["Form.UnidadId"] = admin.UnitId.ToString(), ["Form.Ubicacion"] = "x:1" });
        var located = await PostAsync("/Administracion/UbicacionUnidad", locationPage, new()
        {
            ["Form.UnidadId"] = admin.UnitId.ToString(), ["Form.Ubicacion"] = $"f:{buildingId}:{floorId}",
        });
        var blocked = await PostAsync("/Administracion/EstadoEdificio", located, new() { ["edificioId"] = buildingId, ["activo"] = "false" });
        var nursePage = await PageAsync("ENFERMERIA", null, "/Administracion/Edificios");
        var nurseLocation = await PageAsync("ENFERMERIA", null, $"/Administracion/UbicacionUnidad?unidadId={admin.UnitId}");

        Assert.Contains("Escribe el nombre del edificio", empty);
        Assert.Contains("Edificio creado.", created);
        Assert.Contains($"Edificio {suffix} (ficticio)", created);
        Assert.Contains("Ya existe un edificio con ese nombre en el centro.", duplicated);
        Assert.Contains("Planta creada.", floorCreated);
        Assert.Contains("Nombre guardado.", renamed);
        Assert.Contains("Planta baja", renamed);
        Assert.Contains("Elige una ubicación de la lista.", badLocation);
        Assert.Contains("Ubicación de la unidad guardada.", located);
        Assert.Contains($"Edificio {suffix} (ficticio) · Planta baja", located);
        Assert.Contains("No se puede inactivar un edificio con plantas o unidades activas.", blocked);
        Assert.Contains("No se puede acceder a esta operación", nursePage);
        Assert.DoesNotContain("Crear edificio", nursePage);
        Assert.Contains("No se puede acceder a esta operación", nurseLocation);
    }

    [Fact]
    public async Task Habitaciones_CreaHabitacionYPlaza_ElAltaLaOfrece_LaPlazaOcupadaSeRechaza_YOtroPerfilNoEntra()
    {
        var admin = await SeedAdministratorAsync();
        var client = _factory.CreateClient();
        var loginPage = await client.GetStringAsync("/DevAuth/Login");
        (await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(loginPage, "__RequestVerificationToken"),
            ["externalSubject"] = admin.ExternalSubject,
        }))).EnsureSuccessStatusCode();
        async Task<string> PostAsync(string path, string page, Dictionary<string, string> fields) =>
            WebUtility.HtmlDecode(await (await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>(fields)
            {
                ["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken"),
            }))).Content.ReadAsStringAsync());

        var roomsUrl = $"/Administracion/Habitaciones?unidadId={admin.UnitId}";
        var emptyPage = await client.GetStringAsync(roomsUrl);
        var empty = await PostAsync("/Administracion/NuevaHabitacion", emptyPage, new() { ["Form.OperacionId"] = Guid.NewGuid().ToString(), ["Form.UnidadId"] = admin.UnitId.ToString() });
        var created = await PostAsync("/Administracion/NuevaHabitacion", emptyPage, new()
        {
            ["Form.OperacionId"] = Guid.NewGuid().ToString(), ["Form.UnidadId"] = admin.UnitId.ToString(), ["Form.Nombre"] = "Habitación 12 (ficticia)",
        });
        var roomId = Regex.Match(created, "name=\"habitacionId\" value=\"([0-9a-f-]{36})\"").Groups[1].Value;
        var duplicated = await PostAsync("/Administracion/NuevaHabitacion", created, new()
        {
            ["Form.OperacionId"] = Guid.NewGuid().ToString(), ["Form.UnidadId"] = admin.UnitId.ToString(), ["Form.Nombre"] = "habitación 12 (ficticia)",
        });
        var placeCreated = await PostAsync("/Administracion/NuevaPlaza", created, new()
        {
            ["Form.OperacionId"] = Guid.NewGuid().ToString(), ["Form.UnidadId"] = admin.UnitId.ToString(), ["Form.HabitacionId"] = roomId, ["Form.Nombre"] = "Cama A",
        });
        var placeId = Regex.Match(placeCreated, "name=\"plazaId\" value=\"([0-9a-f-]{36})\"").Groups[1].Value;
        var renamePage = await client.GetStringAsync($"/Administracion/NombrePlaza?unidadId={admin.UnitId}&plazaId={placeId}");
        var renamed = await PostAsync("/Administracion/NombrePlaza", renamePage, new()
        {
            ["Form.Id"] = placeId, ["Form.UnidadId"] = admin.UnitId.ToString(), ["Form.Nombre"] = "Cama 1",
        });

        async Task<string> AdmitAsync(string location)
        {
            var createPage = await client.GetStringAsync("/Residents/Create");
            var fields = new Dictionary<string, string>
            {
                ["OperacionId"] = ExtractValue(createPage, "OperacionId"), ["AmbitoPerfilId"] = ExtractValue(createPage, "AmbitoPerfilId"),
                ["CentroId"] = ExtractValue(createPage, "CentroId"), ["UnidadId"] = admin.UnitId.ToString(), ["NombreVisible"] = $"Residente {Guid.NewGuid():N}"[..18],
                ["FechaNacimiento"] = "1938-02-20", ["SexoDocumentadoCodigo"] = "Hombre", ["Ubicacion"] = location,
            };
            return await PostAsync("/Residents/Create", createPage, fields);
        }

        var offered = WebUtility.HtmlDecode(await client.GetStringAsync("/Residents/Create"));
        var badLocation = await AdmitAsync("x:1");
        var admitted = await AdmitAsync($"p:{placeId}");
        var occupied = await AdmitAsync($"p:{placeId}");
        var offeredAfter = WebUtility.HtmlDecode(await client.GetStringAsync("/Residents/Create"));
        var nursePage = await PageAsync("ENFERMERIA", null, roomsUrl);

        Assert.Contains("Escribe el nombre de la habitación", empty);
        Assert.Contains("Habitación creada.", created);
        Assert.Contains("Habitación 12 (ficticia)", created);
        Assert.Contains("Ya existe una habitación con ese nombre en la unidad.", duplicated);
        Assert.Contains("Plaza creada.", placeCreated);
        Assert.Contains("Nombre guardado.", renamed);
        Assert.Contains("Cama 1", renamed);
        Assert.Contains("Ubicación en la unidad (opcional)", offered);
        Assert.Contains($"value=\"p:{placeId}\">Habitación 12 (ficticia) · Cama 1", offered);
        Assert.Contains($"value=\"r:{roomId}\">Habitación 12 (ficticia)", offered);
        Assert.Contains("Elige una ubicación de la lista.", badLocation);
        Assert.Contains("Id del residente", admitted);
        Assert.DoesNotContain($"value=\"p:{placeId}\"", offeredAfter);
        Assert.Contains($"value=\"r:{roomId}\"", offeredAfter);
        Assert.Contains("Ocupada", await client.GetStringAsync(roomsUrl));
        Assert.DoesNotContain("Id del residente", occupied);
        Assert.Contains("No se puede acceder a esta operación", nursePage);
        Assert.DoesNotContain("Crear habitación", nursePage);
    }

    [Fact]
    public async Task Plataforma_CreaUnCentro_ElInicioSoloLaOfreceAEsePerfil_YOtroPerfilNoEntra()
    {
        var operatorSubject = await SeedPlatformOperatorAsync();
        var client = _factory.CreateClient();
        var loginPage = await client.GetStringAsync("/DevAuth/Login");
        (await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(loginPage, "__RequestVerificationToken"),
            ["externalSubject"] = operatorSubject,
        }))).EnsureSuccessStatusCode();

        var home = WebUtility.HtmlDecode(await client.GetStringAsync("/"));
        var newPage = await client.GetStringAsync("/Plataforma/NuevoCentro");
        async Task<string> PostAsync(Dictionary<string, string> fields) =>
            WebUtility.HtmlDecode(await (await client.PostAsync("/Plataforma/NuevoCentro", new FormUrlEncodedContent(new Dictionary<string, string>(fields)
            {
                ["__RequestVerificationToken"] = ExtractValue(newPage, "__RequestVerificationToken"),
                ["Form.OperacionId"] = ExtractValue(newPage, "Form.OperacionId"),
            }))).Content.ReadAsStringAsync());
        var empty = await PostAsync([]);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var created = await PostAsync(new()
        {
            ["Form.CodigoCentro"] = $"func-{suffix}",
            ["Form.NombreCentro"] = "Residencia Funcional (ficticia)",
            ["Form.CodigoUnidad"] = "planta-1",
            ["Form.NombreUnidad"] = "Planta 1",
            ["Form.IdentificadorAdministrador"] = $"func-admin-{suffix}",
            ["Form.NombreAdministrador"] = "Admin Funcional (ficticio)",
        });
        var repeated = await PostAsync(new()
        {
            ["Form.CodigoCentro"] = $"func2-{suffix}",
            ["Form.NombreCentro"] = "Otra residencia",
            ["Form.CodigoUnidad"] = "planta-1",
            ["Form.NombreUnidad"] = "Planta 1",
            ["Form.IdentificadorAdministrador"] = $"func-admin-{suffix}",
            ["Form.NombreAdministrador"] = "Admin Funcional (ficticio)",
        });
        var adminHome = await PageAsync("ADMINISTRACION", null);
        var adminPlatform = await PageAsync("ADMINISTRACION", null, "/Plataforma");

        Assert.Contains("Dar de alta un centro con su primera unidad y su administrador.", home);
        Assert.Contains("Escribe el código del centro.", empty);
        Assert.Contains("Escribe el identificador de acceso del administrador.", empty);
        Assert.DoesNotContain("The ", empty);
        Assert.Contains("Centro creado.", created);
        Assert.Contains("Residencia Funcional (ficticia)", created);
        Assert.Contains($"FUNC-{suffix}".ToUpperInvariant(), created);
        Assert.Contains("Ya existe un centro con ese código o una cuenta con ese identificador de acceso.", repeated);
        Assert.DoesNotContain("Dar de alta un centro con su primera unidad", adminHome);
        Assert.Contains("No se puede acceder a esta operación", adminPlatform);
        Assert.DoesNotContain("Nuevo centro", adminPlatform);
    }

    /// <summary>Una cuenta con el perfil Plataforma, que vive en el centro reservado.</summary>
    private static async Task<string> SeedPlatformOperatorAsync()
    {
        var accountId = Guid.NewGuid();
        var subject = $"functional-plat-{Guid.NewGuid():N}"[..30];
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        using var connection = await new SqlConnectionFactory(ResidentsFlowTests.WebAppFactory.TestConnectionString).OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@accountId, @subject, 'ACTIVE', @now);
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @accountId, '5F3A1C00-0000-4000-8000-000000000001', 'PLATAFORMA', 'ACTIVE', @now, @accountId);
            """, new { accountId, subject, now });
        return subject;
    }

    [Fact]
    public async Task Auditoria_MuestraLaAccionEnEspañol_IgnoraFiltrosMalFormados_YEnfermeriaNoEntra()
    {
        var admin = await SeedAdministratorAsync();
        var client = _factory.CreateClient();
        var loginPage = await client.GetStringAsync("/DevAuth/Login");
        (await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(loginPage, "__RequestVerificationToken"),
            ["externalSubject"] = admin.ExternalSubject,
        }))).EnsureSuccessStatusCode();
        var emptyPage = WebUtility.HtmlDecode(await client.GetStringAsync("/Administracion/Auditoria"));
        var newPage = await client.GetStringAsync("/Administracion/NuevaUnidad");
        (await client.PostAsync("/Administracion/NuevaUnidad", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(newPage, "__RequestVerificationToken"),
            ["Form.OperacionId"] = ExtractValue(newPage, "Form.OperacionId"),
            ["Form.Codigo"] = $"aud-{Guid.NewGuid():N}"[..14],
            ["Form.Nombre"] = "Unidad de la auditoría (ficticia)",
        }))).EnsureSuccessStatusCode();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/Administracion/Auditoria"));
        var malformed = WebUtility.HtmlDecode(await client.GetStringAsync("/Administracion/Auditoria?desde=xyz&hasta=2026-99-99&accion=HACK&cuenta=nope"));
        var filtered = WebUtility.HtmlDecode(await client.GetStringAsync("/Administracion/Auditoria?accion=ACCOUNT_CREATE"));
        var reversed = WebUtility.HtmlDecode(await client.GetStringAsync("/Administracion/Auditoria?desde=2026-10-01&hasta=2026-09-01"));
        var nursePage = await PageAsync("ENFERMERIA", null, "/Administracion/Auditoria");

        Assert.Contains("Auditoría administrativa", emptyPage);
        Assert.Contains("Ningún evento coincide con la búsqueda.", emptyPage);
        Assert.Contains("Unidad creada", page);
        Assert.Contains("Unidad de la auditoría (ficticia)", page);
        Assert.Contains("<optgroup label=\"Estructura del centro\">", page);
        Assert.DoesNotContain(">UNIT_CREATE<", page);
        Assert.Contains("Unidad creada", malformed);
        Assert.DoesNotContain("The ", malformed);
        Assert.Contains("Ningún evento coincide con la búsqueda.", filtered);
        Assert.Contains("La fecha «desde» no puede ser posterior a la fecha «hasta».", reversed);
        Assert.Contains("No se puede acceder a esta operación", nursePage);
        Assert.DoesNotContain("<table", nursePage);
    }

    [Fact]
    public async Task TurnosYEquipos_SeCreanDesdeLasPantallas_EnEspañol_YEnfermeriaNoEntra()
    {
        var admin = await SeedAdministratorAsync();
        var client = _factory.CreateClient();
        var loginPage = await client.GetStringAsync("/DevAuth/Login");
        (await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(loginPage, "__RequestVerificationToken"),
            ["externalSubject"] = admin.ExternalSubject,
        }))).EnsureSuccessStatusCode();

        async Task<string> PostFormAsync(string path, string pageWithForm, Dictionary<string, string> fields) =>
            WebUtility.HtmlDecode(await (await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>(fields)
            {
                ["__RequestVerificationToken"] = ExtractValue(pageWithForm, "__RequestVerificationToken"),
            }))).Content.ReadAsStringAsync());

        var newShiftPage = await client.GetStringAsync("/Administracion/NuevoTurno");
        var emptyShift = await PostFormAsync("/Administracion/NuevoTurno", newShiftPage, new() { ["Form.OperacionId"] = ExtractValue(newShiftPage, "Form.OperacionId") });
        var shiftName = $"Noche {Guid.NewGuid():N}"[..14];
        var shifts = await PostFormAsync("/Administracion/NuevoTurno", newShiftPage, new()
        {
            ["Form.OperacionId"] = ExtractValue(newShiftPage, "Form.OperacionId"),
            ["Form.Nombre"] = shiftName,
            ["Form.Inicio"] = "22:00",
            ["Form.Fin"] = "06:00",
        });
        var duplicatedShift = await PostFormAsync("/Administracion/NuevoTurno", newShiftPage, new()
        {
            ["Form.OperacionId"] = Guid.NewGuid().ToString(),
            ["Form.Nombre"] = shiftName.ToUpperInvariant(),
            ["Form.Inicio"] = "07:00",
            ["Form.Fin"] = "15:00",
        });

        var newTeamPage = await client.GetStringAsync("/Administracion/NuevoEquipo");
        var emptyTeam = await PostFormAsync("/Administracion/NuevoEquipo", newTeamPage, new() { ["Form.OperacionId"] = ExtractValue(newTeamPage, "Form.OperacionId") });
        var teams = await PostFormAsync("/Administracion/NuevoEquipo", newTeamPage, new()
        {
            ["Form.OperacionId"] = ExtractValue(newTeamPage, "Form.OperacionId"),
            ["Form.UnidadId"] = admin.UnitId.ToString(),
            ["Form.Nombre"] = "Equipo A (ficticio)",
        });
        var teamId = Regex.Match(teams, "equipoId=([0-9a-f-]{36})").Groups[1].Value;
        // Una cuenta de Enfermería en la unidad del equipo, para añadirla como miembro (escribe en equipos_miembros por la app real).
        var memberAccountId = Guid.NewGuid();
        using (var seedConnection = await new SqlConnectionFactory(ResidentsFlowTests.WebAppFactory.TestConnectionString).OpenAsync())
        {
            await seedConnection.ExecuteAsync("""
                DECLARE @centerId UNIQUEIDENTIFIER = (SELECT centro_id FROM dbo.unidades WHERE id = @unitId), @scopeId UNIQUEIDENTIFIER = NEWID();
                INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@memberAccountId, @subject, 'ACTIVE', SYSUTCDATETIME());
                INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
                VALUES (@scopeId, @memberAccountId, @centerId, 'ENFERMERIA', 'ACTIVE', SYSUTCDATETIME(), @memberAccountId);
                INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @scopeId, @centerId, @unitId, SYSUTCDATETIME(), @memberAccountId);
                """, new { memberAccountId, subject = $"functional-miembro-{Guid.NewGuid():N}"[..30], unitId = admin.UnitId });
        }
        var membersPage = WebUtility.HtmlDecode(await client.GetStringAsync($"/Administracion/MiembrosEquipo?equipoId={teamId}"));
        var memberAdded = await PostFormAsync("/Administracion/MiembroEquipo", membersPage, new()
        {
            ["equipoId"] = teamId, ["cuentaId"] = memberAccountId.ToString(), ["anadir"] = "true",
        });
        var nursePage = await PageAsync("ENFERMERIA", null, "/Administracion/Turnos");

        Assert.Contains("Escribe el nombre del turno.", emptyShift);
        Assert.Contains("Indica la hora de inicio.", emptyShift);
        Assert.DoesNotContain("The ", emptyShift);
        Assert.Contains("Turno creado.", shifts);
        Assert.Contains(shiftName, shifts);
        Assert.Contains("22:00 – 06:00 (termina al día siguiente)", shifts);
        Assert.Contains("Ya existe un turno con ese nombre en el centro.", duplicatedShift);
        Assert.Contains("Escribe el nombre del equipo.", emptyTeam);
        Assert.Contains("Equipo creado.", teams);
        Assert.Contains("Equipo A (ficticio)", teams);
        Assert.Contains("El equipo todavía no tiene miembros.", membersPage);
        Assert.Contains("Miembro añadido al equipo.", memberAdded);
        Assert.Contains("No se puede acceder a esta operación", nursePage);
        Assert.DoesNotContain("Nuevo turno", nursePage);
    }

    [Fact]
    public async Task Planificacion_ConSolapamiento_PideJustificacion_ConHuella_YLaAnotaEnLaFecha()
    {
        var admin = await SeedAdministratorAsync();
        var client = _factory.CreateClient();
        var loginPage = await client.GetStringAsync("/DevAuth/Login");
        (await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(loginPage, "__RequestVerificationToken"),
            ["externalSubject"] = admin.ExternalSubject,
        }))).EnsureSuccessStatusCode();

        async Task<string> PostAsync(string path, string pageWithForm, IEnumerable<KeyValuePair<string, string>> fields) =>
            WebUtility.HtmlDecode(await (await client.PostAsync(path, new FormUrlEncodedContent(
                fields.Append(KeyValuePair.Create("__RequestVerificationToken", ExtractValue(pageWithForm, "__RequestVerificationToken")))))).Content.ReadAsStringAsync());

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var morningName = $"Mañana {suffix}";
        var afternoonName = $"Tarde {suffix}";
        var shiftPage = await client.GetStringAsync("/Administracion/NuevoTurno");
        foreach (var (name, start, end) in new[] { (morningName, "07:00", "15:00"), (afternoonName, "14:00", "22:00") })
        {
            await PostAsync("/Administracion/NuevoTurno", shiftPage, new Dictionary<string, string>
            {
                ["Form.OperacionId"] = Guid.NewGuid().ToString(), ["Form.Nombre"] = name, ["Form.Inicio"] = start, ["Form.Fin"] = end,
            });
        }

        var teamPage = await client.GetStringAsync("/Administracion/NuevoEquipo");
        await PostAsync("/Administracion/NuevoEquipo", teamPage, new Dictionary<string, string>
        {
            ["Form.OperacionId"] = ExtractValue(teamPage, "Form.OperacionId"), ["Form.UnidadId"] = admin.UnitId.ToString(), ["Form.Nombre"] = $"Equipo {suffix}",
        });

        var planPage = await client.GetStringAsync("/Administracion/PlanificarTurno");
        string OptionId(string page, string label) => Regex.Match(page, $"<option value=\"([0-9a-f-]{{36}})\"[^>]*>{Regex.Escape(label)}").Groups[1].Value;
        var teamId = OptionId(planPage, $"Equipo {suffix}");
        var morningId = OptionId(WebUtility.HtmlDecode(planPage), morningName);
        var afternoonId = OptionId(WebUtility.HtmlDecode(planPage), afternoonName);
        Assert.True(teamId != "" && morningId != "" && afternoonId != "", $"opciones: [{teamId}] [{morningId}] [{afternoonId}]");
        var day = DateTime.Today.AddDays(3).ToString("yyyy-MM-dd");
        var secondBatch = Guid.NewGuid().ToString();
        List<KeyValuePair<string, string>> Form(string shiftId, string? justification = null, string? seen = null, bool second = false)
        {
            var fields = new List<KeyValuePair<string, string>>
            {
                new("Form.OperacionId", second ? secondBatch : ExtractValue(planPage, "Form.OperacionId")), new("Form.EquipoId", teamId), new("Form.TurnoId", shiftId),
                new("Form.Desde", day), new("Form.Hasta", day), new("Form.Justificacion", justification ?? ""), new("Form.ConflictosVistos", seen ?? ""),
            };
            fields.AddRange(Enum.GetNames<DayOfWeek>().Select(d => new KeyValuePair<string, string>("Form.Dias", d)));
            return fields;
        }

        var first = await PostAsync("/Administracion/PlanificarTurno", planPage, Form(morningId));
        var overlap = await PostAsync("/Administracion/PlanificarTurno", planPage, Form(afternoonId, null, null, true));
        var seen = ExtractValue(overlap, "Form.ConflictosVistos");
        var noJustification = await PostAsync("/Administracion/PlanificarTurno", planPage, Form(afternoonId, null, seen, true));
        var changed = await PostAsync("/Administracion/PlanificarTurno", planPage, Form(afternoonId, "Refuerzo", "HUELLA-ANTIGUA", true));
        var confirmed = await PostAsync("/Administracion/PlanificarTurno", planPage, Form(afternoonId, "Refuerzo por baja (ficticio)", seen, true));
        var badSkip = await PostAsync("/Administracion/PlanificarTurno", planPage,
            Form(afternoonId, null, null, true).Append(new("Form.Saltar", "mañana, 2026-13-45")).ToList());
        var allSkipped = await PostAsync("/Administracion/PlanificarTurno", planPage,
            Form(afternoonId, null, null, true).Append(new("Form.Saltar", day)).ToList());
        var seriesId = Regex.Match(await client.GetStringAsync($"/Administracion/Planificacion?desde={day}"), "SeriePlanificacion\\?loteId=([0-9a-f-]{36})").Groups[1].Value;
        var seriesPage = await client.GetStringAsync($"/Administracion/SeriePlanificacion?loteId={seriesId}");
        var retiredSeries = await PostAsync("/Administracion/RetirarSerie", seriesPage, new Dictionary<string, string> { ["loteId"] = seriesId, ["desde"] = day });
        var retiredAgain = await PostAsync("/Administracion/RetirarSerie", seriesPage, new Dictionary<string, string> { ["loteId"] = seriesId, ["desde"] = day });
        var nursePage = await PageAsync("ENFERMERIA", null, "/Administracion/Planificacion");
        var nurseSeries = await PageAsync("ENFERMERIA", null, $"/Administracion/SeriePlanificacion?loteId={seriesId}");

        Assert.Contains("Planificado en 1 fecha.", first);
        Assert.Contains(morningName, first);
        Assert.Contains("Solapamientos (1)", overlap);
        Assert.Contains($"«Equipo {suffix}» ya está planificado en «{morningName}», que se solapa con «{afternoonName}».", overlap);
        Assert.Contains("Confirmar y planificar con estos solapamientos", overlap);
        Assert.False(string.IsNullOrEmpty(seen));
        Assert.Contains("Escribe por qué sigues adelante a pesar de los solapamientos.", noJustification);
        Assert.Contains("Los solapamientos han cambiado desde que los viste.", changed);
        Assert.Contains("Planificado en 1 fecha.", confirmed);
        Assert.Contains("Queda anotada tu justificación del solapamiento.", confirmed);
        Assert.Contains("Solapamiento justificado", confirmed);
        Assert.Contains("Refuerzo por baja (ficticio)", confirmed);
        Assert.Contains("No entiendo estas fechas a saltar: «mañana», «2026-13-45».", WebUtility.HtmlDecode(badSkip));
        Assert.Contains("No queda ninguna fecha", allSkipped);
        Assert.Contains("Serie de turnos", seriesPage);
        Assert.Contains("Retirar la serie desde esa fecha", seriesPage);
        Assert.Contains("Serie retirada: 1 fecha desde esa fecha.", retiredSeries);
        Assert.Contains("La serie no tiene fechas planificadas desde esa fecha", retiredAgain);
        Assert.Contains("No se puede acceder a esta operación", nursePage);
        Assert.DoesNotContain("Planificar un turno", nursePage);
        Assert.Contains("No se puede acceder a esta operación", nurseSeries);
        Assert.DoesNotContain("Retirar la serie", nurseSeries);
    }

    private static string ExtractValue(string html, string inputName) =>
        Regex.Match(html, $"name=\"{Regex.Escape(inputName)}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;

    [Fact]
    public async Task RangosReferencia_UnaMedicaConPermisoLosGuarda_ConLaAppEntera()
    {
        // Con la app conectada como usuario limitado (RESIDAPP_TEST_APP_CONNECTION_STRING) escribe en las tablas de rangos bajo la seguridad por filas.
        var doctor = await SeedAdministratorAsync("DIRECCION_CLINICA", "REFERENCE_RANGES_MANAGE");
        var client = _factory.CreateClient();
        var loginPage = await client.GetStringAsync("/DevAuth/Login");
        (await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(loginPage, "__RequestVerificationToken"),
            ["externalSubject"] = doctor.ExternalSubject,
        }))).EnsureSuccessStatusCode();

        var page = await client.GetStringAsync("/RangosReferencia");
        var saved = WebUtility.HtmlDecode(await (await client.PostAsync("/RangosReferencia", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(page, "__RequestVerificationToken"),
            ["Form.Version"] = ExtractValue(page, "Form.Version"),
            ["Form.Rangos[0].Constante"] = "Temperatura",
            ["Form.Rangos[0].Minimo"] = "36",
            ["Form.Rangos[0].Maximo"] = "38",
        }))).Content.ReadAsStringAsync());

        Assert.Contains("Rangos de referencia guardados.", saved);
        using var connection = await new SqlConnectionFactory(ResidentsFlowTests.WebAppFactory.TestConnectionString).OpenAsync();
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.rangos_referencia_constantes WHERE centro_id = (SELECT centro_id FROM dbo.unidades WHERE id = @unitId)",
            new { unitId = doctor.UnitId }));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.rangos_referencia_constantes_historial WHERE centro_id = (SELECT centro_id FROM dbo.unidades WHERE id = @unitId)",
            new { unitId = doctor.UnitId }));
    }

    [Fact]
    public async Task RangosReferencia_CargarValoresSugeridos_RellenaElFormularioSinGuardarNada()
    {
        var doctor = await SeedAdministratorAsync("DIRECCION_CLINICA", "REFERENCE_RANGES_MANAGE");
        var client = _factory.CreateClient();
        var loginPage = await client.GetStringAsync("/DevAuth/Login");
        (await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(loginPage, "__RequestVerificationToken"),
            ["externalSubject"] = doctor.ExternalSubject,
        }))).EnsureSuccessStatusCode();

        var normal = await client.GetStringAsync("/RangosReferencia");
        var suggested = WebUtility.HtmlDecode(await client.GetStringAsync("/RangosReferencia?sugeridos=true"));

        Assert.Contains("Cargar valores sugeridos", normal);
        Assert.DoesNotContain("todavía no se han guardado", normal);
        Assert.Contains("todavía no se han guardado", suggested);
        Assert.Contains("name=\"Form.Rangos[0].Minimo\"", suggested);
        Assert.Matches("name=\"Form.Rangos\\[0\\]\\.Maximo\"[^>]*value=\"36.9\"", suggested);
        Assert.Matches("name=\"Form.Rangos\\[6\\]\\.Minimo\"[^>]*value=\"70\"", suggested);
        Assert.Matches("name=\"Form.Rangos\\[6\\]\\.Maximo\"[^>]*value=\"120\"", suggested);
        using var connection = await new SqlConnectionFactory(ResidentsFlowTests.WebAppFactory.TestConnectionString).OpenAsync();
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.rangos_referencia_constantes WHERE centro_id = (SELECT centro_id FROM dbo.unidades WHERE id = @unitId)",
            new { unitId = doctor.UnitId }));
    }

    [Fact]
    public async Task Inicio_EnfermeriaSoloVeElAltaDeResidenteConElPermiso()
    {
        var without = await PageAsync("ENFERMERIA", null);
        var with = await PageAsync("ENFERMERIA", ProfilePermissions.ResidentIdentityCreate);

        Assert.All(new[] { without, with }, page => Assert.Contains("<h2 class=\"h5 card-title\">Enfermería</h2>", page));
        Assert.DoesNotContain("Alta de residente", without);
        Assert.Contains("Alta de residente", with);
    }

    [Theory]
    [InlineData("ENFERMERIA", ProfilePermissions.BaselineInitialComplete)]
    [InlineData("MEDICINA", ProfilePermissions.BaselineReevaluate)]
    public async Task Inicio_SinTarjetaDeFirmaSuelta_AunqueTengaPermisoDeBasal(string profileCode, string permission)
    {
        // El basal se firma desde la ficha del residente (Estado basal → Confirmar y firmar), no desde el Inicio.
        var with = await PageAsync(profileCode, permission);

        Assert.DoesNotContain("Firmar borrador de basal", with);
    }

    [Fact]
    public async Task ConsultaDeDireccion_SinResidentesEnElAmbito_LoDiceYElAmbitoSaleDelActivo()
    {
        var page = await PageAsync("DIRECCION_CLINICA", null, "/Baseline/Direction");

        Assert.Contains("No hay residentes en tu ámbito.", page);
        Assert.DoesNotContain("name=\"ResidenteId\"", page);
        Assert.DoesNotContain("name=\"AmbitoPerfilId\"", page);
        Assert.DoesNotContain("name=\"CentroId\"", page);
        Assert.Contains("Ámbito activo: <strong>Dirección Clínica</strong>", page);
    }

    [Theory]
    [InlineData("ENFERMERIA", null, false)]
    [InlineData("ENFERMERIA", ProfilePermissions.ResidentIdentityCreate, true)]
    [InlineData("MEDICINA", null, false)]
    [InlineData("ADMINISTRACION", null, true)]
    public async Task AltaDeResidente_ElFormularioSoloSaleSiElAmbitoPuedeDarDeAlta(
        string profileCode, string? permission, bool canCreate)
    {
        var page = await PageAsync(profileCode, permission, "/Residents/Create");

        Assert.Equal(canCreate, page.Contains("name=\"NombreVisible\""));
        Assert.Equal(!canCreate, page.Contains("Tu ámbito activo no tiene permiso para dar de alta residentes."));
    }

    private const string DevSubjectCookie = "residapp_dev_subject";

    private async Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string externalSubject)
    {
        var loginPage = await client.GetStringAsync("/DevAuth/Login");
        return await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(loginPage, "__RequestVerificationToken"),
            ["externalSubject"] = externalSubject,
        }));
    }

    [Fact]
    public async Task Login_ConUnaIdentidadSinCuenta_SeRechazaYNoSeIniciaSesion()
    {
        var client = _factory.CreateClient();

        var response = await PostLoginAsync(client, $"dev-inexistente-{Guid.NewGuid():N}");
        var page = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var home = WebUtility.HtmlDecode(await client.GetStringAsync("/"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("No existe ninguna cuenta con ese usuario", page);
        Assert.DoesNotContain(response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [], c => c.Contains(DevSubjectCookie));
        Assert.Contains("No hay ninguna identidad de desarrollo activa", home);
    }

    [Fact]
    public async Task Login_Rechazado_ConservaLaIdentidadPrevia_YSuBotonSalir()
    {
        var account = await SeedAdministratorAsync();
        var client = _factory.CreateClient();
        (await PostLoginAsync(client, account.ExternalSubject)).EnsureSuccessStatusCode();

        var rejected = WebUtility.HtmlDecode(await (await PostLoginAsync(client, $"dev-inexistente-{Guid.NewGuid():N}")).Content.ReadAsStringAsync());
        var empty = WebUtility.HtmlDecode(await (await PostLoginAsync(client, " ")).Content.ReadAsStringAsync());
        var home = WebUtility.HtmlDecode(await client.GetStringAsync("/"));

        Assert.Contains("No existe ninguna cuenta con ese usuario", rejected);
        Assert.Contains($"Salir ({account.ExternalSubject})", rejected);
        Assert.Contains("Escribe el sujeto externo", empty);
        Assert.Contains($"Salir ({account.ExternalSubject})", empty);
        Assert.Contains($"<code>{account.ExternalSubject}</code>", home);
    }

    [Fact]
    public async Task SeleccionDeAmbito_CuentaQuePierdeSusAmbitos_OfreceCambiarDeIdentidadYSalir()
    {
        var account = await SeedAdministratorAsync();
        var client = _factory.CreateClient();
        (await PostLoginAsync(client, account.ExternalSubject)).EnsureSuccessStatusCode();
        using (var connection = await new SqlConnectionFactory(ResidentsFlowTests.WebAppFactory.TestConnectionString).OpenAsync())
        {
            await connection.ExecuteAsync("""
                UPDATE dbo.ambitos_perfil SET estado = 'REVOKED', revocado_en = SYSUTCDATETIME(), revocado_por_cuenta_id = @accountId
                 WHERE cuenta_id = @accountId
                """, new { account.AccountId });
        }

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/ProfileScope/Select"));

        Assert.Contains("Tu cuenta no tiene ningún ámbito activo", page);
        Assert.DoesNotContain("varios ámbitos", page);
        Assert.Contains("href=\"/DevAuth/Login\"", page);
        Assert.Contains("action=\"/DevAuth/Logout\"", page);
        Assert.DoesNotContain("Reintentar", page);
    }

    [Fact]
    public async Task SeleccionDeAmbito_SiNoSePuedenCargarLosAmbitos_MuestraElErrorYNoDiceQueNoTiene()
    {
        var failing = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddScoped<IProfileScopeDirectoryProvider, UnavailableProfileScopeDirectory>()));
        var client = failing.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"{DevSubjectCookie}=dev-cualquiera");

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/ProfileScope/Select"));

        Assert.Contains("No se ha podido completar la operación.", page);
        Assert.DoesNotContain("no tiene ningún ámbito activo", page);
        Assert.Contains("Reintentar", page);
        Assert.Contains("href=\"/DevAuth/Login\"", page);
    }

    private sealed class UnavailableProfileScopeDirectory : IProfileScopeDirectoryProvider
    {
        public Task<IReadOnlyList<ActiveProfileScope>> ListActiveAsync(string externalSubject, CancellationToken ct = default) =>
            throw new InvalidOperationException("La base de datos no responde.");

        public Task<IReadOnlyList<ScopeUnit>> ListUnitsAsync(
            string externalSubject, Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
            throw new InvalidOperationException("La base de datos no responde.");

        public Task<IReadOnlyList<string>> ListPermissionsAsync(
            string externalSubject, Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
            throw new InvalidOperationException("La base de datos no responde.");
    }

    /// <summary>Una página (por defecto el Inicio), ya decodificada, de una cuenta nueva con un ámbito del perfil indicado
    /// y, si se pide, un permiso.</summary>
    private async Task<string> PageAsync(string profileCode, string? permission, string path = "/")
    {
        var account = await SeedAdministratorAsync(profileCode, permission);
        var client = _factory.CreateClient();
        var loginPage = await client.GetStringAsync("/DevAuth/Login");
        (await client.PostAsync("/DevAuth/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractValue(loginPage, "__RequestVerificationToken"),
            ["externalSubject"] = account.ExternalSubject,
        }))).EnsureSuccessStatusCode();
        return WebUtility.HtmlDecode(await client.GetStringAsync(path));
    }

    /// <summary>Una cuenta con un ámbito del perfil indicado (por defecto Administración) en un centro y una unidad nuevos y,
    /// si se pide, un permiso de ese ámbito.</summary>
    private static async Task<(string ExternalSubject, Guid AccountId, Guid UnitId)> SeedAdministratorAsync(
        string profileCode = "ADMINISTRACION", string? permission = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var accountId = Guid.NewGuid();
        var centerId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var profileScopeId = Guid.NewGuid();
        var externalSubject = $"functional-{suffix}";
        var now = DateTimeOffset.UtcNow.UtcDateTime;

        using var connection = await new SqlConnectionFactory(ResidentsFlowTests.WebAppFactory.TestConnectionString).OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@accountId, @externalSubject, 'ACTIVE', @now);
            INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en) VALUES (@centerId, @centerCode, @centerCode, 'ACTIVE', @now);
            INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en) VALUES (@unitId, @centerId, @unitCode, @unitCode, 'ACTIVE', @now);
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (@profileScopeId, @accountId, @centerId, @profileCode, 'ACTIVE', @now, @accountId);
            INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @profileScopeId, @centerId, @unitId, @now, @accountId);
            IF @permission IS NOT NULL
                INSERT INTO dbo.permisos_perfil (id, ambito_perfil_id, centro_id, permiso_codigo, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @profileScopeId, @centerId, @permission, @now, @accountId);
            """, new
        {
            accountId, externalSubject, now, centerId, centerCode = $"FUNC-CENTER-{suffix}", unitId, unitCode = $"FUNC-UNIT-{suffix}",
            profileScopeId, profileCode, permission,
        });
        return (externalSubject, accountId, unitId);
    }
}
