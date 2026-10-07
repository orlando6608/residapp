using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Cuánto dura una declaración de acceso clínico (CJ, 2026-10-06: 1 hora, ajustable más adelante según el centro).</summary>
public sealed record ClinicalAccessSettings(int DeclarationMinutes)
{
    public const int MaxJustificationLength = 300;

    public static ClinicalAccessSettings Default { get; } = new(60);
}

/// <summary>Traduce los campos de entrada de resident-baseline-service.ts::readDirectionBaseline. Proposito
/// viaja como texto libre (igual que en TS): la política es quien decide si es una finalidad válida, no se
/// pre-valida ni se fija aquí, para que ACCESS_PURPOSE_REQUIRED siga siendo una ruta alcanzable. Justificacion es
/// obligatoria (CJ, 2026-10-06) salvo que DeclaracionId sea una declaración vigente del residente, que ya trae la suya.</summary>
public sealed record ReadDirectionBaselineCommand(
    Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, string TipoRecurso, string? Proposito, Guid OperacionId,
    string? Justificacion = null, Guid? DeclaracionId = null);

/// <summary>Traduce readDirectionBaseline de lib/application/resident-baseline-service.ts.</summary>
public sealed class ReadDirectionBaseline(
    IAuthorizationEvidenceProvider evidenceProvider, ISessionIdentityProvider session, IBaselineRepository repository,
    ClinicalAccessSettings? settings = null)
{
    private readonly ClinicalAccessSettings _settings = settings ?? ClinicalAccessSettings.Default;

    public Task<ApplicationResult<IReadOnlyList<AuditedBaselineHeader>>> ExecuteAsync(
        ReadDirectionBaselineCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            if (!EnumCode.TryParseCode<ClinicalResourceType>(command.TipoRecurso, out var resourceType))
            {
                throw new AccessDeniedException();
            }
            var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();

            // Una declaración vigente de esta cuenta, ámbito y residente aporta finalidad y justificación; si ya no vale
            // (caducó, terminó o es de otro residente), se pide declarar de nuevo.
            var declaration = command.DeclaracionId is { } declarationId
                ? await repository.FindActiveAccessDeclarationAsync(
                    identity.ExternalSubject, declarationId, command.AmbitoPerfilId, command.ResidenteId, ct)
                : null;
            var purpose = declaration?.Purpose
                ?? (command.Proposito is not null && EnumCode.TryParseCode<ClinicalDetailAccessPurpose>(command.Proposito, out var parsedPurpose)
                    ? parsedPurpose
                    : (ClinicalDetailAccessPurpose?)null);

            var selection = new AuthorizationSelection(command.AmbitoPerfilId, command.CentroId);
            var context = await RequestAuthorizationContextResolver.ResolveAsync(
                evidenceProvider, session, selection, new AuthorizationTarget.Read(command.ResidenteId), resourceType, purpose, ct);

            // Ya autorizado: la justificación se exige siempre (sin copiar información clínica ni datos personales innecesarios).
            var justification = declaration?.Justification ?? command.Justificacion?.Trim();
            if (string.IsNullOrEmpty(justification) || justification.Length > ClinicalAccessSettings.MaxJustificationLength)
            {
                throw new DomainValidationException("CLINICAL_DETAIL_READ_INPUT_INVALID");
            }

            var payload = new ClinicalDirectionReadPayload(command.OperacionId, justification, declaration?.Id, _settings.DeclarationMinutes);
            return await RequestAuthorizationContextResolver.ExecuteDirectionBaselineReadAsync(context, repository, payload, ct);
        });
}

/// <summary>La declaración de acceso clínico de Dirección fuera de la lectura: consultar la vigente de un residente (para no
/// pedir de nuevo finalidad y justificación) y terminarlas al cambiar de ámbito o cerrar sesión (CJ, 2026-10-06).</summary>
public sealed class ClinicalAccessDeclarations(ISessionIdentityProvider session, IBaselineRepository repository)
{
    public Task<ApplicationResult<ClinicalAccessDeclaration?>> FindActiveAsync(
        Guid profileScopeId, ResidentId residentId, Guid declarationId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
            return await repository.FindActiveAccessDeclarationAsync(identity.ExternalSubject, declarationId, profileScopeId, residentId, ct);
        });

    /// <summary>Sin identidad no hay nada que terminar; nunca falla la salida ni el cambio de ámbito por esto.</summary>
    public async Task EndAllAsync(CancellationToken ct = default)
    {
        try
        {
            var identity = await session.GetVerifiedIdentityAsync(ct);
            if (identity is not null)
            {
                await repository.EndAccessDeclarationsAsync(identity.ExternalSubject, ct);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // La cookie de la declaración se borra igualmente y la declaración caduca sola a la hora.
        }
    }
}
