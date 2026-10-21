namespace ResidApp.Web.Models;

/// <summary>Pantalla del acceso de desarrollo: la cuenta de la sesión actual, si la hay, y si hace falta la clave de desarrollo.</summary>
public sealed record DevAuthLoginViewModel(string? CurrentSubject, bool RequiresKey, string? ReturnUrl);
