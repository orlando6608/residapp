namespace ResidApp.Web.Models;

/// <summary>Formulario de la clave de los documentos de CJ. Error es true tras una clave incorrecta.</summary>
public sealed record AccesoCjViewModel(string? ReturnUrl, bool Error);
