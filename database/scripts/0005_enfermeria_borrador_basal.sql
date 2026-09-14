/*
 * Añade el caso de uso que faltaba para crear el CONTENIDO de un borrador de basal (ENF-19 a ENF-22, grupo
 * E2 del plan de implementación): crear el borrador, completar las nueve áreas, completar Barthel y
 * cancelar el borrador propio. La firma (BaselineController/Sign) ya existía y no cambia de esquema.
 *
 * Puramente aditivo: solo extiende CK_idem_action con el único código de acción nuevo que necesita
 * idempotencia (crear el borrador). Guardar cada área o el Barthel es un upsert por clave única
 * (draft_id/area_code, draft_id) y por tanto ya es naturalmente idempotente sin tabla de idempotencia.
 */

ALTER TABLE dbo.operaciones_idempotencia DROP CONSTRAINT CK_idem_action;
ALTER TABLE dbo.operaciones_idempotencia ADD CONSTRAINT CK_idem_action
    CHECK (accion_codigo IN (
        'RESIDENT_CREATE', 'BASELINE_SIGN', 'CLINICAL_DETAIL_READ',
        'DAILY_CLOSURE_NO_CHANGE', 'DAILY_CLOSURE_NOT_ASSESSABLE', 'DAILY_CLOSURE_CHANGE_REPORTED',
        'BASELINE_DRAFT_CREATE'));
GO

/*
 * Ensancha opcion_seleccionada_codigo: NVARCHAR(32) bastaba para los códigos de ítem, pero varios códigos
 * de opción del catálogo BARTHEL_COMUN_V0_1 (docs/legado-cloudflare/docs/architecture/
 * 0002-contrato-datos-residente-basal.md) superan esa longitud, p. ej.
 * CAMINA_50M_INDEPENDIENTE_CON_AYUDA_TECNICA_SI_PRECISA (54 caracteres). Descubierto al guardar el
 * primer Barthel real con el catálogo completo. Ensanchar una columna NVARCHAR nunca trunca datos
 * existentes.
 */
ALTER TABLE dbo.basales_borrador_barthel_items ALTER COLUMN opcion_seleccionada_codigo NVARCHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL;
ALTER TABLE dbo.basales_version_barthel_items ALTER COLUMN opcion_seleccionada_codigo NVARCHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL;
GO
