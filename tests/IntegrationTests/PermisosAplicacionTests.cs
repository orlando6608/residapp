using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.IntegrationTests.TestSupport;
using Xunit;

namespace ResidApp.IntegrationTests;

/// <summary>Permisos mínimos del usuario de la aplicación (hallazgo M2, script 0091, ADR 0008). El usuario real (residapp_app) se une al rol
/// residapp_aplicacion, que recibe en cada tabla solo lo que el código hace en ella. Esta matriz sale del código de src/ y de los disparadores
/// reales, no del script: si el script y la matriz discrepan, o el código escribe donde la matriz no lo permite, fallan estas pruebas.
/// Una tabla nueva obliga a declararla aquí (y en un script nuevo). INSERT: todas las tablas de la aplicación.</summary>
public sealed class PermisosAplicacionTests
{
    private const string Rol = "residapp_aplicacion";
    private const string ProbeUser = "residapp_permisos_test"; // peor caso: en el rol Y en db_datawriter
    private const string RoleOnlyUser = "residapp_permisos_rol"; // como el usuario real: db_datareader + el rol, nada más
    private static readonly string[] Separators = [" ", "\r", "\n"];

    // Tablas que la aplicación actualiza (columna: solo terminada_en, ver ColumnaTerminada).
    private static readonly HashSet<string> Update = Names("""
        ambitos_perfil ambitos_perfil_residente ambitos_perfil_unidad bajas_residente basales_borrador basales_vigentes_residente cargos centros citas citas_bloqueos
        citas_configuracion_centro citas_franjas citas_solicitudes credenciales cuentas declaraciones_acceso_clinico declaraciones_historial_traslado edificios envios_correo
        episodios_residente_centro equipos equipos_miembros eventos_asistenciales familiares fotos_residente habitaciones indicaciones_medicas intervalos_ubicacion_residente
        invitaciones_acceso logos_centro operaciones_idempotencia passkeys permisos_perfil planificacion_turnos plantas plantillas_intercambio plazas portal_familiar_visitas
        rangos_referencia_constantes residentes residentes_familiares sesiones suspensiones_residente turnos_catalogo unidades valoraciones_enfermeria valoraciones_medicas
        vinculaciones_cuenta
        """);

    // Tablas que la aplicación borra.
    private static readonly HashSet<string> Delete = Names("""
        basales_borrador_areas basales_borrador_barthel cargos_ocupantes cargos_unidades citas_modalidades_habilitadas comunicados_horario_centro credenciales
        fotos_residente fotos_residente_negativas logos_centro passkeys plazos_hitos_centro rangos_referencia_constantes
        """);

    // Su UPDATE es solo de la columna terminada_en (cierre de la declaración): concedido por columna; el disparador rechaza el resto.
    private static readonly HashSet<string> ColumnaTerminada = Names("declaraciones_acceso_clinico declaraciones_historial_traslado");

    // Solo la toca el administrador (aplicar-scripts.sh), no la aplicación.
    private static readonly HashSet<string> SinPermisos = Names("scripts_aplicados");

    private static HashSet<string> Names(string list) => new(list.Split(Separators, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

    private static async Task<List<string>> TablesAsync()
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return (await connection.QueryAsync<string>("SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo')")).ToList();
    }

    private sealed record Grant(string Table, string Permission, string State, int ColumnId, string? Column);

    private static async Task<List<Grant>> RoleGrantsAsync(string principal)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return (await connection.QueryAsync<Grant>("""
            SELECT OBJECT_NAME(p.major_id) AS [Table], p.permission_name AS Permission, p.state_desc AS State, p.minor_id AS ColumnId, COL_NAME(p.major_id, p.minor_id) AS [Column]
              FROM sys.database_permissions p
             WHERE p.class = 1 AND p.grantee_principal_id = DATABASE_PRINCIPAL_ID(@principal)
               AND p.permission_name IN (N'INSERT', N'UPDATE', N'DELETE') AND OBJECTPROPERTY(p.major_id, N'IsUserTable') = 1
            """, new { principal })).ToList();
    }

