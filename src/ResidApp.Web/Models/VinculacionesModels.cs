using System.ComponentModel.DataAnnotations;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Web.Models;

/// <summary>Vincular una cuenta que ya existe en otro centro (ADR 0007, I2b): el perfil y las unidades que concede la Administración.
/// No lleva ninguna cuenta: la persona la canjea con el código de la hoja.</summary>
public sealed class NewAccountLinkFormModel
{
    [Required(ErrorMessage = "Elige el perfil.")]
    [Display(Name = "Perfil")]
    public SystemProfile? Perfil { get; set; }

    [Display(Name = "Unidades")]
    public List<Guid> Unidades { get; set; } = [];
}

/// <summary>Las vinculaciones abiertas del centro (sin canjear, o canjeadas y pendientes de confirmar) y el formulario de una nueva.</summary>
public sealed record AccountLinksViewModel(
    IReadOnlyList<AccountLinkSummary> Open, NewAccountLinkFormModel Form, IReadOnlyList<ScopeUnit> Units, IReadOnlyList<SystemProfile> Profiles);

/// <summary>La hoja que se entrega a la persona: Direccion lleva el token y solo se enseña esta vez.</summary>
public sealed record HojaVinculacionViewModel(
    string Centro, SystemProfile Perfil, IReadOnlyList<string> Unidades, DateTime CaducaEnUtc, string Direccion, string Codigo, string Volver);

/// <summary>Lo que ve la persona antes de aceptar. Token vuelve en el formulario, no en la dirección.</summary>
public sealed record AceptarVinculacionViewModel(LiveAccountLink Vinculacion, string Token);

/// <summary>Cuentas con perfiles en varios centros, para Plataforma.</summary>
public sealed record PlatformMultiCenterAccountsViewModel(IReadOnlyList<PlatformMultiCenterAccount> Accounts);
