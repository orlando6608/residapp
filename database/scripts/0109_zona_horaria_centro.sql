/*
 * Zona horaria por centro, F1 (estudio docs/producto/zonas-horarias-por-centro-estudio.md; lectura de dpo-residapp en docs/privacidad/zona-horaria-centro-lectura-dpo.md).
 * Decidido por Orlando el 2026-11-20: la zona se elige al crear el centro entre las que define Plataforma y después solo Plataforma puede corregirla, con un motivo de una lista cerrada
 * y constancia; Plataforma y su centro reservado usan siempre la hora de la península; al corregir una zona, Plataforma ve cuántas franjas y citas se ven afectadas y lo confirma.
 *
 *   zonas_horarias   El catálogo de zonas que Plataforma ofrece: el nombre estándar (IANA, el que usa la aplicación), el nombre de Windows (el único que entiende AT TIME ZONE en SQL
 *                    Server y Azure SQL), la etiqueta que ve la persona y el orden. Sin datos personales. Solo lo escriben los scripts: añadir una zona es un INSERT en un script nuevo,
 *                    sin tocar ninguna restricción. Este script solo da de alta zonas de España.
 *   zonas_centro     UNA tabla de solo añadir, como modulos_centro (0098): cada fila es la zona de un centro desde un momento y la vigente es la de mayor numero. El numero 1 es el alta del
 *                    centro (motivo ALTA); las correcciones siguientes llevan un motivo de lista cerrada (ERROR_DE_ALTA o CAMBIO_DE_UBICACION; sin texto libre) y los recuentos de franjas
 *                    y de citas que Plataforma vio y confirmó (sin identificar a nadie). Un centro SIN fila usa Europe/Madrid: los centros que ya existen no se tocan ni se rellenan.
 *                    Plataforma no hace UPDATE ni DELETE (ADR 0008): corregir es añadir una fila. El centro reservado de Plataforma no puede tener fila: usa siempre Madrid.
 *
 * Todo sigue guardándose en UTC: cambiar la zona de un centro no toca ningún dato guardado, solo cómo se muestran las horas y qué es «hoy» (CenterTime). Las franjas de citas se guardan
 * como hora local sin zona, así que sus horas reales se mueven con la zona; las citas ya reservadas (UTC) no.
 *
 * Seguridad por filas (ADR 0008; salto de Plataforma como en 0090 y 0098): el filtro es seg.fn_centro_o_plataforma (el centro ve su fila, que la aplicación lee en cada petición para
 * mostrar sus horas; Plataforma las de todos), la inserción exige que la fila lleve como autora la cuenta del ámbito activo (seg.fn_estructura_escribe) y solo admite perfil PLATAFORMA
 * (CK_zc_perfil): ni la Administración del centro puede cambiar su zona. La cuenta del operador es un dato personal seudonimizado (mismo patrón que modulos_centro).
 * Plazo de conservación: sin decidir (docs/privacidad/antes-de-datos-reales.md); por ser inmutable, un expurgo futuro exigirá un procedimiento escrito.
 */

SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO

CREATE TABLE dbo.zonas_horarias (
    codigo          NVARCHAR(40) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT PK_zonas_horarias PRIMARY KEY,
    nombre_windows  NVARCHAR(60) NOT NULL,
    etiqueta        NVARCHAR(80) NOT NULL,
    orden           TINYINT NOT NULL,
    activa          BIT NOT NULL CONSTRAINT DF_zonas_horarias_activa DEFAULT 1,
    CONSTRAINT CK_zh_etiqueta CHECK (LEN(LTRIM(RTRIM(etiqueta))) > 0),
    CONSTRAINT CK_zh_windows CHECK (LEN(LTRIM(RTRIM(nombre_windows))) > 0)
);
GO

-- Antes de dar de alta una zona se comprueba que este servidor conoce su nombre de Windows: si no, AT TIME ZONE fallaría más tarde, en un procedimiento de avisos.
IF NOT EXISTS (SELECT 1 FROM sys.time_zone_info WHERE name = N'Romance Standard Time')
    THROW 50990, 'TIMEZONE_WINDOWS_NAME_UNKNOWN', 1;
IF NOT EXISTS (SELECT 1 FROM sys.time_zone_info WHERE name = N'GMT Standard Time')
    THROW 50990, 'TIMEZONE_WINDOWS_NAME_UNKNOWN', 1;
GO

-- Canarias queda dada de alta pero INACTIVA: no se ofrece hasta que los avisos por correo y los procedimientos de SQL usen la zona del centro (F2) y se haya probado de punta a punta (F3,
-- con su propio script, que la activa). Antes, un centro de Canarias vería sus pantallas a su hora y recibiría los avisos con la de la península.
INSERT INTO dbo.zonas_horarias (codigo, nombre_windows, etiqueta, orden, activa)
VALUES (N'Europe/Madrid', N'Romance Standard Time', N'Península y Baleares (Madrid)', 1, 1),
       (N'Atlantic/Canary', N'GMT Standard Time', N'Canarias', 2, 0);
GO

