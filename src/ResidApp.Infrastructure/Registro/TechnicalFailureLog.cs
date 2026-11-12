using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ResidApp.Application.Errors;

namespace ResidApp.Infrastructure.Registro;

/// <summary>
/// Registro técnico de los fallos que <see cref="ApplicationResultRunner"/> convierte en un mensaje genérico (hallazgo A4 de seguridad). Sirve para
/// detectar y reconstruir un incidente (RGPD art. 32 y 33), no para auditar a las personas: esa es la auditoría funcional.
/// Qué se escribe: un acceso denegado (aviso) con su código; un fallo inesperado (error) con el tipo de excepción, el número y el estado de SQL Server,
/// el código si lo hay y el método de ResidApp donde ocurrió. Los conflictos y los datos no válidos son errores funcionales esperados y no se registran.
/// Qué NO se escribe nunca: el mensaje de una excepción salvo que sea, entero, un código en mayúsculas (el mensaje de un error de SQL puede llevar el
/// valor duplicado de una clave, con datos personales), la excepción completa, parámetros, cuenta ni residente. Un acceso denegado lleva además el
/// identificador de la sesión (un número aleatorio, seudónimo: solo la base lo une a una cuenta) y el centro que declara la cookie del ámbito activo
/// (no firmada: es lo que dice la petición, no lo verificado), para atribuir un pico de denegados y saber a qué responsable avisar (RGPD art. 33.2).
/// Lectura del DPO: docs/privacidad/registros-tecnicos-lectura-dpo.md.
/// </summary>
public static class TechnicalFailureLog
{
    /// <summary>Lo que se sabe de la petición en curso. Ninguno de los dos datos identifica a una persona por sí solo.</summary>
    public sealed record RequestContext(Guid? SessionId, Guid? DeclaredCenterId);

    public const string OmittedMessage = "(mensaje omitido)";

    private static readonly Regex CodePattern = new("^[A-Z][A-Z0-9_]{2,80}$", RegexOptions.Compiled);

    /// <summary>Conecta el registro con el runner. Se llama una vez al arrancar. <paramref name="context"/> da la sesión y el centro de la petición en curso
    /// (null fuera de una petición, por ejemplo en una tarea en segundo plano).</summary>
    public static void Register(ILogger logger, Func<RequestContext?>? context = null) =>
        ApplicationResultRunner.FailureObserver = (error, failure) => Write(logger, error, failure, context);

    public static void Write(ILogger logger, Exception error, ApplicationFailure failure, Func<RequestContext?>? context = null)
    {
        switch (failure.Code)
        {
            case ApplicationFailureCode.AccessDenied:
                var request = Context(context);
                logger.LogWarning("Acceso denegado: {Codigo} en {Origen} (sesión {Sesion}, centro declarado {Centro}).",
                    SafeCode(error), Origin(error), request?.SessionId?.ToString("D") ?? "-", request?.DeclaredCenterId?.ToString("D") ?? "-");
                break;
            case ApplicationFailureCode.Unavailable:
                var sql = Find<SqlException>(error);
                logger.LogError("Fallo inesperado: {Tipo}, SQL {SqlNumero}/{SqlEstado}, código {Codigo}, en {Origen}.",
                    error.GetType().Name, sql?.Number ?? 0, sql?.State ?? 0, SafeCode(error), Origin(error));
                break;
        }
    }

    /// <summary>Si obtener el contexto falla, el aviso se escribe igual, sin él.</summary>
    private static RequestContext? Context(Func<RequestContext?>? context)
    {
        try
        {
            return context?.Invoke();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>El mensaje solo si es, entero, un código (los de ResidApp lo son); cualquier otro texto puede llevar datos.</summary>
    private static string SafeCode(Exception error) => CodePattern.IsMatch(error.Message) ? error.Message : OmittedMessage;

    private static T? Find<T>(Exception error) where T : Exception
    {
        for (var current = error; current is not null; current = current.InnerException)
        {
            if (current is T found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>El primer método de ResidApp de la pila ("Clase.Metodo"), sin argumentos ni valores. Los métodos asíncronos y las lambdas se
    /// muestran con el nombre del método que los contiene, no con el de la clase que genera el compilador.</summary>
    private static string Origin(Exception error)
    {
        foreach (var frame in new StackTrace(error, false).GetFrames())
        {
            var method = frame.GetMethod();
            var holder = method?.DeclaringType;
            if (method is null || holder is null || holder.Assembly.GetName().Name?.StartsWith("ResidApp", StringComparison.Ordinal) != true)
            {
                continue;
            }

            var name = Clean(method.Name);
            while (holder.Name.StartsWith('<') && holder.DeclaringType is { } outer)
            {
                if (name is "MoveNext" or "")
                {
                    name = Clean(holder.Name);
                }

                holder = outer;
            }

            return $"{holder.Name}.{(name.Length == 0 ? method.Name : name)}";
        }

        return "desconocido";
    }

    /// <summary>"&lt;Metodo&gt;d__3" y "&lt;Metodo&gt;b__0_1" pasan a "Metodo"; un nombre normal se devuelve igual.</summary>
    private static string Clean(string name)
    {
        if (!name.StartsWith('<'))
        {
            return name;
        }

        var end = name.IndexOf('>');
        return end > 1 ? name[1..end] : "";
    }
}
