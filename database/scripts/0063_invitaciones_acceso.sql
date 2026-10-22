/*
 * Invitaciones de acceso de un solo uso (ADR 0007, incremento I2).
 *
 *  - Administración (o Plataforma, para una cuenta de Administración) entrega a la persona una invitación impresa o en pantalla, con un
 *    enlace y un QR. Con ella elige su contraseña y, si es del personal, activa el segundo factor. Si la cuenta ya tenía contraseña, la
 *    invitación es de RECUPERACION: al usarla se sustituyen la contraseña y el segundo factor y se cierran sus sesiones abiertas.
 *  - Aquí solo se guarda el resumen SHA-256 del token: el token solo se enseña una vez, al emitirla.
 *  - Vale 72 horas (caduca_en). A lo sumo hay una sin usar ni anular por cuenta: emitir otra anula la anterior. Una invitación se usa o se
 *    anula una sola vez, nunca las dos cosas, y no se borra: queda como registro de quién la dio y cuándo se usó.
 *  - Sin seguridad por filas, como dbo.credenciales: se lee antes de que haya sesión.
 *  - eventos_acceso admite tres tipos nuevos y sesiones, el motivo de cierre RECUPERACION.
 */

CREATE TABLE dbo.invitaciones_acceso (
    id                    UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_invitaciones_acceso PRIMARY KEY,
    cuenta_id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_invitaciones_acceso_cuenta REFERENCES dbo.cuentas(id),
    centro_id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_invitaciones_acceso_centro REFERENCES dbo.centros(id),
    tipo                  NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    resumen_token         CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    emitida_en            DATETIME2(3) NOT NULL,
    emitida_por_cuenta_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_invitaciones_acceso_emisor REFERENCES dbo.cuentas(id),
    perfil_emisor         NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    caduca_en             DATETIME2(3) NOT NULL,
    usada_en              DATETIME2(3) NULL,
    anulada_en            DATETIME2(3) NULL,
    CONSTRAINT CK_invitaciones_acceso_tipo CHECK (tipo IN ('ALTA', 'RECUPERACION')),
    CONSTRAINT CK_invitaciones_acceso_emisor CHECK (perfil_emisor IN ('ADMINISTRACION', 'PLATAFORMA', 'DESARROLLO')),
    CONSTRAINT CK_invitaciones_acceso_fechas CHECK (caduca_en > emitida_en),
    CONSTRAINT CK_invitaciones_acceso_cierre CHECK (usada_en IS NULL OR anulada_en IS NULL)
);
CREATE UNIQUE INDEX UX_invitaciones_acceso_token ON dbo.invitaciones_acceso (resumen_token);
CREATE UNIQUE INDEX UX_invitaciones_acceso_viva ON dbo.invitaciones_acceso (cuenta_id) WHERE usada_en IS NULL AND anulada_en IS NULL;
GO

CREATE TRIGGER dbo.TR_invitaciones_acceso_update_guard ON dbo.invitaciones_acceso AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    -- Solo cambia usada_en o anulada_en, y solo de NULL a un valor.
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE i.cuenta_id <> d.cuenta_id OR i.centro_id <> d.centro_id OR i.tipo <> d.tipo OR i.resumen_token <> d.resumen_token
           OR i.emitida_en <> d.emitida_en OR i.emitida_por_cuenta_id <> d.emitida_por_cuenta_id OR i.perfil_emisor <> d.perfil_emisor
           OR i.caduca_en <> d.caduca_en
           OR (d.usada_en IS NOT NULL AND (i.usada_en IS NULL OR i.usada_en <> d.usada_en))
           OR (d.anulada_en IS NOT NULL AND (i.anulada_en IS NULL OR i.anulada_en <> d.anulada_en)))
        THROW 50539, 'ACCESS_INVITATION_UPDATE_INVALID', 1;
    IF (SELECT COUNT(*) FROM deleted d JOIN inserted i ON i.id = d.id) <> (SELECT COUNT(*) FROM deleted)
        THROW 50539, 'ACCESS_INVITATION_UPDATE_INVALID', 1;
END;
GO

CREATE TRIGGER dbo.TR_invitaciones_acceso_no_delete ON dbo.invitaciones_acceso INSTEAD OF DELETE AS
    THROW 50540, 'ACCESS_INVITATION_DELETE_FORBIDDEN', 1;
GO

ALTER TABLE dbo.eventos_acceso DROP CONSTRAINT CK_eventos_acceso_tipo;
ALTER TABLE dbo.eventos_acceso ADD CONSTRAINT CK_eventos_acceso_tipo CHECK (tipo IN (
    'ENTRADA', 'ENTRADA_DESARROLLO', 'CONTRASENA_FALLIDA', 'BLOQUEO', 'SEGUNDO_FACTOR_FALLIDO', 'SEGUNDO_FACTOR_ALTA',
    'CODIGO_RECUPERACION_USADO', 'CREDENCIALES_DESARROLLO', 'INVITACION_USADA', 'RECUPERACION_USADA', 'INVITACION_NO_VALIDA'));
GO

ALTER TABLE dbo.sesiones DROP CONSTRAINT CK_sesiones_cierre;
ALTER TABLE dbo.sesiones ADD CONSTRAINT CK_sesiones_cierre CHECK (
    (cerrada_en IS NULL AND motivo_cierre IS NULL)
    OR (cerrada_en >= creada_en AND motivo_cierre IN ('LOGOUT', 'INACTIVIDAD', 'CADUCIDAD', 'CAMBIO_IDENTIDAD', 'RECUPERACION')));
GO