-- Sin permisos de escritura para la aplicación (como entorno_bd): no hay GRANT ni DENY de INSERT, UPDATE ni DELETE y el catálogo solo lo cambian los scripts. La aplicación lo lee con su rol de lectura.
GO

CREATE TABLE dbo.zonas_centro (
    id                      UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_zonas_centro PRIMARY KEY,
    centro_id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_zc_centro REFERENCES dbo.centros(id),
    numero                  INT NOT NULL,
    zona_codigo             NVARCHAR(40) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT FK_zc_zona REFERENCES dbo.zonas_horarias(codigo),
    motivo_codigo           NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NOT NULL,
    franjas_afectadas       INT NOT NULL,
    citas_afectadas         INT NOT NULL,
    cambiado_por_cuenta_id  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_zc_cuenta REFERENCES dbo.cuentas(id),
    cambiado_por_perfil     NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    cambiado_en             DATETIME2(3) NOT NULL,
    CONSTRAINT CK_zc_numero CHECK (numero >= 1),
    CONSTRAINT CK_zc_motivo CHECK (motivo_codigo IN ('ALTA', 'ERROR_DE_ALTA', 'CAMBIO_DE_UBICACION')),
    -- El alta es la fila 1 y no afecta a nada; toda corrección (fila 2 en adelante) lleva un motivo que no es ALTA.
    CONSTRAINT CK_zc_alta CHECK ((numero = 1 AND motivo_codigo = 'ALTA' AND franjas_afectadas = 0 AND citas_afectadas = 0) OR (numero > 1 AND motivo_codigo <> 'ALTA')),
    CONSTRAINT CK_zc_recuentos CHECK (franjas_afectadas >= 0 AND citas_afectadas >= 0),
    CONSTRAINT CK_zc_perfil CHECK (cambiado_por_perfil = 'PLATAFORMA')
);
CREATE UNIQUE INDEX UX_zc_numero ON dbo.zonas_centro (centro_id, numero);
GO

CREATE TRIGGER dbo.TR_zc_immutable ON dbo.zonas_centro INSTEAD OF UPDATE, DELETE AS
    THROW 50991, 'CENTER_TIMEZONE_IMMUTABLE', 1;
GO

/* El centro reservado de Plataforma usa siempre Madrid y no tiene fila; cada fila sigue a la anterior y una corrección cambia de verdad de zona. */
CREATE TRIGGER dbo.TR_zc_valid ON dbo.zonas_centro AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.centros c ON c.id = i.centro_id WHERE c.codigo = 'PLATAFORMA')
        THROW 50992, 'CENTER_TIMEZONE_CENTER_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i
          LEFT JOIN dbo.zonas_centro previous ON previous.centro_id = i.centro_id AND previous.numero = i.numero - 1
         WHERE i.numero > 1 AND (previous.id IS NULL OR previous.zona_codigo = i.zona_codigo))
        THROW 50993, 'CENTER_TIMEZONE_SEQUENCE_INVALID', 1;
END;
GO

-- Seguridad por filas (ver arriba). Las funciones son las de 0090.
ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_o_plataforma(centro_id) ON dbo.zonas_centro,
    ADD BLOCK PREDICATE seg.fn_estructura_escribe(centro_id, cambiado_por_cuenta_id, cambiado_por_perfil) ON dbo.zonas_centro AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.zonas_centro AFTER UPDATE;
GO

-- Permisos mínimos del usuario de la aplicación (0091): solo inserta; no actualiza ni borra.
GRANT INSERT ON dbo.zonas_centro TO residapp_aplicacion;
DENY UPDATE, DELETE ON dbo.zonas_centro TO residapp_aplicacion;
GO

-- Lo que se vería afectado al cambiar la zona de un centro: las franjas de citas activas y las citas futuras sin cancelar. Son dos números, sin identificar a nadie. citas y citas_franjas tienen
-- seguridad por filas del centro del ámbito (0054), así que Plataforma, que no tiene ámbito en el centro, vería ceros: el procedimiento lee con los permisos de su dueño. Solo devuelve esos dos
-- recuentos y solo lo puede ejecutar el usuario de la aplicación (no es público, a diferencia de dbo.nombres_unidades_vinculacion, 0090); la aplicación lo llama después de comprobar que quien pide
-- tiene ahora un ámbito activo de Plataforma. Un SQL inyectado desde un centro obtendría, como mucho, esos dos números de otro centro.
CREATE PROCEDURE dbo.recuento_citas_centro
    @centro_id UNIQUEIDENTIFIER
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT (SELECT COUNT(*) FROM dbo.citas_franjas WHERE centro_id = @centro_id AND retirada_en IS NULL) AS franjas,
           (SELECT COUNT(*) FROM dbo.citas WHERE centro_id = @centro_id AND estado_codigo = 'CONFIRMADA' AND inicio_en > SYSUTCDATETIME()) AS citas;
END;
GO

GRANT EXECUTE ON OBJECT::dbo.recuento_citas_centro TO residapp_aplicacion;
GO

COMMIT TRANSACTION;
GO
