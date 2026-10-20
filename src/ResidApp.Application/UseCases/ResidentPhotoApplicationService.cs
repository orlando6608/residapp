using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record SetResidentPhotoCommand(
    Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, byte[] Foto, bool Consentimiento);

/// <summary>
/// Foto de identificación del residente (estudio de mercado, R2; script 0059). Suposiciones a confirmar con CJ en
/// foto-residente-confirmaciones-cj.html: la ven Auxiliar, Enfermería, Medicina y Administración, nunca la familia ni Dirección Clínica; la
/// ponen, cambian y quitan Administración y Enfermería, sin permiso aparte, con el consentimiento marcado; verla no se audita (tampoco las
/// listas en las que sale).
/// El ámbito se comprueba directamente con la evidencia de autorización (AuthorizationTarget.Read), la misma regla que siguen las listas de
/// cada perfil (Auxiliar, solo sus residentes asignados), en vez de RequestAuthorizationContextResolver: la foto no es ninguna de las
/// acciones de ResidentBaselinePolicy, y la lectura de esta se reserva a Dirección Clínica y al basal.
/// </summary>
public sealed class ResidentPhotoApplicationService(
    IAuthorizationEvidenceProvider evidenceProvider, ISessionIdentityProvider session, IResidentPhotoRepository photos)
{
    private static readonly SystemProfile[] Viewers =
        [SystemProfile.Auxiliar, SystemProfile.Enfermeria, SystemProfile.Medicina, SystemProfile.Administracion];

    private static readonly SystemProfile[] Editors = [SystemProfile.Administracion, SystemProfile.Enfermeria];

    /// <summary>El JPEG de la foto; null si el residente no tiene.</summary>
    public Task<ApplicationResult<byte[]?>> ReadAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var target = await RequireAsync(profileScopeId, centerId, residentId, Viewers, ct);
            return await photos.ReadAsync(target.CenterId, target.ResidentId, ct);
        });

    public Task<ApplicationResult<bool>> SetAsync(SetResidentPhotoCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            if (!command.Consentimiento)
            {
                throw new DomainValidationException(ResidentPhoto.InvalidCode);
            }
            var content = ResidentPhoto.Normalize(command.Foto);
            var target = await RequireAsync(command.AmbitoPerfilId, command.CentroId, command.ResidenteId, Editors, ct);
            await photos.SetAsync(target, content, ct);
            return true;
        });

    /// <summary>Devuelve false si el residente no tenía foto.</summary>
    public Task<ApplicationResult<bool>> RemoveAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var target = await RequireAsync(profileScopeId, centerId, residentId, Editors, ct);
            return await photos.RemoveAsync(target, ct);
        });

    private async Task<ResidentPhotoTarget> RequireAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, SystemProfile[] allowed, CancellationToken ct)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct);
        if (identity is null || string.IsNullOrWhiteSpace(identity.ExternalSubject))
        {
            throw new AccessDeniedException();
        }
        var evidence = await evidenceProvider.LoadEvidenceAsync(
            identity.ExternalSubject, new AuthorizationSelection(profileScopeId, centerId), new AuthorizationTarget.Read(residentId), ct);
        if (evidence?.ResidentId is null || !allowed.Contains(evidence.Profile))
        {
            throw new AccessDeniedException();
        }
        return new ResidentPhotoTarget(evidence.AccountId, evidence.Profile, evidence.CenterId, evidence.UnitId, evidence.ResidentId.Value);
    }
}
