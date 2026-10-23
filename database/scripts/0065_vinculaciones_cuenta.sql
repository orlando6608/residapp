/*
 * Vincular una cuenta que ya existe en otro centro (ADR 0007, incremento I2b).
 *
 *  - La Administración del centro que da el acceso emite una vinculación: un perfil (de personal) y las unidades de su ámbito, sin decir
 *    a qué cuenta. Entrega la hoja (enlace y QR) a la persona; aquí solo se guarda el resumen SHA-256 del token y vale 72 horas.
 *  - La persona la canjea desde «Mi cuenta», ya autenticada con su contraseña y su segundo factor: así la verifica ella misma. Canjear no
 *    concede nada: deja la vinculación pendiente (canjeada_en).
 *  - La Administración del centro la confirma (se crea el ámbito de perfil y sus unidades, y se anota ambito_perfil_id) o la rechaza,
 *    tras comprobar que quien canjeó es quien tenía delante. Una vinculación se anula solo mientras nadie la ha canjeado.
 *  - Nada se borra: queda como registro de quién la dio, quién la aceptó y quién la resolvió.
 *  - Sin seguridad por filas, como dbo.invitaciones_acceso: la persona la lee desde un centro distinto del que la emitió.
 *  - eventos_acceso admite VINCULACION_ACEPTADA (la persona canjea la vinculación).
 */

CREATE TABLE dbo.vinculaciones_cuenta (
    id                     UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_vinculaciones_cuenta PRIMARY KEY,
    centro_id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_vinculaciones_cuenta_centro REFERENCES dbo.centros(id),
    perfil_codigo          NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    resumen_token          NVARCHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT UX_vinculaciones_cuenta_token UNIQUE,
    emitida_en             DATETIME2(3) NOT NULL,
    emitida_por_cuenta_id  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_vinculaciones_cuenta_emisor REFERENCES dbo.cuentas(id),
    caduca_en              DATETIME2(3) NOT NULL,
    anulada_en             DATETIME2(3) NULL,
    canjeada_en            DATETIME2(3) NULL,
    canjeada_por_cuenta_id UNIQUEIDENTIFIER NULL CONSTRAINT FK_vinculaciones_cuenta_canje REFERENCES dbo.cuentas(id),
    resuelta_en            DATETIME2(3) NULL,
    resuelta_por_cuenta_id UNIQUEIDENTIFIER NULL CONSTRAINT FK_vinculaciones_cuenta_resolucion REFERENCES dbo.cuentas(id),
    resolucion             NVARCHAR(12) COLLATE Latin1_General_100_BIN2 NULL,
    ambito_perfil_id       UNIQUEIDENTIFIER NULL CONSTRAINT FK_vinculaciones_cuenta_ambito REFERENCES dbo.ambitos_perfil(id),
    CONSTRAINT CK_vinculaciones_cuenta_perfil CHECK (perfil_codigo IN ('AUXILIAR', 'ENFERMERIA', 'MEDICINA', 'DIRECCION_CLINICA', 'ADMINISTRACION')),
    CONSTRAINT CK_vinculaciones_cuenta_fechas CHECK (caduca_en > emitida_en),
    -- Se anula solo lo que nadie ha canjeado.
    CONSTRAINT CK_vinculaciones_cuenta_anulada CHECK (anulada_en IS NULL OR canjeada_en IS NULL),
    CONSTRAINT CK_vinculaciones_cuenta_canje CHECK (
        (canjeada_en IS NULL AND canjeada_por_cuenta_id IS NULL) OR (canjeada_en IS NOT NULL AND canjeada_por_cuenta_id IS NOT NULL)),
    CONSTRAINT CK_vinculaciones_cuenta_resolucion CHECK (
        (resuelta_en IS NULL AND resuelta_por_cuenta_id IS NULL AND resolucion IS NULL AND ambito_perfil_id IS NULL)
        OR (canjeada_en IS NOT NULL AND resuelta_en IS NOT NULL AND resuelta_por_cuenta_id IS NOT NULL
            AND ((resolucion = 'CONFIRMADA' AND ambito_perfil_id IS NOT NULL) OR (resolucion = 'RECHAZADA' AND ambito_perfil_id IS NULL))))
);
CREATE INDEX IX_vinculaciones_cuenta_centro ON dbo.vinculaciones_cuenta (centro_id, emitida_en);
GO

