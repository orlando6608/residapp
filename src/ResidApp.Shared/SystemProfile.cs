namespace ResidApp.Shared;

/// <summary>Traduce lib/domain/access/profiles.ts. Los 6 perfiles del sistema.</summary>
public enum SystemProfile
{
    [Code("AUXILIAR")] Auxiliar,
    [Code("ENFERMERIA")] Enfermeria,
    [Code("MEDICINA")] Medicina,
    [Code("FAMILIAR")] Familiar,
    [Code("ADMINISTRACION")] Administracion,
    [Code("DIRECCION_CLINICA")] DireccionClinica,
}

/// <summary>
/// Traduce `ClinicalProfessionalProfile = Extract&lt;SystemProfile, "ENFERMERIA" | "MEDICINA"&gt;`: en TS es
/// un subconjunto a nivel de tipos; aquí es un enum propio más pequeño, con conversión explícita hacia y
/// desde SystemProfile (nunca implícita) para que el compilador obligue a comprobar el subconjunto.
/// </summary>
public enum ClinicalProfessionalProfile
{
    [Code("ENFERMERIA")] Enfermeria,
    [Code("MEDICINA")] Medicina,
}

public static class ClinicalProfessionalProfileExtensions
{
    public static SystemProfile ToSystemProfile(this ClinicalProfessionalProfile profile) => profile switch
    {
        ClinicalProfessionalProfile.Enfermeria => SystemProfile.Enfermeria,
        ClinicalProfessionalProfile.Medicina => SystemProfile.Medicina,
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };

    public static bool TryFromSystemProfile(SystemProfile profile, out ClinicalProfessionalProfile clinical)
    {
        switch (profile)
        {
            case SystemProfile.Enfermeria:
                clinical = ClinicalProfessionalProfile.Enfermeria;
                return true;
            case SystemProfile.Medicina:
                clinical = ClinicalProfessionalProfile.Medicina;
                return true;
            default:
                clinical = default;
                return false;
        }
    }
}
