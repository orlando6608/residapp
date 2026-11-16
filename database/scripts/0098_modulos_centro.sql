/*
 * Módulos opcionales por centro, incrementos 1 y 2 (ADR 0015; lectura de dpo-residapp del 2026-11-16 en docs/privacidad/modulos-opcionales-lectura-dpo.md y su segunda lectura para el
 * PR 1). Decidido por Orlando el 2026-11-16: solo Plataforma enciende y apaga, a petición del centro; no hay apagado de emergencia global; al vencer la vigencia el módulo pasa solo a solo
 * lectura (se calcula al consultar, sin tarea programada).
 *
 *   modulos_centro   UNA tabla de solo añadir: cada fila es un cambio (alta, renovación o baja) y el estado vigente de un módulo en un centro es el de su último cambio (el de mayor
 *                    numero). Plataforma no hace UPDATE ni DELETE (ADR 0008), así que «apagar» es insertar una fila DISABLED. No hay una tabla de estado y otra de historial que puedan
 *                    discrepar. Un módulo que nunca se ha activado no tiene fila.
 *
 * Lo que guarda cada fila, sin datos de residentes ni texto libre: el centro, el módulo, el estado, la vigencia (vigente_desde y vigente_hasta, obligatorias al activar: el contrato fija la
 * duración, RGPD art. 28.3), la referencia y la versión del anexo del contrato (instrucción documentada del centro, art. 28.3.a), la versión de la ficha de privacidad y los CÓDIGOS de las
 * condiciones comprobadas (no su texto), el motivo de baja (lista cerrada, sin «otro» con comentario), cuándo recibió Plataforma la petición del centro, por qué canal (lista cerrada) y su
 * referencia opaca (sin nombres), y quién lo ejecutó (cuenta y perfil) y cuándo. Sin esas tres marcas de la solicitud no se podría demostrar el plazo de apagado que fije el anexo. La cuenta
 * del operador es un dato personal seudonimizado (mismo patrón que cambiado_por_cuenta_id de administraciones_principales_cambios); la cuenta de quien se va se desactiva, no se borra, para
 * que el historial siga atribuible. Plazo de conservación: sin decidir (docs/privacidad/antes-de-datos-reales.md); por ser inmutable, un expurgo futuro exigirá un procedimiento escrito.
 *
 * Seguridad por filas (ADR 0008; salto de Plataforma como en 0090): el filtro es seg.fn_centro_o_plataforma (el centro ve sus filas; Plataforma las de todos, y las necesita para numerar y
 * para saber el estado), la inserción exige que la fila lleve como autora la cuenta del ámbito activo (seg.fn_estructura_escribe) y la tabla solo admite perfil PLATAFORMA (CK_mc_perfil): ni
 * la Administración del centro puede darse un módulo. Esto MODIFICA la matriz de 0091 (permisos del usuario de la aplicación): INSERT sí, UPDATE y DELETE denegados.
 */

SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO

