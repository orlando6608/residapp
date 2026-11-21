using ResidApp.Application.Errors;
using ResidApp.Application.Intercambio;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Modules;
using ResidApp.Domain.Platform;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>El ámbito activo con el que se opera la plataforma (el de Plataforma, en el centro reservado).</summary>
public sealed record PlatformQuery(Guid AmbitoPerfilId, CenterId CentroId);

/// <summary>Alta de un centro. OperacionId nace con el formulario y es el id del centro, así que un reenvío no lo duplica.</summary>
public sealed record PlatformCenterQuery(Guid AmbitoPerfilId, CenterId CentroId, CenterId CentroDestinoId);

public sealed record SetPlatformAdministrationPrincipalCommand(
    Guid AmbitoPerfilId, CenterId CentroId, CenterId CentroDestinoId, Guid AmbitoDestinoId, bool Principal);

/// <summary>AmbitoDestinoId es el ámbito de Administración (del centro destino) de la cuenta a la que se entrega la invitación.</summary>
public sealed record PlatformAdministrationCommand(Guid AmbitoPerfilId, CenterId CentroId, CenterId CentroDestinoId, Guid AmbitoDestinoId);

/// <summary>I2b: una cuenta con perfiles en varios centros, a la que Plataforma entrega una invitación o suspende.</summary>
public sealed record PlatformAccountCommand(Guid AmbitoPerfilId, CenterId CentroId, AccountId CuentaId);

public sealed record AddUnitToAdministrationCommand(
    Guid AmbitoPerfilId, CenterId CentroId, CenterId CentroDestinoId, Guid AmbitoDestinoId, UnitId UnidadId);

/// <summary>
/// Alta, renovación o baja de un módulo opcional en un centro (ADR 0015). Siempre lleva cuándo y cómo pidió el centro el cambio (lo que prueba el plazo de apagado
/// del anexo). Al activar: vigencia, referencia y versión del anexo y condiciones de privacidad comprobadas; al apagar: el motivo.
/// </summary>
public sealed record ChangeCenterModuleCommand(
    Guid AmbitoPerfilId, CenterId CentroId, CenterId CentroDestinoId, string? ModuloCodigo, bool Activar, DateOnly? VigenteDesde, DateOnly? VigenteHasta,
    string? ReferenciaAnexo, string? VersionAnexo, bool CondicionesComprobadas, ModuleDisableReason? MotivoBaja, DateTimeOffset? SolicitudRecibidaEn,
    ModuleRequestChannel? Canal);

public sealed record CreateCenterCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid OperacionId, string? CodigoCentro, string? NombreCentro, string? CodigoUnidad,
    string? NombreUnidad, string? IdentificadorAdministrador, string? NombreAdministrador, byte[]? Logo = null, string? ZonaHoraria = null,
    CenterServiceType? TipoDeServicio = null);

/// <summary>Corrige la zona horaria de un centro (script 0109). «Confirmado» es lo que Plataforma vio afectado y confirmó; si al guardar es otra cosa, se pide confirmar de nuevo.</summary>
public sealed record ChangeCenterTimeZoneCommand(
    Guid AmbitoPerfilId, CenterId CentroId, CenterId CentroDestinoId, string? ZonaHoraria, CenterTimeZoneChangeReason? Motivo, CenterTimeZoneImpact Confirmado);

/// <summary>Corrige el tipo de servicio de un centro (ADR 0017, script 0113): solo un error de alta y solo con el centro sin personas.</summary>
public sealed record ChangeCenterServiceTypeCommand(
    Guid AmbitoPerfilId, CenterId CentroId, CenterId CentroDestinoId, CenterServiceType? TipoDeServicio);

