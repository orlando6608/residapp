/*
 * Credenciales y registro de accesos (ADR 0007, incremento I1: ASP.NET Core Identity propio).
 *
 *  - El usuario de Identity es la cuenta: su id es cuentas.id y su nombre de usuario, sujeto_externo (el «Identificador de acceso» que
 *    pone Administración). Identity solo guarda aquí lo que custodia: el hash de la contraseña, el sello de seguridad, el bloqueo por
 *    intentos y el segundo factor (clave TOTP y códigos de recuperación). Una cuenta sin fila no puede entrar con contraseña.
 *  - Los códigos de recuperación se guardan como resúmenes SHA-256 (no en claro, como hace el almacén por defecto de Identity). La clave
 *    TOTP va en claro, como en el almacén por defecto: hace falta para calcular el código.
 *  - eventos_acceso es el registro de seguridad de la autenticación, separado de la auditoría clínica (eventos_auditoria): entradas,
 *    contraseñas y segundos factores fallidos, bloqueos y uso de códigos de recuperación. Inmutable. Si el usuario escrito no existe,
 *    cuenta_id es NULL: nunca se guarda lo tecleado (podría ser una contraseña escrita en el campo equivocado).
 *  - Sin seguridad por filas, como dbo.cuentas y dbo.sesiones: se leen y escriben antes de que haya ámbito activo (hallazgo M1).
 */

CREATE TABLE dbo.credenciales (
    cuenta_id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_credenciales PRIMARY KEY
                          CONSTRAINT FK_credenciales_cuenta REFERENCES dbo.cuentas(id),
    hash_contrasena       NVARCHAR(512) NULL,
    sello_seguridad       NVARCHAR(64) NULL,
    clave_autenticador    NVARCHAR(64) NULL,
    segundo_factor_activo BIT NOT NULL CONSTRAINT DF_credenciales_2fa DEFAULT (0),
    intentos_fallidos     INT NOT NULL CONSTRAINT DF_credenciales_intentos DEFAULT (0),
    bloqueada_hasta       DATETIMEOFFSET(3) NULL,
    codigos_recuperacion  NVARCHAR(MAX) NULL,
    actualizada_en        DATETIME2(3) NOT NULL,
    CONSTRAINT CK_credenciales_intentos CHECK (intentos_fallidos >= 0),
    -- Un segundo factor activo necesita su clave.
    CONSTRAINT CK_credenciales_2fa CHECK (segundo_factor_activo = 0 OR clave_autenticador IS NOT NULL)
);
GO

CREATE TABLE dbo.eventos_acceso (
    id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_eventos_acceso PRIMARY KEY,
    ocurrido_en DATETIME2(3) NOT NULL,
    cuenta_id   UNIQUEIDENTIFIER NULL CONSTRAINT FK_eventos_acceso_cuenta REFERENCES dbo.cuentas(id),
    tipo        NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    sesion_id   UNIQUEIDENTIFIER NULL CONSTRAINT FK_eventos_acceso_sesion REFERENCES dbo.sesiones(id),
    ip          NVARCHAR(45) NULL,
    CONSTRAINT CK_eventos_acceso_tipo CHECK (tipo IN (
        'ENTRADA', 'ENTRADA_DESARROLLO', 'CONTRASENA_FALLIDA', 'BLOQUEO', 'SEGUNDO_FACTOR_FALLIDO', 'SEGUNDO_FACTOR_ALTA',
        'CODIGO_RECUPERACION_USADO', 'CREDENCIALES_DESARROLLO')),
    -- Una entrada siempre abre una sesión de una cuenta conocida.
    CONSTRAINT CK_eventos_acceso_entrada CHECK (
        tipo NOT IN ('ENTRADA', 'ENTRADA_DESARROLLO') OR (cuenta_id IS NOT NULL AND sesion_id IS NOT NULL))
);
CREATE INDEX IX_eventos_acceso_cuenta ON dbo.eventos_acceso (cuenta_id, ocurrido_en);
GO

CREATE TRIGGER dbo.TR_eventos_acceso_no_update ON dbo.eventos_acceso INSTEAD OF UPDATE AS
    THROW 50537, 'ACCESS_EVENT_UPDATE_FORBIDDEN', 1;
GO

CREATE TRIGGER dbo.TR_eventos_acceso_no_delete ON dbo.eventos_acceso INSTEAD OF DELETE AS
    THROW 50538, 'ACCESS_EVENT_DELETE_FORBIDDEN', 1;
GO
