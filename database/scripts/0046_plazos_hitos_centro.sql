/*
 * Plazos de los hitos del proceso, por centro (CJ, 2026-10-07; continuidad-supervision-comunicacion, tema 4: «individualizar según centro
 * y contexto»). DIR-11 mide siete hitos con un plazo normal y otro para los eventos prioritarios; los valores de CJ son los de partida y
 * cada centro puede cambiarlos desde una pantalla propia de Dirección Clínica con el permiso PROCESS_DEADLINES_MANAGE.
 *
 * Mismo patrón que los rangos de referencia (0008): plazos_hitos_centro guarda solo lo que el centro ha cambiado respecto a los de CJ (sin
 * fila, valen los de CJ; una fila con un plazo a NULL es «no se mide»), y todo cambio queda en un historial inmutable.
 */

CREATE TABLE dbo.plazos_hitos_centro (
    centro_id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_phc_centro REFERENCES dbo.centros(id),
    hito_codigo              NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    plazo_minutos            INT NULL,
    plazo_prioritario_minutos INT NULL,
    CONSTRAINT PK_phc PRIMARY KEY (centro_id, hito_codigo),
    CONSTRAINT CK_phc_hito CHECK (hito_codigo IN (
        'VALORACION_ENFERMERIA', 'VALORACION_MEDICA', 'RECEPCION_TRANSFERENCIA', 'INFORME_DERIVACION', 'LLAMADA_FAMILIA',
        'LECTURA_INDICACION', 'REALIZACION_INDICACION')),
    CONSTRAINT CK_phc_plazo CHECK (plazo_minutos IS NULL OR plazo_minutos BETWEEN 1 AND 10080),
    CONSTRAINT CK_phc_plazo_prioritario CHECK (plazo_prioritario_minutos IS NULL OR plazo_prioritario_minutos BETWEEN 1 AND 10080)
);
GO

CREATE TABLE dbo.plazos_hitos_centro_historial (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_phch PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_phch_centro REFERENCES dbo.centros(id),
    hito_codigo              NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    plazo_anterior_minutos   INT NULL,
    prioritario_anterior_minutos INT NULL,
    plazo_nuevo_minutos      INT NULL,
    prioritario_nuevo_minutos INT NULL,
    cambiado_por_cuenta_id   UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_phch_cuenta REFERENCES dbo.cuentas(id),
    cambiado_por_perfil      NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    cambiado_en              DATETIME2(3) NOT NULL,
    CONSTRAINT CK_phch_hito CHECK (hito_codigo IN (
        'VALORACION_ENFERMERIA', 'VALORACION_MEDICA', 'RECEPCION_TRANSFERENCIA', 'INFORME_DERIVACION', 'LLAMADA_FAMILIA',
        'LECTURA_INDICACION', 'REALIZACION_INDICACION')),
    CONSTRAINT CK_phch_perfil CHECK (cambiado_por_perfil = 'DIRECCION_CLINICA')
);
CREATE INDEX IX_phch_centro_fecha ON dbo.plazos_hitos_centro_historial (centro_id, cambiado_en);
GO

CREATE TRIGGER dbo.TR_phch_immutable ON dbo.plazos_hitos_centro_historial INSTEAD OF UPDATE, DELETE AS
    THROW 50460, 'PROCESS_DEADLINES_HISTORY_IMMUTABLE', 1;
GO

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.plazos_hitos_centro,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.plazos_hitos_centro AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.plazos_hitos_centro AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.plazos_hitos_centro_historial,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.plazos_hitos_centro_historial AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.plazos_hitos_centro_historial AFTER UPDATE;
GO

-- El permiso nuevo: solo para Dirección Clínica.
ALTER TABLE dbo.permisos_perfil DROP CONSTRAINT CK_pp_code;
ALTER TABLE dbo.permisos_perfil ADD CONSTRAINT CK_pp_code CHECK (permiso_codigo IN
    ('RESIDENT_IDENTITY_CREATE', 'BASELINE_INITIAL_COMPLETE', 'BASELINE_REEVALUATE',
     'BASELINE_DRAFT_CONTRIBUTE', 'CLINICAL_DETAIL_READ', 'REFERENCE_RANGES_MANAGE', 'PROCESS_DEADLINES_MANAGE'));
GO

ALTER TRIGGER dbo.TR_pp_profile_catalog ON dbo.permisos_perfil AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
          JOIN dbo.ambitos_perfil p ON p.id = i.ambito_perfil_id
         WHERE (i.permiso_codigo = 'RESIDENT_IDENTITY_CREATE' AND p.perfil_codigo <> 'ENFERMERIA')
            OR (i.permiso_codigo IN ('BASELINE_INITIAL_COMPLETE', 'BASELINE_REEVALUATE', 'BASELINE_DRAFT_CONTRIBUTE')
                AND p.perfil_codigo NOT IN ('ENFERMERIA', 'MEDICINA'))
            OR (i.permiso_codigo IN ('CLINICAL_DETAIL_READ', 'PROCESS_DEADLINES_MANAGE') AND p.perfil_codigo <> 'DIRECCION_CLINICA'))
        THROW 50420, 'PROFILE_PERMISSION_NOT_ALLOWED', 1;
END;
GO
