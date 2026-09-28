using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Medicina;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record RegisterMedicalIndicationCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId, int Revision,
    string? Texto, DateOnly? FechaPrevista, string? Criterio, string? InformacionAdicional);

/// <summary>MED-06/MED-07 "registrar indicaciones a Enfermería", una de las salidas de la conducta médica:
/// texto, fecha prevista o criterio e información adicional, sin prioridad automática. Exige la valoración
/// médica guardada.</summary>
public sealed class RegisterMedicalIndication(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory,
    ISessionIdentityProvider session, IMedicalAssessmentRepository repository)
{
    public Task<ApplicationResult<int>> ExecuteAsync(RegisterMedicalIndicationCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var indication = new MedicalIndication(
                command.Texto, new FollowUpPlan(command.FechaPrevista, command.Criterio), command.InformacionAdicional);

            var scope = await MedicinaScope.RequireAsync(scopes, session, directory, command.AmbitoPerfilId, command.CentroId, command.EventoId, ct);
            return await repository.RegisterIndicationAsync(
                new RegisterMedicalIndicationInput(scope.AccountId, command.CentroId, command.EventoId, command.Revision, indication), ct);
        });
}
