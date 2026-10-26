/*
 * Permiso QUALITY_REVIEW (CJ, confirmaciones de la tercera tanda 2.1 y aclaraciones 3.1; decisión de Orlando, 2026-10-26): Indicadores e informe de
 * actividad y Calidad de proceso, las dos pantallas de cifras agregadas de Dirección Clínica, solo se abren con este permiso, que concede
 * Administración. Es solo para Dirección Clínica. Las Direcciones que ya existen no lo tienen hasta que Administración se lo dé.
 *
 * Solo cambia el catálogo de permisos (CK_pp_code y TR_pp_profile_catalog): la comprobación al leer está en la aplicación y en
 * SqlSupervisionDirectory. La lista de permisos y el trigger repiten los de 0071 más QUALITY_REVIEW.
 */

ALTER TABLE dbo.permisos_perfil DROP CONSTRAINT CK_pp_code;
ALTER TABLE dbo.permisos_perfil ADD CONSTRAINT CK_pp_code CHECK (permiso_codigo IN
    ('RESIDENT_IDENTITY_CREATE', 'BASELINE_INITIAL_COMPLETE', 'BASELINE_REEVALUATE',
     'BASELINE_DRAFT_CONTRIBUTE', 'CLINICAL_DETAIL_READ', 'REFERENCE_RANGES_MANAGE', 'PROCESS_DEADLINES_MANAGE', 'RESIDENT_PHOTO_MANAGE',
     'QUALITY_REVIEW'));
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
            OR (i.permiso_codigo IN ('CLINICAL_DETAIL_READ', 'PROCESS_DEADLINES_MANAGE', 'QUALITY_REVIEW') AND p.perfil_codigo <> 'DIRECCION_CLINICA'))
        THROW 50420, 'PROFILE_PERMISSION_NOT_ALLOWED', 1;
END;
GO
