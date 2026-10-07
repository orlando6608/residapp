using System.Security.Cryptography;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>ENF-12/MED-14 "firmar y generar PDF", común a Enfermería (SignReferralReport) y Medicina
/// (SignMedicalReferralReport). SeccionesAutomaticas son los datos reunidos de los registros de origen, que
/// no se editan; HuellaVistaPrevia es la que mostró la vista previa obligatoria (DER-02).</summary>
public sealed record SignReferralReportCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId, int Revision, Guid OperacionId,
    IReadOnlyList<ReferralReportSection> SeccionesAutomaticas, string? Motivo, string? InformacionAdicional, string? HuellaVistaPrevia,
    string? Comunicaciones = null);

/// <summary>DER-06: un intento de llamada al contacto familiar, común a los dos perfiles.</summary>
public sealed record RecordFamilyCallAttemptCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId, int Revision, string? Contacto, DateTimeOffset? LlamadoEn,
    FamilyCallResult? Resultado, string? Nota);

public sealed record FindResidentIdentificationCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId);

public sealed record DownloadReferralReportCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId);

/// <summary>Comprobación de ámbito de la derivación (deny-by-default), igual que MedicinaScope pero para el
/// perfil que se indique: identidad verificada, ámbito activo de ese perfil en el centro y evento visible.</summary>
internal static class ReferralScope
{
    public static async Task<(ActiveProfileScope Scope, string Subject, PendingChangeDetail Event)> RequireAsync(
        IProfileScopeDirectoryProvider scopes, ISessionIdentityProvider session, IChangeInboxDirectory directory,
        IReadOnlyCollection<SystemProfile> profiles, Guid ambitoPerfilId, CenterId centroId, Guid eventoId, CancellationToken ct)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
        var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
        var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == ambitoPerfilId && s.CenterId == centroId);
        if (scope is null || !profiles.Contains(scope.Profile))
        {
            throw new AccessDeniedException();
        }
        var detail = await directory.FindAsync(ambitoPerfilId, centroId, eventoId, ct) ?? throw new AccessDeniedException();
        return (scope, identity.ExternalSubject, detail);
    }

    public static readonly SystemProfile[] Clinical = [SystemProfile.Enfermeria, SystemProfile.Medicina];
}

/// <summary>La firma común a los dos perfiles: compone el informe, exige la huella de la vista previa, genera
/// el PDF con la firma simple y lo guarda. El estado del protocolo del perfil lo exige la BD (un perfil no
/// firma en el protocolo del otro: CLINICAL_EVENT_REVISION_CONFLICT).</summary>
internal static class ReferralSigning
{
    public static async Task<int> SignAsync(
        IProfileScopeDirectoryProvider scopes, ISessionIdentityProvider session, IChangeInboxDirectory directory,
        IReferralReportPdfRenderer renderer, SystemProfile profile, SignReferralReportCommand command,
        Func<SignReferralReportInput, Task<int>> persist, CancellationToken ct)
    {
        var report = new ReferralReportInput(command.Motivo, command.InformacionAdicional, command.Comunicaciones);
        var content = ReferralReportContent.Compose(command.SeccionesAutomaticas, report);

        // Un informe por evento lo exige la BD (REFERRAL_REPORT_ALREADY_SIGNED) después de la idempotencia: repetir
        // la misma firma devuelve su resultado, no un error.
        var (scope, subject, detail) = await ReferralScope.RequireAsync(
            scopes, session, directory, [profile], command.AmbitoPerfilId, command.CentroId, command.EventoId, ct);

        var hash = content.Hash();
        if (hash != command.HuellaVistaPrevia)
        {
            throw new DomainValidationException("REFERRAL_REPORT_CHANGED");
        }

        var signedAt = DateTimeOffset.UtcNow;
        var pdf = renderer.Render(detail.ResidentDisplayName, content, new ReferralReportSignature(profile, subject, signedAt, hash, scope.AccountDisplayName));
        return await persist(new SignReferralReportInput(
            scope.AccountId, command.CentroId, command.EventoId, command.Revision, command.OperacionId, report, content, hash,
            pdf, Convert.ToHexStringLower(SHA256.HashData(pdf)), signedAt));
    }

