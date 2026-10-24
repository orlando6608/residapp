using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Platform;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record RenameCenterCommand(Guid AmbitoPerfilId, CenterId CentroId, string? Nombre);

public sealed record SetCenterLogoCommand(Guid AmbitoPerfilId, CenterId CentroId, byte[] Logo);

/// <summary>
/// Administración, datos del propio centro (script 0068): su nombre visible y su logo, que sustituyen a la marca de ResidApp en el encabezado,
/// en la impresión y en los PDF de quien trabaja en el centro. Como el resto de Administración, cada caso de uso exige un ámbito activo de
/// Administración y el repositorio lo repite dentro de la transacción. Plataforma no cambia un centro ya creado (ADR 0006): solo puede
/// subir el logo al darlo de alta.
/// </summary>
public sealed class AdministracionCentroApplicationService(
    AdministrationAccessResolver access, ICenterBrandingDirectory directory, ICenterBrandingRepository writer)
{
    public Task<ApplicationResult<CenterBranding>> GetAsync(AdministracionQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await directory.FindAsync(grant.CenterId, ct) ?? throw new AccessDeniedException();
        });

    public Task<ApplicationResult<bool>> RenameAsync(RenameCenterCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await writer.RenameAsync(grant, NewCenter.ValidateName(command.Nombre), ct);
            return true;
        });

    public Task<ApplicationResult<bool>> SetLogoAsync(SetCenterLogoCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var logo = CenterLogo.Normalize(command.Logo);
            var (grant, _) = await access.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await writer.SetLogoAsync(grant, logo, ct);
            return true;
        });

    /// <summary>Devuelve false si el centro no tenía logo.</summary>
    public Task<ApplicationResult<bool>> RemoveLogoAsync(AdministracionQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await writer.RemoveLogoAsync(grant, ct);
        });
}
