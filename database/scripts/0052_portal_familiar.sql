/*
 * Portal Familiar, bloque 1: la cuenta de un familiar.
 *
 * Un familiar (dbo.familiares, por centro) puede tener una cuenta con la que entra al portal. La cuenta es la misma
 * dbo.cuentas de los profesionales, con un único ámbito de perfil FAMILIAR en el centro y SIN unidades: lo que el familiar
 * ve no sale de las unidades del ámbito sino de sus vínculos con residentes y de que la autorización de cada vínculo esté
 * Activa y vigente (script 0022).
 *
 * cuenta_id solo pasa de NULL a un valor (Administración da el acceso) y no se cambia ni se quita después: si el acceso hay
 * que cortarlo, se revoca la autorización, que surte efecto en la petición siguiente. Una misma cuenta puede tener un familiar
 * en cada centro (una persona con familiares en dos centros), pero no dos en el mismo.
 *
 * La política de seguridad por filas (0030) ya cubre dbo.familiares por centro; el aislamiento entre familiares y entre
 * residentes del mismo centro lo hace la consulta del portal (FamilyPortalAccess), no la política.
 */

ALTER TABLE dbo.familiares ADD cuenta_id UNIQUEIDENTIFIER NULL
    CONSTRAINT FK_fam_cuenta REFERENCES dbo.cuentas(id);
GO

CREATE UNIQUE INDEX UX_fam_cuenta ON dbo.familiares (centro_id, cuenta_id) WHERE cuenta_id IS NOT NULL;
GO

ALTER TRIGGER dbo.TR_fam_update_guard ON dbo.familiares AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE i.centro_id <> d.centro_id OR i.creado_por_cuenta_id <> d.creado_por_cuenta_id OR i.creado_en <> d.creado_en)
        THROW 50400, 'FAMILY_MEMBER_UPDATE_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE d.cuenta_id IS NOT NULL AND (i.cuenta_id IS NULL OR i.cuenta_id <> d.cuenta_id))
        THROW 50407, 'FAMILY_PORTAL_ACCOUNT_IMMUTABLE', 1;
END;
GO
