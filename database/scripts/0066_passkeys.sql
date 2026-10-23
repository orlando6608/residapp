/*
 * Passkeys (ADR 0007, incremento I4): entrar con una credencial WebAuthn (huella, cara o PIN del dispositivo) en lugar de contraseña y
 * código TOTP. ASP.NET Core Identity las gestiona a través de IUserPasskeyStore (SqlCredentialStore).
 *
 *  - dbo.passkeys guarda por cada passkey el identificador de la credencial (único en toda la tabla: así se encuentra la cuenta al entrar sin
 *    escribir el usuario), la clave pública, el contador de firmas, los transportes y los indicadores de copia de seguridad. La clave
 *    privada no sale nunca del dispositivo de la persona. No se guarda el dominio: lo decide IdentityPasskeyOptions.ServerDomain y el
 *    navegador lo compara al ofrecer la passkey (ADR 0007, puerta abierta 5).
 *  - Sin seguridad por filas, como dbo.credenciales: se lee antes de que haya ámbito activo (hallazgo M1).
 *  - eventos_acceso admite ENTRADA_PASSKEY (abre sesión, igual que ENTRADA), PASSKEY_ALTA, PASSKEY_BAJA y PASSKEY_FALLIDA.
 */

CREATE TABLE dbo.passkeys (
    credencial_id    VARBINARY(900) NOT NULL CONSTRAINT PK_passkeys PRIMARY KEY,
    cuenta_id        UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_passkeys_cuenta REFERENCES dbo.cuentas(id),
    clave_publica    VARBINARY(1024) NOT NULL,
    nombre           NVARCHAR(100) NULL,
    creada_en        DATETIME2(3) NOT NULL,
    contador_firmas  BIGINT NOT NULL CONSTRAINT DF_passkeys_contador DEFAULT (0),
    transportes      NVARCHAR(200) NULL,
    verificada       BIT NOT NULL,
    copia_posible    BIT NOT NULL,
    copiada          BIT NOT NULL,
    CONSTRAINT CK_passkeys_contador CHECK (contador_firmas BETWEEN 0 AND 4294967295)
);
CREATE INDEX IX_passkeys_cuenta ON dbo.passkeys (cuenta_id);
GO

ALTER TABLE dbo.eventos_acceso DROP CONSTRAINT CK_eventos_acceso_tipo;
ALTER TABLE dbo.eventos_acceso ADD CONSTRAINT CK_eventos_acceso_tipo CHECK (tipo IN (
    'ENTRADA', 'ENTRADA_DESARROLLO', 'CONTRASENA_FALLIDA', 'BLOQUEO', 'SEGUNDO_FACTOR_FALLIDO', 'SEGUNDO_FACTOR_ALTA',
    'CODIGO_RECUPERACION_USADO', 'CREDENCIALES_DESARROLLO', 'INVITACION_USADA', 'RECUPERACION_USADA', 'INVITACION_NO_VALIDA',
    'CONTRASENA_CAMBIADA', 'CODIGOS_REGENERADOS', 'SESION_CERRADA', 'VINCULACION_ACEPTADA',
    'ENTRADA_PASSKEY', 'PASSKEY_ALTA', 'PASSKEY_BAJA', 'PASSKEY_FALLIDA'));
GO

-- Una entrada siempre abre una sesión de una cuenta conocida (también la de una passkey).
ALTER TABLE dbo.eventos_acceso DROP CONSTRAINT CK_eventos_acceso_entrada;
ALTER TABLE dbo.eventos_acceso ADD CONSTRAINT CK_eventos_acceso_entrada CHECK (
    tipo NOT IN ('ENTRADA', 'ENTRADA_DESARROLLO', 'ENTRADA_PASSKEY') OR (cuenta_id IS NOT NULL AND sesion_id IS NOT NULL));
GO
