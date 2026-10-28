using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>El motivo de la petición del historial tras un traslado (CJ, 2026-10-28, 3.2 B): una finalidad de la lista cerrada y un texto corto, obligatorio solo con «Otro motivo».</summary>
public class TransferHistoryRequestTests
{
    [Theory]
    [InlineData(TransferHistoryPurpose.ContinuidadAsistencial, "Revisión de continuidad asistencial")]
    [InlineData(TransferHistoryPurpose.IncidenciaReclamacion, "Revisión de una incidencia o reclamación asistencial")]
    [InlineData(TransferHistoryPurpose.TrazabilidadDocumental, "Verificación de trazabilidad documental")]
    public void UnaFinalidadDeLaListaValeSinTexto_YElMotivoEsSuEtiqueta(TransferHistoryPurpose purpose, string label)
    {
        foreach (var text in new string?[] { null, "", "   " })
        {
            var reason = TransferHistoryRequest.Normalize(purpose, text);

            Assert.Equal(purpose, reason.Purpose);
            Assert.Null(reason.Text);
            Assert.Equal(label, reason.Reason);
        }
    }

    [Fact]
    public void ConTexto_ElMotivoLleva_LaEtiqueta_YElTextoSinEspaciosSobrantes()
    {
        var reason = TransferHistoryRequest.Normalize(TransferHistoryPurpose.ContinuidadAsistencial, "  Tras el traslado.  ");

        Assert.Equal("Tras el traslado.", reason.Text);
        Assert.Equal("Revisión de continuidad asistencial — Tras el traslado.", reason.Reason);
    }

    [Fact]
    public void OtroMotivo_ExigeTexto_YSinFinalidadCuentaComoOtroMotivo()
    {
        Assert.Throws<DomainValidationException>(() => TransferHistoryRequest.Normalize(TransferHistoryPurpose.Otro, "  "));
        Assert.Throws<DomainValidationException>(() => TransferHistoryRequest.Normalize(null, null));
        Assert.Equal("Otro motivo — Aclarar una duda.", TransferHistoryRequest.Normalize(null, "Aclarar una duda.").Reason);
    }

    [Fact]
    public void ElTextoNoPasaDelLimite_YUnaFinalidadFueraDeLaListaSeRechaza()
    {
        var exact = new string('x', TransferHistoryRequest.MaxTextLength);

        Assert.Equal(exact, TransferHistoryRequest.Normalize(TransferHistoryPurpose.Otro, exact).Text);
        Assert.Throws<DomainValidationException>(() => TransferHistoryRequest.Normalize(TransferHistoryPurpose.Otro, exact + "x"));
        Assert.Throws<DomainValidationException>(() => TransferHistoryRequest.Normalize((TransferHistoryPurpose)99, "Texto."));
    }
}