/// <summary>
/// Fachada del perfil de plataforma (script 0026; 0047): lista de centros, alta de un centro con su primera unidad y su primer
/// administrador. Exige un ámbito activo de la cuenta de la sesión con perfil Plataforma en el centro reservado; cualquier otro
/// perfil o centro es acceso denegado. No entrega ningún dato de residentes ni clínico.
/// </summary>
public sealed class PlatformApplicationService(
    IProfileScopeDirectoryProvider scopes, ISessionIdentityProvider session, IPlatformCenterDirectory directory,
    IPlatformCenterRepository centers, IAccessInvitationRepository invitations, ICenterModuleRepository modules, TimeProvider clock,
    ICenterTimeZoneDirectory timeZoneCatalog, ICenterTimeZoneRepository timeZones, ICenterServiceTypeRepository serviceTypes)
{
    /// <summary>Las zonas horarias que Plataforma ofrece al crear un centro o corregir la suyo.</summary>
    public Task<ApplicationResult<IReadOnlyList<CenterTimeZone>>> ListTimeZonesAsync(PlatformQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync<IReadOnlyList<CenterTimeZone>>(async () =>
        {
            await AccessAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await timeZoneCatalog.ListCatalogAsync(ct);
        });

    /// <summary>La zona de un centro, su historia y lo que afectaría cambiarla ahora.</summary>
    public Task<ApplicationResult<CenterTimeZoneState>> GetCenterTimeZoneAsync(PlatformCenterQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await timeZones.GetAsync(await AccessAsync(query.AmbitoPerfilId, query.CentroId, ct), query.CentroDestinoId, ct));

    /// <summary>Solo Plataforma corrige la zona de un centro, con un motivo de una lista cerrada y habiendo visto lo que se ve afectado; queda en la auditoría.</summary>
    public Task<ApplicationResult<bool>> ChangeCenterTimeZoneAsync(ChangeCenterTimeZoneCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var access = await AccessAsync(command.AmbitoPerfilId, command.CentroId, ct);
            var zone = CenterTimeZoneRules.ValidateZone(command.ZonaHoraria, await timeZoneCatalog.ListCatalogAsync(ct));
            var reason = CenterTimeZoneRules.ValidateReason(command.Motivo);
            await timeZones.ChangeAsync(access, command.CentroDestinoId, zone, reason, command.Confirmado, ct);
            return true;
        });

    /// <summary>ADR 0015: solo Plataforma enciende y apaga un módulo de un centro, a petición del centro. La fila y su auditoría (sin datos) se escriben en una transacción.</summary>
    public Task<ApplicationResult<string>> ChangeCenterModuleAsync(ChangeCenterModuleCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var access = await AccessAsync(command.AmbitoPerfilId, command.CentroId, ct);
            var now = clock.GetUtcNow();
            var change = NewCenterModuleChangeRules.Validate(
                command.CentroDestinoId, PlatformCenter.Id, command.ModuloCodigo, command.Activar, command.VigenteDesde, command.VigenteHasta, command.ReferenciaAnexo,
                command.VersionAnexo, command.CondicionesComprobadas, command.MotivoBaja, command.SolicitudRecibidaEn, command.Canal, now);
            return await modules.AppendAsync(access, change, CenterModuleRules.TodayInMadrid(now), ct);
        });

    /// <summary>Un módulo del catálogo en un centro, tal como lo ve Plataforma: su estado contractual hoy (día de Madrid, sin contar la clave global ni el ajuste de desarrollo) y su historial, el más reciente primero.</summary>
    public sealed record CenterModuleView(CenterModule Module, ModuleState State, PlatformModuleChange? Last, IReadOnlyList<PlatformModuleChange> History);

    /// <summary>ADR 0015: los módulos del catálogo de un centro, con su estado y su historial. Sin la cuenta de quien los cambió y sin datos de residentes.</summary>
    public Task<ApplicationResult<IReadOnlyList<CenterModuleView>>> ListCenterModulesAsync(PlatformCenterQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync<IReadOnlyList<CenterModuleView>>(async () =>
        {
            var access = await AccessAsync(query.AmbitoPerfilId, query.CentroId, ct);
            var changes = await modules.ListChangesAsync(access, query.CentroDestinoId, ct);
            var today = CenterModuleRules.TodayInMadrid(clock.GetUtcNow());
            return CenterModules.All.Select(module =>
            {
                var history = changes.Where(c => c.ModuleCode == module.Code).OrderByDescending(c => c.Number).ToList();
                var last = history.FirstOrDefault();
                var state = CenterModuleRules.Resolve(module, last is null ? null : new CenterModuleChange(last.Number, last.Enabled, last.ValidFrom, last.ValidUntil), today);
                return new CenterModuleView(module, state, last, history);
            }).ToList();
        });

    /// <summary>Días hacia adelante y hacia atrás de los vencimientos que enseña Plataforma: los de los próximos 30 días y los vencidos sin renovar en los últimos 90.</summary>
    public const int ExpiringDaysAhead = 30;

    public const int ExpiredDaysBack = 90;

    /// <summary>ADR 0015 (decisión de Orlando, 2026-11-16): la pantalla de Plataforma enseña los vencimientos próximos y los vencidos sin renovar, de todos los centros.</summary>
    public Task<ApplicationResult<IReadOnlyList<PlatformExpiringModule>>> ListExpiringModulesAsync(PlatformQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var access = await AccessAsync(query.AmbitoPerfilId, query.CentroId, ct);
            var today = CenterModuleRules.TodayInMadrid(clock.GetUtcNow());
            return await modules.ListExpiringAsync(access, today.AddDays(-ExpiredDaysBack), today.AddDays(ExpiringDaysAhead), ct);
        });

    /// <summary>ADR 0007, I2: el soporte de la plataforma entrega la invitación de acceso (o de recuperación) de una cuenta de
    /// Administración del centro: la del primer administrador, o la de uno que no tiene otra Administración que se la dé.</summary>
    public Task<ApplicationResult<IssuedInvitation>> IssueAdministratorInvitationAsync(
        PlatformAdministrationCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var access = await AccessAsync(command.AmbitoPerfilId, command.CentroId, ct);
            var token = AccessInvitation.NewToken();
            return new IssuedInvitation(token, await invitations.IssueForAdministratorAsync(
                access, command.CentroDestinoId, command.AmbitoDestinoId, AccessInvitation.Hash(token), clock.GetUtcNow().UtcDateTime, ct));
        });

    /// <summary>ADR 0007, I2b: las cuentas con perfiles en varios centros, que ninguna Administración gestiona.</summary>
    public Task<ApplicationResult<IReadOnlyList<PlatformMultiCenterAccount>>> ListMultiCenterAccountsAsync(
        PlatformQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await directory.ListMultiCenterAccountsAsync(await AccessAsync(query.AmbitoPerfilId, query.CentroId, ct), clock.GetUtcNow().UtcDateTime, ct));

    public Task<ApplicationResult<IssuedInvitation>> IssueMultiCenterInvitationAsync(PlatformAccountCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var access = await AccessAsync(command.AmbitoPerfilId, command.CentroId, ct);
            var token = AccessInvitation.NewToken();
            return new IssuedInvitation(token, await invitations.IssueForMultiCenterAsync(
                access, command.CuentaId, AccessInvitation.Hash(token), clock.GetUtcNow().UtcDateTime, ct));
        });

    public Task<ApplicationResult<bool>> ChangeMultiCenterAccountStatusAsync(
        PlatformAccountCommand command, AccountStatus status, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            await centers.ChangeMultiCenterAccountStatusAsync(await AccessAsync(command.AmbitoPerfilId, command.CentroId, ct), command.CuentaId, status, ct);
            return true;
        });

    /// <summary>Los documentos de la sección «Documentos» no tocan la base de datos: solo se comprueba, con la misma regla que el resto de operaciones, que quien pide
    /// tiene ahora un ámbito activo de Plataforma. La cookie del ámbito no basta (no es de fiar).</summary>
    public Task<ApplicationResult<bool>> CheckDocumentsAccessAsync(PlatformQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            await AccessAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return true;
        });

    public Task<ApplicationResult<IReadOnlyList<PlatformCenterSummary>>> ListCentersAsync(PlatformQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () => await directory.ListAsync(await AccessAsync(query.AmbitoPerfilId, query.CentroId, ct), ct));

    public Task<ApplicationResult<CenterId>> CreateCenterAsync(CreateCenterCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var access = await AccessAsync(command.AmbitoPerfilId, command.CentroId, ct);
            // Sin código escrito, se deriva del nombre (los códigos no se ven en ninguna dirección ni inicio de sesión).
            var centerCodeGenerated = string.IsNullOrWhiteSpace(command.CodigoCentro);
            var data = NewCenter.Validate(
                centerCodeGenerated ? ExchangeText.UnitCode(command.NombreCentro ?? "") : command.CodigoCentro, command.NombreCentro,
                string.IsNullOrWhiteSpace(command.CodigoUnidad) ? ExchangeText.UnitCode(command.NombreUnidad ?? "") : command.CodigoUnidad,
                command.NombreUnidad, command.IdentificadorAdministrador, command.NombreAdministrador, command.Logo, centerCodeGenerated);
            var zone = CenterTimeZoneRules.ValidateZone(command.ZonaHoraria ?? CenterTimeZoneRules.DefaultZoneCode, await timeZoneCatalog.ListCatalogAsync(ct));
            // Sin valor por defecto: quien no elige el tipo de servicio no crea el centro (ADR 0017).
            var serviceType = CenterServiceTypeRules.Validate(command.TipoDeServicio);
            return await centers.CreateCenterAsync(access, command.OperacionId, data with { ZoneCode = zone, ServiceType = serviceType }, ct);
        });

    /// <summary>El tipo de servicio de un centro, su constancia y si tiene personas (sí o no).</summary>
    public Task<ApplicationResult<CenterServiceTypeState>> GetCenterServiceTypeAsync(PlatformCenterQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await serviceTypes.GetAsync(await AccessAsync(query.AmbitoPerfilId, query.CentroId, ct), query.CentroDestinoId, ct));

    /// <summary>Corrige el tipo de servicio de un centro sin personas (error de alta).</summary>
    public Task<ApplicationResult<bool>> ChangeCenterServiceTypeAsync(ChangeCenterServiceTypeCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var access = await AccessAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await serviceTypes.ChangeAsync(access, command.CentroDestinoId, CenterServiceTypeRules.Validate(command.TipoDeServicio), ct);
            return true;
        });

    public Task<ApplicationResult<PlatformCenterDetail>> FindCenterAsync(PlatformCenterQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await directory.FindAsync(await AccessAsync(query.AmbitoPerfilId, query.CentroId, ct), query.CentroDestinoId, ct)
            ?? throw new AccessDeniedException());

    /// <summary>CJ, 2026-10-07: el soporte de la plataforma marca o desmarca la Administración principal de un centro.</summary>
    public Task<ApplicationResult<bool>> SetAdministrationPrincipalAsync(
        SetPlatformAdministrationPrincipalCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            await centers.SetAdministrationPrincipalAsync(
                await AccessAsync(command.AmbitoPerfilId, command.CentroId, ct), command.CentroDestinoId, command.AmbitoDestinoId, command.Principal, ct);
            return true;
        });

    /// <summary>CJ, 2026-10-07: el soporte de la plataforma añade una unidad del centro al ámbito de una de sus Administraciones.</summary>
    public Task<ApplicationResult<bool>> AddUnitToAdministrationAsync(AddUnitToAdministrationCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            await centers.AddUnitToAdministrationAsync(
                await AccessAsync(command.AmbitoPerfilId, command.CentroId, ct), command.CentroDestinoId, command.AmbitoDestinoId, command.UnidadId, ct);
            return true;
        });

    private async Task<PlatformAccess> AccessAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
        var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
        var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == profileScopeId && s.CenterId == centerId);
        if (scope is null || scope.Profile != SystemProfile.Plataforma || centerId.Value != PlatformCenter.Id)
        {
            throw new AccessDeniedException();
        }

        return new PlatformAccess(scope.ProfileScopeId, scope.AccountId);
    }
}
