using System.Data;
using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;

namespace ResidApp.Infrastructure.Authorization;

/// <summary>
/// Traduce la intención de bindAuthorizedDatabase en db/repositories/authorized-d1.ts: antes de escribir,
/// se re-resuelve la evidencia de autorización DENTRO de la misma transacción y se compara su huella con
/// la capturada en el momento de autorizar (RequestAuthorizationContextResolver.ResolveAsync). Si difiere
/// — alguien revocó o cambió el grant entre la resolución y la escritura — se deniega sin distinguir
/// causa, igual que el original.
/// </summary>
public static class AuthorizationGuard
{
    public static async Task VerifyUnchangedAsync(
        IDbConnection connection, IDbTransaction transaction, string externalSubject,
        AuthorizationSelection selection, AuthorizationTarget target, string expectedFingerprint, CancellationToken ct)
    {
        var current = await SqlAuthorizationEvidenceProvider.LoadAsync(connection, transaction, externalSubject, selection, target, ct);
        if (current is null || EvidenceFingerprint.Of(current) != expectedFingerprint)
        {
            throw new AccessDeniedException();
        }
    }
}
