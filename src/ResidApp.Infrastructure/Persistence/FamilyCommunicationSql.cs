namespace ResidApp.Infrastructure.Persistence;

/// <summary>El contenido vigente de un comunicado a la familia (script 0049): el de su última corrección o, si no tiene ninguna, el original
/// de comunicaciones_familiares. Se usa con el alias <c>family</c>.</summary>
internal static class FamilyCommunicationSql
{
    public const string CurrentType =
        "COALESCE((SELECT TOP (1) fc.tipo_codigo FROM dbo.comunicaciones_familiares_correcciones fc WHERE fc.comunicacion_id = family.id ORDER BY fc.numero DESC), family.tipo_codigo)";

    public const string CurrentText =
        "COALESCE((SELECT TOP (1) fc.texto FROM dbo.comunicaciones_familiares_correcciones fc WHERE fc.comunicacion_id = family.id ORDER BY fc.numero DESC), family.texto)";

    /// <summary>Cuántas correcciones tiene: la versión vigente (0 es el original).</summary>
    public const string Version =
        "(SELECT COUNT(*) FROM dbo.comunicaciones_familiares_correcciones fc WHERE fc.comunicacion_id = family.id)";
}
