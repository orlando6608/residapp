using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Authorization;
using ResidApp.Application.Ports;
using ResidApp.Domain.Baseline;
using ResidApp.Domain.Baseline.Catalogs;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>
/// Contra la instancia real de SQL Server. La declaración de acceso clínico de Dirección (CJ, 2026-10-06): finalidad y
/// justificación declaradas una vez, válidas 1 hora para un residente y un ámbito, que termina al cambiar de residente, al
/// cerrar sesión o al caducar. Cada apertura sigue escribiendo su propia fila de auditoría antes de entregar el contenido.
/// </summary>
public class ClinicalAccessDeclarationTests
{
    private readonly SqlBaselineRepository _repository = new(TestDatabase.ConnectionFactory);
    private readonly SqlResidentRepository _residents = new(TestDatabase.ConnectionFactory);

    private sealed record Scenario(SeededProfile Direction, SeededProfile Admin, ResidentId Resident);

    /// <summary>Un residente con un basal firmado y una cuenta de Dirección Clínica con permiso de lectura clínica.</summary>
    private async Task<Scenario> ScenarioAsync(bool withPermission = true, string name = "Residente Declaración Acceso")
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(
            SystemProfile.Enfermeria, admin.CenterId, admin.UnitId, ["BASELINE_INITIAL_COMPLETE", "BASELINE_REEVALUATE"]);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            admin.AccountId, SystemProfile.Administracion, admin.CenterId, admin.UnitId,
            name, new DateOnly(1940, 6, 6), DocumentedSexCode.Mujer, null, null, null, null, null, Guid.NewGuid()));
        var created = await _repository.CreateDraftAsync(new CreateBaselineDraftInput(
            nurse.AccountId, SystemProfile.Enfermeria, nurse.CenterId, nurse.UnitId, resident.ResidentId, BaselineReason.Alta,
            InformationSourceCode.ValoracionDirecta, null, new DateOnly(2026, 9, 14), Guid.NewGuid()));
        var owner = new OwnedActiveDraftInput(nurse.AccountId, SystemProfile.Enfermeria, nurse.CenterId, resident.ResidentId);
        foreach (var (area, answer) in BaselineTestData.NineAreas())
        {
            await _repository.SaveAreaAsync(new SaveBaselineDraftAreaInput(owner, area, answer, null));
        }
        await _repository.SaveBarthelAsync(new SaveBaselineDraftBarthelInput(owner, new DateOnly(2026, 9, 14), BaselineTestData.FullBarthelItems()));
        await _repository.SignDraftAsync(new SignBaselineDraftInput(
            nurse.AccountId, SystemProfile.Enfermeria, nurse.CenterId, nurse.UnitId, resident.ResidentId, created.DraftId, created.DraftRevision, Guid.NewGuid()));

        var direction = await SeedFixture.AddProfileToCenterAsync(
            SystemProfile.DireccionClinica, admin.CenterId, admin.UnitId, withPermission ? [ResidentBaselinePermission.ClinicalDetailRead.ToCode()] : null);
        return new Scenario(direction, admin, resident.ResidentId);
    }

    private static ClinicalDirectionReadInput Read(
        Scenario s, Guid operationId, Guid? reuse = null, ClinicalDetailAccessPurpose purpose = ClinicalDetailAccessPurpose.ContinuidadAsistencial,
        string justification = DirectionReadTestData.Justification, ClinicalResourceType type = ClinicalResourceType.BaselineHistory, ResidentId? resident = null) =>
        DirectionReadTestData.Input(s.Direction, s.Admin.CenterId, s.Admin.UnitId, resident ?? s.Resident, type, operationId, reuse, purpose, justification);

    private static async Task<List<(string Purpose, string? Justification)>> AuditAsync(ResidentId residentId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return (await connection.QueryAsync<(string, string?)>(
            "SELECT proposito_codigo, justificacion FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE residente_id = @Id AND accion_codigo = 'CLINICAL_DETAIL_READ' ORDER BY ocurrido_en",
            new { Id = residentId.Value })).ToList();
    }

    private static async Task<int> DeclarationCountAsync(AccountId accountId, bool onlyOpen)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.QuerySingleAsync<int>($"""
            SELECT COUNT(*) FROM dbo.declaraciones_acceso_clinico WITH (NOLOCK)
             WHERE cuenta_id = @Id {(onlyOpen ? "AND terminada_en IS NULL AND caduca_en > SYSUTCDATETIME()" : "")}
            """, new { Id = accountId.Value });
    }

    [Fact]
    public async Task PrimeraLectura_CreaLaDeclaracionDeUnaHora_YAudita_ConFinalidadYJustificacion()
    {
        var s = await ScenarioAsync();
        var operationId = Guid.NewGuid();

        var headers = await _repository.ReadAsClinicalDirectionAsync(Read(s, operationId, purpose: ClinicalDetailAccessPurpose.IncidenciaReclamacion));

        Assert.Single(headers);
        var audit = Assert.Single(await AuditAsync(s.Resident));
        Assert.Equal(("INCIDENCIA_RECLAMACION", DirectionReadTestData.Justification), audit);
        var declaration = await _repository.FindActiveAccessDeclarationAsync(s.Direction.ExternalSubject, operationId, s.Direction.ProfileScopeId, s.Resident);
        Assert.NotNull(declaration);
        Assert.Equal(ClinicalDetailAccessPurpose.IncidenciaReclamacion, declaration!.Purpose);
        Assert.Equal(DirectionReadTestData.Justification, declaration.Justification);
        Assert.InRange(declaration.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(59), TimeSpan.FromMinutes(61));
    }

    [Fact]
    public async Task ConLaDeclaracionVigente_CadaAperturaDejaSuPropiaAuditoria_ConLaFinalidadDeclarada()
    {
        var s = await ScenarioAsync();
        var declarationId = Guid.NewGuid();
        await _repository.ReadAsClinicalDirectionAsync(Read(s, declarationId, purpose: ClinicalDetailAccessPurpose.TrazabilidadDocumental));

        // La segunda y la tercera apertura reutilizan la declaración: el texto que traigan se ignora, vale el declarado.
        await _repository.ReadAsClinicalDirectionAsync(Read(s, Guid.NewGuid(), declarationId, ClinicalDetailAccessPurpose.ContinuidadAsistencial, "Otra cosa distinta."));
        await _repository.ReadAsClinicalDirectionAsync(Read(s, Guid.NewGuid(), declarationId, type: ClinicalResourceType.BaselineCurrent));

        var audit = await AuditAsync(s.Resident);
        Assert.Equal(3, audit.Count);
        Assert.All(audit, a => Assert.Equal(("TRAZABILIDAD_DOCUMENTAL", DirectionReadTestData.Justification), a));
        Assert.Equal(1, await DeclarationCountAsync(s.Direction.AccountId, onlyOpen: false));
    }

    [Fact]
    public async Task ReintentarLaMismaOperacionConLaCookieYaEscrita_DevuelveLoMismo_SinDuplicarAuditoria()
    {
        var s = await ScenarioAsync();
        var operationId = Guid.NewGuid();
        var first = await _repository.ReadAsClinicalDirectionAsync(Read(s, operationId));

        var retry = await _repository.ReadAsClinicalDirectionAsync(Read(s, operationId, reuse: operationId));

        Assert.Equal(first.Select(h => h.Id), retry.Select(h => h.Id));
        Assert.Single(await AuditAsync(s.Resident));
    }

    [Fact]
    public async Task DeclaracionDeOtroResidenteOAmbito_OInexistente_NoSirve_YNoAuditaNada()
    {
        var s = await ScenarioAsync();
        var declarationId = Guid.NewGuid();
        await _repository.ReadAsClinicalDirectionAsync(Read(s, declarationId));

        // Otro residente de la misma unidad.
        var other = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            s.Admin.AccountId, SystemProfile.Administracion, s.Admin.CenterId, s.Admin.UnitId,
            "Otro Residente Declaración", new DateOnly(1941, 1, 1), DocumentedSexCode.Hombre, null, null, null, null, null, Guid.NewGuid()));

        Assert.Null(await _repository.FindActiveAccessDeclarationAsync(s.Direction.ExternalSubject, declarationId, s.Direction.ProfileScopeId, other.ResidentId));
        Assert.Null(await _repository.FindActiveAccessDeclarationAsync(s.Direction.ExternalSubject, declarationId, Guid.NewGuid(), s.Resident));
        Assert.Null(await _repository.FindActiveAccessDeclarationAsync("otra-cuenta", declarationId, s.Direction.ProfileScopeId, s.Resident));
        Assert.Null(await _repository.FindActiveAccessDeclarationAsync(s.Direction.ExternalSubject, Guid.NewGuid(), s.Direction.ProfileScopeId, s.Resident));
        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            _repository.ReadAsClinicalDirectionAsync(Read(s, Guid.NewGuid(), declarationId, resident: other.ResidentId)));
        Assert.Contains("CLINICAL_ACCESS_DECLARATION_INVALID", ex.Message);
        Assert.Empty(await AuditAsync(other.ResidentId));
    }

    [Fact]
    public async Task DeclaracionCaducada_NoSirve_ParaBuscarlaNiParaLeer()
    {
        var s = await ScenarioAsync();
        var declarationId = Guid.NewGuid();
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            // Una declaración que ya pasó de hora (el trigger solo permite terminar, no cambiar la caducidad; se inserta caducada).
            await connection.ExecuteAsync("""
                INSERT INTO dbo.declaraciones_acceso_clinico
                    (id, centro_id, cuenta_id, ambito_perfil_id, residente_id, proposito_codigo, justificacion, creada_en, caduca_en)
                VALUES (@declarationId, @centerId, @accountId, @scopeId, @residentId, 'CONTINUIDAD_ASISTENCIAL', 'Declaración antigua.',
                        DATEADD(MINUTE, -61, SYSUTCDATETIME()), DATEADD(MINUTE, -1, SYSUTCDATETIME()))
                """, new
            {
                declarationId, centerId = s.Admin.CenterId.Value, accountId = s.Direction.AccountId.Value,
                scopeId = s.Direction.ProfileScopeId, residentId = s.Resident.Value,
            });
        }

        Assert.Null(await _repository.FindActiveAccessDeclarationAsync(s.Direction.ExternalSubject, declarationId, s.Direction.ProfileScopeId, s.Resident));
        await Assert.ThrowsAsync<SqlException>(() => _repository.ReadAsClinicalDirectionAsync(Read(s, Guid.NewGuid(), declarationId)));
        Assert.Empty(await AuditAsync(s.Resident));
    }

    [Fact]
    public async Task DeclararOtraVez_TerminaLaAnterior_YTerminarTodasLasCierra()
    {
        var s = await ScenarioAsync();
        var first = Guid.NewGuid();
        await _repository.ReadAsClinicalDirectionAsync(Read(s, first));
        var second = Guid.NewGuid();
        await _repository.ReadAsClinicalDirectionAsync(Read(s, second));

        Assert.Null(await _repository.FindActiveAccessDeclarationAsync(s.Direction.ExternalSubject, first, s.Direction.ProfileScopeId, s.Resident));
        Assert.NotNull(await _repository.FindActiveAccessDeclarationAsync(s.Direction.ExternalSubject, second, s.Direction.ProfileScopeId, s.Resident));
        Assert.Equal(1, await DeclarationCountAsync(s.Direction.AccountId, onlyOpen: true));

        await _repository.EndAccessDeclarationsAsync(s.Direction.ExternalSubject);

        Assert.Equal(0, await DeclarationCountAsync(s.Direction.AccountId, onlyOpen: true));
        Assert.Null(await _repository.FindActiveAccessDeclarationAsync(s.Direction.ExternalSubject, second, s.Direction.ProfileScopeId, s.Resident));
        await _repository.EndAccessDeclarationsAsync(s.Direction.ExternalSubject); // sin ninguna abierta no falla
    }

    [Fact]
    public async Task SinPermiso_NoSeCreaDeclaracionNiAuditoria()
    {
        var s = await ScenarioAsync(withPermission: false);

        var ex = await Assert.ThrowsAsync<SqlException>(() => _repository.ReadAsClinicalDirectionAsync(Read(s, Guid.NewGuid())));

        Assert.Contains("CLINICAL_DETAIL_READ_NOT_AUTHORIZED", ex.Message);
        Assert.Equal(0, await DeclarationCountAsync(s.Direction.AccountId, onlyOpen: false));
        Assert.Empty(await AuditAsync(s.Resident));
    }

    [Fact]
    public async Task UnAmbitoQueNoEsElDeLaCuenta_NoLee_AunqueLaCuentaTengaPermiso()
    {
        var s = await ScenarioAsync();
        var foreign = DirectionReadTestData.Input(
            s.Direction with { ProfileScopeId = Guid.NewGuid() }, s.Admin.CenterId, s.Admin.UnitId, s.Resident,
            ClinicalResourceType.BaselineHistory, Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<SqlException>(() => _repository.ReadAsClinicalDirectionAsync(foreign));

        Assert.Contains("CLINICAL_DETAIL_READ_NOT_AUTHORIZED", ex.Message);
        Assert.Empty(await AuditAsync(s.Resident));
    }

    [Fact]
    public async Task LaBaseDeDatos_NoAdmiteBorrarNiCambiarUnaDeclaracion_SoloTerminarla()
    {
        var s = await ScenarioAsync();
        var declarationId = Guid.NewGuid();
        await _repository.ReadAsClinicalDirectionAsync(Read(s, declarationId));
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        var delete = await Assert.ThrowsAsync<SqlException>(() =>
            connection.ExecuteAsync("DELETE FROM dbo.declaraciones_acceso_clinico WHERE id = @declarationId", new { declarationId }));
        var extend = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.declaraciones_acceso_clinico SET caduca_en = DATEADD(HOUR, 5, caduca_en) WHERE id = @declarationId", new { declarationId }));
        var rewrite = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.declaraciones_acceso_clinico SET justificacion = N'Otra.' WHERE id = @declarationId", new { declarationId }));
        var ended = await connection.ExecuteAsync(
            "UPDATE dbo.declaraciones_acceso_clinico SET terminada_en = SYSUTCDATETIME() WHERE id = @declarationId", new { declarationId });
        var reopen = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.declaraciones_acceso_clinico SET terminada_en = NULL WHERE id = @declarationId", new { declarationId }));

        Assert.All(new[] { delete, extend, rewrite, reopen }, e => Assert.Contains("CLINICAL_ACCESS_DECLARATION_IMMUTABLE", e.Message));
        Assert.Equal(1, ended);
    }

    [Fact]
    public async Task LaAuditoria_DeLecturaClinicaDeDireccion_ExigeFinalidadDeLaListaYJustificacion()
    {
        var s = await ScenarioAsync();
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        const string insert = """
            INSERT INTO dbo.eventos_auditoria
                (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, proposito_codigo, justificacion, ocurrido_en)
            VALUES (NEWID(), @accountId, 'DIRECCION_CLINICA', @centerId, @unitId, @residentId, 'BASELINE_HISTORY', NEWID(),
                    'CLINICAL_DETAIL_READ', @purpose, @justification, SYSUTCDATETIME())
            """;
        object Row(string? purpose, string? justification) => new
        {
            accountId = s.Direction.AccountId.Value, centerId = s.Admin.CenterId.Value, unitId = s.Admin.UnitId.Value,
            residentId = s.Resident.Value, purpose, justification,
        };

        Task Insert(string? purpose, string? justification) => connection.ExecuteAsync(insert, Row(purpose, justification));

        await Insert("CONTINUIDAD_ASISTENCIAL", "Motivo concreto.");
        await Insert("SUPERVISION_CLINICA", null); // las filas anteriores a CJ siguen siendo válidas
        foreach (var (purpose, justification) in new (string?, string?)[]
        {
            ("CONTINUIDAD_ASISTENCIAL", null), ("CONTINUIDAD_ASISTENCIAL", "   "), ("SUPERVISION_CLINICA", "Con justificación."),
            ("CALIDAD_ASISTENCIAL", "Una finalidad que no está en la lista."), (null, "Sin finalidad."),
        })
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => Insert(purpose, justification));
            Assert.Contains("CK_audit_direction_read", ex.Message);
        }
    }
}
