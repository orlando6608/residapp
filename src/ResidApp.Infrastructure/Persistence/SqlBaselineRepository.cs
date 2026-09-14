using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Authorization;
using ResidApp.Application.Ports;
using ResidApp.Domain.Baseline;
using ResidApp.Domain.Baseline.Answers;
using ResidApp.Domain.Baseline.Catalogs;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Traduce signBaselineDraft (db/repositories/baseline-repository.ts) y readBaselineAsClinicalDirection
/// (db/repositories/audit-repository.ts). Tablas/columnas en español desde
/// database/scripts/0002_renombrado_espanol_sqlserver.sql.
/// </summary>
public sealed class SqlBaselineRepository(SqlConnectionFactory connections) : IBaselineRepository
{
    private const string BarthelInstrument = "BARTHEL_COMUN_V0_1";

    public async Task<SignBaselineDraftResult> SignDraftAsync(SignBaselineDraftInput input, CancellationToken ct = default)
    {
        var requestHash = SignRequestHash.Of(input);
        using var connection = await connections.OpenAsync(ct);

        var previous = await FindSignIdempotencyAsync(connection, null, input, requestHash, ct);
        if (previous is not null)
        {
            return previous;
        }

        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            var draft = await LoadAuthorizedDraftAsync(connection, transaction, input, ct)
                ?? throw new DomainValidationException("BASELINE_SIGN_NOT_AUTHORIZED");

            var areas = await LoadAreasAsync(connection, transaction, draft.Id, ct);
            AssertAllAreasComplete(areas);

            var barthelRow = await LoadBarthelAsync(connection, transaction, draft.Id, ct);
            var barthelItems = await LoadBarthelItemsAsync(connection, transaction, barthelRow.Id, ct);
            // La construcción valida completitud/consistencia (BASELINE_BARTHEL_INCOMPLETE si falla).
            _ = new BarthelAssessment(
                barthelRow.AssessmentDate!.Value.ToString("yyyy-MM-dd"), barthelRow.TotalScore!.Value,
                barthelItems.Select(i => new BarthelItem(EnumCode.ParseCode<BarthelItemCode>(i.ItemCode), i.SelectedOptionCode, i.AwardedScore)).ToList());

            var current = await connection.QuerySingleOrDefaultAsync<CurrentRow>(new CommandDefinition("""
                SELECT cb.version_basal_id AS BaselineVersionId, bv.numero_version AS VersionNumber
                  FROM dbo.basales_vigentes_residente cb
                  JOIN dbo.basales_version bv ON bv.id = cb.version_basal_id
                 WHERE cb.centro_id = @CenterId AND cb.residente_id = @ResidentId
                """, new { CenterId = input.CenterId.Value, ResidentId = input.ResidentId.Value }, transaction, cancellationToken: ct));

            var versionNumber = (current?.VersionNumber ?? 0) + 1;
            var versionId = BaselineVersionId.New();
            var barthelVersionId = Guid.NewGuid();
            var occurredAt = DateTimeOffset.UtcNow;
            var result = new SignBaselineDraftResult(versionId, versionNumber);

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.operaciones_idempotencia (id, cuenta_id, accion_codigo, operacion_id, hash_solicitud, estado, creado_en)
                VALUES (@Id, @AccountId, 'BASELINE_SIGN', @OperationId, @RequestHash, 'IN_PROGRESS', @OccurredAt)
                """, new
            {
                Id = Guid.NewGuid(), AccountId = input.AccountId.Value, input.OperationId, RequestHash = requestHash, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.basales_version
                    (id, borrador_origen_id, residente_id, centro_id, creado_en_unidad_id, numero_version, motivo_codigo,
                     fuente_informacion_comun_codigo, fuente_informacion_comun_otro_texto, fecha_informacion_comun,
                     creado_por_cuenta_id, creado_por_perfil, creado_en,
                     firmado_por_cuenta_id, firmado_por_perfil, firmado_en, vigente_desde, operacion_activacion_id)
                VALUES (@VersionId, @DraftId, @ResidentId, @CenterId, @UnitId, @VersionNumber, @ReasonCode,
                     @SourceCode, @SourceOtherText, @InformationDate,
                     @AccountId, @ActiveProfile, @CreatedAt, @AccountId, @ActiveProfile, @OccurredAt, @OccurredAt, @OperationId)
                """, new
            {
                VersionId = versionId.Value, DraftId = draft.Id, ResidentId = draft.ResidentId, CenterId = draft.CenterId,
                UnitId = draft.CreatedInUnitId, VersionNumber = versionNumber, ReasonCode = draft.ReasonCode,
                SourceCode = draft.CommonInformationSourceCode, SourceOtherText = draft.CommonInformationSourceOtherText,
                InformationDate = draft.CommonInformationDate, AccountId = draft.CreatedByAccountId,
                ActiveProfile = draft.CreatedByProfile, CreatedAt = draft.CreatedAt, OccurredAt = occurredAt, input.OperationId,
            }, transaction, cancellationToken: ct));

            foreach (var area in areas)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO dbo.basales_version_areas
                        (id, version_basal_id, residente_id, centro_id, area_codigo, catalogo_version_codigo, respuestas_json,
                         observacion, fuente_informacion_sustituta_codigo, fuente_informacion_sustituta_otro_texto,
                         fecha_informacion_sustituta, registrado_por_cuenta_id, registrado_por_perfil, registrado_en)
                    VALUES (@Id, @VersionId, @ResidentId, @CenterId, @AreaCode, @CatalogVersionCode, @AnswerPayload,
                         @Observation, @SourceOverrideCode, @SourceOverrideOtherText, @DateOverride,
                         @RecordedByAccountId, @RecordedByProfile, @RecordedAt)
                    """, new
                {
                    Id = Guid.NewGuid(), VersionId = versionId.Value, ResidentId = draft.ResidentId, CenterId = draft.CenterId,
                    area.AreaCode, area.CatalogVersionCode, area.AnswerPayload, area.Observation,
                    SourceOverrideCode = area.InformationSourceOverrideCode, SourceOverrideOtherText = area.InformationSourceOverrideOtherText,
                    DateOverride = area.InformationDateOverride, area.RecordedByAccountId, area.RecordedByProfile, area.RecordedAt,
                }, transaction, cancellationToken: ct));
            }

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.basales_version_barthel
                    (id, version_basal_id, residente_id, centro_id, instrumento_version_codigo, fecha_valoracion, puntuacion_total,
                     registrado_por_cuenta_id, registrado_por_perfil, registrado_en)
                VALUES (@Id, @VersionId, @ResidentId, @CenterId, @Instrument, @AssessmentDate, @TotalScore,
                     @RecordedByAccountId, @RecordedByProfile, @RecordedAt)
                """, new
            {
                Id = barthelVersionId, VersionId = versionId.Value, ResidentId = draft.ResidentId, CenterId = draft.CenterId,
                Instrument = BarthelInstrument, AssessmentDate = barthelRow.AssessmentDate, TotalScore = barthelRow.TotalScore,
                barthelRow.RecordedByAccountId, barthelRow.RecordedByProfile, barthelRow.RecordedAt,
            }, transaction, cancellationToken: ct));

            foreach (var item in barthelItems)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO dbo.basales_version_barthel_items
                        (id, barthel_id, version_basal_id, residente_id, centro_id, instrumento_version_codigo,
                         item_codigo, opcion_seleccionada_codigo, puntuacion_otorgada)
                    VALUES (@Id, @BarthelVersionId, @VersionId, @ResidentId, @CenterId, @Instrument,
                         @ItemCode, @SelectedOptionCode, @AwardedScore)
                    """, new
                {
                    Id = Guid.NewGuid(), BarthelVersionId = barthelVersionId, VersionId = versionId.Value,
                    ResidentId = draft.ResidentId, CenterId = draft.CenterId, Instrument = BarthelInstrument,
                    item.ItemCode, item.SelectedOptionCode, item.AwardedScore,
                }, transaction, cancellationToken: ct));
            }

            if (current is not null)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO dbo.basales_sustituciones (version_anterior_id, version_nueva_id, residente_id, centro_id, sustituido_en)
                    VALUES (@Previous, @New, @ResidentId, @CenterId, @OccurredAt)
                    """, new
                {
                    Previous = current.BaselineVersionId, New = versionId.Value, ResidentId = input.ResidentId.Value, CenterId = input.CenterId.Value, OccurredAt = occurredAt,
                }, transaction, cancellationToken: ct));

                await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.basales_vigentes_residente SET version_basal_id = @New, activado_en = @OccurredAt
                     WHERE centro_id = @CenterId AND residente_id = @ResidentId AND version_basal_id = @Previous
                    """, new
                {
                    New = versionId.Value, OccurredAt = occurredAt, CenterId = input.CenterId.Value, ResidentId = input.ResidentId.Value, Previous = current.BaselineVersionId,
                }, transaction, cancellationToken: ct));
            }
            else
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO dbo.basales_vigentes_residente (residente_id, centro_id, version_basal_id, activado_en)
                    VALUES (@ResidentId, @CenterId, @VersionId, @OccurredAt)
                    """, new { ResidentId = input.ResidentId.Value, CenterId = input.CenterId.Value, VersionId = versionId.Value, OccurredAt = occurredAt },
                    transaction, cancellationToken: ct));
            }

            // Concurrencia optimista: el TS original solo comprueba la revisión en el SELECT inicial (ver
            // Riesgos del plan — ventana TOCTOU teórica). Aquí se exige además RowsAffected==1 en el propio
            // UPDATE de cierre, dentro de la misma transacción: corrección deliberada, no solo un puerto fiel.
            var closedRows = await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.basales_borrador
                   SET estado = 'SIGNED', actualizado_por_cuenta_id = @AccountId, actualizado_por_perfil = @ActiveProfile, actualizado_en = @OccurredAt
                 WHERE id = @DraftId AND estado = 'ACTIVE' AND revision_borrador = @ExpectedDraftRevision
                """, new
            {
                AccountId = input.AccountId.Value, ActiveProfile = input.ActiveProfile.ToCode(), OccurredAt = occurredAt,
                DraftId = draft.Id, input.ExpectedDraftRevision,
            }, transaction, cancellationToken: ct));
            if (closedRows != 1)
            {
                throw new DomainValidationException("BASELINE_DRAFT_REVISION_CONFLICT");
            }

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.eventos_auditoria (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
                VALUES (@Id, @AccountId, @ActiveProfile, @CenterId, @UnitId, @ResidentId, 'BASELINE_VERSION', @VersionId, 'BASELINE_SIGN', @OccurredAt)
                """, new
            {
                Id = Guid.NewGuid(), AccountId = input.AccountId.Value, ActiveProfile = input.ActiveProfile.ToCode(),
                CenterId = input.CenterId.Value, UnitId = input.UnitId.Value, ResidentId = input.ResidentId.Value, VersionId = versionId.Value, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));

            var resultJson = JsonSerializer.Serialize(result, ResidAppJson.Options);
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.operaciones_idempotencia
                   SET estado = 'SUCCEEDED', recurso_resultado_id = @VersionId, resultado_json = @ResultJson, completado_en = @OccurredAt
                 WHERE cuenta_id = @AccountId AND accion_codigo = 'BASELINE_SIGN'
                   AND operacion_id = @OperationId AND hash_solicitud = @RequestHash AND estado = 'IN_PROGRESS'
                """, new
            {
                VersionId = versionId.Value, ResultJson = resultJson, OccurredAt = occurredAt,
                AccountId = input.AccountId.Value, input.OperationId, RequestHash = requestHash,
            }, transaction, cancellationToken: ct));

            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            var recovered = await FindSignIdempotencyAsync(connection, null, input, requestHash, ct);
            if (recovered is not null)
            {
                return recovered;
            }
            throw;
        }
    }

    /// <summary>Envuelve la lectura auditada de Dirección Clínica en el mismo patrón de idempotencia que
    /// CreateWithInitialLocationAsync/SignDraftAsync: sin esto, un reintento desde una tablet con
    /// cobertura inestable duplicaba la fila de auditoría (dbo.operaciones_idempotencia ya reservaba
    /// 'CLINICAL_DETAIL_READ' como accion_codigo válido sin que nada lo usara). Ver
    /// docs/decisiones-arquitectura/directrices-pwa-movil.md, punto 4.</summary>
    public async Task<IReadOnlyList<AuditedBaselineHeader>> ReadAsClinicalDirectionAsync(
        ClinicalDirectionReadInput input, CancellationToken ct = default)
    {
        var requestHash = ReadRequestHash.Of(input);
        using var connection = await connections.OpenAsync(ct);

        var previous = await FindReadIdempotencyAsync(connection, null, input.AccountId.Value, input.OperationId, requestHash, ct);
        if (previous is not null)
        {
            return previous;
        }

        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            var occurredAt = DateTimeOffset.UtcNow;

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.operaciones_idempotencia (id, cuenta_id, accion_codigo, operacion_id, hash_solicitud, estado, creado_en)
                VALUES (@Id, @AccountId, 'CLINICAL_DETAIL_READ', @OperationId, @RequestHash, 'IN_PROGRESS', @OccurredAt)
                """, new
            {
                Id = Guid.NewGuid(), AccountId = input.AccountId.Value, input.OperationId, RequestHash = requestHash, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));

            var isCurrent = input.ResourceType == ClinicalResourceType.BaselineCurrent;
            // "current" es palabra reservada en T-SQL (sintaxis de cursores FOR UPDATE OF ... CURRENT OF);
            // usarla como alias de tabla rompe el parser ("Incorrect syntax near the keyword 'current'"),
            // descubierto al ejercitar por primera vez BASELINE_CURRENT con un basal realmente firmado. Se
            // usa "cur", mismo alias seguro que ya usa ReadCurrentSummaryAsync más abajo en este archivo.
            var resourceSql = isCurrent
                ? "SELECT version.id FROM dbo.basales_vigentes_residente cur JOIN dbo.basales_version version ON version.id = cur.version_basal_id WHERE cur.centro_id = @CenterId AND cur.residente_id = @ResidentId"
                : "SELECT version.id FROM dbo.basales_version version WHERE version.centro_id = @CenterId AND version.residente_id = @ResidentId";
            var finalSelect = isCurrent
                ? """
                  SELECT version.id AS Id, version.numero_version AS VersionNumber, version.motivo_codigo AS ReasonCode, version.firmado_en AS SignedAt
                    FROM dbo.basales_vigentes_residente cur
                    JOIN dbo.basales_version version ON version.id = cur.version_basal_id
                    JOIN @AuditedResourceIds audited ON audited.ResourceId = version.id
                   WHERE cur.centro_id = @CenterId AND cur.residente_id = @ResidentId
                  """
                : """
                  SELECT version.id AS Id, version.numero_version AS VersionNumber, version.motivo_codigo AS ReasonCode, version.firmado_en AS SignedAt
                    FROM dbo.basales_version version
                    JOIN @AuditedResourceIds audited ON audited.ResourceId = version.id
                   WHERE version.centro_id = @CenterId AND version.residente_id = @ResidentId
                   ORDER BY version.numero_version DESC
                  """;

            // Patrón "auditoría o nada": el SELECT final solo ve resource_id que ESTE INSERT acaba de
            // auditar (capturado vía OUTPUT en una tabla variable), dentro de la misma transacción. Si el
            // EXISTS de autorización no matchea, la tabla queda vacía y se aborta antes de llegar al
            // SELECT — sin distinguir "no autorizado" de "recurso inexistente", igual que el original.
            var sql = $"""
                DECLARE @AuditedResourceIds TABLE (ResourceId UNIQUEIDENTIFIER PRIMARY KEY);

                INSERT INTO dbo.eventos_auditoria
                    (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, proposito_codigo, ocurrido_en)
                OUTPUT inserted.recurso_id INTO @AuditedResourceIds
                SELECT NEWID(), @AccountId, 'DIRECCION_CLINICA', @CenterId, @UnitId, @ResidentId, @ResourceType, resource.id,
                       'CLINICAL_DETAIL_READ', 'SUPERVISION_CLINICA', @OccurredAt
                  FROM ({resourceSql}) resource
                 WHERE EXISTS (
                    SELECT 1 FROM dbo.cuentas account
                    JOIN dbo.ambitos_perfil profile ON profile.cuenta_id = account.id AND profile.centro_id = @CenterId
                         AND profile.perfil_codigo = 'DIRECCION_CLINICA' AND profile.estado = 'ACTIVE'
                    JOIN dbo.ambitos_perfil_unidad unit_scope ON unit_scope.ambito_perfil_id = profile.id
                         AND unit_scope.centro_id = profile.centro_id AND unit_scope.unidad_id = @UnitId AND unit_scope.revocado_en IS NULL
                    JOIN dbo.permisos_perfil permission ON permission.ambito_perfil_id = profile.id
                         AND permission.centro_id = profile.centro_id AND permission.permiso_codigo = 'CLINICAL_DETAIL_READ'
                         AND permission.revocado_en IS NULL
                    JOIN dbo.residentes resident ON resident.id = @ResidentId AND resident.centro_id = profile.centro_id AND resident.estado = 'ACTIVE'
                    JOIN dbo.intervalos_ubicacion_residente location ON location.residente_id = resident.id
                         AND location.centro_id = resident.centro_id AND location.unidad_id = unit_scope.unidad_id AND location.vigente_hasta IS NULL
                   WHERE account.id = @AccountId AND account.estado = 'ACTIVE'
                 );

                IF (SELECT COUNT(*) FROM @AuditedResourceIds) < 1
                    THROW 51000, 'CLINICAL_DETAIL_READ_NOT_AUTHORIZED', 1;

                {finalSelect}
                """;

            var parameters = new
            {
                AccountId = input.AccountId.Value, CenterId = input.CenterId.Value, UnitId = input.UnitId.Value, ResidentId = input.ResidentId.Value,
                ResourceType = input.ResourceType.ToCode(), OccurredAt = occurredAt,
            };
            var headers = (await connection.QueryAsync<AuditedHeaderRow>(
                new CommandDefinition(sql, parameters, transaction, cancellationToken: ct)))
                .Select(row => new AuditedBaselineHeader(
                    BaselineVersionId.From(row.Id), row.VersionNumber, EnumCode.ParseCode<BaselineReason>(row.ReasonCode),
                    new DateTimeOffset(row.SignedAt, TimeSpan.Zero)))
                .ToList();

            var resultJson = JsonSerializer.Serialize(headers, ResidAppJson.Options);
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.operaciones_idempotencia
                   SET estado = 'SUCCEEDED', recurso_resultado_id = @ResidentId, resultado_json = @ResultJson, completado_en = @OccurredAt
                 WHERE cuenta_id = @AccountId AND accion_codigo = 'CLINICAL_DETAIL_READ'
                   AND operacion_id = @OperationId AND hash_solicitud = @RequestHash AND estado = 'IN_PROGRESS'
                """, new
            {
                ResidentId = input.ResidentId.Value, ResultJson = resultJson, OccurredAt = occurredAt,
                AccountId = input.AccountId.Value, input.OperationId, RequestHash = requestHash,
            }, transaction, cancellationToken: ct));

            transaction.Commit();
            return headers;
        }
        catch
        {
            transaction.Rollback();
            var recovered = await FindReadIdempotencyAsync(connection, null, input.AccountId.Value, input.OperationId, requestHash, ct);
            if (recovered is not null)
            {
                return recovered;
            }
            throw;
        }
    }

    /// <summary>Traduce AUX-03/ENF-20/MED-21: lectura resumida del basal vigente, sin auditoría (a
    /// diferencia de ReadAsClinicalDirectionAsync) porque ResidentBaselinePolicy.AuthorizeBaselineCurrentRead
    /// ya lo permite a Auxiliar/Enfermería/Medicina sin esa obligación. Reutiliza BaselineAreaAnswerReader
    /// (el mismo parser que valida el borrador al firmar) para devolver cada área ya tipada, nunca el
    /// Barthel detallado por ítem.</summary>
    public async Task<CurrentBaselineSummary?> ReadCurrentSummaryAsync(
        ReadCurrentBaselineSummaryInput input, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);

        var version = await connection.QuerySingleOrDefaultAsync<CurrentVersionRow>(new CommandDefinition("""
            SELECT v.id AS Id, v.numero_version AS VersionNumber, v.motivo_codigo AS ReasonCode, v.firmado_en AS SignedAt
              FROM dbo.basales_vigentes_residente cur
              JOIN dbo.basales_version v ON v.id = cur.version_basal_id AND v.residente_id = cur.residente_id AND v.centro_id = cur.centro_id
             WHERE cur.residente_id = @ResidentId AND cur.centro_id = @CenterId
            """, new { ResidentId = input.ResidentId.Value, CenterId = input.CenterId.Value }, cancellationToken: ct));
        if (version is null)
        {
            return null;
        }

        var areas = (await connection.QueryAsync<CurrentAreaRow>(new CommandDefinition("""
            SELECT area_codigo AS AreaCode, respuestas_json AS AnswerPayload, observacion AS Observation
              FROM dbo.basales_version_areas WHERE version_basal_id = @VersionId ORDER BY area_codigo
            """, new { VersionId = version.Id }, cancellationToken: ct)))
            .Select(area => new BaselineAreaSummary(
                EnumCode.ParseCode<BaselineArea>(area.AreaCode),
                BaselineAreaAnswerReader.Parse(EnumCode.ParseCode<BaselineArea>(area.AreaCode), area.AnswerPayload),
                area.Observation))
            .ToList();

        var barthelTotal = await connection.QuerySingleAsync<int>(new CommandDefinition("""
            SELECT puntuacion_total FROM dbo.basales_version_barthel WHERE version_basal_id = @VersionId
            """, new { VersionId = version.Id }, cancellationToken: ct));

        return new CurrentBaselineSummary(
            BaselineVersionId.From(version.Id), version.VersionNumber, EnumCode.ParseCode<BaselineReason>(version.ReasonCode),
            new DateTimeOffset(version.SignedAt, TimeSpan.Zero), areas, barthelTotal);
    }

    /// <summary>Traduce ENF-19/ENF-20 "crear borrador": comprueba primero, en aplicación, que no exista ya
    /// un borrador activo para el residente (UX_bd_active es la defensa en profundidad si esta
    /// comprobación se saltara) e inserta el borrador con los campos comunes de versión ya completos.</summary>
    public async Task<CreateBaselineDraftResult> CreateDraftAsync(CreateBaselineDraftInput input, CancellationToken ct = default)
    {
        var requestHash = CreateDraftRequestHash.Of(input);
        using var connection = await connections.OpenAsync(ct);

        var previous = await FindCreateDraftIdempotencyAsync(connection, null, input.AccountId.Value, input.OperationId, requestHash, ct);
        if (previous is not null)
        {
            return previous;
        }

        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            var alreadyActive = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition("""
                SELECT id FROM dbo.basales_borrador WHERE residente_id = @ResidentId AND centro_id = @CenterId AND estado = 'ACTIVE'
                """, new { ResidentId = input.ResidentId.Value, CenterId = input.CenterId.Value }, transaction, cancellationToken: ct));
            if (alreadyActive is not null)
            {
                throw new DomainValidationException("BASELINE_DRAFT_ALREADY_ACTIVE");
            }

            var occurredAt = DateTimeOffset.UtcNow;
            var draftId = BaselineDraftId.New();
            var result = new CreateBaselineDraftResult(draftId, 1);

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.operaciones_idempotencia (id, cuenta_id, accion_codigo, operacion_id, hash_solicitud, estado, creado_en)
                VALUES (@Id, @AccountId, 'BASELINE_DRAFT_CREATE', @OperationId, @RequestHash, 'IN_PROGRESS', @OccurredAt)
                """, new
            {
                Id = Guid.NewGuid(), AccountId = input.AccountId.Value, input.OperationId, RequestHash = requestHash, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));

            var activeProfileCode = input.ActiveProfile.ToCode();
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.basales_borrador
                    (id, residente_id, centro_id, creado_en_unidad_id, estado, motivo_codigo, fuente_informacion_comun_codigo,
                     fuente_informacion_comun_otro_texto, fecha_informacion_comun, creado_por_cuenta_id, creado_por_perfil, creado_en,
                     actualizado_por_cuenta_id, actualizado_por_perfil, actualizado_en, revision_borrador)
                VALUES (@DraftId, @ResidentId, @CenterId, @UnitId, 'ACTIVE', @ReasonCode, @SourceCode,
                     @SourceOtherText, @InformationDate, @AccountId, @ActiveProfile, @OccurredAt,
                     @AccountId, @ActiveProfile, @OccurredAt, 1)
                """, new
            {
                DraftId = draftId.Value, ResidentId = input.ResidentId.Value, CenterId = input.CenterId.Value, UnitId = input.UnitId.Value,
                ReasonCode = input.ReasonCode.ToCode(), SourceCode = input.CommonInformationSourceCode.ToCode(),
                SourceOtherText = input.CommonInformationSourceOtherText, InformationDate = input.CommonInformationDate,
                AccountId = input.AccountId.Value, ActiveProfile = activeProfileCode, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.eventos_auditoria (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
                VALUES (@Id, @AccountId, @ActiveProfile, @CenterId, @UnitId, @ResidentId, 'BASELINE_DRAFT', @DraftId, 'BASELINE_DRAFT_CREATE', @OccurredAt)
                """, new
            {
                Id = Guid.NewGuid(), AccountId = input.AccountId.Value, ActiveProfile = activeProfileCode, CenterId = input.CenterId.Value,
                UnitId = input.UnitId.Value, ResidentId = input.ResidentId.Value, DraftId = draftId.Value, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));

            var resultJson = JsonSerializer.Serialize(result, ResidAppJson.Options);
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.operaciones_idempotencia
                   SET estado = 'SUCCEEDED', recurso_resultado_id = @DraftId, resultado_json = @ResultJson, completado_en = @OccurredAt
                 WHERE cuenta_id = @AccountId AND accion_codigo = 'BASELINE_DRAFT_CREATE'
                   AND operacion_id = @OperationId AND hash_solicitud = @RequestHash AND estado = 'IN_PROGRESS'
                """, new
            {
                DraftId = draftId.Value, ResultJson = resultJson, OccurredAt = occurredAt,
                AccountId = input.AccountId.Value, input.OperationId, RequestHash = requestHash,
            }, transaction, cancellationToken: ct));

            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            var recovered = await FindCreateDraftIdempotencyAsync(connection, null, input.AccountId.Value, input.OperationId, requestHash, ct);
            if (recovered is not null)
            {
                return recovered;
            }
            throw;
        }
    }

    /// <summary>ENF-20/ENF-21/ENF-22: el estado completo del borrador activo propio, o null si no hay
    /// ninguno (o el que hay no es propio/autorizado). A diferencia de LoadAuthorizedDraftAsync (que exige
    /// completitud porque lo usa la firma), aquí se devuelve lo que exista, aunque esté a medias.</summary>
    public async Task<BaselineDraftDetail?> LoadOwnedDraftAsync(OwnedActiveDraftInput input, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var draft = await LoadOwnedDraftRowAsync(connection, null, input, ct);
        if (draft is null)
        {
            return null;
        }

        var areas = await LoadAreasAsync(connection, null!, draft.Id, ct);
        var barthel = await TryLoadBarthelAsync(connection, null, draft.Id, ct);
        var barthelItems = barthel is null ? [] : await LoadBarthelItemsAsync(connection, null!, barthel.Id, ct);

        return new BaselineDraftDetail(
            BaselineDraftId.From(draft.Id), input.ResidentId, draft.DraftRevision, EnumCode.ParseCode<BaselineReason>(draft.ReasonCode!),
            EnumCode.ParseCode<InformationSourceCode>(draft.CommonInformationSourceCode!), draft.CommonInformationSourceOtherText,
            DateOnly.FromDateTime(draft.CommonInformationDate!.Value), new DateTimeOffset(draft.CreatedAt, TimeSpan.Zero),
            areas.Select(a => new BaselineDraftAreaDetail(
                EnumCode.ParseCode<BaselineArea>(a.AreaCode), BaselineAreaAnswerReader.Parse(EnumCode.ParseCode<BaselineArea>(a.AreaCode), a.AnswerPayload), a.Observation)).ToList(),
            new BaselineDraftBarthelDetail(
                barthel?.AssessmentDate is { } assessmentDate ? DateOnly.FromDateTime(assessmentDate) : null, barthel?.TotalScore,
                barthelItems.Select(i => new BarthelItem(EnumCode.ParseCode<BarthelItemCode>(i.ItemCode), i.SelectedOptionCode, i.AwardedScore)).ToList()));
    }

    /// <summary>ENF-20: guarda (crea o reemplaza) la respuesta de una de las nueve áreas. Upsert por
    /// borrado+inserción, seguro porque la clave única es (borrador_id, area_codigo) y nada referencia a
    /// estas filas como padre.</summary>
    public async Task SaveAreaAsync(SaveBaselineDraftAreaInput input, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            var draft = await LoadOwnedDraftRowAsync(connection, transaction, input.Owner, ct)
                ?? throw new DomainValidationException("BASELINE_DRAFT_NOT_AUTHORIZED");

            await connection.ExecuteAsync(new CommandDefinition("""
                DELETE FROM dbo.basales_borrador_areas WHERE borrador_id = @DraftId AND area_codigo = @AreaCode
                """, new { DraftId = draft.Id, AreaCode = input.AreaCode.ToCode() }, transaction, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.basales_borrador_areas
                    (id, borrador_id, residente_id, centro_id, area_codigo, catalogo_version_codigo, respuestas_json,
                     observacion, registrado_por_cuenta_id, registrado_por_perfil, registrado_en)
                VALUES (@Id, @DraftId, @ResidentId, @CenterId, @AreaCode, 'BASAL_AREAS_V0_1', @AnswerPayload,
                     @Observation, @AccountId, @ActiveProfile, @OccurredAt)
                """, new
            {
                Id = Guid.NewGuid(), DraftId = draft.Id, ResidentId = input.Owner.ResidentId.Value, CenterId = input.Owner.CenterId.Value,
                AreaCode = input.AreaCode.ToCode(), AnswerPayload = JsonSerializer.Serialize(input.Answer, input.Answer.GetType(), ResidAppJson.Options),
                input.Observation, AccountId = input.Owner.AccountId.Value, ActiveProfile = input.Owner.ActiveProfile.ToCode(),
                OccurredAt = DateTimeOffset.UtcNow,
            }, transaction, cancellationToken: ct));

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>ENF-21: guarda (crea o reemplaza) los diez ítems de Barthel y su total, derivado siempre en
    /// servidor a partir del catálogo (BarthelCatalog), nunca aceptado como dato de entrada.</summary>
    public async Task SaveBarthelAsync(SaveBaselineDraftBarthelInput input, CancellationToken ct = default)
    {
        // La construcción valida completitud/consistencia (BASELINE_BARTHEL_INCOMPLETE si falla) antes de tocar la base de datos.
        var totalScore = input.Items.Sum(i => i.AwardedScore);
        _ = new BarthelAssessment(input.AssessmentDate.ToString("yyyy-MM-dd"), totalScore, input.Items);

        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            var draft = await LoadOwnedDraftRowAsync(connection, transaction, input.Owner, ct)
                ?? throw new DomainValidationException("BASELINE_DRAFT_NOT_AUTHORIZED");
            var occurredAt = DateTimeOffset.UtcNow;

            await connection.ExecuteAsync(new CommandDefinition("""
                DELETE FROM dbo.basales_borrador_barthel WHERE borrador_id = @DraftId
                """, new { DraftId = draft.Id }, transaction, cancellationToken: ct));

            var barthelId = Guid.NewGuid();
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.basales_borrador_barthel
                    (id, borrador_id, residente_id, centro_id, instrumento_version_codigo, fecha_valoracion, puntuacion_total,
                     registrado_por_cuenta_id, registrado_por_perfil, registrado_en)
                VALUES (@Id, @DraftId, @ResidentId, @CenterId, @Instrument, @AssessmentDate, @TotalScore,
                     @AccountId, @ActiveProfile, @OccurredAt)
                """, new
            {
                Id = barthelId, DraftId = draft.Id, ResidentId = input.Owner.ResidentId.Value, CenterId = input.Owner.CenterId.Value,
                Instrument = BarthelInstrument, AssessmentDate = input.AssessmentDate, TotalScore = totalScore,
                AccountId = input.Owner.AccountId.Value, ActiveProfile = input.Owner.ActiveProfile.ToCode(), OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));

            foreach (var item in input.Items)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO dbo.basales_borrador_barthel_items
                        (id, barthel_id, borrador_id, residente_id, centro_id, instrumento_version_codigo,
                         item_codigo, opcion_seleccionada_codigo, puntuacion_otorgada)
                    VALUES (@Id, @BarthelId, @DraftId, @ResidentId, @CenterId, @Instrument, @ItemCode, @SelectedOptionCode, @AwardedScore)
                    """, new
                {
                    Id = Guid.NewGuid(), BarthelId = barthelId, DraftId = draft.Id, ResidentId = input.Owner.ResidentId.Value,
                    CenterId = input.Owner.CenterId.Value, Instrument = BarthelInstrument,
                    ItemCode = item.ItemCode.ToCode(), item.SelectedOptionCode, item.AwardedScore,
                }, transaction, cancellationToken: ct));
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>ENF-20 "cancelar el borrador propio con motivo": mantiene revision_borrador sin cambios
    /// (TR_bd_transition_guard lo exige así fuera de una transición ACTIVE-a-ACTIVE).</summary>
    public async Task CancelDraftAsync(CancelBaselineDraftInput input, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            var draft = await LoadOwnedDraftRowAsync(connection, transaction, input.Owner, ct)
                ?? throw new DomainValidationException("BASELINE_DRAFT_NOT_AUTHORIZED");
            var occurredAt = DateTimeOffset.UtcNow;

            var updatedRows = await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.basales_borrador
                   SET estado = 'CANCELLED', cancelado_en = @OccurredAt, cancelado_por_cuenta_id = @AccountId,
                       cancelado_por_perfil = @ActiveProfile, motivo_cancelacion = @Reason
                 WHERE id = @DraftId AND estado = 'ACTIVE'
                """, new
            {
                DraftId = draft.Id, OccurredAt = occurredAt, AccountId = input.Owner.AccountId.Value,
                ActiveProfile = input.Owner.ActiveProfile.ToCode(), input.Reason,
            }, transaction, cancellationToken: ct));
            if (updatedRows != 1)
            {
                throw new DomainValidationException("BASELINE_DRAFT_NOT_AUTHORIZED");
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>Comparte el mismo criterio de propiedad y permiso vigente que LoadAuthorizedDraftAsync
    /// exige para firmar, pero sin requerir completitud ni una revisión esperada concreta: "el borrador
    /// activo que esta cuenta, con este perfil, creó para este residente".</summary>
    private static async Task<OwnedDraftRow?> LoadOwnedDraftRowAsync(
        IDbConnection connection, IDbTransaction? transaction, OwnedActiveDraftInput input, CancellationToken ct) =>
        await connection.QuerySingleOrDefaultAsync<OwnedDraftRow>(new CommandDefinition("""
            SELECT d.id AS Id, d.revision_borrador AS DraftRevision, d.motivo_codigo AS ReasonCode,
                   d.fuente_informacion_comun_codigo AS CommonInformationSourceCode,
                   d.fuente_informacion_comun_otro_texto AS CommonInformationSourceOtherText,
                   d.fecha_informacion_comun AS CommonInformationDate, d.creado_en AS CreatedAt
              FROM dbo.basales_borrador d
             WHERE d.residente_id = @ResidentId AND d.centro_id = @CenterId AND d.estado = 'ACTIVE'
               AND d.creado_por_cuenta_id = @AccountId AND d.creado_por_perfil = @ActiveProfile
               AND EXISTS (
                 SELECT 1 FROM dbo.ambitos_perfil ps
                 JOIN dbo.ambitos_perfil_unidad pus ON pus.ambito_perfil_id = ps.id AND pus.centro_id = ps.centro_id
                      AND pus.unidad_id = d.creado_en_unidad_id AND pus.revocado_en IS NULL
                 JOIN dbo.permisos_perfil pp ON pp.ambito_perfil_id = ps.id AND pp.centro_id = ps.centro_id AND pp.revocado_en IS NULL
                WHERE ps.cuenta_id = @AccountId AND ps.centro_id = @CenterId AND ps.perfil_codigo = @ActiveProfile AND ps.estado = 'ACTIVE'
                  AND pp.permiso_codigo = CASE WHEN d.motivo_codigo = 'ALTA' THEN 'BASELINE_INITIAL_COMPLETE' ELSE 'BASELINE_REEVALUATE' END)
            """, new
        {
            AccountId = input.AccountId.Value, ActiveProfile = input.ActiveProfile.ToCode(),
            CenterId = input.CenterId.Value, ResidentId = input.ResidentId.Value,
        }, transaction, cancellationToken: ct));

    private static async Task<BarthelRow?> TryLoadBarthelAsync(IDbConnection c, IDbTransaction? t, Guid draftId, CancellationToken ct) =>
        await c.QuerySingleOrDefaultAsync<BarthelRow>(new CommandDefinition("""
            SELECT id AS Id, fecha_valoracion AS AssessmentDate, puntuacion_total AS TotalScore,
                   registrado_por_cuenta_id AS RecordedByAccountId, registrado_por_perfil AS RecordedByProfile, registrado_en AS RecordedAt
              FROM dbo.basales_borrador_barthel WHERE borrador_id = @DraftId
            """, new { DraftId = draftId }, t, cancellationToken: ct));

    private static async Task<CreateBaselineDraftResult?> FindCreateDraftIdempotencyAsync(
        IDbConnection connection, IDbTransaction? transaction, Guid accountId, Guid operationId, string requestHash, CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<IdempotencyRow>(new CommandDefinition("""
            SELECT hash_solicitud AS RequestHash, estado AS Status, resultado_json AS ResultJson
              FROM dbo.operaciones_idempotencia WHERE cuenta_id = @AccountId AND accion_codigo = 'BASELINE_DRAFT_CREATE' AND operacion_id = @OperationId
            """, new { AccountId = accountId, OperationId = operationId }, transaction, cancellationToken: ct));
        if (row is null)
        {
            return null;
        }
        if (row.RequestHash != requestHash)
        {
            throw new DomainValidationException("IDEMPOTENCY_KEY_REUSED");
        }
        return row.Status == "SUCCEEDED" && row.ResultJson is not null
            ? JsonSerializer.Deserialize<CreateBaselineDraftResult>(row.ResultJson, ResidAppJson.Options)
            : null;
    }

    // CommonInformationDate/AssessmentDate/InformationDateOverride se leen como DateTime, no DateOnly: el
    // deserializador de Dapper basado en constructor (records) no aplica DapperDateOnlyTypeHandler para
    // materializar parámetros — solo lo aplica al enlazar parámetros de escritura. Descubierto al ejecutar
    // por primera vez el ciclo completo crear-borrador -> firmar contra SQL Server real (antes de este
    // grupo, SignDraftAsync nunca se había ejercido con un borrador real). La conversión a DateOnly ocurre
    // en el código que consume la fila, no en la fila misma.
    private sealed record OwnedDraftRow(
        Guid Id, int DraftRevision, string? ReasonCode, string? CommonInformationSourceCode,
        string? CommonInformationSourceOtherText, DateTime? CommonInformationDate, DateTime CreatedAt);

    private static async Task<DraftRow?> LoadAuthorizedDraftAsync(
        IDbConnection connection, IDbTransaction transaction, SignBaselineDraftInput input, CancellationToken ct) =>
        await connection.QuerySingleOrDefaultAsync<DraftRow>(new CommandDefinition("""
            SELECT d.id AS Id, d.residente_id AS ResidentId, d.centro_id AS CenterId, d.creado_en_unidad_id AS CreatedInUnitId,
                   d.motivo_codigo AS ReasonCode, d.fuente_informacion_comun_codigo AS CommonInformationSourceCode,
                   d.fuente_informacion_comun_otro_texto AS CommonInformationSourceOtherText,
                   d.fecha_informacion_comun AS CommonInformationDate, d.creado_por_cuenta_id AS CreatedByAccountId,
                   d.creado_por_perfil AS CreatedByProfile, d.creado_en AS CreatedAt, d.revision_borrador AS DraftRevision
              FROM dbo.basales_borrador d
              JOIN dbo.residentes r ON r.id = d.residente_id AND r.centro_id = d.centro_id
              JOIN dbo.intervalos_ubicacion_residente li ON li.residente_id = d.residente_id AND li.centro_id = d.centro_id AND li.vigente_hasta IS NULL
              JOIN dbo.cuentas a ON a.id = @AccountId AND a.estado = 'ACTIVE'
             WHERE d.id = @DraftId AND d.centro_id = @CenterId AND d.residente_id = @ResidentId
               AND d.creado_en_unidad_id = @UnitId AND li.unidad_id = @UnitId
               AND d.estado = 'ACTIVE' AND d.revision_borrador = @ExpectedDraftRevision
               AND d.creado_por_cuenta_id = @AccountId AND d.creado_por_perfil = @ActiveProfile
               AND r.estado = 'ACTIVE'
               AND d.motivo_codigo IS NOT NULL AND d.fuente_informacion_comun_codigo IS NOT NULL AND d.fecha_informacion_comun IS NOT NULL
               AND EXISTS (
                 SELECT 1 FROM dbo.ambitos_perfil ps
                 JOIN dbo.ambitos_perfil_unidad pus ON pus.ambito_perfil_id = ps.id AND pus.centro_id = ps.centro_id
                      AND pus.unidad_id = @UnitId AND pus.revocado_en IS NULL
                 JOIN dbo.permisos_perfil pp ON pp.ambito_perfil_id = ps.id AND pp.centro_id = ps.centro_id AND pp.revocado_en IS NULL
                WHERE ps.cuenta_id = @AccountId AND ps.centro_id = @CenterId AND ps.perfil_codigo = @ActiveProfile AND ps.estado = 'ACTIVE'
                  AND pp.permiso_codigo = CASE WHEN d.motivo_codigo = 'ALTA' THEN 'BASELINE_INITIAL_COMPLETE' ELSE 'BASELINE_REEVALUATE' END)
            """, new
        {
            AccountId = input.AccountId.Value, DraftId = input.DraftId.Value, CenterId = input.CenterId.Value,
            ResidentId = input.ResidentId.Value, UnitId = input.UnitId.Value,
            input.ExpectedDraftRevision, ActiveProfile = input.ActiveProfile.ToCode(),
        }, transaction, cancellationToken: ct));

    private static async Task<IReadOnlyList<AreaRow>> LoadAreasAsync(IDbConnection c, IDbTransaction t, Guid draftId, CancellationToken ct) =>
        (await c.QueryAsync<AreaRow>(new CommandDefinition("""
            SELECT id AS Id, area_codigo AS AreaCode, catalogo_version_codigo AS CatalogVersionCode, respuestas_json AS AnswerPayload,
                   observacion AS Observation, fuente_informacion_sustituta_codigo AS InformationSourceOverrideCode,
                   fuente_informacion_sustituta_otro_texto AS InformationSourceOverrideOtherText,
                   fecha_informacion_sustituta AS InformationDateOverride, registrado_por_cuenta_id AS RecordedByAccountId,
                   registrado_por_perfil AS RecordedByProfile, registrado_en AS RecordedAt
              FROM dbo.basales_borrador_areas WHERE borrador_id = @DraftId ORDER BY area_codigo
            """, new { DraftId = draftId }, t, cancellationToken: ct))).ToList();

    private static async Task<BarthelRow> LoadBarthelAsync(IDbConnection c, IDbTransaction t, Guid draftId, CancellationToken ct)
    {
        var row = await c.QuerySingleOrDefaultAsync<BarthelRow>(new CommandDefinition("""
            SELECT id AS Id, fecha_valoracion AS AssessmentDate, puntuacion_total AS TotalScore,
                   registrado_por_cuenta_id AS RecordedByAccountId, registrado_por_perfil AS RecordedByProfile, registrado_en AS RecordedAt
              FROM dbo.basales_borrador_barthel WHERE borrador_id = @DraftId
            """, new { DraftId = draftId }, t, cancellationToken: ct));
        return row is null || row.AssessmentDate is null || row.TotalScore is null
            ? throw new DomainValidationException("BASELINE_BARTHEL_INCOMPLETE")
            : row;
    }

    private static async Task<IReadOnlyList<BarthelItemRow>> LoadBarthelItemsAsync(IDbConnection c, IDbTransaction t, Guid barthelId, CancellationToken ct) =>
        (await c.QueryAsync<BarthelItemRow>(new CommandDefinition("""
            SELECT item_codigo AS ItemCode, opcion_seleccionada_codigo AS SelectedOptionCode, puntuacion_otorgada AS AwardedScore
              FROM dbo.basales_borrador_barthel_items WHERE barthel_id = @BarthelId ORDER BY item_codigo
            """, new { BarthelId = barthelId }, t, cancellationToken: ct))).ToList();

    /// <summary>Traduce assertAllBaselineAreasComplete de validation.ts: 9 áreas, sin duplicados, cada
    /// payload válido para su record de dominio (la construcción del record ya aplica las reglas
    /// cruzadas).</summary>
    private static void AssertAllAreasComplete(IReadOnlyList<AreaRow> areas)
    {
        var codes = areas.Select(a => a.AreaCode).ToHashSet();
        if (areas.Count != 9 || codes.Count != 9)
        {
            throw new DomainValidationException("BASELINE_AREAS_INCOMPLETE");
        }
        foreach (var area in areas)
        {
            BaselineAreaAnswerReader.Parse(EnumCode.ParseCode<BaselineArea>(area.AreaCode), area.AnswerPayload);
        }
    }

    private static async Task<SignBaselineDraftResult?> FindSignIdempotencyAsync(
        IDbConnection connection, IDbTransaction? transaction, SignBaselineDraftInput input, string requestHash, CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<IdempotencyRow>(new CommandDefinition("""
            SELECT hash_solicitud AS RequestHash, estado AS Status, resultado_json AS ResultJson
              FROM dbo.operaciones_idempotencia WHERE cuenta_id = @AccountId AND accion_codigo = 'BASELINE_SIGN' AND operacion_id = @OperationId
            """, new { AccountId = input.AccountId.Value, input.OperationId }, transaction, cancellationToken: ct));
        if (row is null)
        {
            return null;
        }
        if (row.RequestHash != requestHash)
        {
            throw new DomainValidationException("IDEMPOTENCY_KEY_REUSED");
        }
        return row.Status == "SUCCEEDED" && row.ResultJson is not null
            ? JsonSerializer.Deserialize<SignBaselineDraftResult>(row.ResultJson, ResidAppJson.Options)
            : null;
    }

    private static async Task<IReadOnlyList<AuditedBaselineHeader>?> FindReadIdempotencyAsync(
        IDbConnection connection, IDbTransaction? transaction, Guid accountId, Guid operationId, string requestHash, CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<IdempotencyRow>(new CommandDefinition("""
            SELECT hash_solicitud AS RequestHash, estado AS Status, resultado_json AS ResultJson
              FROM dbo.operaciones_idempotencia WHERE cuenta_id = @AccountId AND accion_codigo = 'CLINICAL_DETAIL_READ' AND operacion_id = @OperationId
            """, new { AccountId = accountId, OperationId = operationId }, transaction, cancellationToken: ct));
        if (row is null)
        {
            return null;
        }
        if (row.RequestHash != requestHash)
        {
            throw new DomainValidationException("IDEMPOTENCY_KEY_REUSED");
        }
        return row.Status == "SUCCEEDED" && row.ResultJson is not null
            ? JsonSerializer.Deserialize<IReadOnlyList<AuditedBaselineHeader>>(row.ResultJson, ResidAppJson.Options)
            : null;
    }

    private sealed record CurrentRow(Guid BaselineVersionId, int VersionNumber);

    // DateTimeOffset tampoco lo acepta el deserializador de Dapper basado en constructor (records): igual
    // que DateOnly (ver comentario junto a OwnedDraftRow), solo System.DateTime "tal cual" viene de una
    // columna DATETIME2 en esta ruta. La conversión a DateTimeOffset ocurre donde el valor sale hacia un
    // tipo de dominio/puerto público.
    private sealed record CurrentVersionRow(Guid Id, int VersionNumber, string ReasonCode, DateTime SignedAt);

    private sealed record CurrentAreaRow(string AreaCode, string AnswerPayload, string? Observation);

    private sealed record DraftRow(
        Guid Id, Guid ResidentId, Guid CenterId, Guid CreatedInUnitId, string ReasonCode, string CommonInformationSourceCode,
        string? CommonInformationSourceOtherText, DateTime CommonInformationDate, Guid CreatedByAccountId,
        string CreatedByProfile, DateTime CreatedAt, int DraftRevision);

    private sealed record AreaRow(
        Guid Id, string AreaCode, string CatalogVersionCode, string AnswerPayload, string? Observation,
        string? InformationSourceOverrideCode, string? InformationSourceOverrideOtherText, DateTime? InformationDateOverride,
        Guid RecordedByAccountId, string RecordedByProfile, DateTime RecordedAt);

    private sealed record BarthelRow(
        Guid Id, DateTime? AssessmentDate, int? TotalScore, Guid RecordedByAccountId, string RecordedByProfile, DateTime RecordedAt);

    private sealed record BarthelItemRow(string ItemCode, string SelectedOptionCode, int AwardedScore);

    private sealed record IdempotencyRow(string RequestHash, string Status, string? ResultJson);

    private sealed record AuditedHeaderRow(Guid Id, int VersionNumber, string ReasonCode, DateTime SignedAt);
}

file static class SignRequestHash
{
    public static string Of(SignBaselineDraftInput input)
    {
        var canonical = JsonSerializer.Serialize(new object[]
        {
            input.AccountId.Value, input.ActiveProfile.ToCode(), input.CenterId.Value, input.UnitId.Value,
            input.ResidentId.Value, input.DraftId.Value, input.ExpectedDraftRevision, input.OperationId,
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

file static class CreateDraftRequestHash
{
    public static string Of(CreateBaselineDraftInput input)
    {
        var canonical = JsonSerializer.Serialize(new object?[]
        {
            input.AccountId.Value, input.ActiveProfile.ToCode(), input.CenterId.Value, input.UnitId.Value, input.ResidentId.Value,
            input.ReasonCode.ToCode(), input.CommonInformationSourceCode.ToCode(), input.CommonInformationSourceOtherText,
            input.CommonInformationDate.ToString("yyyy-MM-dd"), input.OperationId,
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

file static class ReadRequestHash
{
    public static string Of(ClinicalDirectionReadInput input)
    {
        var canonical = JsonSerializer.Serialize(new object[]
        {
            input.AccountId.Value, input.CenterId.Value, input.UnitId.Value, input.ResidentId.Value,
            input.ResourceType.ToCode(), input.Purpose.ToCode(), input.OperationId,
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

/// <summary>Traduce el switch de assertCompleteBaselineAreaAnswer: deserializa el JSON persistido al
/// record de dominio correcto según el área; la propia construcción del record ya valida (lanza
/// DomainValidationException si algo incumple una regla cruzada).</summary>
internal static class BaselineAreaAnswerReader
{
    public static IBaselineAreaAnswer Parse(BaselineArea area, string json)
    {
        try
        {
            return area switch
            {
                BaselineArea.Movilidad => JsonSerializer.Deserialize<MobilityAreaAnswer>(json, ResidAppJson.Options)!,
                BaselineArea.Alimentacion => JsonSerializer.Deserialize<FeedingAreaAnswer>(json, ResidAppJson.Options)!,
                BaselineArea.Continencia => JsonSerializer.Deserialize<ContinenceAreaAnswer>(json, ResidAppJson.Options)!,
                BaselineArea.AseoHigiene => JsonSerializer.Deserialize<PersonalCareAreaAnswer>(json, ResidAppJson.Options)!,
                BaselineArea.Cognicion => JsonSerializer.Deserialize<CognitionAreaAnswer>(json, ResidAppJson.Options)!,
                BaselineArea.Comunicacion => JsonSerializer.Deserialize<CommunicationAreaAnswer>(json, ResidAppJson.Options)!,
                BaselineArea.Conducta => JsonSerializer.Deserialize<BehaviorAreaAnswer>(json, ResidAppJson.Options)!,
                BaselineArea.Sueno => JsonSerializer.Deserialize<SleepAreaAnswer>(json, ResidAppJson.Options)!,
                BaselineArea.AyudasHabituales => JsonSerializer.Deserialize<UsualAidsAreaAnswer>(json, ResidAppJson.Options)!,
                _ => throw new DomainValidationException("BASELINE_AREA_PAYLOAD_INVALID"),
            };
        }
        catch (JsonException)
        {
            throw new DomainValidationException("BASELINE_AREA_PAYLOAD_INVALID");
        }
    }
}
