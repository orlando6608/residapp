/*
 * Añade "Registrar cambio" (AUX-06 a AUX-12, grupos A3+A4 del plan de implementación): la tercera acción
 * de cierre cotidiano del Auxiliar, además de Sin cambios y No valorable (0003). Puramente aditivo sobre
 * 0003, sin perder los cierres ya sembrados/probados: nuevas columnas nullable en
 * dbo.cierres_cotidianos_residente, una tabla hija para las áreas y checks extendidos.
 *
 * Sin catálogo de opciones rápidas por área todavía (AUX-07): el PDF legado que las detallaba no se pudo
 * leer en esta sesión (falta poppler-utils); cada área admite solo texto libre. Añadir las opciones
 * rápidas después es un cambio aditivo sobre texto_libre, no una migración destructiva.
 */

------------------------------------------------------------
-- Extiende la idempotencia con el tercer código de acción de este vertical
------------------------------------------------------------
ALTER TABLE dbo.operaciones_idempotencia DROP CONSTRAINT CK_idem_action;
ALTER TABLE dbo.operaciones_idempotencia ADD CONSTRAINT CK_idem_action
    CHECK (accion_codigo IN (
        'RESIDENT_CREATE', 'BASELINE_SIGN', 'CLINICAL_DETAIL_READ',
        'DAILY_CLOSURE_NO_CHANGE', 'DAILY_CLOSURE_NOT_ASSESSABLE', 'DAILY_CLOSURE_CHANGE_REPORTED'));
GO

------------------------------------------------------------
-- Índice único que la tabla hija de áreas necesita para su FK compuesta (mismo patrón que
-- basales_version -> basales_version_areas: FK con centro_id/residente_id, aunque el PK ya sea solo id).
------------------------------------------------------------
CREATE UNIQUE INDEX UX_ccr_scope ON dbo.cierres_cotidianos_residente (id, residente_id, centro_id);
GO

------------------------------------------------------------
-- Columnas nuevas para el cambio (todas NULL salvo cuando tipo_codigo = 'CAMBIO_ENVIADO', CK_ccr_change
-- lo exige más abajo). Cierre único append-only, igual que el resto de la tabla.
------------------------------------------------------------
ALTER TABLE dbo.cierres_cotidianos_residente ADD
    temperatura_celsius        DECIMAL(4, 1) NULL,
    clasificacion_codigo       NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NULL,
    motivo_prioritario_codigo  NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NULL,
    aviso_directo_documentado  NVARCHAR(1000) NULL;
GO

ALTER TABLE dbo.cierres_cotidianos_residente DROP CONSTRAINT CK_ccr_type;
ALTER TABLE dbo.cierres_cotidianos_residente ADD CONSTRAINT CK_ccr_type
    CHECK (tipo_codigo IN ('SIN_CAMBIOS', 'NO_VALORABLE', 'CAMBIO_ENVIADO'));

ALTER TABLE dbo.cierres_cotidianos_residente DROP CONSTRAINT CK_ccr_reason;
ALTER TABLE dbo.cierres_cotidianos_residente ADD CONSTRAINT CK_ccr_reason CHECK (
    (tipo_codigo = 'NO_VALORABLE' AND LEN(LTRIM(RTRIM(ISNULL(motivo_no_valorable, '')))) > 0)
    OR (tipo_codigo IN ('SIN_CAMBIOS', 'CAMBIO_ENVIADO') AND motivo_no_valorable IS NULL)
);

-- Regla de negocio "Registrar cambio no puede guardarse sin clasificación" (AUX-10), repetida como CHECK
-- igual que el resto del esquema: solo un cambio enviado lleva clasificación/temperatura/aviso.
ALTER TABLE dbo.cierres_cotidianos_residente ADD CONSTRAINT CK_ccr_change CHECK (
    (tipo_codigo = 'CAMBIO_ENVIADO' AND clasificacion_codigo IN ('ORDINARIO', 'PRIORITARIO'))
    OR (tipo_codigo <> 'CAMBIO_ENVIADO' AND clasificacion_codigo IS NULL AND temperatura_celsius IS NULL
        AND motivo_prioritario_codigo IS NULL AND aviso_directo_documentado IS NULL)
);

-- "Un evento prioritario exige documentar el aviso directo antes de confirmar" y elegir motivo de
-- catálogo cerrado (AUX-10/AUX-11B); un ordinario no lleva ninguno de los dos.
ALTER TABLE dbo.cierres_cotidianos_residente ADD CONSTRAINT CK_ccr_priority CHECK (
    (clasificacion_codigo = 'PRIORITARIO' AND motivo_prioritario_codigo IS NOT NULL
        AND LEN(LTRIM(RTRIM(ISNULL(aviso_directo_documentado, '')))) > 0)
    OR (clasificacion_codigo = 'ORDINARIO' AND motivo_prioritario_codigo IS NULL AND aviso_directo_documentado IS NULL)
    OR clasificacion_codigo IS NULL
);
GO

------------------------------------------------------------
-- Áreas observadas de un cambio (AUX-06/AUX-07): 1:N, al menos una fila por cierre de tipo CAMBIO_ENVIADO
-- (exigido en la capa de aplicación; aquí solo se garantiza que cada fila referencia un cierre válido y
-- que un área no se repite dentro del mismo cierre). Append-only, igual que el resto del esquema clínico.
------------------------------------------------------------
CREATE TABLE dbo.cierres_cotidianos_cambio_areas (
    id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ccca PRIMARY KEY,
    cierre_id     UNIQUEIDENTIFIER NOT NULL,
    residente_id  UNIQUEIDENTIFIER NOT NULL,
    centro_id     UNIQUEIDENTIFIER NOT NULL,
    area_codigo   NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    texto_libre   NVARCHAR(1000) NOT NULL,
    CONSTRAINT FK_ccca_closure FOREIGN KEY (cierre_id, residente_id, centro_id) REFERENCES dbo.cierres_cotidianos_residente(id, residente_id, centro_id),
    CONSTRAINT CK_ccca_area CHECK (area_codigo IN (
        'ALIMENTACION_HIDRATACION', 'MOVILIDAD_FUNCIONALIDAD', 'ANIMO_CONDUCTA', 'DOLOR_MALESTAR', 'HECES_DIURESIS',
        'SUENO', 'LESIONES_PIEL', 'PARTICIPACION_RELACION_SOCIAL', 'INCIDENCIAS_CAIDAS', 'ESTADO_CONCIENCIA')),
    CONSTRAINT CK_ccca_text CHECK (LEN(LTRIM(RTRIM(texto_libre))) > 0)
);
CREATE UNIQUE INDEX UX_ccca_area ON dbo.cierres_cotidianos_cambio_areas (cierre_id, area_codigo);
GO

CREATE TRIGGER dbo.TR_ccca_immutable ON dbo.cierres_cotidianos_cambio_areas INSTEAD OF UPDATE, DELETE AS
    THROW 50301, 'DAILY_CLOSURE_CHANGE_AREA_IMMUTABLE', 1;
GO
