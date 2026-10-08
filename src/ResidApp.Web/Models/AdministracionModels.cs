using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc.Rendering;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Families;
using ResidApp.Domain.Residents;
using ResidApp.Domain.Structure;
using ResidApp.Shared;

namespace ResidApp.Web.Models;

/// <summary>ADM-01: el inicio de Administración, con el número de residentes activos del ámbito y el de cuentas con
/// algún perfil en el centro.</summary>
public sealed record AdministracionInicioViewModel(int Residents, int Accounts);

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
public sealed record AdministrativeResidentViewModel(
    AdministrativeResidentDetail Detail, DateOnly Today, ResidentSuspension? Suspension = null);

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

/// <summary>ADM-09 (0022): formulario de un familiar, para añadirlo (OperacionId, que nace con el formulario y evita
/// duplicarlo al reenviar) o para editarlo (VinculoId).</summary>
public sealed class FamilyMemberFormModel
{
    public Guid ResidenteId { get; set; }

    public Guid OperacionId { get; set; }

    public Guid? VinculoId { get; set; }

    /// <summary>Solo al editar: FamilyMemberData.Version de los datos con que se abrió el formulario.</summary>
    public string? Version { get; set; }

    [Required(ErrorMessage = "Escribe el nombre.")]
    [StringLength(FamilyMember.MaxDisplayNameLength, ErrorMessage = "El nombre no puede pasar de {1} caracteres.")]
    [Display(Name = "Nombre")]
    public string? NombreVisible { get; set; }

    [Required(ErrorMessage = "Escribe la relación con el residente.")]
    [StringLength(FamilyMember.MaxRelationshipLength, ErrorMessage = "La relación no puede pasar de {1} caracteres.")]
    [Display(Name = "Relación con el residente (p. ej., «Hija»)")]
    public string? Relacion { get; set; }

    [Required(ErrorMessage = "Escribe el teléfono.")]
    [StringLength(FamilyMember.MaxPhoneLength, ErrorMessage = "El teléfono no puede pasar de {1} caracteres.")]
    [Display(Name = "Teléfono")]
    public string? Telefono { get; set; }

    [StringLength(FamilyMember.MaxEmailLength, ErrorMessage = "El correo no puede pasar de {1} caracteres.")]
    [EmailAddress(ErrorMessage = "Escribe un correo válido.")]
    [Display(Name = "Correo electrónico (opcional)")]
    public string? Correo { get; set; }

    [Display(Name = "Familiar referente")]
    public bool Referente { get; set; }

    [Display(Name = "Tutor legal")]
    public bool TutorLegal { get; set; }
}

/// <summary>ADM-09: el residente junto al formulario del familiar.</summary>
/// <summary>SharedWith es a cuántos otros residentes está vinculado también el familiar que se edita (sus datos de contacto valen para todos).</summary>
public sealed record FamilyMemberViewModel(AdministrativeResidentSummary Resident, FamilyMemberFormModel Form, int SharedWith = 0);

/// <summary>Vincular a un residente un familiar que ya existe: se elige entre los vinculables y se escribe la relación con este residente.
/// OperacionId nace con el formulario y es el identificador del vínculo.</summary>
public sealed class LinkFamilyFormModel
{
    public Guid ResidenteId { get; set; }

    public Guid OperacionId { get; set; }

    [Required(ErrorMessage = "Elige un familiar.")]
    [Display(Name = "Familiar")]
    public Guid? FamiliarId { get; set; }

    [Required(ErrorMessage = "Escribe la relación con el residente.")]
    [StringLength(FamilyMember.MaxRelationshipLength, ErrorMessage = "La relación no puede pasar de {1} caracteres.")]
    [Display(Name = "Relación con este residente (p. ej., «Hija»)")]
    public string? Relacion { get; set; }
}

public sealed record LinkFamilyViewModel(
    AdministrativeResidentSummary Resident, IReadOnlyList<LinkableFamilyMember> Candidates, LinkFamilyFormModel Form);

/// <summary>ADM-10/ADM-11 (0022): un cambio de la autorización. CambiosEsperados es cuántos cambios tenía al abrir la
/// pantalla: si otro se adelanta, da conflicto.</summary>
public sealed class FamilyAuthorizationFormModel
{
    public Guid ResidenteId { get; set; }

    public Guid VinculoId { get; set; }

    public int CambiosEsperados { get; set; }

