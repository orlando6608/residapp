/*
 * Rangos de referencia de constantes: solo Dirección / Coordinación Clínica (CJ, 2026-10-07; pregunta 3.2: «solo quien coordina,
 * no Medicina»). Quien coordina fija los rangos del centro; Medicina deja de poder hacerlo.
 *
 *   - TR_pp_reference_ranges_profile solo admite DIRECCION_CLINICA para REFERENCE_RANGES_MANAGE (antes también MEDICINA).
 *   - Se revocan los permisos REFERENCE_RANGES_MANAGE vigentes de ámbitos MEDICINA. La revocación exige una cuenta
 *     (CK_pp_revocation): se usa la de quien lo concedió, porque no hay una cuenta de sistema. Nada se borra.
 *   - El historial de rangos conserva su restricción de perfil (CK_rrch_perfil): las filas anteriores hechas por Medicina siguen siendo válidas.
 */

CREATE OR ALTER TRIGGER dbo.TR_pp_reference_ranges_profile ON dbo.permisos_perfil AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
          JOIN dbo.ambitos_perfil p ON p.id = i.ambito_perfil_id
         WHERE i.permiso_codigo = 'REFERENCE_RANGES_MANAGE' AND p.perfil_codigo <> 'DIRECCION_CLINICA')
        THROW 50321, 'REFERENCE_RANGES_PERMISSION_PROFILE_INVALID', 1;
END;
GO

UPDATE pp
   SET revocado_en = SYSUTCDATETIME(), revocado_por_cuenta_id = pp.concedido_por_cuenta_id
  FROM dbo.permisos_perfil pp
  JOIN dbo.ambitos_perfil p ON p.id = pp.ambito_perfil_id AND p.centro_id = pp.centro_id
 WHERE pp.permiso_codigo = 'REFERENCE_RANGES_MANAGE' AND pp.revocado_en IS NULL AND p.perfil_codigo = 'MEDICINA';
GO

