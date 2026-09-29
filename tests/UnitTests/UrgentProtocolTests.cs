using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

public class UrgentProtocolTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Activacion_SinNota_SeAcepta_YConNotaLaRecorta()
    {
        Assert.Null(new UrgentProtocolActivation("   ").Note);
        Assert.Equal("Desaturación brusca.", new UrgentProtocolActivation("  Desaturación brusca. ").Note);
    }

    [Fact]
    public void Activacion_NotaDemasiadoLarga_SeRechaza()
    {
        var ex = Assert.Throws<DomainValidationException>(() => new UrgentProtocolActivation(new string('a', UrgentProtocolActivation.MaxNoteLength + 1)));
        Assert.Equal("URGENT_PROTOCOL_ACTIVATION_INVALID", ex.Message);
    }

    [Fact]
    public void Registros_ConSusDatos_SeAceptan()
    {
        Assert.Equal("Oxigenoterapia.", UrgentProtocolEntry.Action(" Oxigenoterapia. ").Text);
        Assert.Equal(UrgentProtocolEntryType.Evolucion, UrgentProtocolEntry.Evolution("Mejora.").Type);

        var contact = UrgentProtocolEntry.Contact(" 112 ", Now.AddMinutes(-30), "  ", Now);
        Assert.Equal("112", contact.Service);
        Assert.Equal(Now.AddMinutes(-30), contact.ContactedAt);
        Assert.Null(contact.Text);

        // Unos minutos de desfase entre relojes se admiten.
        Assert.NotNull(UrgentProtocolEntry.Contact("112", Now.AddMinutes(4), null, Now).ContactedAt);
    }

    public static TheoryData<Func<UrgentProtocolEntry>> RegistrosIncompletos => new()
    {
        () => UrgentProtocolEntry.Action(" "),
        () => UrgentProtocolEntry.Evolution(null),
        () => UrgentProtocolEntry.Action(new string('a', UrgentProtocolEntry.MaxTextLength + 1)),
        () => UrgentProtocolEntry.Contact(" ", Now, null, Now),
        () => UrgentProtocolEntry.Contact("112", null, null, Now),
        () => UrgentProtocolEntry.Contact("112", Now.AddMinutes(10), null, Now),
        () => UrgentProtocolEntry.Contact(new string('a', UrgentProtocolEntry.MaxServiceLength + 1), Now, null, Now),
    };

    [Theory]
    [MemberData(nameof(RegistrosIncompletos))]
    public void Registros_Incompletos_SeRechazan(Func<UrgentProtocolEntry> build)
    {
        var ex = Assert.Throws<DomainValidationException>(() => build());
        Assert.Equal("URGENT_PROTOCOL_ENTRY_INVALID", ex.Message);
    }
}