    public FamilyAuthorizationChange Cambio { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Válida hasta (opcional; ese día incluido)")]
    public DateOnly? ValidaHasta { get; set; }

    [StringLength(FamilyAuthorizationRules.MaxReasonLength, ErrorMessage = "El motivo no puede pasar de {1} caracteres.")]
    [Display(Name = "Motivo")]
    public string? Motivo { get; set; }
}

/// <summary>ADM-10: la autorización de un familiar, con su estado de hoy y los cambios posibles.</summary>
public sealed record FamilyAuthorizationViewModel(
    AdministrativeResidentSummary Resident, ResidentFamilyMember Member, DateOnly Today, FamilyAuthorizationFormModel Form)
{
    public FamilyAuthorizationStatus? Effective => Member.CurrentAuthorization is { } current
        ? FamilyAuthorizationRules.Effective(current.Status, current.ValidUntil, Today)
        : null;

    public IReadOnlyList<FamilyAuthorizationChange> Allowed => FamilyAuthorizationRules.Allowed(Effective);
}

/// <summary>ADM-08 (0022, 0044): designar los contactos urgentes. VinculosIds son los que quedan (vacío es «sin contacto urgente»);
/// DesignacionesEsperadas es cuántas designaciones tenía el residente al abrir la pantalla.</summary>
public sealed class EmergencyContactFormModel
{
    public Guid ResidenteId { get; set; }

    public int DesignacionesEsperadas { get; set; }

    public List<Guid> VinculosIds { get; set; } = [];
}

public sealed record EmergencyContactViewModel(AdministrativeResidentDetail Detail, EmergencyContactFormModel Form);

public static class AdministrativeResidentDisplay
{
    /// <summary>ADM-08: la autorización tal como cuenta hoy, p. ej. «Activa hasta el 31/10/2026» o «Sin autorización».</summary>
    public static string Authorization(ResidentFamilyMember member, DateOnly today)
    {
        if (member.CurrentAuthorization is not { } current)
        {
            return "Sin autorización";
        }

        var effective = FamilyAuthorizationRules.Effective(current.Status, current.ValidUntil, today);
        return effective switch
        {
            FamilyAuthorizationStatus.Activa when current.ValidUntil is { } until => $"Activa hasta el {until:dd/MM/yyyy}",
            FamilyAuthorizationStatus.Caducada => $"Caducada (fue válida hasta el {current.ValidUntil:dd/MM/yyyy})",
            _ => EnumDisplay.Label(effective),
        };
    }

    /// <summary>RES-03: la edad se calcula desde la fecha de nacimiento y la de hoy; no se guarda.</summary>
    public static int Age(DateOnly birthDate, DateOnly today)
    {
        var age = today.Year - birthDate.Year;
        return birthDate.AddYears(age) > today ? age - 1 : age;
    }

    public static string Identity(ResidentIdentity identity) =>
        $"{identity.DisplayName} · {identity.BirthDate:dd/MM/yyyy} · {EnumDisplay.Label(identity.DocumentedSex)}";
}

/// <summary>ADM-13 (0023): alta de una cuenta profesional con su primer perfil. OperacionId nace con el formulario y es el
/// id de la cuenta: reenviarlo no la duplica.</summary>
public sealed class NewProfessionalAccountFormModel
{
    public Guid OperacionId { get; set; }

    [Required(ErrorMessage = "Escribe el identificador de acceso.")]
    [StringLength(ProfessionalAccount.MaxSubjectLength, MinimumLength = ProfessionalAccount.MinSubjectLength,
        ErrorMessage = "El identificador lleva entre {2} y {1} caracteres.")]
    [Display(Name = "Identificador de acceso")]
    public string? Identificador { get; set; }

    [Required(ErrorMessage = "Escribe el nombre.")]
    [StringLength(ProfessionalAccount.MaxDisplayNameLength, ErrorMessage = "El nombre no puede pasar de {1} caracteres.")]
    [Display(Name = "Nombre")]
    public string? NombreVisible { get; set; }

    [Required(ErrorMessage = "Elige el perfil.")]
    [Display(Name = "Perfil")]
    public SystemProfile? Perfil { get; set; }

    [Display(Name = "Unidades")]
    public List<Guid> Unidades { get; set; } = [];
}

/// <summary>ADM-13: Units son las unidades del ámbito de quien gestiona, las únicas que puede conceder.</summary>
public sealed record NewProfessionalAccountViewModel(NewProfessionalAccountFormModel Form, IReadOnlyList<ScopeUnit> Units);

public sealed class RenameProfessionalAccountFormModel
{
    public Guid CuentaId { get; set; }

    [Required(ErrorMessage = "Escribe el nombre.")]
    [StringLength(ProfessionalAccount.MaxDisplayNameLength, ErrorMessage = "El nombre no puede pasar de {1} caracteres.")]
    [Display(Name = "Nombre")]
    public string? NombreVisible { get; set; }
}

public sealed record RenameProfessionalAccountViewModel(ProfessionalAccountSummary Account, RenameProfessionalAccountFormModel Form);

/// <summary>ADM-13: conceder un perfil más. OperacionId es el id del ámbito nuevo.</summary>
public sealed class GrantAccountProfileFormModel
{
    public Guid CuentaId { get; set; }

    public Guid OperacionId { get; set; }

    [Required(ErrorMessage = "Elige el perfil.")]
    [Display(Name = "Perfil")]
    public SystemProfile? Perfil { get; set; }

