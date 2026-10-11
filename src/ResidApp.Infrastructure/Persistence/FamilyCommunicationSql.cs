namespace ResidApp.Infrastructure.Persistence;

/// <summary>El contenido vigente de un comunicado a la familia (script 0049): el de su última corrección o, si no tiene ninguna, el original
/// de comunicaciones_familiares. Se usa con el alias <c>family</c>.</summary>
internal static class FamilyCommunicationSql
{
    public const string CurrentType =
        "COALESCE((SELECT TOP (1) fc.tipo_codigo FROM dbo.comunicaciones_familiares_correcciones fc WHERE fc.comunicacion_id = family.id ORDER BY fc.numero DESC), family.tipo_codigo)";

    public const string CurrentText =
        "COALESCE((SELECT TOP (1) fc.texto FROM dbo.comunicaciones_familiares_correcciones fc WHERE fc.comunicacion_id = family.id ORDER BY fc.numero DESC), family.texto)";

    /// <summary>Cuándo se publica solo: la hora fijada al guardarlo (script 0051) o, en los guardados antes, la de CJ (19:00 y 1 hora). Alias
    /// <c>family.publicacion_programada_en</c> leído como UTC.</summary>
    public static DateTimeOffset ScheduledAt(DateTime preparedAtUtc, DateTime? storedUtc) => storedUtc is { } stored
        ? new DateTimeOffset(DateTime.SpecifyKind(stored, DateTimeKind.Utc))
        : Domain.Enfermeria.FamilyCommunicationSchedule.ScheduledAt(
            new DateTimeOffset(DateTime.SpecifyKind(preparedAtUtc, DateTimeKind.Utc)), TimeZoneInfo.Local);

    /// <summary>Cuántas correcciones tiene: la versión vigente (0 es el original).</summary>
    public const string Version =
        "(SELECT COUNT(*) FROM dbo.comunicaciones_familiares_correcciones fc WHERE fc.comunicacion_id = family.id)";
}
