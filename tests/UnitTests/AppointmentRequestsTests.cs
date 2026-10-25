using ResidApp.Domain.Appointments;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>La solicitud previa de citas (script 0057): lo que valida el dominio antes de tocar la base de datos.</summary>
public sealed class AppointmentRequestsTests
{
    private static AppointmentRequestInput Valid(string? comment = null, IEnumerable<int>? days = null, AppointmentDayPart part = AppointmentDayPart.Indiferente) =>
        AppointmentRequestInput.Validate(AppointmentTeam.Medico, AppointmentModality.Telefonica, AppointmentRequestReason.Resultados, comment, days, part);

    [Fact]
    public void LaSolicitud_UnificaYOrdenaLosDias_RecortaElComentario_YUnComentarioEnBlancoEsNinguno()
    {
        var request = Valid("  Quiero hablar de los análisis.  ", [5, 1, 5, 3], AppointmentDayPart.Tarde);

        Assert.Equal([1, 3, 5], request.Availability.Days);
        Assert.Equal("1,3,5", request.Availability.DaysCode);
        Assert.Equal(AppointmentDayPart.Tarde, request.Availability.DayPart);
        Assert.Equal("Quiero hablar de los análisis.", request.Comment);
        Assert.Null(Valid("   ").Comment);
        Assert.Null(Valid(null).Comment);
        Assert.Equal(string.Empty, Valid(days: null).Availability.DaysCode);
        Assert.Equal(AppointmentTeam.Medico, request.Team);
        Assert.Equal(AppointmentModality.Telefonica, request.Modality);
    }

    [Fact]
    public void LaSolicitud_ExigeEquipoModalidadMotivoYParteDelDia_ConDiasDelUnoAlSiete_YUnComentarioDeHastaTrescientos()
    {
        Assert.Equal(300, AppointmentRequestInput.MaxCommentLength);
        Assert.Equal(300, Valid(new string('a', 300)).Comment!.Length);

        var invalid = new Action[]
        {
            () => Valid(new string('a', 301)),
            () => Valid(days: [0]),
            () => Valid(days: [8]),
            () => Valid(days: [-1, 2]),
            () => AppointmentRequestInput.Validate(null, AppointmentModality.Presencial, AppointmentRequestReason.Otro, null, [], AppointmentDayPart.Manana),
            () => AppointmentRequestInput.Validate(AppointmentTeam.Medico, null, AppointmentRequestReason.Otro, null, [], AppointmentDayPart.Manana),
            () => AppointmentRequestInput.Validate(AppointmentTeam.Medico, AppointmentModality.Presencial, null, null, [], AppointmentDayPart.Manana),
            () => AppointmentRequestInput.Validate(AppointmentTeam.Medico, AppointmentModality.Presencial, AppointmentRequestReason.Otro, null, [], null),
        };
        foreach (var attempt in invalid)
        {
            Assert.Equal("APPOINTMENT_REQUEST_INVALID", Assert.Throws<DomainValidationException>(attempt).Message);
        }
    }

    [Theory]
    [InlineData("", AppointmentDayPart.Indiferente, 2026, 10, 12, 9, 0, true)]
    [InlineData("1,3", AppointmentDayPart.Indiferente, 2026, 10, 12, 9, 0, true)]
    [InlineData("1,3", AppointmentDayPart.Indiferente, 2026, 10, 13, 9, 0, false)]
    [InlineData("7", AppointmentDayPart.Indiferente, 2026, 10, 18, 9, 0, true)]
    [InlineData("", AppointmentDayPart.Manana, 2026, 10, 12, 13, 30, true)]
    [InlineData("", AppointmentDayPart.Manana, 2026, 10, 12, 14, 0, false)]
    [InlineData("", AppointmentDayPart.Tarde, 2026, 10, 12, 14, 0, true)]
    [InlineData("", AppointmentDayPart.Tarde, 2026, 10, 12, 10, 0, false)]
    [InlineData("1", AppointmentDayPart.Tarde, 2026, 10, 12, 16, 0, true)]
    [InlineData("1", AppointmentDayPart.Tarde, 2026, 10, 12, 10, 0, false)]
    public void LaDisponibilidad_CoincideConUnHueco_SegunElDiaYLaParteDelDia(
        string days, AppointmentDayPart part, int year, int month, int day, int hour, int minute, bool expected)
    {
        var availability = AppointmentAvailability.FromCodes(days, part);

        Assert.Equal(expected, availability.Matches(new DateOnly(year, month, day), new TimeOnly(hour, minute)));
    }

    [Fact]
    public void LaPropuesta_Caduca_A48HorasO24HorasAntesDeLaCita_LoQueLlegueAntes()
    {
        var proposedAt = new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

        // Cita dentro de 10 días: gana el límite de 48 horas desde que se propuso.
        var farStart = proposedAt.AddDays(10);
        Assert.Equal(proposedAt.AddHours(48), AppointmentRequestRules.ProposalExpiresAt(proposedAt, farStart));
        Assert.True(AppointmentRequestRules.IsProposalAlive(proposedAt, farStart, proposedAt.AddHours(48).AddTicks(-1)));
        Assert.False(AppointmentRequestRules.IsProposalAlive(proposedAt, farStart, proposedAt.AddHours(48)));

        // Cita dentro de 50 horas: gana el límite de 24 horas antes de la cita.
        var nearStart = proposedAt.AddHours(50);
        Assert.Equal(proposedAt.AddHours(26), AppointmentRequestRules.ProposalExpiresAt(proposedAt, nearStart));
        Assert.True(AppointmentRequestRules.IsProposalAlive(proposedAt, nearStart, proposedAt.AddHours(26).AddTicks(-1)));
        Assert.False(AppointmentRequestRules.IsProposalAlive(proposedAt, nearStart, proposedAt.AddHours(26)));
    }

    [Fact]
    public void ElProfesionalInterno_EsOpcional_SeRecorta_YTieneCienCaracteresComoMaximo()
    {
        Assert.Null(AppointmentRequestRules.NormalizeProfessional(null));
        Assert.Null(AppointmentRequestRules.NormalizeProfessional("   "));
        Assert.Equal("Dra. Ficticia", AppointmentRequestRules.NormalizeProfessional("  Dra. Ficticia "));
        Assert.Equal(100, AppointmentRequestRules.NormalizeProfessional(new string('p', 100))!.Length);

        var error = Assert.Throws<DomainValidationException>(() => AppointmentRequestRules.NormalizeProfessional(new string('p', 101)));
        Assert.Equal("APPOINTMENT_PROPOSAL_INVALID", error.Message);
    }

    [Fact]
    public void LosCodigosDeLosEstados_SonLosQueGuardaLaBaseDeDatos()
    {
        Assert.Equal("PENDIENTE", AppointmentRequestStatus.Pendiente.ToCode());
        Assert.Equal("PROPUESTA", AppointmentRequestStatus.Propuesta.ToCode());
        Assert.Equal("ACEPTADA", AppointmentRequestStatus.Aceptada.ToCode());
        Assert.Equal("CANCELADA", AppointmentRequestStatus.Cancelada.ToCode());
        Assert.Equal("CERRADA", AppointmentRequestStatus.Cerrada.ToCode());
        Assert.Equal("OTRO_HORARIO", AppointmentRequestAction.OtroHorario.ToCode());
        Assert.Equal("INFORMACION_CUIDADOS", AppointmentRequestReason.InformacionCuidados.ToCode());
        Assert.Equal("MANANA", AppointmentDayPart.Manana.ToCode());
    }
}
