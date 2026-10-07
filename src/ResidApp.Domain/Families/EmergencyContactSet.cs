namespace ResidApp.Domain.Families;

/// <summary>
/// Los contactos urgentes de un residente (script 0044): varios a la vez. Se guardan como un historial de designaciones por orden de número y
/// el conjunto vigente es lo que queda al recorrerlas: REEMPLAZAR lo deja solo en ese familiar (o vacío si no lleva ninguno), AGREGAR lo
/// añade y QUITAR lo retira.
/// </summary>
public static class EmergencyContactSet
{
    public const string Replace = "REEMPLAZAR";
    public const string Add = "AGREGAR";
    public const string Remove = "QUITAR";

    /// <summary>Los vínculos vigentes, en el orden en que se designaron.</summary>
    public static IReadOnlyList<Guid> Current(IEnumerable<(string Action, Guid? LinkId)> designationsInOrder)
    {
        var current = new List<Guid>();
        foreach (var (action, linkId) in designationsInOrder)
        {
            switch (action)
            {
                case Add when linkId is { } added && !current.Contains(added):
                    current.Add(added);
                    break;
                case Remove when linkId is { } removed:
                    current.Remove(removed);
                    break;
                case Replace:
                    current.Clear();
                    if (linkId is { } only)
                    {
                        current.Add(only);
                    }

                    break;
            }
        }

        return current;
    }
}