    public static FamilyCallAttempt ToAttempt(RecordFamilyCallAttemptCommand command) =>
        FamilyCallAttempt.Create(command.Contacto, command.LlamadoEn, command.Resultado, command.Nota, DateTimeOffset.UtcNow);
}

/// <summary>ENF-12: firmar el informe de derivación desde el protocolo urgente de Enfermería. El evento
/// sigue en el protocolo.</summary>
public sealed class SignReferralReport(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session,
    INursingAssessmentRepository repository, IReferralReportPdfRenderer renderer)
{
    public Task<ApplicationResult<int>> ExecuteAsync(SignReferralReportCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(() => ReferralSigning.SignAsync(
            scopes, session, directory, renderer, SystemProfile.Enfermeria, command,
            input => repository.SignReferralReportAsync(input, ct), ct));
}

/// <summary>MED-14: firmar el informe de derivación desde el protocolo urgente de Medicina.</summary>
public sealed class SignMedicalReferralReport(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session,
    IMedicalAssessmentRepository repository, IReferralReportPdfRenderer renderer)
{
    public Task<ApplicationResult<int>> ExecuteAsync(SignReferralReportCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(() => ReferralSigning.SignAsync(
            scopes, session, directory, renderer, SystemProfile.Medicina, command,
            input => repository.SignReferralReportAsync(input, ct), ct));
}

/// <summary>ENF-14/DER-06: registrar un intento de llamada al contacto familiar tras derivar desde Enfermería.</summary>
public sealed class RecordFamilyCallAttempt(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session,
    INursingAssessmentRepository repository)
{
    public Task<ApplicationResult<int>> ExecuteAsync(RecordFamilyCallAttemptCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var attempt = ReferralSigning.ToAttempt(command);
            var (scope, _, _) = await ReferralScope.RequireAsync(
                scopes, session, directory, [SystemProfile.Enfermeria], command.AmbitoPerfilId, command.CentroId, command.EventoId, ct);
            return await repository.RecordFamilyCallAttemptAsync(new RecordFamilyCallAttemptInput(
                scope.AccountId, command.CentroId, command.EventoId, command.Revision, attempt), ct);
        });
}

/// <summary>MED-16/DER-06: registrar un intento de llamada al contacto familiar tras derivar desde Medicina.</summary>
public sealed class RecordMedicalFamilyCallAttempt(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session,
    IMedicalAssessmentRepository repository)
{
    public Task<ApplicationResult<int>> ExecuteAsync(RecordFamilyCallAttemptCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var attempt = ReferralSigning.ToAttempt(command);
            var (scope, _, _) = await ReferralScope.RequireAsync(
                scopes, session, directory, [SystemProfile.Medicina], command.AmbitoPerfilId, command.CentroId, command.EventoId, ct);
            return await repository.RecordFamilyCallAttemptAsync(new RecordFamilyCallAttemptInput(
                scope.AccountId, command.CentroId, command.EventoId, command.Revision, attempt), ct);
        });
}

/// <summary>DER-03: identificación del residente y del centro para componer el informe, desde cualquiera de
/// los dos perfiles.</summary>
public sealed class FindResidentIdentification(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<ResidentIdentification>> ExecuteAsync(FindResidentIdentificationCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            await ReferralScope.RequireAsync(
                scopes, session, directory, ReferralScope.Clinical, command.AmbitoPerfilId, command.CentroId, command.EventoId, ct);
            return await directory.FindResidentIdentificationAsync(command.AmbitoPerfilId, command.CentroId, command.EventoId, ct)
                ?? throw new AccessDeniedException();
        });
}

/// <summary>DER-05: descargar el PDF firmado, desde cualquiera de los dos perfiles que ven el evento. Cada
/// descarga queda auditada.</summary>
public sealed class DownloadReferralReport(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session,
    IReferralReportRepository repository)
{
    public Task<ApplicationResult<ReferralReportPdf>> ExecuteAsync(DownloadReferralReportCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (scope, _, _) = await ReferralScope.RequireAsync(
                scopes, session, directory, ReferralScope.Clinical, command.AmbitoPerfilId, command.CentroId, command.EventoId, ct);
            return await repository.DownloadAsync(
                    scope.AccountId, scope.Profile, command.AmbitoPerfilId, command.CentroId, command.EventoId, ct)
                ?? throw new AccessDeniedException();
        });
}
