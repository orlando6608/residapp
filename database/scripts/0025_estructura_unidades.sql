/*
 * Administración, estructura del centro (historia 2, ADM-05): crear, renombrar y activar o inactivar unidades desde la
 * aplicación. Hasta ahora las unidades solo se creaban por SQL.
 *
 * dbo.unidades ya existe (0002). Este script solo la protege, para que su identidad no se reescriba:
 *   - el centro, el código, el edificio, la planta y la fecha de creación no cambian nunca;
 *   - cambian el nombre visible y el estado (ACTIVE / INACTIVE);
 *   - una unidad no se borra: se inactiva, porque ubicaciones, concesiones y auditoría la referencian.
 */

CREATE TRIGGER dbo.TR_units_guard ON dbo.unidades AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
         WHERE i.centro_id <> d.centro_id OR i.creado_en <> d.creado_en
            OR i.codigo COLLATE Latin1_General_100_BIN2 <> d.codigo COLLATE Latin1_General_100_BIN2
            OR ISNULL(i.edificio_id, '00000000-0000-0000-0000-000000000000') <> ISNULL(d.edificio_id, '00000000-0000-0000-0000-000000000000')
            OR ISNULL(i.planta_id, '00000000-0000-0000-0000-000000000000') <> ISNULL(d.planta_id, '00000000-0000-0000-0000-000000000000'))
        THROW 50430, 'UNIT_IMMUTABLE_FIELD', 1;
END;
GO
CREATE TRIGGER dbo.TR_units_no_delete ON dbo.unidades INSTEAD OF DELETE AS
    THROW 50431, 'UNIT_DELETE_FORBIDDEN', 1;
GO
