using Dapper;
using ResidApp.IntegrationTests.TestSupport;
using Xunit;

namespace ResidApp.IntegrationTests;

/// <summary>Cada tabla de dbo está bajo la política de seguridad por filas (database/scripts/0030, ADR 0008) o figura aquí con su motivo.
/// Así una tabla nueva no queda sin proteger sin que nadie lo decida: o se añade a la política o se justifica aquí. Si una tabla
/// de la lista pasa a estar cubierta, hay que quitarla.</summary>
public sealed class CoberturaRlsTests
{
    private const string Diseno = "Por diseño: la lee el login antes de que haya ámbito, o no es de datos";

    private static readonly Dictionary<string, string> Excepciones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["centros"] = Diseno,
        ["cuentas"] = Diseno,
        ["sesiones"] = Diseno,
        ["credenciales"] = Diseno,
        ["passkeys"] = Diseno,
        ["eventos_acceso"] = Diseno,
        ["invitaciones_acceso"] = Diseno,
        ["vinculaciones_cuenta"] = Diseno,
        ["vinculaciones_cuenta_unidades"] = Diseno,
        ["ambitos_perfil"] = Diseno,
        ["scripts_aplicados"] = Diseno,
        ["sysdiagrams"] = Diseno,
    };

    private sealed record TableRow(string Name, bool HasPolicy);

    private static async Task<List<TableRow>> ReadTablesAsync()
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return (await connection.QueryAsync<TableRow>("""
            SELECT t.name AS Name,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM sys.security_predicates predicate WHERE predicate.target_object_id = t.object_id)
                             THEN 1 ELSE 0 END AS bit) AS HasPolicy
              FROM sys.tables t
             WHERE t.schema_id = SCHEMA_ID(N'dbo')
            """)).ToList();
    }

    // Hijas a las que 0031 añadió centro_id (ADR 0008): obligatorio y dentro de una clave foránea con su padre (o con centros).
    private static readonly string[] ChildTables =
    [
        "cierres_cotidianos_cambio_area_opciones", "intentos_llamada_familia", "operaciones_idempotencia", "protocolo_urgente_registros",
        "seguimiento_acciones", "seguimiento_medico_acciones", "valoraciones_enfermeria_correcciones", "valoraciones_enfermeria_versiones",
        "valoraciones_medicas_correcciones", "valoraciones_medicas_versiones",
    ];

    [Fact]
    public async Task Las_tablas_hijas_llevan_centro_id_obligatorio_y_en_una_clave_foranea()
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var required = (await connection.QueryAsync<string>("""
            SELECT t.name FROM sys.tables t JOIN sys.columns c ON c.object_id = t.object_id
             WHERE t.schema_id = SCHEMA_ID(N'dbo') AND c.name = N'centro_id' AND c.is_nullable = 0
            """)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var withForeignKey = (await connection.QueryAsync<string>("""
            SELECT DISTINCT OBJECT_NAME(fkc.parent_object_id)
              FROM sys.foreign_key_columns fkc JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
             WHERE c.name = N'centro_id'
            """)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(ChildTables, name => Assert.Contains(name, required));
        Assert.All(ChildTables, name => Assert.Contains(name, withForeignKey));
    }

    [Fact]
    public async Task Toda_tabla_cubierta_tiene_filtro_y_bloqueo_al_insertar_y_al_actualizar()
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var predicates = (await connection.QueryAsync<(string Table, string Type, string Operation)>("""
            SELECT OBJECT_NAME(target_object_id), predicate_type_desc, ISNULL(operation_desc, N'') FROM sys.security_predicates
            """)).ToList();

        string[] required = ["FILTER|", "BLOCK|AFTER INSERT", "BLOCK|AFTER UPDATE"];
        var incomplete = predicates.Select(p => p.Table).Distinct()
            .Where(table => required.Any(r => !predicates.Any(p => p.Table == table && $"{p.Type}|{p.Operation}" == r)))
            .Order().ToList();

        Assert.True(incomplete.Count == 0, "Tablas con la política a medias (falta FILTER o BLOCK al insertar/actualizar): " + string.Join(", ", incomplete));
    }

    /// <summary>Script 0090: qué función protege cada tabla de la estructura del centro, por tipo y operación. Las tres funciones de ese script no se usan en ninguna otra tabla:
    /// un «salto» no se reutiliza sin que alguien lo decida aquí.</summary>
    [Fact]
    public async Task Las_tablas_de_estructura_usan_las_funciones_previstas_y_ninguna_otra_tabla_las_usa()
    {
        var expected = new Dictionary<string, string>
        {
            ["unidades|FILTER|"] = "fn_centro_o_plataforma",
            ["unidades|BLOCK|AFTER INSERT"] = "fn_unidad_escribe",
            ["unidades|BLOCK|AFTER UPDATE"] = "fn_centro_del_ambito",
            ["ambitos_perfil_unidad|FILTER|"] = "fn_centro_o_plataforma",
            ["ambitos_perfil_unidad|BLOCK|AFTER INSERT"] = "fn_asignacion_escribe",
            ["ambitos_perfil_unidad|BLOCK|AFTER UPDATE"] = "fn_centro_del_ambito",
            ["administraciones_principales_cambios|FILTER|"] = "fn_centro_o_plataforma",
            ["administraciones_principales_cambios|BLOCK|AFTER INSERT"] = "fn_estructura_escribe",
            ["administraciones_principales_cambios|BLOCK|AFTER UPDATE"] = "fn_centro_del_ambito",
            ["logos_centro|FILTER|"] = "fn_centro_del_ambito",
            ["logos_centro|BLOCK|AFTER INSERT"] = "fn_estructura_escribe",
            ["logos_centro|BLOCK|AFTER UPDATE"] = "fn_centro_del_ambito",
        };
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var rows = (await connection.QueryAsync<(string Table, string Type, string Operation, string Definition)>("""
            SELECT OBJECT_NAME(sp.target_object_id), sp.predicate_type_desc, ISNULL(sp.operation_desc, N''), sp.predicate_definition
              FROM sys.security_predicates sp
            """)).ToList();

        foreach (var (key, function) in expected)
        {
            var parts = key.Split('|');
            var row = Assert.Single(rows, r => r.Table == parts[0] && r.Type == parts[1] && r.Operation == parts[2]);
            Assert.Contains($"[{function}]", row.Definition);
        }

        var tableNames = expected.Keys.Select(k => k.Split('|')[0]).ToHashSet();
        foreach (var function in new[] { "fn_centro_o_plataforma", "fn_unidad_escribe", "fn_asignacion_escribe", "fn_estructura_escribe" })
        {
            var users = rows.Where(r => r.Definition.Contains($"[{function}]")).Select(r => r.Table).Distinct().ToList();
            Assert.All(users, table => Assert.Contains(table, tableNames));
        }
    }

    [Fact]
    public async Task Toda_tabla_esta_bajo_la_politica_o_figura_como_excepcion_con_su_motivo()
    {
        var undecided = (await ReadTablesAsync())
            .Where(t => !t.HasPolicy && !Excepciones.ContainsKey(t.Name))
            .Select(t => t.Name)
            .Order()
            .ToList();

        Assert.True(undecided.Count == 0,
            "Tablas sin decidir su seguridad por filas: " + string.Join(", ", undecided) +
            ". Añádelas a seg.pol_centro en un script nuevo, o a Excepciones con su motivo.");
    }

    [Fact]
    public async Task La_lista_de_excepciones_no_tiene_entradas_obsoletas()
    {
        var tables = (await ReadTablesAsync()).ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

        var covered = Excepciones.Keys.Where(name => tables.TryGetValue(name, out var table) && table.HasPolicy).Order().ToList();
        var missing = Excepciones.Keys.Where(name => !tables.ContainsKey(name)).Order().ToList();

        Assert.True(covered.Count == 0, "Ya están bajo la política, quítalas de Excepciones: " + string.Join(", ", covered));
        // sysdiagrams solo existe si alguien usó diagramas de base de datos.
        Assert.True(missing.Where(name => name != "sysdiagrams").Count() == 0,
            "No existen, quítalas o renómbralas en Excepciones: " + string.Join(", ", missing));
    }
}
