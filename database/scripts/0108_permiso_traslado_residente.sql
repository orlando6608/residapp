/*
 * Permiso RESIDENT_TRANSFER (CJ, 2026-10-07: el traslado es «inicialmente Administración»; decisión de Orlando, 2026-11-20): Enfermería puede trasladar a un
 * residente de unidad, habitación o plaza solo si Administración le concede este permiso. Es solo para Enfermería y está apagado por defecto: ninguna cuenta
 * lo tiene hasta que Administración se lo da.
 *
 * Solo cambia el catálogo de permisos (CK_pp_code y TR_pp_profile_catalog): las tablas del traslado ya admitían el perfil ENFERMERIA (CK_trr_profile y
 * CK_rli_profile). La comprobación al trasladar está en la aplicación (ResidentTransferApplicationService, con el objetivo propio AuthorizationTarget.Transfer),
 * que vuelve a leer el permiso vigente (revocado_en IS NULL) en cada traslado. La lista de permisos y el trigger repiten los de 0101 más RESIDENT_TRANSFER.
 */

ALTER TABLE dbo.permisos_perfil DROP CONSTRAINT CK_pp_code;
ALTER TABLE dbo.permisos_perfil ADD CONSTRAINT CK_pp_code CHECK (permiso_codigo IN
    ('RESIDENT_IDENTITY_CREATE', 'BASELINE_INITIAL_COMPLETE', 'BASELINE_REEVALUATE',
     'BASELINE_DRAFT_CONTRIBUTE', 'CLINICAL_DETAIL_READ', 'REFERENCE_RANGES_MANAGE', 'PROCESS_DEADLINES_MANAGE', 'RESIDENT_PHOTO_MANAGE',
     'QUALITY_REVIEW', 'HEALTH_BACKGROUND_EDIT', 'EXTERNAL_APPOINTMENT_MANAGE', 'RESIDENT_TRANSFER'));
GO

ALTER TRIGGER dbo.TR_pp_profile_catalog ON dbo.permisos_perfil AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
          JOIN dbo.ambitos_perfil p ON p.id = i.ambito_perfil_id
         WHERE (i.permiso_codigo IN ('RESIDENT_IDENTITY_CREATE', 'RESIDENT_PHOTO_MANAGE', 'EXTERNAL_APPOINTMENT_MANAGE', 'RESIDENT_TRANSFER') AND p.perfil_codigo <> 'ENFERMERIA')
            OR (i.permiso_codigo IN ('BASELINE_INITIAL_COMPLETE', 'BASELINE_REEVALUATE', 'BASELINE_DRAFT_CONTRIBUTE', 'HEALTH_BACKGROUND_EDIT')
                AND p.perfil_codigo NOT IN ('ENFERMERIA', 'MEDICINA'))
            OR (i.permiso_codigo IN ('CLINICAL_DETAIL_READ', 'PROCESS_DEADLINES_MANAGE', 'QUALITY_REVIEW') AND p.perfil_codigo <> 'DIRECCION_CLINICA'))
        THROW 50420, 'PROFILE_PERMISSION_NOT_ALLOWED', 1;
END;
GO
