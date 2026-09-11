using System.Security.Cryptography;
using System.Text;
using ResidApp.Domain.Baseline;
using ResidApp.Shared;

namespace ResidApp.Application.Authorization;

/// <summary>Traduce AuthorizationTarget de db/repositories/authorization-subject-repository.ts.</summary>
public abstract record AuthorizationTarget
{
    public sealed record Create(UnitId UnitId) : AuthorizationTarget;
    public sealed record Read(ResidentId ResidentId) : AuthorizationTarget;
    public sealed record Sign(ResidentId ResidentId, BaselineDraftId DraftId) : AuthorizationTarget;
}

/// <summary>Traduce AuthorizationSelection de authorization-subject-repository.ts. profileScopeId viaja
/// como Guid plano (no se le da un tipo bránded propio; es idéntico al criterio adoptado para
/// operationId).</summary>
public sealed record AuthorizationSelection(Guid ProfileScopeId, CenterId CenterId);

public sealed record AuthorizationPermission(Guid Id, string Code);

/// <summary>Traduce AuthorizationEvidence de authorization-subject-repository.ts. En el TS original se
/// materializa como un único JSON construido en SQL (json_object); aquí IAuthorizationEvidenceProvider lo
/// resuelve con dos consultas Dapper (fila + permisos) y devuelve directamente este tipo, sin paso
/// intermedio por JSON.</summary>
public sealed record AuthorizationEvidence(
    AccountId AccountId, Guid ProfileScopeId, SystemProfile Profile, CenterId CenterId, UnitId UnitId, Guid UnitScopeId,
    ResidentId? ResidentId, Guid? LocationId, Guid? ResidentScopeId,
    IReadOnlyList<AuthorizationPermission> Permissions, BaselineReason? DraftReason);

/// <summary>
/// Traduce el guard TOCTOU de db/repositories/authorized-d1.ts (bindAuthorizedDatabase): una huella
/// determinista de la evidencia resuelta, para volver a compararla dentro de la misma transacción justo
/// antes de escribir.
/// </summary>
public static class EvidenceFingerprint
{
    public static string Of(AuthorizationEvidence evidence)
    {
        var permissions = string.Join(",", evidence.Permissions
            .OrderBy(p => p.Code, StringComparer.Ordinal)
            .ThenBy(p => p.Id)
            .Select(p => $"{p.Id}:{p.Code}"));
        var canonical = string.Join('|',
            evidence.AccountId.Value, evidence.ProfileScopeId, evidence.Profile,
            evidence.CenterId.Value, evidence.UnitId.Value, evidence.UnitScopeId,
            evidence.ResidentId?.Value.ToString() ?? "", evidence.LocationId?.ToString() ?? "",
            evidence.ResidentScopeId?.ToString() ?? "", permissions, evidence.DraftReason?.ToString() ?? "");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
