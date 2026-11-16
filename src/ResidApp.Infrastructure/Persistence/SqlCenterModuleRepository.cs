using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Modules;
using ResidApp.Domain.Platform;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Módulos opcionales por centro (ADR 0015, script 0098). La tabla es de solo añadir: el estado vigente es el último cambio. La escritura es una sola transacción que comprueba
/// de nuevo el ámbito de Plataforma, bloquea la fila del centro (ordena los cambios simultáneos y hace fiable la numeración), valida la secuencia y las dependencias, inserta la
/// fila y deja la auditoría <c>CENTER_MODULE_ENABLE</c> o <c>CENTER_MODULE_DISABLE</c> sin datos (el recurso es la fila; el módulo se lee de ella). No hay activación sin auditoría.
/// </summary>
public sealed class SqlCenterModuleRepository(SqlConnectionFactory connections) : ICenterModuleRepository
{
    public async Task<CenterModuleChange?> FindLastAsync(CenterId centerId, string moduleCode, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await FindLastAsync(connection, null, centerId, moduleCode, ct);
    }

    public async Task AppendAsync(PlatformAccess access, NewCenterModuleChange change, DateOnly today, CancellationToken ct = default)
    {
        var module = CenterModules.Find(change.ModuleCode) ?? throw new DomainValidationException(NewCenterModuleChangeRules.Invalid);
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlPlatformCenterRepository.EnsureOperatorAsync(connection, transaction, access, ct);

        // Apagar se puede siempre (cada hora de retraso es tratar contra una instrucción del centro); activar, solo en un centro activo.
        var centerState = await connection.ExecuteScalarAsync<string?>(new CommandDefinition("""
            SELECT estado FROM dbo.centros WITH (UPDLOCK, ROWLOCK) WHERE id = @CenterId AND codigo <> @PlatformCode
            """, new { CenterId = change.CenterId.Value, PlatformCode = PlatformCenter.Code }, transaction, cancellationToken: ct));
        if (centerState is null || (change.Enable && centerState != "ACTIVE"))
        {
            throw new DomainValidationException(NewCenterModuleChangeRules.Invalid);
        }

        var last = await FindLastAsync(connection, transaction, change.CenterId, module.Code, ct);
        if (!change.Enable && (last is null || !last.Enabled))
        {
            throw new DomainStateException("MODULE_CHANGE_CONFLICT");
        }

        if (change.Enable)
        {
            foreach (var dependency in module.Dependencies)
            {
                var requirement = CenterModules.Find(dependency) ?? throw new DomainValidationException(NewCenterModuleChangeRules.Invalid);
                var state = CenterModuleRules.Resolve(
                    requirement, await FindLastAsync(connection, transaction, change.CenterId, dependency, ct), today);
                if (state != ModuleState.Active)
                {
                    throw new DomainStateException("MODULE_DEPENDENCY_MISSING");
                }
            }
        }

        var id = Guid.NewGuid();
        var occurredAt = DateTime.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.modulos_centro
                (id, centro_id, modulo_codigo, numero, estado_codigo, vigente_desde, vigente_hasta, referencia_anexo, version_anexo, version_ficha, condiciones_codigos,
                 motivo_baja_codigo, solicitud_recibida_en, canal_codigo, referencia_solicitud, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES
                (@Id, @CenterId, @ModuleCode, @Number, @State, @ValidFrom, @ValidUntil, @AnnexReference, @AnnexVersion, @SheetVersion, @Conditions,
                 @DisableReason, @RequestReceivedAt, @Channel, @RequestReference, @ActorId, 'PLATAFORMA', @OccurredAt);
            INSERT INTO dbo.eventos_auditoria (id, cuenta_id, perfil_activo, centro_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
            VALUES (NEWID(), @ActorId, 'PLATAFORMA', @CenterId, 'CENTER_MODULE', @Id, @Action, @OccurredAt);
            """, new
        {
            Id = id, CenterId = change.CenterId.Value, ModuleCode = module.Code, Number = (last?.Number ?? 0) + 1, State = change.Enable ? "ACTIVE" : "DISABLED",
            ValidFrom = change.ValidFrom?.ToDateTime(TimeOnly.MinValue), ValidUntil = change.ValidUntil?.ToDateTime(TimeOnly.MinValue),
            change.AnnexReference, change.AnnexVersion, SheetVersion = change.PrivacySheetVersion, Conditions = change.ConditionCodes,
            DisableReason = change.DisableReason is { } reason ? reason.ToCode() : null, RequestReceivedAt = change.RequestReceivedAt.UtcDateTime,
            Channel = change.Channel.ToCode(), RequestReference = change.RequestReference, ActorId = access.AccountId.Value,
            Action = change.Enable ? "CENTER_MODULE_ENABLE" : "CENTER_MODULE_DISABLE", OccurredAt = occurredAt,
        }, transaction, cancellationToken: ct));
        transaction.Commit();
    }

    private static async Task<CenterModuleChange?> FindLastAsync(
        SqlConnection connection, SqlTransaction? transaction, CenterId centerId, string moduleCode, CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("""
            SELECT TOP (1) numero AS Number, estado_codigo AS State, CAST(vigente_desde AS DATETIME2(0)) AS ValidFrom, CAST(vigente_hasta AS DATETIME2(0)) AS ValidUntil
              FROM dbo.modulos_centro
             WHERE centro_id = @CenterId AND modulo_codigo = @ModuleCode
             ORDER BY numero DESC
            """, new { CenterId = centerId.Value, ModuleCode = moduleCode }, transaction, cancellationToken: ct));
        return row is null
            ? null
            : new CenterModuleChange(
                row.Number, row.State == "ACTIVE", row.ValidFrom is { } from ? DateOnly.FromDateTime(from) : null,
                row.ValidUntil is { } until ? DateOnly.FromDateTime(until) : null);
    }

    private sealed class Row
    {
        public int Number { get; set; }

        public string State { get; set; } = "";

        public DateTime? ValidFrom { get; set; }

        public DateTime? ValidUntil { get; set; }
    }
}
