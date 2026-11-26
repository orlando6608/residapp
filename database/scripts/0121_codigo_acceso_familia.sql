/*
 * Código de acceso por correo para la familia (ADR 0012, pieza 3; docs/privacidad/codigo-acceso-familiar-por-correo.md, borrador por validar).
 *
 * Una fila por código que la aplicación intenta mandar. Sirve para comprobar el código, para los límites de envío y de fallos y para explicar un «no me llegó»
 * (también es el registro de envíos de este correo: dbo.envios_correo exige centro, y aquí todavía no hay ámbito).
 *
 *   resumen              HMAC-SHA256 (con una clave que está fuera de la base) del identificador de la fila, la cuenta, la dirección y el código: ni el código ni la
 *                        dirección se guardan, y con la base sola no se puede probar el millón de códigos. Solo lo tiene un código VIVO; al cerrarse se borra.
 *   motivo_cierre        USADO, ANULADO (se pidió otro o se usó una invitación), AGOTADO (5 fallos), NO_ENVIADO (el correo no salió) o CADUCADO (lo cierra la purga).
 *   cuenta_para_limites  0 al usar una invitación: los envíos y fallos anteriores dejan de contar.
 *   envio_*              Estado y motivo cerrado del envío, nunca el texto de una excepción. ENVIADO es que el proveedor lo aceptó, no que se entregó.
 * El familiar se deduce de la cuenta: aquí no hay centro, residente, nombre, IP ni navegador.
 *
 * Sin seguridad por filas, como dbo.credenciales: se lee y escribe antes de que haya ámbito activo. Conservación: 30 días desde que se creó (propuesta de ingeniería, no un
 * plazo legal; la decide el DPO), con dbo.purgar_codigos_acceso, que solo ejecuta la tarea programada (rol residapp_tareas).
 *
 * dbo.credenciales.codigo_correo_bloqueado_en: con 15 códigos fallidos en 24 horas la cuenta no entra hasta que use una invitación de recuperación.
 */

SET XACT_ABORT ON;
BEGIN TRANSACTION;

CREATE TABLE dbo.codigos_acceso_correo (
    id                   UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_codigos_acceso_correo PRIMARY KEY,
    cuenta_id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_codigos_acceso_cuenta REFERENCES dbo.cuentas(id),
    resumen              VARBINARY(32) NULL,
    creado_en            DATETIME2(3) NOT NULL,
    caduca_en            DATETIME2(3) NOT NULL,
    intentos_fallidos    TINYINT NOT NULL CONSTRAINT DF_codigos_acceso_intentos DEFAULT (0),
    cerrado_en           DATETIME2(3) NULL,
    motivo_cierre        NVARCHAR(12) COLLATE Latin1_General_100_BIN2 NULL,
    cuenta_para_limites  BIT NOT NULL CONSTRAINT DF_codigos_acceso_limites DEFAULT (1),
    plantilla_version    NVARCHAR(8) COLLATE Latin1_General_100_BIN2 NOT NULL,
    idioma               NVARCHAR(2) COLLATE Latin1_General_100_BIN2 NOT NULL,
    envio_estado_codigo  NVARCHAR(10) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_codigos_acceso_envio DEFAULT (N'PENDIENTE'),
    envio_motivo_codigo  NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NULL,
    acs_operacion_id     NVARCHAR(64) NULL,
    CONSTRAINT CK_codigos_acceso_fechas CHECK (caduca_en > creado_en),
    CONSTRAINT CK_codigos_acceso_idioma CHECK (idioma IN ('es', 'ca')),
    CONSTRAINT CK_codigos_acceso_cierre CHECK (
        (cerrado_en IS NULL AND motivo_cierre IS NULL AND resumen IS NOT NULL)
        OR (cerrado_en IS NOT NULL AND motivo_cierre IS NOT NULL AND motivo_cierre IN ('USADO', 'ANULADO', 'AGOTADO', 'NO_ENVIADO', 'CADUCADO') AND resumen IS NULL)),
    CONSTRAINT CK_codigos_acceso_envio CHECK (
        (envio_estado_codigo IN ('PENDIENTE', 'ENVIADO') AND envio_motivo_codigo IS NULL)
        OR (envio_estado_codigo = 'FALLIDO' AND envio_motivo_codigo IS NOT NULL
            AND envio_motivo_codigo IN ('DESTINO_NO_PERMITIDO', 'ACS_RECHAZADO', 'ACS_NO_DISPONIBLE', 'TIEMPO_AGOTADO')))
);

CREATE INDEX IX_codigos_acceso_cuenta ON dbo.codigos_acceso_correo (cuenta_id, creado_en);
-- Un solo código vivo por cuenta.
CREATE UNIQUE INDEX UX_codigos_acceso_vivo ON dbo.codigos_acceso_correo (cuenta_id) WHERE cerrado_en IS NULL;

ALTER TABLE dbo.credenciales ADD codigo_correo_bloqueado_en DATETIME2(3) NULL;
GO

-- Un código no cambia de cuenta ni de plazo, sus fallos no bajan y, cerrado, no vuelve a estar vivo.
CREATE TRIGGER dbo.TR_codigos_acceso_guard ON dbo.codigos_acceso_correo AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.id = d.id
         WHERE i.id IS NULL OR i.cuenta_id <> d.cuenta_id OR i.creado_en <> d.creado_en OR i.caduca_en <> d.caduca_en
            OR i.intentos_fallidos < d.intentos_fallidos
            OR (d.cerrado_en IS NOT NULL AND (i.cerrado_en IS NULL OR i.cerrado_en <> d.cerrado_en OR i.motivo_cierre <> d.motivo_cierre))
            OR (i.resumen IS NOT NULL AND (d.resumen IS NULL OR i.resumen <> d.resumen)))
        THROW 51180, 'ACCESS_CODE_UPDATE_INVALID', 1;
END;
GO

CREATE PROCEDURE dbo.purgar_codigos_acceso
    @lote INT = 2000
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    IF @lote < 1 OR @lote > 10000
        THROW 51181, 'ACCESS_CODE_PURGE_BATCH_INVALID', 1;

    DECLARE @ahora DATETIME2(3) = SYSUTCDATETIME();
    -- Un código caducado que nadie cerró pierde su resumen.
    UPDATE dbo.codigos_acceso_correo SET resumen = NULL, cerrado_en = @ahora, motivo_cierre = N'CADUCADO'
     WHERE cerrado_en IS NULL AND caduca_en < @ahora;

    DECLARE @limite DATETIME2(3) = DATEADD(DAY, -30, @ahora), @borradas INT = 0, @lote_borrado INT = 1;
    WHILE @lote_borrado > 0
    BEGIN
        DELETE TOP (@lote) FROM dbo.codigos_acceso_correo WHERE creado_en < @limite;
        SET @lote_borrado = @@ROWCOUNT;
        SET @borradas += @lote_borrado;
    END;

    SELECT @borradas AS borradas;
END;
GO

GRANT INSERT, UPDATE ON dbo.codigos_acceso_correo TO residapp_aplicacion;
DENY DELETE ON dbo.codigos_acceso_correo TO residapp_aplicacion;
IF DATABASE_PRINCIPAL_ID(N'residapp_tareas') IS NOT NULL
    GRANT EXECUTE ON OBJECT::dbo.purgar_codigos_acceso TO residapp_tareas;
GO

COMMIT TRANSACTION;
GO
