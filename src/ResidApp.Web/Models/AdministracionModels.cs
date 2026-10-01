using System.ComponentModel.DataAnnotations;
using System.Globalization;
using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;

namespace ResidApp.Web.Models;

/// <summary>ADM-01: el inicio de Administración, con el número de residentes activos del ámbito.</summary>
public sealed record AdministracionInicioViewModel(int Residents);

/// <summary>ADM-02: buscar y filtrar la lista de residentes. Llega por GET (?q=&amp;unidad=); los campos vacíos no
/// filtran.</summary>
public sealed class AdministrativeResidentFilter
{
    [Display(Name = "Nombre")]
    public string? Q { get; set; }

    [Display(Name = "Unidad")]
    public Guid? Unidad { get; set; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Q) && Unidad is null;
}

/// <summary>ADM-02: la lista del ámbito (ya autorizada) filtrada en memoria, como la de Enfermería: el nombre se busca
/// sin distinguir mayúsculas ni acentos, y la unidad solo se ofrece si hay más de una.</summary>
public sealed record AdministrativeResidentListViewModel(
    IReadOnlyList<AdministrativeResidentSummary> Residents, int TotalCount, AdministrativeResidentFilter Filter,
    IReadOnlyList<(Guid Id, string Name)> Units, DateOnly Today)
{
    public static AdministrativeResidentListViewModel From(
        IReadOnlyList<AdministrativeResidentSummary> all, AdministrativeResidentFilter filter, DateOnly today)
    {
        var name = filter.Q?.Trim();
        var shown = all
            .Where(r => string.IsNullOrEmpty(name) || CultureInfo.InvariantCulture.CompareInfo.IndexOf(
                r.DisplayName, name, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)
            .Where(r => filter.Unidad is null || r.UnitId.Value == filter.Unidad)
            .ToList();
        var units = all
            .GroupBy(r => r.UnitId.Value)
            .Select(g => (g.Key, g.First().UnitName))
            .OrderBy(u => u.Item2)
            .ToList();
        return new(shown, all.Count, filter, units, today);
    }
}

/// <summary>ADM-03: la ficha administrativa y la fecha de hoy, para la edad.</summary>
public sealed record AdministrativeResidentViewModel(AdministrativeResidentDetail Detail, DateOnly Today);

/// <summary>ADM-03 (0021): formulario de corrección de identidad. CorreccionesEsperadas es cuántas correcciones tenía el
/// residente al abrirlo: si otra se adelanta, la corrección da conflicto y se conserva lo escrito.</summary>
public sealed class CorrectIdentityFormModel
{
    public Guid ResidenteId { get; set; }

    public int CorreccionesEsperadas { get; set; }

    [Required(ErrorMessage = "Escribe el nombre.")]
    [StringLength(ResidentIdentityCorrection.MaxDisplayNameLength, ErrorMessage = "El nombre no puede pasar de {1} caracteres.")]
    [Display(Name = "Nombre")]
    public string? NombreVisible { get; set; }

    [Required(ErrorMessage = "Indica la fecha de nacimiento.")]
    [DataType(DataType.Date)]
    [Display(Name = "Fecha de nacimiento")]
    public DateOnly? FechaNacimiento { get; set; }

    [Display(Name = "Sexo documentado")]
    public DocumentedSexCode SexoDocumentado { get; set; }

    [Required(ErrorMessage = "Escribe el motivo de la corrección.")]
    [StringLength(ResidentIdentityCorrection.MaxReasonLength, ErrorMessage = "El motivo no puede pasar de {1} caracteres.")]
    [Display(Name = "Motivo de la corrección")]
    public string? Motivo { get; set; }
}

/// <summary>ADM-03: la ficha vigente junto al formulario de corrección.</summary>
public sealed record CorrectIdentityViewModel(AdministrativeResidentSummary Resident, CorrectIdentityFormModel Form);

public static class AdministrativeResidentDisplay
{
    /// <summary>RES-03: la edad se calcula desde la fecha de nacimiento y la de hoy; no se guarda.</summary>
    public static int Age(DateOnly birthDate, DateOnly today)
    {
        var age = today.Year - birthDate.Year;
        return birthDate.AddYears(age) > today ? age - 1 : age;
    }

    public static string Identity(ResidentIdentity identity) =>
        $"{identity.DisplayName} · {identity.BirthDate:dd/MM/yyyy} · {EnumDisplay.Label(identity.DocumentedSex)}";
}
