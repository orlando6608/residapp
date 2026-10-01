using ResidApp.Domain.Accounts;
using ResidApp.Domain.Structure;
using ResidApp.Shared;

namespace ResidApp.Domain.Platform;

/// <summary>El centro reservado «Plataforma» (script 0026), al que pertenece el ámbito del operador de plataforma. No tiene
/// unidades ni residentes, y en él solo vale el perfil Plataforma.</summary>
public static class PlatformCenter
{
    public static readonly Guid Id = new("5F3A1C00-0000-4000-8000-000000000001");
    public const string Code = "PLATAFORMA";
}

/// <summary>Un centro nuevo con su primera unidad y la cuenta de su primer administrador.</summary>
public sealed record NewCenterData(
    string CenterCode, string CenterName, CenterUnitData Unit, ProfessionalAccountData Administrator);

/// <summary>
/// Reglas del alta de un centro por el operador de plataforma. El código del centro sigue las del código de una unidad (de 2 a
/// 64 caracteres: letras sin acentos, dígitos, «-» y «_», en mayúsculas); el nombre, de 1 a 200. El código reservado no se
/// puede usar. La unidad y el administrador siguen las reglas de CenterUnit y ProfessionalAccount.
/// </summary>
public static class NewCenter
{
    public const string InvalidCode = "CENTER_INVALID";

    public static NewCenterData Validate(
        string? centerCode, string? centerName, string? unitCode, string? unitName, string? adminSubject, string? adminName)
    {
        try
        {
            var center = CenterUnit.Validate(centerCode, centerName);
            if (center.Code == PlatformCenter.Code)
            {
                throw new DomainValidationException(InvalidCode);
            }

            return new NewCenterData(
                center.Code, center.Name, CenterUnit.Validate(unitCode, unitName), ProfessionalAccount.Validate(adminSubject, adminName));
        }
        catch (DomainValidationException error) when (error.Message is CenterUnit.InvalidCode or ProfessionalAccount.InvalidCode)
        {
            throw new DomainValidationException(InvalidCode);
        }
    }
}
