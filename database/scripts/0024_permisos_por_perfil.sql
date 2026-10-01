/*
 * Administración, bloque 4 (historia 4): permisos configurables concedidos desde la aplicación.
 *
 * Decisión del usuario (2026-10-01): la «política del centro» de la matriz la aplica su Administración, que concede y
 * revoca los permisos del catálogo de cada perfil. Este script solo impide en la BD conceder un permiso a un perfil que
 * no lo usa, como ya hace TR_pp_reference_ranges_profile (0008) con REFERENCE_RANGES_MANAGE, que se queda en ese trigger.
 *
 *   RESIDENT_IDENTITY_CREATE ................ solo ENFERMERIA (RES-02: Medicina no da altas en esta versión)
 *   BASELINE_INITIAL_COMPLETE / _REEVALUATE . ENFERMERIA o MEDICINA
 *   BASELINE_DRAFT_CONTRIBUTE ............... ENFERMERIA o MEDICINA (matriz §12; sin uso todavía)
 *   CLINICAL_DETAIL_READ .................... solo DIRECCION_CLINICA
 */

CREATE TRIGGER dbo.TR_pp_profile_catalog ON dbo.permisos_perfil AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
          JOIN dbo.ambitos_perfil p ON p.id = i.ambito_perfil_id
         WHERE (i.permiso_codigo = 'RESIDENT_IDENTITY_CREATE' AND p.perfil_codigo <> 'ENFERMERIA')
            OR (i.permiso_codigo IN ('BASELINE_INITIAL_COMPLETE', 'BASELINE_REEVALUATE', 'BASELINE_DRAFT_CONTRIBUTE')
                AND p.perfil_codigo NOT IN ('ENFERMERIA', 'MEDICINA'))
            OR (i.permiso_codigo = 'CLINICAL_DETAIL_READ' AND p.perfil_codigo <> 'DIRECCION_CLINICA'))
        THROW 50420, 'PROFILE_PERMISSION_NOT_ALLOWED', 1;
END;
GO
