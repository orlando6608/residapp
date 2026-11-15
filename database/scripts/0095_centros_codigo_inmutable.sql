/*
 * El código de un centro no cambia nunca (paralelo a TR_units_guard, 0025, para las unidades). Hasta ahora solo era una
 * convención de la aplicación: ninguna pantalla ni caso de uso lo cambia (el único UPDATE sobre dbo.centros es el nombre,
 * «Nombre y logo del centro»), pero la base no lo impedía.
 *
 * El código es un identificador técnico de la organización (no identifica a ninguna persona): lo escriben las semillas y los
 * scripts de operaciones, y la aplicación lo muestra en «Centros» y en el detalle del centro. Desde que la aplicación lo genera
 * a partir del nombre (alta de centro), una errata del nombre se hereda en el código; por eso conviene que no se pueda
 * reescribir por debajo sin dejar rastro.
 *
 * Solo protege el código. El nombre visible, el estado y lo demás siguen igual, y no se bloquea el borrado (la aplicación no
 * borra centros: ADR 0006).
 *
 * La comparación es binaria (Latin1_General_100_BIN2): la columna es insensible a mayúsculas, así que sin ella cambiar «abc» por
 * «ABC» no se detectaría.
 */

CREATE TRIGGER dbo.TR_centers_guard ON dbo.centros AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
         WHERE i.codigo COLLATE Latin1_General_100_BIN2 <> d.codigo COLLATE Latin1_General_100_BIN2)
        THROW 50432, 'CENTER_IMMUTABLE_FIELD', 1;
END;
GO
