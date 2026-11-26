using System.Reflection;
using ResidApp.Application.Ports;

namespace ResidApp.UnitTests;

/// <summary>La baja de avisos por familiar (script 0123) corta los avisos y no los correos de seguridad. Una plantilla nueva tiene que clasificarse aquí: si es un aviso,
/// su envío debe mirar la baja y tener su prueba.</summary>
public class BajaAvisosPlantillasTests
{
    private static readonly string[] Avisos =
    [
        EmailLog.ProposalTemplate, EmailLog.ChangeTemplate, EmailLog.ReminderTemplate, EmailLog.ExternalNoticeTemplate, EmailLog.ExternalNoticeUncoveredTemplate,
    ];

    private static readonly string[] Seguridad = [EmailLog.EmailChangeTemplate];

    [Fact]
    public void CadaPlantillaDelRegistroDeEnvios_EstaClasificadaComoAvisoOComoSeguridad()
    {
        var templates = typeof(EmailLog).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.Name.EndsWith("Template", StringComparison.Ordinal))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Order().ToArray();

        Assert.Equal(Avisos.Concat(Seguridad).Order().ToArray(), templates);
    }
}
