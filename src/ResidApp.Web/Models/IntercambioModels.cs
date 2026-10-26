using ResidApp.Application.Intercambio;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;

namespace ResidApp.Web.Models;

/// <summary>
/// ADR 0010: el formulario de importar (y el de crear una plantilla de exportación). El fichero viaja en cada paso (analizar, vista
/// previa, importar) porque no se guarda en el servidor. Campos, FormatoFecha y el sexo de cada valor solo llegan después del primer
/// análisis (ConEmparejamiento): en el primero se propone el emparejamiento automático o el de la plantilla del centro.
/// </summary>
public sealed class ExchangeFormModel
{
    public ExchangeEntity Entidad { get; set; }

    public IFormFile? Fichero { get; set; }

    public bool ConEmparejamiento { get; set; }

    /// <summary>El código de campo de cada columna del fichero, por su posición; vacío es «no se usa».</summary>
    public List<string?> Campos { get; set; } = [];

    public string? FormatoFecha { get; set; }

    /// <summary>La clave normalizada de cada valor de la columna de sexo y, en SexoCodigos con la misma posición, el sexo elegido.</summary>
    public List<string> SexoClaves { get; set; } = [];

    public List<string?> SexoCodigos { get; set; } = [];

    public string? NombrePlantilla { get; set; }

    /// <summary>Los números de fila de los posibles duplicados que se han marcado como «Importar de todos modos» en la vista previa.</summary>
    public List<int> Confirmadas { get; set; } = [];

    public ExchangeMappingInput? Input() => ConEmparejamiento
        ? new ExchangeMappingInput(
            Campos, FormatoFecha, SexoClaves.Select((key, i) => new SexValueInput(key, i < SexoCodigos.Count ? SexoCodigos[i] : null)).ToList(),
            Confirmadas)
        : null;
}

/// <summary>La pantalla de exportar: las plantillas de exportación del centro, por entidad y nombre, y la que se acaba de guardar (si
/// está en la lista).</summary>
public sealed record ExportViewModel(IReadOnlyList<ExchangeTemplate> Templates, string? SavedTemplate = null);
