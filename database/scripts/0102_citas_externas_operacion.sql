/*
 * Citas externas de los residentes, PR 2: identificador de operación al registrar (decisión de Orlando, 2026-11-18). Un doble clic en «Registrar la cita» (o reenviar el formulario) creaba dos
 * citas idénticas. El formulario lleva ahora un identificador aleatorio de operación (un GUID que genera la aplicación al abrirlo; no contiene datos personales ni de salud ni se deriva de
 * ellos) y la base impide que dos citas de la misma cuenta compartan identificador: si llega por segunda vez, la aplicación no crea otra y devuelve la ya creada. Es el patrón de las citas con la
 * familia (0054, UX_citas_operacion) y del alta de residente.
 *
 *   citas_externas.operacion_id   NULL permitido (las citas anteriores y las que registre código sin formulario no lo llevan). Índice único filtrado (cuenta, operación) cuando no es NULL.
 *                                 Inmutable, como el resto de la identidad de la cita (TR_cext_update_guard).
 *
 * Sin cambios de permisos: el GRANT de INSERT y UPDATE de 0101 cubre la columna nueva. El identificador no es dato de salud ni sale en la auditoría.
 */

SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO

ALTER TABLE dbo.citas_externas ADD operacion_id UNIQUEIDENTIFIER NULL;
GO

CREATE UNIQUE INDEX UX_cext_operacion ON dbo.citas_externas (creada_por_cuenta_id, operacion_id) WHERE operacion_id IS NOT NULL;
GO

/* Igual que en 0101, más operacion_id: una vez registrada, el identificador no cambia. */
ALTER TRIGGER dbo.TR_cext_update_guard ON dbo.citas_externas AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(id) OR UPDATE(centro_id) OR UPDATE(residente_id) OR UPDATE(creada_por_cuenta_id) OR UPDATE(creada_por_perfil) OR UPDATE(creada_en) OR UPDATE(operacion_id)
        THROW 50932, 'EXTERNAL_APPOINTMENT_IDENTITY_IMMUTABLE', 1;
    IF EXISTS (SELECT 1 FROM deleted WHERE estado_codigo IN ('REALIZADA', 'NO_ACUDIDA', 'CANCELADA'))
        THROW 50933, 'EXTERNAL_APPOINTMENT_CLOSED', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id WHERE i.version <> d.version + 1)
        THROW 50934, 'EXTERNAL_APPOINTMENT_VERSION_INVALID', 1;
END;
GO

COMMIT TRANSACTION;
GO