    [Display(Name = "Unidades")]
    public List<Guid> Unidades { get; set; } = [];
}

/// <summary>ADM-13: Profiles son los concedibles que la cuenta no tiene vigentes en este centro.</summary>
public sealed record GrantAccountProfileViewModel(
    ProfessionalAccountSummary Account, GrantAccountProfileFormModel Form, IReadOnlyList<SystemProfile> Profiles, IReadOnlyList<ScopeUnit> Units);

/// <summary>ADM-13: un perfil de la cuenta con sus unidades, residentes y permisos. AdministratorUnits son las unidades del ámbito
/// de quien gestiona (las únicas que puede conceder o revocar); CanChange es falso en la propia cuenta, en un perfil
/// revocado y en Familiar.</summary>
public sealed record AccountProfileViewModel(
    ProfessionalAccountDetail Detail, AccountProfileScope Profile, IReadOnlyList<ScopeUnit> AdministratorUnits,
    IReadOnlyList<AssignableResident> AssignableResidents, bool ViewerIsPrincipal = false)
{
    public bool CanChange => !Detail.IsOwnAccount && Profile.Active && ProfessionalAccount.IsGrantable(Profile.Profile);

    public bool Manages(Guid unitId) => AdministratorUnits.Any(u => u.UnitId.Value == unitId);

    public IReadOnlyList<ScopeUnit> AddableUnits => AdministratorUnits
        .Where(u => !Profile.Units.Any(g => g.Active && g.TargetId == u.UnitId.Value))
        .ToList();

    /// <summary>0024: cada permiso del catálogo del perfil con su concesión vigente, o null si no lo tiene.</summary>
    public IReadOnlyList<(string Code, AccountScopeGrant? Grant)> Permissions => ProfilePermissions.For(Profile.Profile)
        .Select(code => (code, Profile.Permissions.FirstOrDefault(p => p.Active && p.Name == code)))
        .ToList();
}

public static class ProfessionalAccountDisplay
{
    /// <summary>El nombre, o el identificador de acceso en las cuentas que aún no lo tienen.</summary>
    public static string Name(ProfessionalAccountSummary account) => account.DisplayName ?? account.Subject;

    public static string Status(AccountStatus status) => status == AccountStatus.Active ? "Activa" : "Suspendida";

    public static string ActiveUnits(AccountProfileScope profile) =>
        string.Join(", ", profile.Units.Where(u => u.Active).Select(u => u.Name));

    public static IEnumerable<SelectListItem> ProfileOptions(IEnumerable<SystemProfile> profiles) =>
        profiles.Select(p => new SelectListItem(SystemProfileDisplay.Label(p), p.ToString()));
}

/// <summary>ADM-13: las casillas de unidades (Form.Unidades) del alta y de «Conceder perfil».</summary>
public sealed record ProfileUnitsFieldModel(IReadOnlyList<ScopeUnit> Units, IReadOnlyList<Guid> Selected);

/// <summary>ADM-05: alta de una unidad. OperacionId es el id de la unidad nueva, así que reenviar no la duplica.</summary>
public sealed class NewUnitFormModel
{
    public Guid OperacionId { get; set; }

    [Required(ErrorMessage = "Escribe el código.")]
    [StringLength(CenterUnit.MaxCodeLength, MinimumLength = CenterUnit.MinCodeLength, ErrorMessage = "El código lleva entre {2} y {1} caracteres.")]
    [Display(Name = "Código")]
    public string? Codigo { get; set; }

    [Required(ErrorMessage = "Escribe el nombre.")]
    [StringLength(CenterUnit.MaxNameLength, ErrorMessage = "El nombre no puede pasar de {1} caracteres.")]
    [Display(Name = "Nombre")]
    public string? Nombre { get; set; }
}

public sealed class RenameUnitFormModel
{
    public Guid UnidadId { get; set; }

    [Required(ErrorMessage = "Escribe el nombre.")]
    [StringLength(CenterUnit.MaxNameLength, ErrorMessage = "El nombre no puede pasar de {1} caracteres.")]
    [Display(Name = "Nombre")]
    public string? Nombre { get; set; }
}

public sealed record RenameUnitViewModel(StructureUnit Unit, RenameUnitFormModel Form);

public sealed record NewUnitViewModel(NewUnitFormModel Form);

/// <summary>CJ, 2026-10-07: desvincular a un familiar de un residente. El motivo es obligatorio.</summary>
public sealed class UnlinkFamilyFormModel
{
    public Guid ResidenteId { get; set; }

    public Guid VinculoId { get; set; }

    [Required(ErrorMessage = "Indica el motivo.")]
    [StringLength(500, ErrorMessage = "El motivo no puede pasar de {1} caracteres.")]
    [Display(Name = "Motivo")]
    public string? Motivo { get; set; }
}

public sealed record UnlinkFamilyViewModel(AdministrativeResidentSummary Resident, ResidentFamilyMember Member, UnlinkFamilyFormModel Form);

/// <summary>CJ, 2026-10-07: las unidades del centro vistas por una Administración y si esta es la principal.</summary>
public sealed record AdministrationScopeViewModel(ResidApp.Application.UseCases.AdministrationScopeView View);
