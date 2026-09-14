/*
 * Añade el catálogo de opciones rápidas predefinidas por área (AUX-07,
 * docs/legado-cloudflare/docs/2026-09-02_Wireframe_funcional_Auxiliar_v0.2_cerrado.pdf), que 0004 dejó
 * pendiente: el PDF legado no se había podido leer en esa sesión. Puramente aditivo sobre 0004, sin perder
 * los cambios ya sembrados/probados: texto_libre pasa a admitir NULL (ahora es opcional cuando el área
 * tiene checklist y se marcó al menos una opción) y se añade una tabla hija para las opciones marcadas.
 */

------------------------------------------------------------
-- Índice único que la nueva tabla hija de opciones necesita para su FK compuesta (mismo patrón que
-- cierres_cotidianos_residente -> UX_ccr_scope y basales_version -> basales_version_areas).
------------------------------------------------------------
CREATE UNIQUE INDEX UX_ccca_scope ON dbo.cierres_cotidianos_cambio_areas (id, area_codigo);
GO

------------------------------------------------------------
-- texto_libre pasa a ser opcional: siete de las diez áreas tienen checklist propio (AUX-07) y basta con
-- marcar una opción, sin texto. Las tres áreas sin checklist (participación/relación social, incidencias/
-- caídas, estado de conciencia) siguen exigiendo texto no vacío, al ser su único contenido posible.
------------------------------------------------------------
ALTER TABLE dbo.cierres_cotidianos_cambio_areas DROP CONSTRAINT CK_ccca_text;
ALTER TABLE dbo.cierres_cotidianos_cambio_areas ALTER COLUMN texto_libre NVARCHAR(1000) NULL;
ALTER TABLE dbo.cierres_cotidianos_cambio_areas ADD CONSTRAINT CK_ccca_text CHECK (
    area_codigo NOT IN ('PARTICIPACION_RELACION_SOCIAL', 'INCIDENCIAS_CAIDAS', 'ESTADO_CONCIENCIA')
    OR LEN(LTRIM(RTRIM(ISNULL(texto_libre, '')))) > 0
);
GO

------------------------------------------------------------
-- Opciones rápidas marcadas por área (AUX-07): 1:N, cero o varias filas por área observada. La regla de
-- negocio "un área con checklist exige al menos una opción o texto" (RegisterDailyChange) no se repite
-- aquí como CHECK: depende de contar filas de esta tabla hija, igual que "Registrar cambio exige al menos
-- un área" ya depende de dbo.cierres_cotidianos_cambio_areas sin CHECK propio. Append-only, mismo criterio
-- que el resto del esquema clínico.
------------------------------------------------------------
CREATE TABLE dbo.cierres_cotidianos_cambio_area_opciones (
    id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_cccao PRIMARY KEY,
    area_id       UNIQUEIDENTIFIER NOT NULL,
    area_codigo   NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    opcion_codigo NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CONSTRAINT FK_cccao_area FOREIGN KEY (area_id, area_codigo) REFERENCES dbo.cierres_cotidianos_cambio_areas(id, area_codigo),
    CONSTRAINT CK_cccao_opcion CHECK (opcion_codigo IN (
        'NULA_INGESTA', 'RECHAZA_INGESTA', 'DISMINUCION_INGESTA_LIQUIDOS', 'ATRAGANTAMIENTO',
        'NO_QUIERE_LEVANTARSE', 'INCAPACIDAD_CAMINAR', 'CAMINA_CON_DIFICULTAD', 'DEBILIDAD_GENERALIZADA',
        'DISMINUCION_ANIMO', 'IRRITABILIDAD', 'AGRESIVIDAD', 'HIPERREACTIVIDAD',
        'CEFALEA', 'DOLOR_MMSS', 'DOLOR_MMII', 'DOLOR_ABDOMINAL', 'DOLOR_OTRO',
        'DIARREA', 'ESTRENIMIENTO', 'DISMINUCION_DIURESIS', 'CAMBIOS_COLORACION_ORINA',
        'INSOMNIO', 'SOMNOLENCIA',
        'HERIDA', 'UPP', 'HEMATOMA'))
);
CREATE UNIQUE INDEX UX_cccao_opcion ON dbo.cierres_cotidianos_cambio_area_opciones (area_id, opcion_codigo);
GO

CREATE TRIGGER dbo.TR_cccao_immutable ON dbo.cierres_cotidianos_cambio_area_opciones INSTEAD OF UPDATE, DELETE AS
    THROW 50302, 'DAILY_CLOSURE_CHANGE_AREA_OPTION_IMMUTABLE', 1;
GO
