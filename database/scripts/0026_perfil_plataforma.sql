/*
 * Perfil de plataforma (séptimo perfil del sistema): da de alta un centro nuevo con su primera unidad y su primer
 * administrador, sin SQL a mano. Decisión del usuario (2026-10-01); ver el ADR 0006.
 *
 * ambitos_perfil.centro_id es NOT NULL y toda la autorización trabaja con un centro, así que el operador de plataforma no
 * está «sin centro»: pertenece a un centro reservado, «Plataforma» (id fijo), que no tiene unidades ni residentes.
 *   - PLATAFORMA solo vale en el centro reservado, y en él no vale ningún otro perfil (TR_ps_platform_center).
 *   - El perfil se añade a las dos listas de perfiles válidos: ámbitos y auditoría.
 *
 * La primera cuenta de plataforma no la crea la aplicación: es la raíz de confianza. En desarrollo la pone
 * database/seed/dev_seed_plataforma.sql; en un entorno real, un INSERT único documentado en el ADR.
 */

ALTER TABLE dbo.ambitos_perfil DROP CONSTRAINT CK_ps_profile;
ALTER TABLE dbo.ambitos_perfil ADD CONSTRAINT CK_ps_profile
    CHECK (perfil_codigo IN ('AUXILIAR', 'ENFERMERIA', 'MEDICINA', 'FAMILIAR', 'ADMINISTRACION', 'DIRECCION_CLINICA', 'PLATAFORMA'));
ALTER TABLE dbo.eventos_auditoria DROP CONSTRAINT CK_audit_profile;
ALTER TABLE dbo.eventos_auditoria ADD CONSTRAINT CK_audit_profile
    CHECK (perfil_activo IN ('AUXILIAR', 'ENFERMERIA', 'MEDICINA', 'FAMILIAR', 'ADMINISTRACION', 'DIRECCION_CLINICA', 'PLATAFORMA'));
GO

IF NOT EXISTS (SELECT 1 FROM dbo.centros WHERE id = '5F3A1C00-0000-4000-8000-000000000001')
    INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en)
    VALUES ('5F3A1C00-0000-4000-8000-000000000001', 'PLATAFORMA', N'Plataforma (operación de ResidApp)', 'ACTIVE', SYSUTCDATETIME());
GO

CREATE TRIGGER dbo.TR_ps_platform_center ON dbo.ambitos_perfil AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @PlatformCenterId UNIQUEIDENTIFIER = '5F3A1C00-0000-4000-8000-000000000001';
    IF EXISTS (SELECT 1 FROM inserted WHERE perfil_codigo = 'PLATAFORMA' AND centro_id <> @PlatformCenterId)
        THROW 50440, 'PLATFORM_PROFILE_ONLY_IN_PLATFORM_CENTER', 1;
    IF EXISTS (SELECT 1 FROM inserted WHERE perfil_codigo <> 'PLATAFORMA' AND centro_id = @PlatformCenterId)
        THROW 50441, 'PLATFORM_CENTER_ONLY_PLATFORM_PROFILE', 1;
END;
GO
