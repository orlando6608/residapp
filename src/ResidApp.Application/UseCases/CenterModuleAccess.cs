using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Modules;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>
/// «¿Tiene este centro este módulo?» (ADR 0015): una sola consulta con tres estados, que se hace en cada petición, sin caché (un apagado se nota en la petición siguiente).
/// La comprobación real va en el caso de uso, con el centro VERIFICADO (el del ámbito de la cuenta de la sesión, no el que dice la cookie), antes de auditar y antes de llamar a un
/// tercero; el atributo <c>[RequiereModulo]</c> del controlador solo oculta y orienta.
/// Orden: sin el interruptor global del proveedor (la clave de Mistral), un módulo con tercero no funciona para nadie aunque la fila diga ACTIVO; después, el ajuste de desarrollo con
/// todo activo; después, el último cambio del centro y su vigencia.
/// </summary>
public sealed class CenterModuleAccess(
    ICenterModuleRepository modules, IModuleSettings settings, IProfileScopeDirectoryProvider scopes, ISessionIdentityProvider session, TimeProvider clock,
    IReadOnlyList<CenterModule>? catalog = null)
{
    public const string NotAvailable = "MODULE_NOT_AVAILABLE";
    public const string ReadOnly = "MODULE_READ_ONLY";

    /// <summary>El estado del módulo en un centro ya verificado por quien llama. Un código que no está en el catálogo es un error de programación.</summary>
    public async Task<ModuleState> StateAsync(CenterId verifiedCenterId, string moduleCode, CancellationToken ct = default)
    {
        var module = (catalog ?? CenterModules.All).FirstOrDefault(m => m.Code == moduleCode) ?? throw new ArgumentException($"Módulo fuera del catálogo: {moduleCode}", nameof(moduleCode));
        if (module.UsesThirdParty && !settings.IsThirdPartyConfigured(module.Code))
        {
            return ModuleState.NotAvailable;
        }

        if (settings.ActivateAll)
        {
            return ModuleState.Active;
        }

        var last = await modules.FindLastAsync(verifiedCenterId, module.Code, ct);
        return CenterModuleRules.Resolve(module, last, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
    }

    /// <summary>El estado en el centro del ámbito activo que declara la cookie, comprobado contra la cuenta de la sesión (el centro sale del ámbito, no de la cookie). Sin sesión o con un ámbito ajeno: no disponible.</summary>
    public async Task<ModuleState> StateForScopeAsync(Guid profileScopeId, string moduleCode, CancellationToken ct = default)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct);
        if (identity is null)
        {
            return ModuleState.NotAvailable;
        }

        var scope = (await scopes.ListActiveAsync(identity.ExternalSubject, ct)).FirstOrDefault(s => s.ProfileScopeId == profileScopeId);
        return scope is null ? ModuleState.NotAvailable : await StateAsync(scope.CenterId, moduleCode, ct);
    }

    /// <summary>Para consultar: no disponible lanza MODULE_NOT_AVAILABLE; activo y solo lectura pasan.</summary>
    public async Task RequireReadableAsync(CenterId verifiedCenterId, string moduleCode, CancellationToken ct = default)
    {
        if (await StateAsync(verifiedCenterId, moduleCode, ct) == ModuleState.NotAvailable)
        {
            throw new DomainStateException(NotAvailable);
        }
    }

    /// <summary>Para crear o editar: no disponible lanza MODULE_NOT_AVAILABLE y solo lectura lanza MODULE_READ_ONLY. Se llama antes de auditar y de llamar a un tercero.</summary>
    public async Task RequireWritableAsync(CenterId verifiedCenterId, string moduleCode, CancellationToken ct = default)
    {
        switch (await StateAsync(verifiedCenterId, moduleCode, ct))
        {
            case ModuleState.NotAvailable:
                throw new DomainStateException(NotAvailable);
            case ModuleState.ReadOnly:
                throw new DomainStateException(ReadOnly);
        }
    }
}