CREATE TABLE dbo.vinculaciones_cuenta_unidades (
    vinculacion_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_vinculaciones_unidades_vinculacion REFERENCES dbo.vinculaciones_cuenta(id),
    centro_id      UNIQUEIDENTIFIER NOT NULL,
    unidad_id      UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT PK_vinculaciones_cuenta_unidades PRIMARY KEY (vinculacion_id, unidad_id),
    CONSTRAINT FK_vinculaciones_unidades_unidad FOREIGN KEY (centro_id, unidad_id) REFERENCES dbo.unidades(centro_id, id)
);
GO

-- Lo emitido no cambia; canjear, resolver o anular se hace una sola vez y no se deshace.
CREATE TRIGGER dbo.TR_vinculaciones_cuenta_update_guard ON dbo.vinculaciones_cuenta AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE i.centro_id <> d.centro_id OR i.perfil_codigo <> d.perfil_codigo OR i.resumen_token <> d.resumen_token
           OR i.emitida_en <> d.emitida_en OR i.emitida_por_cuenta_id <> d.emitida_por_cuenta_id OR i.caduca_en <> d.caduca_en
           OR (d.anulada_en IS NOT NULL AND (i.anulada_en IS NULL OR i.anulada_en <> d.anulada_en))
           OR (d.canjeada_en IS NOT NULL AND (i.canjeada_en IS NULL OR i.canjeada_en <> d.canjeada_en OR i.canjeada_por_cuenta_id <> d.canjeada_por_cuenta_id))
           OR (d.resuelta_en IS NOT NULL AND (i.resuelta_en IS NULL OR i.resuelta_en <> d.resuelta_en
               OR i.resuelta_por_cuenta_id <> d.resuelta_por_cuenta_id OR i.resolucion <> d.resolucion
               OR ISNULL(i.ambito_perfil_id, CAST(0x0 AS UNIQUEIDENTIFIER)) <> ISNULL(d.ambito_perfil_id, CAST(0x0 AS UNIQUEIDENTIFIER)))))
        THROW 50541, 'ACCOUNT_LINK_UPDATE_INVALID', 1;
    -- Cambiar el id dejaría deleted sin pareja en inserted.
    IF (SELECT COUNT(*) FROM deleted d JOIN inserted i ON i.id = d.id) <> (SELECT COUNT(*) FROM deleted)
        THROW 50541, 'ACCOUNT_LINK_UPDATE_INVALID', 1;
END;
GO

CREATE TRIGGER dbo.TR_vinculaciones_cuenta_no_delete ON dbo.vinculaciones_cuenta INSTEAD OF DELETE AS
    THROW 50542, 'ACCOUNT_LINK_DELETE_FORBIDDEN', 1;
GO

CREATE TRIGGER dbo.TR_vinculaciones_cuenta_unidades_no_change ON dbo.vinculaciones_cuenta_unidades INSTEAD OF UPDATE, DELETE AS
    THROW 50542, 'ACCOUNT_LINK_DELETE_FORBIDDEN', 1;
GO

ALTER TABLE dbo.eventos_acceso DROP CONSTRAINT CK_eventos_acceso_tipo;
ALTER TABLE dbo.eventos_acceso ADD CONSTRAINT CK_eventos_acceso_tipo CHECK (tipo IN (
    'ENTRADA', 'ENTRADA_DESARROLLO', 'CONTRASENA_FALLIDA', 'BLOQUEO', 'SEGUNDO_FACTOR_FALLIDO', 'SEGUNDO_FACTOR_ALTA',
    'CODIGO_RECUPERACION_USADO', 'CREDENCIALES_DESARROLLO', 'INVITACION_USADA', 'RECUPERACION_USADA', 'INVITACION_NO_VALIDA',
    'CONTRASENA_CAMBIADA', 'CODIGOS_REGENERADOS', 'SESION_CERRADA', 'VINCULACION_ACEPTADA'));
GO
