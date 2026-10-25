/*
 * Foto del residente con las respuestas de CJ del 2026-10-25 (docs/pendientes-cj/foto-residente-confirmaciones-cj, 1.1, 1.2, 1.3 y 2.2):
 *
 *  - El consentimiento se guarda con la foto: quién lo firmó (el residente o su representante) y la fecha de la firma (1.1). Las fotos que ya
 *    había no lo tienen y no se puede inventar: se borran todas antes de añadir las columnas (decisión de Orlando). Con ellas se van también las
 *    de los residentes ya dados de baja. No se audita una por una: este script es la constancia.
 *  - fotos_residente_negativas: la marca «No quiere foto» (1.2), una por residente. Anotarla borra la foto y, mientras exista, no se pone otra
 *    (lo comprueba la aplicación bajo el bloqueo del residente).
 *  - RESIDENT_PHOTO_MANAGE: el permiso que Administración da a Enfermería para poner, cambiar o quitar la foto (1.3). Solo para Enfermería; las
 *    Enfermerías que ya existen no lo tienen hasta que se lo den.
 *  - Al dar de baja se borra la foto (2.2): lo hace la aplicación en la transacción de la baja.
 */

DELETE FROM dbo.fotos_residente;
GO

ALTER TABLE dbo.fotos_residente ADD
    consentimiento_firmante_codigo NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL
        CONSTRAINT CK_fotos_residente_firmante CHECK (consentimiento_firmante_codigo IN ('RESIDENTE', 'REPRESENTANTE')),
    consentimiento_fecha DATE NOT NULL;
GO

ALTER TABLE dbo.fotos_residente ADD CONSTRAINT CK_fotos_residente_consentimiento_fecha CHECK (consentimiento_fecha <= CAST(actualizada_en AS DATE));
GO

CREATE TABLE dbo.fotos_residente_negativas (
    residente_id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_fotos_residente_negativas PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL,
    anotada_por_cuenta_id    UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_frn_cuenta REFERENCES dbo.cuentas(id),
    anotada_por_perfil       NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NOT NULL,
    anotada_en               DATETIME2(3) NOT NULL,
    CONSTRAINT FK_frn_residente FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes(centro_id, id),
    CONSTRAINT CK_frn_perfil CHECK (anotada_por_perfil IN ('ADMINISTRACION', 'ENFERMERIA'))
);
GO

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.fotos_residente_negativas,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.fotos_residente_negativas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.fotos_residente_negativas AFTER UPDATE;
GO

ALTER TABLE dbo.permisos_perfil DROP CONSTRAINT CK_pp_code;
ALTER TABLE dbo.permisos_perfil ADD CONSTRAINT CK_pp_code CHECK (permiso_codigo IN
    ('RESIDENT_IDENTITY_CREATE', 'BASELINE_INITIAL_COMPLETE', 'BASELINE_REEVALUATE',
     'BASELINE_DRAFT_CONTRIBUTE', 'CLINICAL_DETAIL_READ', 'REFERENCE_RANGES_MANAGE', 'PROCESS_DEADLINES_MANAGE', 'RESIDENT_PHOTO_MANAGE'));
GO

ALTER TRIGGER dbo.TR_pp_profile_catalog ON dbo.permisos_perfil AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
          JOIN dbo.ambitos_perfil p ON p.id = i.ambito_perfil_id
         WHERE (i.permiso_codigo IN ('RESIDENT_IDENTITY_CREATE', 'RESIDENT_PHOTO_MANAGE') AND p.perfil_codigo <> 'ENFERMERIA')
            OR (i.permiso_codigo IN ('BASELINE_INITIAL_COMPLETE', 'BASELINE_REEVALUATE', 'BASELINE_DRAFT_CONTRIBUTE')
                AND p.perfil_codigo NOT IN ('ENFERMERIA', 'MEDICINA'))
            OR (i.permiso_codigo IN ('CLINICAL_DETAIL_READ', 'PROCESS_DEADLINES_MANAGE') AND p.perfil_codigo <> 'DIRECCION_CLINICA'))
        THROW 50420, 'PROFILE_PERMISSION_NOT_ALLOWED', 1;
END;
GO
