using ResidApp.Shared;

namespace ResidApp.Web.Models;

public sealed record ProfileScopeOptionViewModel(Guid ProfileScopeId, Guid CenterId, string CenterName, string ProfileLabel);

public sealed class SelectProfileScopeViewModel
{
    public IReadOnlyList<ProfileScopeOptionViewModel> Options { get; init; } = [];
    public string? ReturnUrl { get; init; }
}

public static class SystemProfileDisplay
{
    public static string Label(SystemProfile profile) => profile switch
    {
        SystemProfile.Auxiliar => "Auxiliar",
        SystemProfile.Enfermeria => "Enfermería",
        SystemProfile.Medicina => "Medicina",
        SystemProfile.Familiar => "Familiar",
        SystemProfile.Administracion => "Administración",
        SystemProfile.DireccionClinica => "Dirección Clínica",
        _ => profile.ToString(),
    };
}