CREATE TABLE dbo.modulos_centro (
    id                      UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_mc PRIMARY KEY,
    centro_id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_mc_centro REFERENCES dbo.centros(id),
    modulo_codigo           NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NOT NULL,
    numero                  INT NOT NULL,
    estado_codigo           NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    vigente_desde           DATE NULL,
    vigente_hasta           DATE NULL,
    referencia_anexo        NVARCHAR(40) COLLATE Latin1_General_100_BIN2 NULL,
    version_anexo           NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NULL,
    version_ficha           NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NULL,
    condiciones_codigos     NVARCHAR(400) COLLATE Latin1_General_100_BIN2 NULL,
    motivo_baja_codigo      NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NULL,
    solicitud_recibida_en   DATETIME2(3) NOT NULL,
    canal_codigo            NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    referencia_solicitud    NVARCHAR(40) COLLATE Latin1_General_100_BIN2 NOT NULL,
    cambiado_por_cuenta_id  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_mc_cuenta REFERENCES dbo.cuentas(id),
    cambiado_por_perfil     NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    cambiado_en             DATETIME2(3) NOT NULL,
    CONSTRAINT CK_mc_modulo CHECK (modulo_codigo IN ('DICTADO_VOZ')),
    CONSTRAINT CK_mc_numero CHECK (numero >= 1),
    CONSTRAINT CK_mc_estado CHECK (estado_codigo IN ('ACTIVE', 'DISABLED')),
    CONSTRAINT CK_mc_canal CHECK (canal_codigo IN ('CORREO', 'TELEFONO', 'TICKET', 'OTRO')),
    CONSTRAINT CK_mc_motivo CHECK (motivo_baja_codigo IS NULL OR motivo_baja_codigo IN ('PETICION_CENTRO', 'FIN_CONTRATO', 'IMPAGO', 'OTRO')),
    CONSTRAINT CK_mc_perfil CHECK (cambiado_por_perfil = 'PLATAFORMA'),
    CONSTRAINT CK_mc_referencia CHECK (LEN(LTRIM(RTRIM(referencia_solicitud))) > 0),
    CONSTRAINT CK_mc_solicitud CHECK (solicitud_recibida_en <= cambiado_en),
    -- Un alta lleva toda su base contractual y de privacidad; una baja, su motivo y nada de vigencia ni de anexo.
    CONSTRAINT CK_mc_alta CHECK (estado_codigo <> 'ACTIVE' OR (
        vigente_desde IS NOT NULL AND vigente_hasta IS NOT NULL AND vigente_hasta >= vigente_desde
        AND referencia_anexo IS NOT NULL AND LEN(LTRIM(RTRIM(referencia_anexo))) > 0
        AND version_anexo IS NOT NULL AND LEN(LTRIM(RTRIM(version_anexo))) > 0
        AND version_ficha IS NOT NULL AND LEN(LTRIM(RTRIM(version_ficha))) > 0
        AND condiciones_codigos IS NOT NULL AND motivo_baja_codigo IS NULL)),
    CONSTRAINT CK_mc_baja CHECK (estado_codigo <> 'DISABLED' OR (
        motivo_baja_codigo IS NOT NULL AND vigente_desde IS NULL AND vigente_hasta IS NULL AND referencia_anexo IS NULL
        AND version_anexo IS NULL AND version_ficha IS NULL AND condiciones_codigos IS NULL))
);
CREATE UNIQUE INDEX UX_mc_numero ON dbo.modulos_centro (centro_id, modulo_codigo, numero);
GO

CREATE TRIGGER dbo.TR_mc_immutable ON dbo.modulos_centro INSTEAD OF UPDATE, DELETE AS
    THROW 50980, 'CENTER_MODULE_IMMUTABLE', 1;
GO

/* No se activa ni se apaga nada en el centro reservado de Plataforma; el primer cambio es el número 1 y es un alta; cada uno sigue al anterior, y una baja solo sigue a un alta. */
CREATE TRIGGER dbo.TR_mc_valid ON dbo.modulos_centro AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.centros c ON c.id = i.centro_id WHERE c.codigo = 'PLATAFORMA')
        THROW 50981, 'CENTER_MODULE_CENTER_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i
          LEFT JOIN dbo.modulos_centro previous
                 ON previous.centro_id = i.centro_id AND previous.modulo_codigo = i.modulo_codigo AND previous.numero = i.numero - 1
         WHERE (i.numero = 1 AND i.estado_codigo <> 'ACTIVE')
            OR (i.numero > 1 AND (previous.id IS NULL OR (i.estado_codigo = 'DISABLED' AND previous.estado_codigo = 'DISABLED'))))
        THROW 50982, 'CENTER_MODULE_SEQUENCE_INVALID', 1;
END;
GO

-- Seguridad por filas (ver arriba). Las funciones son las de 0090.
ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_o_plataforma(centro_id) ON dbo.modulos_centro,
    ADD BLOCK PREDICATE seg.fn_estructura_escribe(centro_id, cambiado_por_cuenta_id, cambiado_por_perfil) ON dbo.modulos_centro AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.modulos_centro AFTER UPDATE;
GO

-- Permisos mínimos del usuario de la aplicación (0091): solo inserta; no actualiza ni borra.
GRANT INSERT ON dbo.modulos_centro TO residapp_aplicacion;
DENY UPDATE, DELETE ON dbo.modulos_centro TO residapp_aplicacion;
GO

COMMIT TRANSACTION;
GO
