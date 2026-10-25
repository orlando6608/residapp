using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record SetResidentPhotoCommand(
    Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, byte[] Foto, ResidentPhotoConsentSigner? Firmante, DateOnly? FechaConsentimiento);

/// <summary>Lo que necesita el recuadro de la foto en una ficha: su estado y si quien mira puede cambiarla.</summary>
public sealed record ResidentPhotoEditorState(ResidentPhotoStatus Status, bool CanEdit);

/// <summary>
/// Foto de identificación del residente (estudio de mercado, R2; scripts 0059 y 0071), con las respuestas de CJ del 2026-10-25
/// (foto-residente-confirmaciones-cj): la ven Auxiliar, Enfermería, Medicina y Administración, nunca la familia ni Dirección Clínica (2.1); la
/// ponen, cambian y quitan Administración, y Enfermería solo con el permiso RESIDENT_PHOTO_MANAGE que da Administración (1.3); se guarda quién
/// firmó el consentimiento y cuándo (1.1); «No quiere foto» la borra y evita que se vuelva a pedir (1.2); verla no se audita (2.4).
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
            var (target, _) = await RequireAsync(profileScopeId, centerId, residentId, Viewers, ct);
            return await photos.ReadAsync(target.CenterId, target.ResidentId, ct);
        });

    /// <summary>El estado de la foto para el recuadro de la ficha (Administración y Enfermería); CanEdit dice si puede cambiarla.</summary>
    public Task<ApplicationResult<ResidentPhotoEditorState>> ReadEditorAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (target, canEdit) = await RequireAsync(profileScopeId, centerId, residentId, Editors, ct);
            return new ResidentPhotoEditorState(await photos.ReadStatusAsync(target.CenterId, target.ResidentId, ct), canEdit);
        });

    public Task<ApplicationResult<bool>> SetAsync(SetResidentPhotoCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var consent = ResidentPhotoConsent.Validate(command.Firmante, command.FechaConsentimiento, DateOnly.FromDateTime(DateTime.Today));
            var content = ResidentPhoto.Normalize(command.Foto);
            var target = await RequireEditorAsync(command.AmbitoPerfilId, command.CentroId, command.ResidenteId, ct);
            await photos.SetAsync(target, content, consent, ct);
            return true;
        });

    /// <summary>Devuelve false si el residente no tenía foto.</summary>
    public Task<ApplicationResult<bool>> RemoveAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await photos.RemoveAsync(await RequireEditorAsync(profileScopeId, centerId, residentId, ct), ct));

    /// <summary>Anota «No quiere foto» y borra la que hubiera. Devuelve false si ya estaba anotado.</summary>
    public Task<ApplicationResult<bool>> MarkRefusalAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await photos.MarkRefusalAsync(await RequireEditorAsync(profileScopeId, centerId, residentId, ct), ct));

    /// <summary>Quita la marca, por ejemplo si cambia de opinión y firma un consentimiento nuevo. Devuelve false si no la tenía.</summary>
    public Task<ApplicationResult<bool>> ClearRefusalAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await photos.ClearRefusalAsync(await RequireEditorAsync(profileScopeId, centerId, residentId, ct), ct));

    private async Task<ResidentPhotoTarget> RequireEditorAsync(Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct)
    {
        var (target, canEdit) = await RequireAsync(profileScopeId, centerId, residentId, Editors, ct);
        return canEdit ? target : throw new AccessDeniedException();
    }

    /// <summary>El residente si el perfil está entre los permitidos y lo tiene en su ámbito; y si puede cambiar la foto: Administración siempre,
    /// Enfermería solo con RESIDENT_PHOTO_MANAGE.</summary>
    private async Task<(ResidentPhotoTarget Target, bool CanEdit)> RequireAsync(
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
        var canEdit = evidence.Profile == SystemProfile.Administracion
            || (evidence.Profile == SystemProfile.Enfermeria && evidence.Permissions.Any(p => p.Code == ProfilePermissions.ResidentPhotoManage));
        return (new ResidentPhotoTarget(
            evidence.AccountId, profileScopeId, evidence.Profile, evidence.CenterId, evidence.UnitId, evidence.ResidentId.Value), canEdit);
    }
}
