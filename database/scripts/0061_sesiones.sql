/*
 * Sesiones de usuario en SQL (ADR 0007, incremento I0: capa común de sesión; cierra el hallazgo C1 junto con la cookie cifrada).
 *
 *  - Cada inicio de sesión crea una fila. La cookie (cifrada y firmada por Data Protection) solo lleva el sujeto, el id de la sesión y el
 *    nombre visible; en cada petición la aplicación lee esta fila y rechaza la sesión si está cerrada, si pasó caduca_en (caducidad
 *    absoluta), si lleva más de la inactividad permitida sin uso o si la cuenta no está activa. Así un cierre de sesión se nota en la
 *    petición siguiente aunque alguien reutilice la cookie.
 *  - caduca_en no cambia nunca: la renovación de la cookie no amplía la caducidad absoluta. ultima_actividad_en solo avanza.
 *  - Una sesión cerrada no se reabre ni cambia de motivo, y no se borra ninguna: queda como registro de quién entró y cuándo salió.
 *  - Sin seguridad por filas, como dbo.cuentas: se lee antes de que haya ámbito activo (hallazgo M1).
 */

CREATE TABLE dbo.sesiones (
    id                  UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_sesiones PRIMARY KEY,
    cuenta_id           UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_sesiones_cuenta REFERENCES dbo.cuentas(id),
    creada_en           DATETIME2(3) NOT NULL,
    caduca_en           DATETIME2(3) NOT NULL,
    ultima_actividad_en DATETIME2(3) NOT NULL,
    cerrada_en          DATETIME2(3) NULL,
    motivo_cierre       NVARCHAR(20) COLLATE Latin1_General_100_BIN2 NULL,
    CONSTRAINT CK_sesiones_fechas CHECK (caduca_en > creada_en AND ultima_actividad_en >= creada_en),
    CONSTRAINT CK_sesiones_cierre CHECK (
        (cerrada_en IS NULL AND motivo_cierre IS NULL)
        OR (cerrada_en >= creada_en AND motivo_cierre IN ('LOGOUT', 'INACTIVIDAD', 'CADUCIDAD', 'CAMBIO_IDENTIDAD')))
);
CREATE INDEX IX_sesiones_cuenta_abiertas ON dbo.sesiones (cuenta_id) WHERE cerrada_en IS NULL;
GO

CREATE TRIGGER dbo.TR_sesiones_update_guard ON dbo.sesiones AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE i.cuenta_id <> d.cuenta_id OR i.creada_en <> d.creada_en OR i.caduca_en <> d.caduca_en
           OR i.ultima_actividad_en < d.ultima_actividad_en
           OR (d.cerrada_en IS NOT NULL AND (i.cerrada_en IS NULL OR i.cerrada_en <> d.cerrada_en
               OR i.motivo_cierre <> d.motivo_cierre OR i.ultima_actividad_en <> d.ultima_actividad_en)))
        THROW 50535, 'SESSION_UPDATE_INVALID', 1;
    -- Cambiar el id dejaría deleted sin pareja en inserted.
    IF (SELECT COUNT(*) FROM deleted d JOIN inserted i ON i.id = d.id) <> (SELECT COUNT(*) FROM deleted)
        THROW 50535, 'SESSION_UPDATE_INVALID', 1;
END;
GO

CREATE TRIGGER dbo.TR_sesiones_no_delete ON dbo.sesiones INSTEAD OF DELETE AS
    THROW 50536, 'SESSION_DELETE_FORBIDDEN', 1;
GO
