using ResidApp.Application.Intercambio;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>ADR 0010: lee la primera hoja de un fichero CSV, XLS o XLSX. El tipo se reconoce por su contenido, no por su nombre ni por
/// el Content-Type. Un fichero ilegible es EXCHANGE_FILE_INVALID; uno que pasa de <see cref="ExchangeLimits"/>, EXCHANGE_FILE_TOO_LARGE.</summary>
public interface ITableFileReader
{
    ExchangeTable Read(byte[] content);
}

/// <summary>Una plantilla de emparejamiento guardada (script 0060). Mapping es la de ExchangeMapping; Headers, las cabeceras del
/// fichero con que se guardó (en exportar, las columnas que se escriben).</summary>
public sealed record ExchangeTemplate(
    Guid TemplateId, ExchangeDirection Direction, ExchangeEntity Entity, string Name, IReadOnlyList<string> Headers, ExchangeMapping Mapping);

/// <summary>Columns tiene un campo (o null, «no se usa») por cabecera, en su orden. SexValues va de la clave normalizada del valor al
/// sexo documentado; solo se usa al importar residentes.</summary>
public sealed record ExchangeMapping(
    IReadOnlyList<ExchangeField?> Columns, ExchangeDateFormat DateFormat, IReadOnlyDictionary<string, DocumentedSexCode> SexValues);

/// <summary>Un residente activo del ámbito para la exportación del censo y para detectar duplicados al importar: solo identidad
/// administrativa, ubicación y referencia interna del episodio vigente, nunca contenido clínico.</summary>
public sealed record CensusEntry(
    string DisplayName, DateOnly BirthDate, DocumentedSexCode Sex, string UnitName, string? RoomName, string? PlaceName,
    string? InternalReference);

/// <summary>Lecturas de la importación y la exportación, siempre del centro y del ámbito de Administración de quien consulta.</summary>
public interface IExchangeDirectory
{
    /// <summary>La plantilla más reciente con esa firma de cabeceras, o null.</summary>
    Task<ExchangeTemplate?> FindTemplateAsync(
        AccountAdministrationAccess access, ExchangeDirection direction, ExchangeEntity entity, string signature, CancellationToken ct = default);

    Task<ExchangeTemplate?> FindTemplateAsync(AccountAdministrationAccess access, Guid templateId, CancellationToken ct = default);

    /// <summary>Las plantillas del centro en una dirección, por entidad y nombre.</summary>
    Task<IReadOnlyList<ExchangeTemplate>> ListTemplatesAsync(
        AccountAdministrationAccess access, ExchangeDirection direction, CancellationToken ct = default);

    /// <summary>Los residentes activos del ámbito, por nombre (la misma regla de ámbito que la lista de Administración, ADM-02).</summary>
    Task<IReadOnlyList<CensusEntry>> ListCensusAsync(AccountAdministrationAccess access, CancellationToken ct = default);
}

/// <summary>Escrituras de la importación y la exportación. Cada una repite el ámbito de Administración dentro de su transacción y deja
/// su evento en dbo.eventos_auditoria, sin datos.</summary>
public interface IExchangeRepository
{
    /// <summary>Guarda la plantilla; si ya hay una con ese nombre (en el centro, la dirección y la entidad), la sustituye.
    /// Auditoría EXCHANGE_TEMPLATE_SAVE.</summary>
    Task<Guid> SaveTemplateAsync(
        AccountAdministrationAccess access, ExchangeDirection direction, ExchangeEntity entity, string name, IReadOnlyList<string> headers,
        ExchangeMapping mapping, CancellationToken ct = default);

    /// <summary>Deja constancia de una importación (IMPORT_RUN) o de una exportación (EXPORT_RUN), sin datos ni recuentos.</summary>
    Task RecordRunAsync(AccountAdministrationAccess access, ExchangeDirection direction, CancellationToken ct = default);
}
