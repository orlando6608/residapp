using System.Data;
using Dapper;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Dapper 2.1.79 no resuelve System.DateOnly a un DbType por defecto y lanza NotSupportedException al
/// usarlo como parámetro ("The member ... of type System.DateOnly cannot be used as a parameter value") —
/// descubierto al ejecutar SqlResidentRepository contra SQL Server real por primera vez (ver
/// docs/tareas/alta-prioridad/pendientes-migracion-inicial.md, punto 1: "hoy solo están verificados por
/// lectura, no ejecutados"). ResidApp.Web debe llamar a Register() una vez al arrancar, antes de resolver
/// cualquier repositorio Dapper; cubre también DateOnly? (SqlBaselineRepository), porque Dapper busca el
/// handler del tipo subyacente al desenvolver Nullable&lt;T&gt;.
/// </summary>
public static class DapperDateOnlyTypeHandler
{
    public static void Register() => SqlMapper.AddTypeHandler(new Handler());

    private sealed class Handler : SqlMapper.TypeHandler<DateOnly>
    {
        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = DbType.Date;
            parameter.Value = value.ToDateTime(TimeOnly.MinValue);
        }

        public override DateOnly Parse(object value) => DateOnly.FromDateTime((DateTime)value);
    }
}
