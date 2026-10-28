/*
 * Historial de un residente trasladado (CJ, confirmaciones de la tercera tanda 5.2 C; decisiones de Orlando, 2026-10-26).
 *
 * Los episodios cerrados no se mueven con el traslado (0040), así que la unidad de destino no los ve. La unidad de destino puede abrirlos solo si lo pide
 * con un motivo: el historial, la línea temporal y el detalle de los episodios cerrados en la unidad anterior (no el informe de derivación). La petición es una
 * declaración por cuenta y residente que vale 1 hora y termina al cambiar de residente, de perfil o de ámbito y al cerrar sesión (como la de Dirección, 0038).
 *
 * dbo.declaraciones_historial_traslado: una declaración solo se crea y se termina. Cada lectura con ella escribe antes su fila de auditoría TRANSFER_HISTORY_READ,
 * con el motivo (eventos_auditoria no tiene lista cerrada de acciones). La consulta que entrega los episodios la vuelve a comprobar (cuenta, ámbito, residente,
 * hora y que no haya terminado), así que una cookie manipulada o ajena no concede nada.
 */

CREATE TABLE dbo.declaraciones_historial_traslado (
    id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_dht PRIMARY KEY,
    centro_id         UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_dht_center REFERENCES dbo.centros(id),
    cuenta_id         UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_dht_account REFERENCES dbo.cuentas(id),
    ambito_perfil_id  UNIQUEIDENTIFIER NOT NULL,
    residente_id      UNIQUEIDENTIFIER NOT NULL,
    motivo            NVARCHAR(300) NOT NULL,
    creada_en         DATETIME2(3) NOT NULL,
    caduca_en         DATETIME2(3) NOT NULL,
    terminada_en      DATETIME2(3) NULL,
    CONSTRAINT FK_dht_scope FOREIGN KEY (ambito_perfil_id, centro_id) REFERENCES dbo.ambitos_perfil(id, centro_id),
    CONSTRAINT FK_dht_resident FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes(centro_id, id),
    CONSTRAINT CK_dht_reason CHECK (LEN(LTRIM(RTRIM(motivo))) > 0),
    CONSTRAINT CK_dht_expiry CHECK (caduca_en > creada_en),
    CONSTRAINT CK_dht_end CHECK (terminada_en IS NULL OR terminada_en >= creada_en)
);
CREATE INDEX IX_dht_account_open ON dbo.declaraciones_historial_traslado (cuenta_id, terminada_en, caduca_en);
GO

/* Solo se puede terminar una declaración abierta; ningún otro cambio ni borrado. */
CREATE TRIGGER dbo.TR_dht_guard ON dbo.declaraciones_historial_traslado INSTEAD OF UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM deleted) RETURN; -- una sentencia que no afecta a ninguna fila
    IF NOT EXISTS (SELECT 1 FROM inserted) -- DELETE
        THROW 50570, 'TRANSFER_HISTORY_DECLARATION_IMMUTABLE', 1;

    IF EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
         WHERE d.terminada_en IS NOT NULL OR i.terminada_en IS NULL
            OR i.centro_id <> d.centro_id OR i.cuenta_id <> d.cuenta_id OR i.ambito_perfil_id <> d.ambito_perfil_id
            OR i.residente_id <> d.residente_id OR i.motivo <> d.motivo
            OR i.creada_en <> d.creada_en OR i.caduca_en <> d.caduca_en)
        THROW 50570, 'TRANSFER_HISTORY_DECLARATION_IMMUTABLE', 1;

    UPDATE t SET terminada_en = i.terminada_en
      FROM dbo.declaraciones_historial_traslado t WITH (FORCESEEK) JOIN inserted i ON i.id = t.id;
END;
GO

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.declaraciones_historial_traslado,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.declaraciones_historial_traslado AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.declaraciones_historial_traslado AFTER UPDATE;
GO
