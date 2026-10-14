/*
 * Guía de pruebas 1, prueba 4 (CJ, 2026-10-14): registrar un cambio prioritario con el motivo «Alteración de conciencia o del estado
 * general» devolvía «No se ha podido completar la operación». Su código, ALTERACION_CONCIENCIA_ESTADO_GENERAL, mide 36 caracteres y
 * motivo_prioritario_codigo (0004) era NVARCHAR(32): SQL Server cortaba el INSERT («String or binary data would be truncated»). Los
 * otros cinco motivos miden de 23 a 26 y por eso pasaban. Se ensancha a 64, como margen para el catálogo.
 *
 * Una columna que usa un CHECK no se puede alterar: se retiran CK_ccr_change y CK_ccr_priority (0004) y se vuelven a crear idénticos.
 */

ALTER TABLE dbo.cierres_cotidianos_residente DROP CONSTRAINT CK_ccr_change;
ALTER TABLE dbo.cierres_cotidianos_residente DROP CONSTRAINT CK_ccr_priority;
GO

ALTER TABLE dbo.cierres_cotidianos_residente
    ALTER COLUMN motivo_prioritario_codigo NVARCHAR(64) COLLATE Latin1_General_100_BIN2 NULL;
GO

ALTER TABLE dbo.cierres_cotidianos_residente ADD CONSTRAINT CK_ccr_change CHECK (
    (tipo_codigo = 'CAMBIO_ENVIADO' AND clasificacion_codigo IN ('ORDINARIO', 'PRIORITARIO'))
    OR (tipo_codigo <> 'CAMBIO_ENVIADO' AND clasificacion_codigo IS NULL AND temperatura_celsius IS NULL
        AND motivo_prioritario_codigo IS NULL AND aviso_directo_documentado IS NULL)
);

ALTER TABLE dbo.cierres_cotidianos_residente ADD CONSTRAINT CK_ccr_priority CHECK (
    (clasificacion_codigo = 'PRIORITARIO' AND motivo_prioritario_codigo IS NOT NULL
        AND LEN(LTRIM(RTRIM(ISNULL(aviso_directo_documentado, '')))) > 0)
    OR (clasificacion_codigo = 'ORDINARIO' AND motivo_prioritario_codigo IS NULL AND aviso_directo_documentado IS NULL)
    OR clasificacion_codigo IS NULL
);
GO
