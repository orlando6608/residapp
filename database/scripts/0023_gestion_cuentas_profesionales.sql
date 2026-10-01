/*
 * Administración, bloque 3 (historia 4 sin turnos ni permisos configurables; ADM-12 y ADM-13): usuarios profesionales.
 *
 * Decisiones del usuario (2026-10-01):
 *   - Las cuentas tienen un nombre visible, obligatorio al darlas de alta desde la aplicación y editable con auditoría.
 *     Las cuentas anteriores a este script quedan sin nombre (NULL) y se muestran por su sujeto externo.
 *   - Perfiles, unidades y residentes concedidos se siguen guardando en ambitos_perfil, ambitos_perfil_unidad y
 *     ambitos_perfil_residente (0002), que solo admiten revocar; este script no los cambia.
 *
 * cuentas: el sujeto externo es el identificador con el que se entra y no cambia nunca (tampoco en mayúsculas o
 *   acentos); solo cambian el nombre visible y el estado. No se borran.
 */

ALTER TABLE dbo.cuentas ADD nombre_visible NVARCHAR(200) NULL;
GO

ALTER TABLE dbo.cuentas ADD CONSTRAINT CK_accounts_display_name
    CHECK (nombre_visible IS NULL OR LEN(LTRIM(RTRIM(nombre_visible))) > 0);
GO

CREATE TRIGGER dbo.TR_acc_update_guard ON dbo.cuentas AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE i.sujeto_externo COLLATE Latin1_General_100_BIN2 <> d.sujeto_externo COLLATE Latin1_General_100_BIN2
           OR i.creado_en <> d.creado_en)
        THROW 50410, 'ACCOUNT_UPDATE_INVALID', 1;
    -- Cambiar el id dejaría deleted sin pareja en inserted.
    IF (SELECT COUNT(*) FROM deleted d JOIN inserted i ON i.id = d.id) <> (SELECT COUNT(*) FROM deleted)
        THROW 50410, 'ACCOUNT_UPDATE_INVALID', 1;
END;
GO

CREATE TRIGGER dbo.TR_acc_no_delete ON dbo.cuentas INSTEAD OF DELETE AS
    THROW 50411, 'ACCOUNT_DELETE_FORBIDDEN', 1;
GO
