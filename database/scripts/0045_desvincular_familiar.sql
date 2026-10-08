/*
 * Desvincular a un familiar de un residente (CJ, 2026-10-07; administracion-ambito-familiares-cargos, 2.1).
 *
 * Administración puede desvincular a un familiar de un residente indicando un motivo. El vínculo no se borra: se marca como desvinculado
 * (cuándo, quién y por qué) y se sigue viendo en el historial del residente. El familiar sigue con los demás residentes. Un vínculo
 * desvinculado no se puede volver a tocar; volver a vincular al mismo familiar crea un vínculo nuevo, por eso la unicidad de la pareja
 * residente-familiar pasa a ser solo entre vínculos vigentes.
 */
ALTER TABLE dbo.residentes_familiares ADD
    desvinculado_en           DATETIME2(3) NULL,
    desvinculado_por_cuenta_id UNIQUEIDENTIFIER NULL CONSTRAINT FK_rfa_desvinculado_por REFERENCES dbo.cuentas(id),
    desvinculado_motivo       NVARCHAR(500) NULL;
GO

ALTER TABLE dbo.residentes_familiares ADD CONSTRAINT CK_rfa_desvinculo CHECK (
    (desvinculado_en IS NULL AND desvinculado_por_cuenta_id IS NULL AND desvinculado_motivo IS NULL)
    OR (desvinculado_en IS NOT NULL AND desvinculado_por_cuenta_id IS NOT NULL
        AND desvinculado_motivo IS NOT NULL AND LEN(LTRIM(RTRIM(desvinculado_motivo))) > 0));
GO

DROP INDEX UX_rfa_par ON dbo.residentes_familiares;
CREATE UNIQUE INDEX UX_rfa_par ON dbo.residentes_familiares (residente_id, familiar_id) WHERE desvinculado_en IS NULL;
GO

/* Mismas reglas de siempre, y un vínculo ya desvinculado no admite ningún cambio más. */
ALTER TRIGGER dbo.TR_rfa_update_guard ON dbo.residentes_familiares AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE i.centro_id <> d.centro_id OR i.residente_id <> d.residente_id OR i.familiar_id <> d.familiar_id
           OR i.vinculado_por_cuenta_id <> d.vinculado_por_cuenta_id OR i.vinculado_en <> d.vinculado_en)
        THROW 50402, 'RESIDENT_FAMILY_LINK_UPDATE_INVALID', 1;
    IF EXISTS (SELECT 1 FROM deleted d WHERE d.desvinculado_en IS NOT NULL)
        THROW 50402, 'RESIDENT_FAMILY_LINK_UPDATE_INVALID', 1;
END;
GO
