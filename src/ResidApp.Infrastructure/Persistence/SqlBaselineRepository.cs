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
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Traduce signBaselineDraft (db/repositories/baseline-repository.ts) y readBaselineAsClinicalDirection
/// (db/repositories/audit-repository.ts).
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
                SELECT cb.baseline_version_id AS BaselineVersionId, bv.version_number AS VersionNumber
                  FROM dbo.resident_current_baselines cb
                  JOIN dbo.baseline_versions bv ON bv.id = cb.baseline_version_id
                 WHERE cb.center_id = @CenterId AND cb.resident_id = @ResidentId
                """, new { CenterId = input.CenterId.Value, ResidentId = input.ResidentId.Value }, transaction, cancellationToken: ct));

            var versionNumber = (current?.VersionNumber ?? 0) + 1;
            var versionId = BaselineVersionId.New();
            var barthelVersionId = Guid.NewGuid();
            var occurredAt = DateTimeOffset.UtcNow;
            var result = new SignBaselineDraftResult(versionId, versionNumber);

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.idempotency_operations (id, account_id, action_code, operation_id, request_hash, status, created_at)
                VALUES (@Id, @AccountId, 'BASELINE_SIGN', @OperationId, @RequestHash, 'IN_PROGRESS', @OccurredAt)
                """, new
            {
                Id = Guid.NewGuid(), AccountId = input.AccountId.Value, input.OperationId, RequestHash = requestHash, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.baseline_versions
                    (id, source_draft_id, resident_id, center_id, created_in_unit_id, version_number, reason_code,
                     common_information_source_code, common_information_source_other_text, common_information_date,
                     created_by_account_id, created_by_profile, created_at,
                     signed_by_account_id, signed_by_profile, signed_at, valid_from, activation_operation_id)
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
                    INSERT INTO dbo.baseline_version_areas
                        (id, baseline_version_id, resident_id, center_id, area_code, catalog_version_code, answer_payload,
                         observation, information_source_override_code, information_source_override_other_text,
                         information_date_override, recorded_by_account_id, recorded_by_profile, recorded_at)
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
                INSERT INTO dbo.baseline_version_barthel
                    (id, baseline_version_id, resident_id, center_id, instrument_version_code, assessment_date, total_score,
                     recorded_by_account_id, recorded_by_profile, recorded_at)
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
                    INSERT INTO dbo.baseline_version_barthel_items
                        (id, barthel_id, baseline_version_id, resident_id, center_id, instrument_version_code,
                         item_code, selected_option_code, awarded_score)
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
                    INSERT INTO dbo.baseline_supersessions (previous_version_id, new_version_id, resident_id, center_id, superseded_at)
                    VALUES (@Previous, @New, @ResidentId, @CenterId, @OccurredAt)
                    """, new
                {
                    Previous = current.BaselineVersionId, New = versionId.Value, ResidentId = input.ResidentId.Value, CenterId = input.CenterId.Value, OccurredAt = occurredAt,
                }, transaction, cancellationToken: ct));

                await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE dbo.resident_current_baselines SET baseline_version_id = @New, activated_at = @OccurredAt
                     WHERE center_id = @CenterId AND resident_id = @ResidentId AND baseline_version_id = @Previous
                    """, new
                {
                    New = versionId.Value, OccurredAt = occurredAt, CenterId = input.CenterId.Value, ResidentId = input.ResidentId.Value, Previous = current.BaselineVersionId,
                }, transaction, cancellationToken: ct));
            }
            else
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO dbo.resident_current_baselines (resident_id, center_id, baseline_version_id, activated_at)
                    VALUES (@ResidentId, @CenterId, @VersionId, @OccurredAt)
                    """, new { ResidentId = input.ResidentId.Value, CenterId = input.CenterId.Value, VersionId = versionId.Value, OccurredAt = occurredAt },
                    transaction, cancellationToken: ct));
            }

            // Concurrencia optimista: el TS original solo comprueba la revisión en el SELECT inicial (ver
            // Riesgos del plan — ventana TOCTOU teórica). Aquí se exige además RowsAffected==1 en el propio
            // UPDATE de cierre, dentro de la misma transacción: corrección deliberada, no solo un puerto fiel.
            var closedRows = await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.baseline_drafts
                   SET status = 'SIGNED', updated_by_account_id = @AccountId, updated_by_profile = @ActiveProfile, updated_at = @OccurredAt
                 WHERE id = @DraftId AND status = 'ACTIVE' AND draft_revision = @ExpectedDraftRevision
                """, new
            {
                input.AccountId.Value, ActiveProfile = input.ActiveProfile.ToCode(), OccurredAt = occurredAt,
                DraftId = draft.Id, input.ExpectedDraftRevision,
            }, transaction, cancellationToken: ct));
            if (closedRows != 1)
            {
                throw new DomainValidationException("BASELINE_DRAFT_REVISION_CONFLICT");
            }

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.audit_events (id, account_id, active_profile, center_id, unit_id, resident_id, resource_type, resource_id, action_code, occurred_at)
                VALUES (@Id, @AccountId, @ActiveProfile, @CenterId, @UnitId, @ResidentId, 'BASELINE_VERSION', @VersionId, 'BASELINE_SIGN', @OccurredAt)
                """, new
            {
                Id = Guid.NewGuid(), AccountId = input.AccountId.Value, ActiveProfile = input.ActiveProfile.ToCode(),
                CenterId = input.CenterId.Value, UnitId = input.UnitId.Value, ResidentId = input.ResidentId.Value, VersionId = versionId.Value, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));

            var resultJson = JsonSerializer.Serialize(result, ResidAppJson.Options);
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.idempotency_operations
                   SET status = 'SUCCEEDED', result_resource_id = @VersionId, result_json = @ResultJson, completed_at = @OccurredAt
                 WHERE account_id = @AccountId AND action_code = 'BASELINE_SIGN'
                   AND operation_id = @OperationId AND request_hash = @RequestHash AND status = 'IN_PROGRESS'
                """, new
            {
                VersionId = versionId.Value, ResultJson = resultJson, OccurredAt = occurredAt,
                input.AccountId.Value, input.OperationId, RequestHash = requestHash,
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
    /// cobertura inestable duplicaba la fila de auditoría (dbo.idempotency_operations ya reservaba
    /// 'CLINICAL_DETAIL_READ' como action_code válido sin que nada lo usara). Ver
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
                INSERT INTO dbo.idempotency_operations (id, account_id, action_code, operation_id, request_hash, status, created_at)
                VALUES (@Id, @AccountId, 'CLINICAL_DETAIL_READ', @OperationId, @RequestHash, 'IN_PROGRESS', @OccurredAt)
                """, new
            {
                Id = Guid.NewGuid(), AccountId = input.AccountId.Value, input.OperationId, RequestHash = requestHash, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));

            var isCurrent = input.ResourceType == ClinicalResourceType.BaselineCurrent;
            var resourceSql = isCurrent
                ? "SELECT version.id FROM dbo.resident_current_baselines current JOIN dbo.baseline_versions version ON version.id = current.baseline_version_id WHERE current.center_id = @CenterId AND current.resident_id = @ResidentId"
                : "SELECT version.id FROM dbo.baseline_versions version WHERE version.center_id = @CenterId AND version.resident_id = @ResidentId";
            var finalSelect = isCurrent
                ? """
                  SELECT version.id AS Id, version.version_number AS VersionNumber, version.reason_code AS ReasonCode, version.signed_at AS SignedAt
                    FROM dbo.resident_current_baselines current
                    JOIN dbo.baseline_versions version ON version.id = current.baseline_version_id
                    JOIN @AuditedResourceIds audited ON audited.ResourceId = version.id
                   WHERE current.center_id = @CenterId AND current.resident_id = @ResidentId
                  """
                : """
                  SELECT version.id AS Id, version.version_number AS VersionNumber, version.reason_code AS ReasonCode, version.signed_at AS SignedAt
                    FROM dbo.baseline_versions version
                    JOIN @AuditedResourceIds audited ON audited.ResourceId = version.id
                   WHERE version.center_id = @CenterId AND version.resident_id = @ResidentId
                   ORDER BY version.version_number DESC
                  """;

            // Patrón "auditoría o nada": el SELECT final solo ve resource_id que ESTE INSERT acaba de
            // auditar (capturado vía OUTPUT en una tabla variable), dentro de la misma transacción. Si el
            // EXISTS de autorización no matchea, la tabla queda vacía y se aborta antes de llegar al
            // SELECT — sin distinguir "no autorizado" de "recurso inexistente", igual que el original.
            var sql = $"""
                DECLARE @AuditedResourceIds TABLE (ResourceId UNIQUEIDENTIFIER PRIMARY KEY);

                INSERT INTO dbo.audit_events
                    (id, account_id, active_profile, center_id, unit_id, resident_id, resource_type, resource_id, action_code, purpose_code, occurred_at)
                OUTPUT inserted.resource_id INTO @AuditedResourceIds
                SELECT NEWID(), @AccountId, 'DIRECCION_CLINICA', @CenterId, @UnitId, @ResidentId, @ResourceType, resource.id,
                       'CLINICAL_DETAIL_READ', 'SUPERVISION_CLINICA', @OccurredAt
                  FROM ({resourceSql}) resource
                 WHERE EXISTS (
                    SELECT 1 FROM dbo.accounts account
                    JOIN dbo.profile_scopes profile ON profile.account_id = account.id AND profile.center_id = @CenterId
                         AND profile.profile_code = 'DIRECCION_CLINICA' AND profile.status = 'ACTIVE'
                    JOIN dbo.profile_unit_scopes unit_scope ON unit_scope.profile_scope_id = profile.id
                         AND unit_scope.center_id = profile.center_id AND unit_scope.unit_id = @UnitId AND unit_scope.revoked_at IS NULL
                    JOIN dbo.profile_permissions permission ON permission.profile_scope_id = profile.id
                         AND permission.center_id = profile.center_id AND permission.permission_code = 'CLINICAL_DETAIL_READ'
                         AND permission.revoked_at IS NULL
                    JOIN dbo.residents resident ON resident.id = @ResidentId AND resident.center_id = profile.center_id AND resident.status = 'ACTIVE'
                    JOIN dbo.resident_location_intervals location ON location.resident_id = resident.id
                         AND location.center_id = resident.center_id AND location.unit_id = unit_scope.unit_id AND location.valid_until IS NULL
                   WHERE account.id = @AccountId AND account.status = 'ACTIVE'
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
                    BaselineVersionId.From(row.Id), row.VersionNumber, EnumCode.ParseCode<BaselineReason>(row.ReasonCode), row.SignedAt))
                .ToList();

            var resultJson = JsonSerializer.Serialize(headers, ResidAppJson.Options);
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.idempotency_operations
                   SET status = 'SUCCEEDED', result_resource_id = @ResidentId, result_json = @ResultJson, completed_at = @OccurredAt
                 WHERE account_id = @AccountId AND action_code = 'CLINICAL_DETAIL_READ'
                   AND operation_id = @OperationId AND request_hash = @RequestHash AND status = 'IN_PROGRESS'
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

    private static async Task<DraftRow?> LoadAuthorizedDraftAsync(
        IDbConnection connection, IDbTransaction transaction, SignBaselineDraftInput input, CancellationToken ct) =>
        await connection.QuerySingleOrDefaultAsync<DraftRow>(new CommandDefinition("""
            SELECT d.id AS Id, d.resident_id AS ResidentId, d.center_id AS CenterId, d.created_in_unit_id AS CreatedInUnitId,
                   d.reason_code AS ReasonCode, d.common_information_source_code AS CommonInformationSourceCode,
                   d.common_information_source_other_text AS CommonInformationSourceOtherText,
                   d.common_information_date AS CommonInformationDate, d.created_by_account_id AS CreatedByAccountId,
                   d.created_by_profile AS CreatedByProfile, d.created_at AS CreatedAt, d.draft_revision AS DraftRevision
              FROM dbo.baseline_drafts d
              JOIN dbo.residents r ON r.id = d.resident_id AND r.center_id = d.center_id
              JOIN dbo.resident_location_intervals li ON li.resident_id = d.resident_id AND li.center_id = d.center_id AND li.valid_until IS NULL
              JOIN dbo.accounts a ON a.id = @AccountId AND a.status = 'ACTIVE'
             WHERE d.id = @DraftId AND d.center_id = @CenterId AND d.resident_id = @ResidentId
               AND d.created_in_unit_id = @UnitId AND li.unit_id = @UnitId
               AND d.status = 'ACTIVE' AND d.draft_revision = @ExpectedDraftRevision
               AND d.created_by_account_id = @AccountId AND d.created_by_profile = @ActiveProfile
               AND r.status = 'ACTIVE'
               AND d.reason_code IS NOT NULL AND d.common_information_source_code IS NOT NULL AND d.common_information_date IS NOT NULL
               AND EXISTS (
                 SELECT 1 FROM dbo.profile_scopes ps
                 JOIN dbo.profile_unit_scopes pus ON pus.profile_scope_id = ps.id AND pus.center_id = ps.center_id
                      AND pus.unit_id = @UnitId AND pus.revoked_at IS NULL
                 JOIN dbo.profile_permissions pp ON pp.profile_scope_id = ps.id AND pp.center_id = ps.center_id AND pp.revoked_at IS NULL
                WHERE ps.account_id = @AccountId AND ps.center_id = @CenterId AND ps.profile_code = @ActiveProfile AND ps.status = 'ACTIVE'
                  AND pp.permission_code = CASE WHEN d.reason_code = 'ALTA' THEN 'BASELINE_INITIAL_COMPLETE' ELSE 'BASELINE_REEVALUATE' END)
            """, new
        {
            AccountId = input.AccountId.Value, DraftId = input.DraftId.Value, CenterId = input.CenterId.Value,
            ResidentId = input.ResidentId.Value, UnitId = input.UnitId.Value,
            input.ExpectedDraftRevision, ActiveProfile = input.ActiveProfile.ToCode(),
        }, transaction, cancellationToken: ct));

    private static async Task<IReadOnlyList<AreaRow>> LoadAreasAsync(IDbConnection c, IDbTransaction t, Guid draftId, CancellationToken ct) =>
        (await c.QueryAsync<AreaRow>(new CommandDefinition("""
            SELECT id AS Id, area_code AS AreaCode, catalog_version_code AS CatalogVersionCode, answer_payload AS AnswerPayload,
                   observation AS Observation, information_source_override_code AS InformationSourceOverrideCode,
                   information_source_override_other_text AS InformationSourceOverrideOtherText,
                   information_date_override AS InformationDateOverride, recorded_by_account_id AS RecordedByAccountId,
                   recorded_by_profile AS RecordedByProfile, recorded_at AS RecordedAt
              FROM dbo.baseline_draft_areas WHERE draft_id = @DraftId ORDER BY area_code
            """, new { DraftId = draftId }, t, cancellationToken: ct))).ToList();

    private static async Task<BarthelRow> LoadBarthelAsync(IDbConnection c, IDbTransaction t, Guid draftId, CancellationToken ct)
    {
        var row = await c.QuerySingleOrDefaultAsync<BarthelRow>(new CommandDefinition("""
            SELECT id AS Id, assessment_date AS AssessmentDate, total_score AS TotalScore,
                   recorded_by_account_id AS RecordedByAccountId, recorded_by_profile AS RecordedByProfile, recorded_at AS RecordedAt
              FROM dbo.baseline_draft_barthel WHERE draft_id = @DraftId
            """, new { DraftId = draftId }, t, cancellationToken: ct));
        return row is null || row.AssessmentDate is null || row.TotalScore is null
            ? throw new DomainValidationException("BASELINE_BARTHEL_INCOMPLETE")
            : row;
    }

    private static async Task<IReadOnlyList<BarthelItemRow>> LoadBarthelItemsAsync(IDbConnection c, IDbTransaction t, Guid barthelId, CancellationToken ct) =>
        (await c.QueryAsync<BarthelItemRow>(new CommandDefinition("""
            SELECT item_code AS ItemCode, selected_option_code AS SelectedOptionCode, awarded_score AS AwardedScore
              FROM dbo.baseline_draft_barthel_items WHERE barthel_id = @BarthelId ORDER BY item_code
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
            SELECT request_hash AS RequestHash, status AS Status, result_json AS ResultJson
              FROM dbo.idempotency_operations WHERE account_id = @AccountId AND action_code = 'BASELINE_SIGN' AND operation_id = @OperationId
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
            SELECT request_hash AS RequestHash, status AS Status, result_json AS ResultJson
              FROM dbo.idempotency_operations WHERE account_id = @AccountId AND action_code = 'CLINICAL_DETAIL_READ' AND operation_id = @OperationId
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

    private sealed record DraftRow(
        Guid Id, Guid ResidentId, Guid CenterId, Guid CreatedInUnitId, string ReasonCode, string CommonInformationSourceCode,
        string? CommonInformationSourceOtherText, DateOnly CommonInformationDate, Guid CreatedByAccountId,
        string CreatedByProfile, DateTimeOffset CreatedAt, int DraftRevision);

    private sealed record AreaRow(
        Guid Id, string AreaCode, string CatalogVersionCode, string AnswerPayload, string? Observation,
        string? InformationSourceOverrideCode, string? InformationSourceOverrideOtherText, DateOnly? InformationDateOverride,
        Guid RecordedByAccountId, string RecordedByProfile, DateTimeOffset RecordedAt);

    private sealed record BarthelRow(
        Guid Id, DateOnly? AssessmentDate, int? TotalScore, Guid RecordedByAccountId, string RecordedByProfile, DateTimeOffset RecordedAt);

    private sealed record BarthelItemRow(string ItemCode, string SelectedOptionCode, int AwardedScore);

    private sealed record IdempotencyRow(string RequestHash, string Status, string? ResultJson);

    private sealed record AuditedHeaderRow(Guid Id, int VersionNumber, string ReasonCode, DateTimeOffset SignedAt);
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
