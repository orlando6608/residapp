/*
 * Al dar de baja a un residente, sus citas futuras se cancelan y sus solicitudes de cita abiertas se cierran solas (CJ, portal-familiar-cuarta-tanda-cj 1.1,
 * opción B, 2026-10-28). La suspensión no las toca.
 *
 * El historial de la cita y el de la solicitud dicen que fue el sistema a causa de la baja: causa_codigo = BAJA_RESIDENTE. La autoría sigue siendo la cuenta de
 * Administración que dio la baja (no hay cuenta de sistema; mismo criterio que 0042). Cada trigger solo admite esa causa si hay una baja de ese residente
 * creada en la MISMA transacción (bajas_residente.transaccion_id, 0042): no hay otra forma de anotar una cancelación o un cierre «por la baja».
 * Los CHECK de acción no cambian: CANCELADA y CERRADA ya existían.
 */

ALTER TABLE dbo.citas_cambios ADD causa_codigo NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NULL;
GO
ALTER TABLE dbo.citas_cambios ADD CONSTRAINT CK_citas_cambios_causa CHECK (
    causa_codigo IS NULL OR (causa_codigo = 'BAJA_RESIDENTE' AND accion_codigo = 'CANCELADA'));
GO
ALTER TABLE dbo.citas_solicitudes_cambios ADD causa_codigo NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NULL;
GO
ALTER TABLE dbo.citas_solicitudes_cambios ADD CONSTRAINT CK_citas_solicitudes_cambios_causa CHECK (
    causa_codigo IS NULL OR (causa_codigo = 'BAJA_RESIDENTE' AND accion_codigo = 'CERRADA'));
GO

CREATE TRIGGER dbo.TR_citas_cambios_causa ON dbo.citas_cambios AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
         WHERE i.causa_codigo IS NOT NULL
           AND (ISNULL(i.motivo, N'') <> N'Cancelada por el sistema al dar de baja al residente.' -- motivo fijo: nada de texto libre (DPO)
                OR NOT EXISTS (SELECT 1 FROM dbo.citas c WITH (FORCESEEK)
                                 JOIN dbo.bajas_residente b ON b.residente_id = c.residente_id AND b.centro_id = c.centro_id
                                WHERE c.id = i.cita_id AND c.centro_id = i.centro_id AND b.transaccion_id = CURRENT_TRANSACTION_ID())))
        THROW 50580, 'APPOINTMENT_CAUSE_INVALID', 1;
END;
GO

CREATE TRIGGER dbo.TR_citas_solicitudes_cambios_causa ON dbo.citas_solicitudes_cambios AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
         WHERE i.causa_codigo IS NOT NULL
           AND (ISNULL(i.detalle, N'') <> N'Cerrada por el sistema al dar de baja al residente.'
                OR NOT EXISTS (SELECT 1 FROM dbo.citas_solicitudes s WITH (FORCESEEK)
                                 JOIN dbo.bajas_residente b ON b.residente_id = s.residente_id AND b.centro_id = s.centro_id
                                WHERE s.id = i.solicitud_id AND s.centro_id = i.centro_id AND b.transaccion_id = CURRENT_TRANSACTION_ID())))
        THROW 50581, 'APPOINTMENT_REQUEST_CAUSE_INVALID', 1;
END;
GO
