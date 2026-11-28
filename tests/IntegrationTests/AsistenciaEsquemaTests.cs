using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Platform;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using Day = ResidApp.IntegrationTests.TestSupport.DayCenterFixture.Day;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>
/// El esquema de la asistencia del Centro de Día (script 0115, ADR 0019): cuatro tablas de solo añadir con sus reglas en la propia base de datos. Se prueban con SQL directo, como dueño de la base
/// (los disparadores y las restricciones rigen para cualquiera): solo en un Centro de Día, formas válidas e inválidas, versiones, solapes y que nada se actualiza ni se borra.
/// </summary>
public class AsistenciaEsquemaTests
{
    private static async Task<string?> TryAsync(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        try
        {
            await connection.ExecuteAsync(sql, parameters);
            return null;
        }
        catch (SqlException error)
        {
            return error.Message;
        }
    }

    private static DateTime Day0(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    private static Task<string?> AttendanceAsync(Day d, string state, string? cause, string? note, int number = 1, DateOnly? date = null, string profile = "AUXILIAR") => TryAsync("""
        INSERT INTO dbo.asistencia_dia (id, centro_id, residente_id, fecha, numero, estado_codigo, causa_codigo, nota, anotado_por_cuenta_id, anotado_por_perfil, anotado_en)
        VALUES (NEWID(), @CenterId, @ResidentId, @date, @number, @state, @cause, @note, @AccountId, @profile, SYSUTCDATETIME())
        """, new { d.CenterId, d.ResidentId, d.AccountId, date = Day0(date ?? Today), number, state, cause, note, profile });

    private static Task<string?> AbsenceAsync(Day d, Guid id, string type, Guid reference, DateOnly? from, DateOnly? to, string? reason, string? cause) => TryAsync("""
        INSERT INTO dbo.ausencias_programadas (id, centro_id, residente_id, tipo_codigo, ausencia_id, desde, hasta, motivo_codigo, causa_codigo, registrado_por_cuenta_id, registrado_por_perfil, registrado_en)
        VALUES (@id, @CenterId, @ResidentId, @type, @reference, @from, @to, @reason, @cause, @AccountId, 'ADMINISTRACION', SYSUTCDATETIME())
        """, new
    {
        d.CenterId, d.ResidentId, d.AccountId, id, type, reference, from = from is null ? (DateTime?)null : Day0(from.Value), to = to is null ? (DateTime?)null : Day0(to.Value), reason, cause,
    });

    /// <summary>El día del centro, el que usan las reglas de la base: de 00:00 a 00:00 UTC no es el mismo que el día UTC.</summary>
    private static DateOnly Today => CenterTime.Today;

    [Theory]
    [InlineData("asistencia_centro")]
    [InlineData("asistencia_pauta")]
    [InlineData("asistencia_dia")]
    [InlineData("ausencias_programadas")]
    public async Task Una_residencia_no_admite_ninguna_fila_de_asistencia(string table)
    {
        var d = await DayCenterFixture.CreateAsync(CenterServiceType.Residencia);
        var absence = Guid.NewGuid();

        var message = table switch
        {
            "asistencia_centro" => await TryAsync("""
                INSERT INTO dbo.asistencia_centro (id, centro_id, numero, activa, confirma_informada, confirma_eipd, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
                VALUES (NEWID(), @CenterId, 1, 1, 1, 1, @AccountId, 'ADMINISTRACION', SYSUTCDATETIME())
                """, new { d.CenterId, d.AccountId }),
            "asistencia_pauta" => await TryAsync("""
                INSERT INTO dbo.asistencia_pauta (id, centro_id, residente_id, numero, dias_esperados, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
                VALUES (NEWID(), @CenterId, @ResidentId, 1, 31, @AccountId, 'ADMINISTRACION', SYSUTCDATETIME())
                """, new { d.CenterId, d.ResidentId, d.AccountId }),
            "asistencia_dia" => await AttendanceAsync(d, "ASISTIO", null, null),
            _ => await AbsenceAsync(d, absence, "REGISTRADA", absence, Today, Today, "VACACIONES", null),
        };

        Assert.Contains("ASSISTANCE_CENTER_INVALID", message ?? "sin error");
    }

    [Fact]
    public async Task El_interruptor_empieza_apagado_encender_exige_las_dos_confirmaciones_y_cada_fila_cambia_el_valor()
    {
        var d = await DayCenterFixture.CreateAsync(CenterServiceType.CentroDeDia);
        Task<string?> Switch(int number, bool on, bool informed = true, bool eipd = true) => TryAsync("""
            INSERT INTO dbo.asistencia_centro (id, centro_id, numero, activa, confirma_informada, confirma_eipd, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (NEWID(), @CenterId, @number, @on, @informed, @eipd, @AccountId, 'ADMINISTRACION', SYSUTCDATETIME())
            """, new { d.CenterId, d.AccountId, number, on, informed, eipd });

        Assert.Contains("ASSISTANCE_SEQUENCE_INVALID", await Switch(1, on: false) ?? "");
        Assert.Contains("CK_asc_confirmaciones", await Switch(1, on: true, informed: false) ?? "");
        Assert.Contains("CK_asc_confirmaciones", await Switch(1, on: true, eipd: false) ?? "");
        Assert.Null(await Switch(1, on: true));
        Assert.Contains("ASSISTANCE_SEQUENCE_INVALID", await Switch(2, on: true) ?? "");
        Assert.Null(await Switch(2, on: false, informed: false, eipd: false));
        Assert.Null(await Switch(3, on: true));
    }

    [Fact]
    public async Task La_pauta_admite_de_1_a_127_y_cada_version_es_distinta_de_la_anterior()
    {
        var d = await DayCenterFixture.CreateAsync(CenterServiceType.CentroDeDia);
        Task<string?> Pattern(int number, int days) => TryAsync("""
            INSERT INTO dbo.asistencia_pauta (id, centro_id, residente_id, numero, dias_esperados, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (NEWID(), @CenterId, @ResidentId, @number, @days, @AccountId, 'ADMINISTRACION', SYSUTCDATETIME())
            """, new { d.CenterId, d.ResidentId, d.AccountId, number, days });

        Assert.Contains("CK_asp_dias", await Pattern(1, 0) ?? "");
        Assert.Contains("CK_asp_dias", await Pattern(1, 128) ?? "");
        Assert.Null(await Pattern(1, 21));
        Assert.Contains("ASSISTANCE_SEQUENCE_INVALID", await Pattern(2, 21) ?? "");
        Assert.Contains("ASSISTANCE_SEQUENCE_INVALID", await Pattern(3, 31) ?? "");
        Assert.Null(await Pattern(2, 31));
    }

    [Fact]
    public async Task La_asistencia_exige_causa_con_NO_ASISTIO_nota_solo_con_OTRO_de_una_linea_y_80_caracteres()
    {
        var d = await DayCenterFixture.CreateAsync(CenterServiceType.CentroDeDia);

        Assert.Contains("CK_asd_causa", await AttendanceAsync(d, "NO_ASISTIO", null, null) ?? "");
        Assert.Contains("CK_asd_causa", await AttendanceAsync(d, "ASISTIO", "SIN_AVISO", null) ?? "");
        Assert.Contains("CK_asd_causa", await AttendanceAsync(d, "NO_ASISTIO", "DIAGNOSTICO", null) ?? "");
        Assert.Contains("CK_asd_nota", await AttendanceAsync(d, "NO_ASISTIO", "SIN_AVISO", "una nota") ?? "");
        Assert.Contains("CK_asd_nota", await AttendanceAsync(d, "NO_ASISTIO", "OTRO", "dos\nlíneas") ?? "");
        Assert.Contains("CK_asd_nota", await AttendanceAsync(d, "NO_ASISTIO", "OTRO", "   ") ?? "");
        Assert.NotNull(await AttendanceAsync(d, "NO_ASISTIO", "OTRO", new string('x', 81)));
        Assert.Contains("CK_asd_perfil", await AttendanceAsync(d, "ASISTIO", null, null, profile: "ADMINISTRACION") ?? "");
        Assert.Null(await AttendanceAsync(d, "NO_ASISTIO", "OTRO", new string('x', 80)));
    }

    [Fact]
    public async Task La_asistencia_no_es_futura_ni_anterior_al_alta_y_cada_correccion_cambia_algo_y_sigue_a_la_anterior()
    {
        var d = await DayCenterFixture.CreateAsync(CenterServiceType.CentroDeDia);

        Assert.Contains("ASSISTANCE_DATE_INVALID", await AttendanceAsync(d, "ASISTIO", null, null, date: Today.AddDays(2)) ?? "");
        Assert.Contains("ASSISTANCE_DATE_INVALID", await AttendanceAsync(d, "ASISTIO", null, null, date: Today.AddDays(-2)) ?? "");
        Assert.Contains("ASSISTANCE_SEQUENCE_INVALID", await AttendanceAsync(d, "ASISTIO", null, null, number: 2) ?? "");
        Assert.Null(await AttendanceAsync(d, "ASISTIO", null, null));
        Assert.Contains("ASSISTANCE_SEQUENCE_INVALID", await AttendanceAsync(d, "ASISTIO", null, null, number: 2) ?? "");
        Assert.Contains("ASSISTANCE_SEQUENCE_INVALID", await AttendanceAsync(d, "NO_ASISTIO", "SIN_AVISO", null, number: 3) ?? "");
        Assert.Null(await AttendanceAsync(d, "NO_ASISTIO", "SIN_AVISO", null, number: 2));
        Assert.Contains("UX_asd_numero", await AttendanceAsync(d, "NO_ASISTIO", "AVISO_FAMILIA", null, number: 2) ?? "");
    }

    [Fact]
    public async Task Un_residente_suspendido_no_admite_asistencia()
    {
        var d = await DayCenterFixture.CreateAsync(CenterServiceType.CentroDeDia);
        Assert.Null(await TryAsync("""
            INSERT INTO dbo.suspensiones_residente (id, residente_id, centro_id, unidad_id, iniciada_por_cuenta_id, iniciada_en)
            VALUES (NEWID(), @ResidentId, @CenterId, @UnitId, @AccountId, SYSUTCDATETIME())
            """, new { d.ResidentId, d.CenterId, d.UnitId, d.AccountId }));

        Assert.Contains("ASSISTANCE_RESIDENT_INVALID", await AttendanceAsync(d, "ASISTIO", null, null) ?? "sin error");
    }

    [Fact]
    public async Task Las_ausencias_tienen_hasta_90_dias_como_maximo_sin_solapes_y_anular_o_acortar_siguen_reglas()
    {
        var d = await DayCenterFixture.CreateAsync(CenterServiceType.CentroDeDia);
        var first = Guid.NewGuid();
        var start = Today.AddDays(1);

        Assert.Contains("CK_aup_forma", await AbsenceAsync(d, Guid.NewGuid(), "REGISTRADA", Guid.Empty, start, null, "VACACIONES", null) ?? "");
        var tooLong = Guid.NewGuid();
        Assert.Contains("CK_aup_forma", await AbsenceAsync(d, tooLong, "REGISTRADA", tooLong, start, start.AddDays(90), "VACACIONES", null) ?? "");
        var badReason = Guid.NewGuid();
        Assert.Contains("CK_aup_motivo", await AbsenceAsync(d, badReason, "REGISTRADA", badReason, start, start.AddDays(3), "GRIPE", null) ?? "");
        Assert.Null(await AbsenceAsync(d, first, "REGISTRADA", first, start, start.AddDays(89), "INGRESO_HOSPITALARIO", null));

        var overlapping = Guid.NewGuid();
        Assert.Contains("ASSISTANCE_ABSENCE_OVERLAP", await AbsenceAsync(d, overlapping, "REGISTRADA", overlapping, start.AddDays(10), start.AddDays(12), "OTRO", null) ?? "");

        // Acortar: debe terminar antes del último día vigente y no antes del primero.
        Assert.Contains("ASSISTANCE_ABSENCE_INVALID", await AbsenceAsync(d, Guid.NewGuid(), "ACORTADA", first, null, start.AddDays(89), null, "REGRESO_ANTES") ?? "");
        Assert.Contains("ASSISTANCE_ABSENCE_INVALID", await AbsenceAsync(d, Guid.NewGuid(), "ACORTADA", first, null, start.AddDays(-1), null, "REGRESO_ANTES") ?? "");
        Assert.Null(await AbsenceAsync(d, Guid.NewGuid(), "ACORTADA", first, null, start.AddDays(5), null, "REGRESO_ANTES"));
        // Ya acortada: ahora sí cabe otra que empiece después del nuevo último día.
        Assert.Null(await AbsenceAsync(d, overlapping, "REGISTRADA", overlapping, start.AddDays(10), start.AddDays(12), "OTRO", null));

        // Anular: una vez; después no admite más cambios y deja de contar para los solapes.
        Assert.Null(await AbsenceAsync(d, Guid.NewGuid(), "ANULADA", overlapping, null, null, null, "ERROR"));
        Assert.Contains("ASSISTANCE_ABSENCE_INVALID", await AbsenceAsync(d, Guid.NewGuid(), "ANULADA", overlapping, null, null, null, "ERROR") ?? "");
        Assert.Contains("ASSISTANCE_ABSENCE_INVALID", await AbsenceAsync(d, Guid.NewGuid(), "ACORTADA", overlapping, null, start.AddDays(11), null, "REGRESO_ANTES") ?? "");
        var again = Guid.NewGuid();
        Assert.Null(await AbsenceAsync(d, again, "REGISTRADA", again, start.AddDays(10), start.AddDays(12), "OTRO", null));

        // La de otra persona del mismo centro o la de otro centro no se pueden anular ni acortar.
        var neighbour = d with { ResidentId = await DayCenterFixture.AddResidentAsync(d.AccountId, d.CenterId, d.UnitId, "Otra persona (ficticia)") };
        var elsewhere = await DayCenterFixture.CreateAsync(CenterServiceType.CentroDeDia);
        foreach (var outsider in new[] { neighbour, elsewhere })
        {
            Assert.Contains("ASSISTANCE_ABSENCE_INVALID", await AbsenceAsync(outsider, Guid.NewGuid(), "ANULADA", again, null, null, null, "ERROR") ?? "");
            Assert.Contains("ASSISTANCE_ABSENCE_INVALID", await AbsenceAsync(outsider, Guid.NewGuid(), "ACORTADA", again, null, start.AddDays(11), null, "REGRESO_ANTES") ?? "");
        }
    }

    [Fact]
    public async Task Las_ausencias_de_personas_distintas_se_registran_acortan_y_anulan_a_la_vez_sin_interbloqueos()
    {
        var days = new List<Day>();
        for (var i = 0; i < 8; i++)
        {
            days.Add(await DayCenterFixture.CreateAsync(CenterServiceType.CentroDeDia));
        }

        // Cada persona repite su propio ciclo mientras las demás hacen el suyo: la regla de la base solo puede leer las filas de esa persona.
        var errors = await Task.WhenAll(days.Select(async d =>
        {
            for (var round = 0; round < 25; round++)
            {
                var id = Guid.NewGuid();
                var error = await AbsenceAsync(d, id, "REGISTRADA", id, Today.AddDays(1), Today.AddDays(10), "OTRO", null)
                    ?? await AbsenceAsync(d, Guid.NewGuid(), "ACORTADA", id, null, Today.AddDays(5), null, "REGRESO_ANTES")
                    ?? await AbsenceAsync(d, Guid.NewGuid(), "ANULADA", id, null, null, null, "ERROR");
                if (error is not null)
                {
                    return error;
                }
            }

            return null;
        }));

        Assert.All(errors, Assert.Null);
    }

    [Theory]
    [InlineData("asistencia_centro")]
    [InlineData("asistencia_pauta")]
    [InlineData("asistencia_dia")]
    [InlineData("ausencias_programadas")]
    public async Task Ninguna_tabla_de_asistencia_se_actualiza_ni_se_borra(string table)
    {
        var d = await DayCenterFixture.CreateAsync(CenterServiceType.CentroDeDia);
        var id = Guid.NewGuid();
        var inserted = table switch
        {
            "asistencia_centro" => await TryAsync("""
                INSERT INTO dbo.asistencia_centro (id, centro_id, numero, activa, confirma_informada, confirma_eipd, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
                VALUES (@id, @CenterId, 1, 1, 1, 1, @AccountId, 'ADMINISTRACION', SYSUTCDATETIME())
                """, new { id, d.CenterId, d.AccountId }),
            "asistencia_pauta" => await TryAsync("""
                INSERT INTO dbo.asistencia_pauta (id, centro_id, residente_id, numero, dias_esperados, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
                VALUES (@id, @CenterId, @ResidentId, 1, 31, @AccountId, 'ADMINISTRACION', SYSUTCDATETIME())
                """, new { id, d.CenterId, d.ResidentId, d.AccountId }),
            "asistencia_dia" => await TryAsync("""
                INSERT INTO dbo.asistencia_dia (id, centro_id, residente_id, fecha, numero, estado_codigo, anotado_por_cuenta_id, anotado_por_perfil, anotado_en)
                VALUES (@id, @CenterId, @ResidentId, @today, 1, 'ASISTIO', @AccountId, 'AUXILIAR', SYSUTCDATETIME())
                """, new { id, d.CenterId, d.ResidentId, d.AccountId, today = Day0(Today) }),
            _ => await AbsenceAsync(d, id, "REGISTRADA", id, Today, Today, "VACACIONES", null),
        };
        Assert.Null(inserted);

        Assert.Contains("ASSISTANCE_IMMUTABLE", await TryAsync($"UPDATE dbo.{table} SET centro_id = centro_id WHERE id = @id", new { id }) ?? "");
        Assert.Contains("ASSISTANCE_IMMUTABLE", await TryAsync($"DELETE FROM dbo.{table} WHERE id = @id", new { id }) ?? "");
    }

    [Fact]
    public async Task La_pauta_tiene_fecha_de_efecto_la_primera_vale_desde_siempre_y_las_siguientes_no_van_antes_de_la_que_esta_en_vigor_ni_pasan_de_90_dias()
    {
        var d = await DayCenterFixture.CreateAsync(CenterServiceType.CentroDeDia);
        Task<string?> Pattern(int number, int days, string from) => TryAsync("""
            INSERT INTO dbo.asistencia_pauta (id, centro_id, residente_id, numero, dias_esperados, vigente_desde, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (NEWID(), @CenterId, @ResidentId, @number, @days, CAST(@from AS DATE), @AccountId, 'ADMINISTRACION', SYSUTCDATETIME())
            """, new { d.CenterId, d.ResidentId, d.AccountId, number, days, from });
        string Day(int offset) => DateTime.UtcNow.Date.AddDays(offset).ToString("yyyy-MM-dd");

        // La primera versión vale desde siempre.
        Assert.Contains("ASSISTANCE_PATTERN_DATE_INVALID", await Pattern(1, 21, Day(0)) ?? "");
        Assert.Null(await Pattern(1, 21, "0001-01-01"));
        // La versión en vigor hoy es la 1 (desde siempre): una nueva puede ser de hoy, pasada, futura hasta 90 días, pero no más allá.
        Assert.Contains("ASSISTANCE_PATTERN_DATE_INVALID", await Pattern(2, 31, Day(95)) ?? "");
        Assert.Null(await Pattern(2, 31, Day(60)));
        // La 2 todavía no está en vigor (empieza dentro de 60 días): la en vigor sigue siendo la 1, así que una 3 puede sustituirla con una fecha anterior a la de la 2.
        Assert.Null(await Pattern(3, 63, Day(10)));
        // Ahora la en vigor sigue siendo la 1 (la 3 empieza dentro de 10 días). Una versión pasada solo vale si no es anterior a la en vigor.
        Assert.Null(await Pattern(4, 3, Day(-3)));
        // La 4 ya está en vigor desde hace 3 días: una 5 anterior a esa fecha se rechaza, la misma fecha o posterior vale.
        Assert.Contains("ASSISTANCE_PATTERN_DATE_INVALID", await Pattern(5, 5, Day(-4)) ?? "");
        Assert.Null(await Pattern(5, 5, Day(-3)));
    }
}