    [Fact]
    public async Task Cada_tabla_tiene_declarados_y_concedidos_exactamente_sus_permisos_de_escritura()
    {
        var tables = await TablesAsync();
        var grants = await RoleGrantsAsync(Rol);
        Assert.True(grants.Count > 0, $"El rol {Rol} no existe o no tiene permisos: ¿se aplicó el script 0091?");

        var problems = new List<string>();
        foreach (var table in tables.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
        {
            var own = grants.Where(g => string.Equals(g.Table, table, StringComparison.OrdinalIgnoreCase)).ToList();
            if (SinPermisos.Contains(table))
            {
                if (own.Count > 0) problems.Add($"{table}: no debe tener permisos para la aplicación");
                continue;
            }

            bool Granted(string permission, bool column) =>
                own.Any(g => g.Permission == permission && g.State == "GRANT" && (g.ColumnId > 0) == column);
            bool Denied(string permission) => own.Any(g => g.Permission == permission && g.State == "DENY" && g.ColumnId == 0);

            if (!Granted("INSERT", false)) problems.Add($"{table}: falta INSERT (¿tabla nueva sin declarar en la matriz y en un script?)");
            if (ColumnaTerminada.Contains(table))
            {
                // Un GRANT de columna prevalece sobre un DENY de tabla, así que aquí no hay UPDATE de tabla (ni concedido ni denegado): solo la columna.
                if (Granted("UPDATE", false) || Denied("UPDATE")) problems.Add($"{table}: no debe tener UPDATE de tabla (ni GRANT ni DENY)");
            }
            else
            {
                Check(table, "UPDATE", Update.Contains(table), Granted("UPDATE", false), Denied("UPDATE"), problems);
            }

            Check(table, "DELETE", Delete.Contains(table), Granted("DELETE", false), Denied("DELETE"), problems);
            var columnGrants = own.Count(g => g.Permission == "UPDATE" && g.State == "GRANT" && g.ColumnId > 0);
            var columnDenies = own.Count(g => g.Permission == "UPDATE" && g.State == "DENY" && g.ColumnId > 0);
            var grantedColumn = own.Where(g => g.Permission == "UPDATE" && g.State == "GRANT" && g.ColumnId > 0).Select(g => g.Column).SingleOrDefault();
            if (ColumnaTerminada.Contains(table) && (columnGrants != 1 || grantedColumn != "terminada_en" || columnDenies == 0)) problems.Add($"{table}: debe tener UPDATE solo de terminada_en (esa columna concedida y el resto denegadas)");
            if (!ColumnaTerminada.Contains(table) && (columnGrants > 0 || columnDenies > 0)) problems.Add($"{table}: no debe tener permisos de UPDATE por columna");
        }

        foreach (var name in Update.Concat(Delete).Concat(ColumnaTerminada))
            if (!tables.Contains(name, StringComparer.OrdinalIgnoreCase)) problems.Add($"{name}: figura en la matriz pero la tabla no existe");

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static void Check(string table, string permission, bool expectedGrant, bool granted, bool denied, List<string> problems)
    {
        if (expectedGrant && (!granted || denied)) problems.Add($"{table}: debe tener {permission} concedido");
        if (!expectedGrant && (granted || !denied)) problems.Add($"{table}: {permission} debe estar denegado y no concedido");
    }

    [Fact]
    public async Task Public_y_guest_no_tienen_permisos_de_escritura_y_el_usuario_real_no_es_escritor_general()
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var open = (await connection.QueryAsync<string>("""
            SELECT OBJECT_NAME(p.major_id) + N' ' + p.permission_name
              FROM sys.database_permissions p
             WHERE p.class = 1 AND p.state IN ('G', 'W') AND p.grantee_principal_id IN (0, DATABASE_PRINCIPAL_ID(N'guest'))
               AND p.permission_name IN (N'INSERT', N'UPDATE', N'DELETE') AND OBJECTPROPERTY(p.major_id, N'IsUserTable') = 1
            """)).ToList();
        Assert.Empty(open);

        // El usuario real solo existe donde se creó con crear_usuario_aplicacion*.sql (CI, Azure): allí debe estar en el rol y no en db_datawriter ni db_owner.
        var real = await connection.QuerySingleAsync<(int? InRole, int? Writer, int? Owner)>("""
            SELECT IS_ROLEMEMBER(N'residapp_aplicacion', N'residapp_app'), IS_ROLEMEMBER(N'db_datawriter', N'residapp_app'), IS_ROLEMEMBER(N'db_owner', N'residapp_app')
            """);
        if (real.InRole is not null)
        {
            Assert.Equal(1, real.InRole);
            Assert.Equal(0, real.Writer);
            Assert.Equal(0, real.Owner);
        }
    }

    // Una sola vez por proceso: cambiar la pertenencia a roles mientras otras pruebas consultan puede bloquearlas.
    private static readonly Lazy<Task> ProbeUserCreated = new(async () =>
    {
        using var admin = await TestDatabase.ConnectionFactory.OpenAsync();
        // Peor caso: el usuario de prueba está en el rol Y en db_datawriter (como estaba la aplicación antes); los DENY del rol deben ganar.
        await admin.ExecuteAsync($"""
            IF DATABASE_PRINCIPAL_ID(N'{ProbeUser}') IS NULL CREATE USER {ProbeUser} WITHOUT LOGIN;
            IF IS_ROLEMEMBER(N'db_datareader', N'{ProbeUser}') = 0 ALTER ROLE db_datareader ADD MEMBER {ProbeUser};
            IF IS_ROLEMEMBER(N'db_datawriter', N'{ProbeUser}') = 0 ALTER ROLE db_datawriter ADD MEMBER {ProbeUser};
            IF IS_ROLEMEMBER(N'{Rol}', N'{ProbeUser}') = 0 ALTER ROLE {Rol} ADD MEMBER {ProbeUser};
            IF DATABASE_PRINCIPAL_ID(N'{RoleOnlyUser}') IS NULL CREATE USER {RoleOnlyUser} WITHOUT LOGIN;
            IF IS_ROLEMEMBER(N'db_datareader', N'{RoleOnlyUser}') = 0 ALTER ROLE db_datareader ADD MEMBER {RoleOnlyUser};
            IF IS_ROLEMEMBER(N'{Rol}', N'{RoleOnlyUser}') = 0 ALTER ROLE {Rol} ADD MEMBER {RoleOnlyUser};
            """);
    });

    private static async Task<int?> ErrorOfAsync(string statement, string user = ProbeUser)
    {
        await ProbeUserCreated.Value;
        // Sin pool ni MARS: ver RlsCentroTests (EXECUTE AS no se lleva bien con conexiones reutilizadas).
        var impersonable = new SqlConnectionStringBuilder(TestDatabase.ConnectionString) { Pooling = false, MultipleActiveResultSets = false }.ConnectionString;
        using var connection = new SqlConnection(impersonable);
        await connection.OpenAsync();
        await connection.ExecuteAsync($"EXECUTE AS USER = N'{user}'");
        try
        {
            await connection.ExecuteAsync(statement);
            return null;
        }
        catch (SqlException ex)
        {
            return ex.Number;
        }
        finally
        {
            await connection.ExecuteAsync("REVERT");
        }
    }

    private const int PermissionDenied = 229; // «The UPDATE permission was denied on the object…»; 230 es el de columna

    [Theory]
    [InlineData("UPDATE dbo.eventos_auditoria SET accion_codigo = accion_codigo WHERE 1 = 0")]
    [InlineData("DELETE FROM dbo.eventos_auditoria WHERE 1 = 0")]
    [InlineData("UPDATE dbo.basales_version SET centro_id = centro_id WHERE 1 = 0")]
    [InlineData("DELETE FROM dbo.valoraciones_enfermeria WHERE 1 = 0")]
    [InlineData("DELETE FROM dbo.cuentas WHERE 1 = 0")]
    [InlineData("DELETE FROM dbo.residentes WHERE 1 = 0")]
    [InlineData("DELETE FROM dbo.declaraciones_acceso_clinico WHERE 1 = 0")]
    [InlineData("UPDATE dbo.cargos_ocupantes SET centro_id = centro_id WHERE 1 = 0")]
    [InlineData("DELETE FROM dbo.envios_correo WHERE 1 = 0")]
    [InlineData("DELETE FROM dbo.sesiones WHERE 1 = 0")]
    [InlineData("DELETE FROM dbo.invitaciones_acceso WHERE 1 = 0")]
    [InlineData("EXEC dbo.purgar_envios_correo")]
    [InlineData("EXEC dbo.cerrar_envios_correo_sin_resolver")]
    public async Task Modificar_o_borrar_lo_que_no_se_concede_da_permiso_denegado_aunque_sea_db_datawriter(string statement)
    {
        Assert.Equal(PermissionDenied, await ErrorOfAsync(statement));
    }

    [Fact]
    public async Task TRUNCATE_de_una_tabla_inmutable_esta_denegado()
    {
        Assert.NotNull(await ErrorOfAsync("TRUNCATE TABLE dbo.eventos_auditoria", RoleOnlyUser));
    }

    [Fact]
    public async Task Los_permisos_efectivos_de_un_usuario_solo_del_rol_son_exactamente_los_de_la_matriz()
    {
        await ProbeUserCreated.Value;
        var tables = await TablesAsync();
        var impersonable = new SqlConnectionStringBuilder(TestDatabase.ConnectionString) { Pooling = false, MultipleActiveResultSets = false }.ConnectionString;
        using var connection = new SqlConnection(impersonable);
        await connection.OpenAsync();
        await connection.ExecuteAsync($"EXECUTE AS USER = N'{RoleOnlyUser}'");
        var effective = new List<(string Table, int Insert, int Update, int Delete)>();
        try
        {
            foreach (var table in tables)
            {
                var name = "dbo." + table;
                effective.Add((table,
                    await connection.QuerySingleAsync<int>("SELECT ISNULL(HAS_PERMS_BY_NAME(@name, N'OBJECT', N'INSERT'), 0)", new { name }),
                    await connection.QuerySingleAsync<int>("SELECT ISNULL(HAS_PERMS_BY_NAME(@name, N'OBJECT', N'UPDATE'), 0)", new { name }),
                    await connection.QuerySingleAsync<int>("SELECT ISNULL(HAS_PERMS_BY_NAME(@name, N'OBJECT', N'DELETE'), 0)", new { name })));
            }
        }
        finally
        {
            await connection.ExecuteAsync("REVERT");
        }

        var problems = new List<string>();
        foreach (var (table, insert, update, delete) in effective.OrderBy(e => e.Table, StringComparer.OrdinalIgnoreCase))
        {
            var expectedInsert = SinPermisos.Contains(table) ? 0 : 1;
            // HAS_PERMS_BY_NAME de tabla da 1 solo con UPDATE de tabla entera; las declaraciones (solo una columna) se prueban aparte.
            var expectedUpdate = Update.Contains(table) && !ColumnaTerminada.Contains(table) ? 1 : 0;
            var expectedDelete = Delete.Contains(table) ? 1 : 0;
            if (insert != expectedInsert || update != expectedUpdate || delete != expectedDelete)
                problems.Add($"{table}: efectivo I/U/D {insert}/{update}/{delete}, esperado {expectedInsert}/{expectedUpdate}/{expectedDelete}");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public async Task Sin_permisos_de_base_ni_de_esquema_y_el_usuario_real_solo_tiene_sus_dos_roles()
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        // class 0 = base de datos, 3 = esquema. Solo se admiten los de serie de public (CONNECT y ver las definiciones de claves de cifrado de columna).
        var wide = (await connection.QueryAsync<(string Principal, string Permission, string Class)>("""
            SELECT pr.name, p.permission_name, p.class_desc
              FROM sys.database_permissions p JOIN sys.database_principals pr ON pr.principal_id = p.grantee_principal_id
             WHERE p.class IN (0, 3) AND p.state IN ('G', 'W') AND p.permission_name NOT IN (N'CONNECT', N'VIEW ANY COLUMN ENCRYPTION KEY DEFINITION', N'VIEW ANY COLUMN MASTER KEY DEFINITION')
               AND pr.name IN (N'residapp_aplicacion', N'public', N'guest', N'residapp_app')
            """)).ToList();
        Assert.Empty(wide);

        var direct = (await connection.QueryAsync<string>("""
            SELECT OBJECT_NAME(p.major_id) + N' ' + p.permission_name
              FROM sys.database_permissions p
             WHERE p.class = 1 AND p.state IN ('G', 'W') AND p.grantee_principal_id = DATABASE_PRINCIPAL_ID(N'residapp_app')
               AND p.permission_name IN (N'INSERT', N'UPDATE', N'DELETE')
            """)).ToList();
        Assert.Empty(direct);

        // El usuario real solo existe donde se creó con crear_usuario_aplicacion*.sql (CI, Azure).
        var roles = (await connection.QueryAsync<string>("""
            SELECT r.name FROM sys.database_role_members m
              JOIN sys.database_principals r ON r.principal_id = m.role_principal_id
              JOIN sys.database_principals u ON u.principal_id = m.member_principal_id
             WHERE u.name = N'residapp_app' ORDER BY r.name
            """)).ToList();
        if (roles.Count > 0) Assert.Equal(["db_datareader", "residapp_aplicacion"], roles);
    }

    [Fact]
    public async Task Las_declaraciones_solo_se_actualizan_en_terminada_en()
    {
        // Con el usuario solo-rol (sin db_datawriter), para que un GRANT olvidado no lo tape el escritor general.
        Assert.Null(await ErrorOfAsync("UPDATE dbo.declaraciones_acceso_clinico SET terminada_en = SYSUTCDATETIME() WHERE 1 = 0", RoleOnlyUser));
        Assert.Null(await ErrorOfAsync("UPDATE dbo.declaraciones_historial_traslado SET terminada_en = SYSUTCDATETIME() WHERE 1 = 0", RoleOnlyUser));
        Assert.Equal(230, await ErrorOfAsync("UPDATE dbo.declaraciones_acceso_clinico SET cuenta_id = cuenta_id WHERE 1 = 0", RoleOnlyUser));
        // El peor caso (también db_datawriter): los DENY de columna ganan al UPDATE de tabla.
        Assert.Equal(230, await ErrorOfAsync("UPDATE dbo.declaraciones_acceso_clinico SET cuenta_id = cuenta_id WHERE 1 = 0"));
        Assert.Equal(230, await ErrorOfAsync("UPDATE dbo.declaraciones_historial_traslado SET caduca_en = caduca_en WHERE 1 = 0"));
    }

    [Fact]
    public async Task Lo_que_la_aplicacion_si_hace_sigue_permitido()
    {
        Assert.Null(await ErrorOfAsync("UPDATE dbo.residentes SET nombre_visible = nombre_visible WHERE 1 = 0", RoleOnlyUser));
        Assert.Null(await ErrorOfAsync("DELETE FROM dbo.passkeys WHERE 1 = 0", RoleOnlyUser));
        Assert.Null(await ErrorOfAsync("DELETE FROM dbo.cargos_ocupantes WHERE 1 = 0", RoleOnlyUser));
    }

    // --- Red de seguridad estática: el código de src/ no escribe donde la matriz no lo permite -----------------------------------------------

    private static readonly Regex InsertDirect = new(@"\b(?:INSERT\s+INTO|MERGE(?:\s+INTO)?)\s+dbo\.(\w+)", RegexOptions.IgnoreCase);
    private static readonly Regex UpdateDirect = new(@"\b(?:UPDATE|MERGE(?:\s+INTO)?)\s+dbo\.(\w+)", RegexOptions.IgnoreCase);
    private static readonly Regex DeleteDirect = new(@"\bDELETE\s+(?:FROM\s+)?dbo\.(\w+)", RegexOptions.IgnoreCase);
    private static readonly Regex UpdateAlias = new(@"\bUPDATE\s+(\w+)\s+SET\b[\s\S]{0,800}?\bFROM\s+dbo\.(\w+)\s+(?:AS\s+)?\1\b", RegexOptions.IgnoreCase);
    private static readonly Regex DeleteAlias = new(@"\bDELETE\s+(\w+)\s+FROM\s+dbo\.(\w+)\s+(?:AS\s+)?\1\b", RegexOptions.IgnoreCase);

    private static IEnumerable<string> SourceFiles()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "ResidApp.Infrastructure")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Directory.EnumerateFiles(Path.Combine(directory!.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
    }

    [Fact]
    public async Task El_codigo_no_inserta_actualiza_ni_borra_donde_la_matriz_no_lo_permite()
    {
        var tables = (await TablesAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);
            var name = Path.GetFileName(file);
            foreach (Match m in InsertDirect.Matches(text))
                if (!tables.Contains(m.Groups[1].Value) || SinPermisos.Contains(m.Groups[1].Value)) problems.Add($"{name}: INSERT en {m.Groups[1].Value}, que no es una tabla de la aplicación");
            foreach (var table in UpdateDirect.Matches(text).Select(m => m.Groups[1].Value).Concat(UpdateAlias.Matches(text).Select(m => m.Groups[2].Value)))
                if (!Update.Contains(table)) problems.Add($"{name}: UPDATE en {table}, que la matriz no permite");
            foreach (var table in DeleteDirect.Matches(text).Select(m => m.Groups[1].Value).Concat(DeleteAlias.Matches(text).Select(m => m.Groups[2].Value)))
                if (!Delete.Contains(table)) problems.Add($"{name}: DELETE en {table}, que la matriz no permite");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Distinct()));
    }
}
