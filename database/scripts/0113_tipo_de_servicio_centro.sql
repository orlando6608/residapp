/*
 * 0113 — Tipo de servicio del centro: Residencia o Centro de Día (ADR 0017; lectura de dpo-residapp en docs/privacidad/centro-de-dia-lectura-dpo.md, apartado 6.4).
 * Decidido por Orlando el 2026-11-21: el tipo se elige al crear el centro (sin valor preseleccionado en la pantalla), después no lo cambia nadie salvo Plataforma, que solo puede corregir un
 * ERROR DE ALTA y solo si el centro NO TIENE NINGUNA PERSONA (ni residentes, de cualquier estado, ni familiares). Con personas se crea un centro nuevo.
 *
 *   centros.tipo_de_servicio       La fuente de verdad: RESIDENCIA (por defecto y el de todos los centros que ya existen) o CENTRO_DE_DIA. Se lee igual que el nombre del centro: dbo.centros no
 *                                  tiene seguridad por filas (0068) y el selector de ámbito y las invitaciones, que se ven antes de tener ámbito en ese centro, necesitan leerlo.
 *   tipos_servicio_centro          La constancia, de solo añadir (como zonas_centro, 0109): cada fila es el tipo de un centro desde un momento, y la vigente es la de mayor numero. La fila 1 es
 *                                  el alta (motivo ALTA); las correcciones llevan el único motivo ERROR_DE_ALTA. Sin recuentos ni texto libre. Un centro SIN fila es uno anterior a este script
 *                                  (RESIDENCIA) y no se rellena.
 *   corregir_tipo_servicio_centro  La ÚNICA vía de cambio, con los permisos del dueño. La aplicación tiene DENY UPDATE sobre la columna: ni la pantalla ni un SQL manipulado pueden cambiarla.
 *   centro_tiene_personas          Para que Plataforma sepa si puede corregir, sin saber cuántas personas hay: solo «sí» o «no».
 *
 * LA TRAMPA DE ESTE DISEÑO. Un procedimiento con los permisos del dueño se salta la seguridad por filas (IS_MEMBER(N'db_owner') = 1 en 0030, 0090 y 0091): el bloqueo de inserción que en 0109
 * garantizaba que solo Plataforma escribe deja de proteger aquí. Por eso los dos procedimientos comprueban POR SU CUENTA que el ámbito activo de la sesión (SESSION_CONTEXT) es un ámbito vigente
 * de perfil PLATAFORMA; el de corrección, además, de la cuenta que se indica como autora. Y comprueban «centro sin personas» con los permisos del dueño: con los de la aplicación, la seguridad
 * por filas devolvería cero filas a Plataforma (no tiene ámbito en el centro) y un centro con personas parecería vacío.
 *
 * Alta concurrente. El alta de un residente o de un familiar no bloquea la fila del centro, pero sí la referencia por clave foránea. El procedimiento bloquea esa fila (XLOCK) hasta el final y lee a
 * las personas con bloqueo (READCOMMITTEDLOCK, que vale también con aislamiento por versiones, el de Azure SQL): un alta que empiece después espera a que termine la corrección, y una que ya esté a
 * medias se espera antes de decidir. Si se cruzan pueden dar un interbloqueo (error 1205, como en 0107): SQL Server deja caer a uno de los dos, que falla con un error genérico de «no disponible» y se repite a mano (la
 * aplicación no reintenta). La corrección es un acto raro de Plataforma sobre un centro vacío, así que se acepta; la prueba cubre las esperas, no provoca el interbloqueo.
 *
 * La auditoría (CENTER_SERVICE_TYPE_CHANGE) la escribe el propio procedimiento, en la misma transacción que el cambio, para que no se pueda cambiar sin dejar rastro. Solo identificadores: ni el
 * tipo ni el motivo (están en la fila de constancia). Ninguna cifra sale de los procedimientos: solo éxito o un código de error cerrado.
 *
 * Seguridad por filas de la tabla nueva (ADR 0008): las tres piezas de 0109. El centro ve su fila (la aplicación no la lee: lee la columna), Plataforma las de todos; solo se inserta si la autora es
 * la cuenta del ámbito activo y de perfil PLATAFORMA (CK_tsc_perfil). La cuenta del operador es un dato personal seudonimizado (mismo patrón que zonas_centro y modulos_centro); su plazo de
 * conservación queda sin decidir (docs/privacidad/antes-de-datos-reales.md); por ser inmutable, un expurgo futuro exigirá un procedimiento escrito.
 *
 * residapp_tareas no recibe nada: no puede leer la columna ni la tabla. Cuando la tarea de correo la necesite (fase 5c) será otro script con su propia lectura del DPO.
 */

SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO

IF DATABASE_PRINCIPAL_ID(N'residapp_aplicacion') IS NULL
    THROW 51130, 'ROL_APLICACION_NO_EXISTE: aplica antes 0091', 1;
IF OBJECT_ID(N'dbo.zonas_centro', N'U') IS NULL
    THROW 51130, 'ZONAS_CENTRO_NO_EXISTE: aplica antes 0109', 1;
GO

-- 1. La columna. Los centros que ya existen quedan en RESIDENCIA por el valor por defecto. La comparación es binaria, como la de estado (0002): sin ella, «residencia» y «RESIDENCIA» serían lo mismo.
ALTER TABLE dbo.centros ADD
    tipo_de_servicio NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_centros_tipo_servicio DEFAULT N'RESIDENCIA',
    CONSTRAINT CK_centros_tipo_servicio CHECK (tipo_de_servicio IN (N'RESIDENCIA', N'CENTRO_DE_DIA'));
GO

-- 2. La constancia, de solo añadir.
CREATE TABLE dbo.tipos_servicio_centro (
    id                      UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_tipos_servicio_centro PRIMARY KEY,
    centro_id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_tsc_centro REFERENCES dbo.centros(id),
    numero                  INT NOT NULL,
    tipo_codigo             NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    motivo_codigo           NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    cambiado_por_cuenta_id  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_tsc_cuenta REFERENCES dbo.cuentas(id),
    cambiado_por_perfil     NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    cambiado_en             DATETIME2(3) NOT NULL,
    CONSTRAINT CK_tsc_numero CHECK (numero >= 1),
    CONSTRAINT CK_tsc_tipo CHECK (tipo_codigo IN (N'RESIDENCIA', N'CENTRO_DE_DIA')),
    CONSTRAINT CK_tsc_motivo CHECK (motivo_codigo IN (N'ALTA', N'ERROR_DE_ALTA')),
    -- El alta es siempre la fila 1. Una corrección puede ser la fila 1 solo en un centro anterior a este script (sin fila de alta); en los demás es la 2 en adelante.
    CONSTRAINT CK_tsc_alta CHECK (motivo_codigo <> N'ALTA' OR numero = 1),
    CONSTRAINT CK_tsc_perfil CHECK (cambiado_por_perfil = N'PLATAFORMA')
);
CREATE UNIQUE INDEX UX_tsc_numero ON dbo.tipos_servicio_centro (centro_id, numero);
GO

CREATE TRIGGER dbo.TR_tsc_immutable ON dbo.tipos_servicio_centro INSTEAD OF UPDATE, DELETE AS
    THROW 51132, 'CENTER_SERVICE_TYPE_HISTORY_IMMUTABLE', 1;
GO

/* El centro reservado de Plataforma no es un servicio y no tiene fila. El alta coincide con el tipo del centro (que ya existe con ese valor); cada corrección sigue a la anterior y cambia de
   verdad el tipo (respecto de la fila anterior o, si no hay, de RESIDENCIA: el de un centro anterior a este script). Una corrección solo la inserta quien tiene permisos de dueño, es decir, el
   procedimiento de abajo: la aplicación inserta el alta (lo necesita al crear el centro) pero no puede dejar a mano una fila de corrección sin que la columna cambie. */
CREATE TRIGGER dbo.TR_tsc_valid ON dbo.tipos_servicio_centro AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.centros c ON c.id = i.centro_id WHERE c.codigo = N'PLATAFORMA')
        THROW 51131, 'CENTER_SERVICE_TYPE_CENTER_INVALID', 1;
    IF IS_MEMBER(N'db_owner') = 0 AND EXISTS (SELECT 1 FROM inserted WHERE motivo_codigo = N'ERROR_DE_ALTA')
        THROW 51133, 'CENTER_SERVICE_TYPE_SEQUENCE_INVALID', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.centros c ON c.id = i.centro_id WHERE i.motivo_codigo = N'ALTA' AND c.tipo_de_servicio <> i.tipo_codigo)
        THROW 51133, 'CENTER_SERVICE_TYPE_SEQUENCE_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i
          LEFT JOIN dbo.tipos_servicio_centro previous ON previous.centro_id = i.centro_id AND previous.numero = i.numero - 1
         WHERE i.motivo_codigo = N'ERROR_DE_ALTA'
           AND ((i.numero > 1 AND previous.id IS NULL) OR ISNULL(previous.tipo_codigo, N'RESIDENCIA') = i.tipo_codigo))
        THROW 51133, 'CENTER_SERVICE_TYPE_SEQUENCE_INVALID', 1;
END;
GO

-- Seguridad por filas (ver arriba). Las funciones son las de 0090.
ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_o_plataforma(centro_id) ON dbo.tipos_servicio_centro,
    ADD BLOCK PREDICATE seg.fn_estructura_escribe(centro_id, cambiado_por_cuenta_id, cambiado_por_perfil) ON dbo.tipos_servicio_centro AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.tipos_servicio_centro AFTER UPDATE;
GO

-- 3. Permisos mínimos de la aplicación (0091): en la tabla nueva solo inserta (lo hace el alta del centro); en centros conserva UPDATE (cambia el nombre) pero NO el de la columna del tipo: un DENY de
--    columna gana al GRANT de tabla, también con db_datawriter.
GRANT INSERT ON dbo.tipos_servicio_centro TO residapp_aplicacion;
DENY UPDATE, DELETE ON dbo.tipos_servicio_centro TO residapp_aplicacion;
DENY UPDATE (tipo_de_servicio) ON dbo.centros TO residapp_aplicacion;
GO

/* 4. Defensa en profundidad: si alguien con permisos de dueño actualiza la columna a mano, el cambio solo pasa si hay una fila de constancia con ese tipo como la última y si el centro está vacío.
   Se compara en binario (las columnas lo son). Lee a las personas con READCOMMITTEDLOCK, como el procedimiento: con aislamiento por versiones (el de Azure SQL) una lectura normal no vería un alta a
   medias. No se toca TR_centers_guard (0095). */
CREATE TRIGGER dbo.TR_centers_service_type_guard ON dbo.centros AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(tipo_de_servicio)
        RETURN;
    IF NOT EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id WHERE i.tipo_de_servicio <> d.tipo_de_servicio)
        RETURN;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id WHERE i.tipo_de_servicio <> d.tipo_de_servicio AND i.codigo = N'PLATAFORMA')
        THROW 51131, 'CENTER_SERVICE_TYPE_CENTER_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
         WHERE i.tipo_de_servicio <> d.tipo_de_servicio
           AND NOT EXISTS (
               SELECT 1 FROM dbo.tipos_servicio_centro h
                WHERE h.centro_id = i.id AND h.tipo_codigo = i.tipo_de_servicio
                  AND h.numero = (SELECT MAX(x.numero) FROM dbo.tipos_servicio_centro x WHERE x.centro_id = i.id)))
        THROW 51134, 'CENTER_SERVICE_TYPE_HISTORY_MISSING', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
         WHERE i.tipo_de_servicio <> d.tipo_de_servicio
           AND (EXISTS (SELECT 1 FROM dbo.residentes r WITH (READCOMMITTEDLOCK) WHERE r.centro_id = i.id)
                OR EXISTS (SELECT 1 FROM dbo.familiares f WITH (READCOMMITTEDLOCK) WHERE f.centro_id = i.id)))
        THROW 51135, 'CENTER_SERVICE_TYPE_NOT_EMPTY', 1;
END;
GO

/* 5. «¿Tiene personas el centro?» para la pantalla de Plataforma. Solo devuelve sí o no, nunca cuántas ni de qué tipo (Plataforma no ve a las personas de un centro). Solo lo ejecuta el usuario de la
   aplicación y solo contesta si el ámbito activo de la sesión es de Plataforma; un centro que no existe o el reservado dan error. */
CREATE PROCEDURE dbo.centro_tiene_personas
    @centro_id UNIQUEIDENTIFIER
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (
        SELECT 1
          FROM dbo.ambitos_perfil ambito
          JOIN dbo.cuentas cuenta ON cuenta.id = ambito.cuenta_id
         WHERE ambito.id = TRY_CONVERT(UNIQUEIDENTIFIER, SESSION_CONTEXT(N'ambito_perfil_id'))
           AND cuenta.sujeto_externo = CONVERT(NVARCHAR(200), SESSION_CONTEXT(N'sujeto_externo'))
           AND ambito.estado = 'ACTIVE' AND ambito.revocado_en IS NULL AND cuenta.estado = 'ACTIVE'
           AND ambito.perfil_codigo = N'PLATAFORMA')
        THROW 51136, 'CENTER_SERVICE_TYPE_FORBIDDEN', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.centros WHERE id = @centro_id AND codigo <> N'PLATAFORMA')
        THROW 51131, 'CENTER_SERVICE_TYPE_CENTER_INVALID', 1;
    SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.residentes WITH (READCOMMITTEDLOCK) WHERE centro_id = @centro_id)
                       OR EXISTS (SELECT 1 FROM dbo.familiares WITH (READCOMMITTEDLOCK) WHERE centro_id = @centro_id) THEN 1 ELSE 0 END AS BIT) AS tiene_personas;
END;
GO

/* 6. La corrección. Rechaza a quien no es Plataforma (o no es la cuenta indicada), un tipo que no existe, el centro reservado o inexistente, el mismo tipo que ya tiene y un centro con personas. Bloquea
   la fila del centro hasta el final. Con SET XACT_ABORT ON cualquier error deshace todo, también cuando la aplicación la llama dentro de su propia transacción. */
CREATE PROCEDURE dbo.corregir_tipo_servicio_centro
    @centro_id   UNIQUEIDENTIFIER,
    @tipo_codigo NVARCHAR(16),
    @cuenta_id   UNIQUEIDENTIFIER
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (
        SELECT 1
          FROM dbo.ambitos_perfil ambito
          JOIN dbo.cuentas cuenta ON cuenta.id = ambito.cuenta_id
         WHERE ambito.id = TRY_CONVERT(UNIQUEIDENTIFIER, SESSION_CONTEXT(N'ambito_perfil_id'))
           AND cuenta.sujeto_externo = CONVERT(NVARCHAR(200), SESSION_CONTEXT(N'sujeto_externo'))
           AND ambito.estado = 'ACTIVE' AND ambito.revocado_en IS NULL AND cuenta.estado = 'ACTIVE'
           AND cuenta.id = @cuenta_id AND ambito.perfil_codigo = N'PLATAFORMA')
        THROW 51136, 'CENTER_SERVICE_TYPE_FORBIDDEN', 1;
    IF @tipo_codigo IS NULL OR @tipo_codigo COLLATE Latin1_General_100_BIN2 NOT IN (N'RESIDENCIA', N'CENTRO_DE_DIA')
        THROW 51137, 'CENTER_SERVICE_TYPE_INVALID', 1;
    -- SQL Server ignora los espacios finales al comparar, también en binario: «CENTRO_DE_DIA » pasaría la lista. Se guarda siempre el valor canónico.
    SET @tipo_codigo = CASE WHEN @tipo_codigo COLLATE Latin1_General_100_BIN2 = N'RESIDENCIA' THEN N'RESIDENCIA' ELSE N'CENTRO_DE_DIA' END;

    BEGIN TRANSACTION;

    DECLARE @actual NVARCHAR(16);
    SELECT @actual = tipo_de_servicio FROM dbo.centros WITH (XLOCK, ROWLOCK) WHERE id = @centro_id AND codigo <> N'PLATAFORMA';
    IF @actual IS NULL
        THROW 51131, 'CENTER_SERVICE_TYPE_CENTER_INVALID', 1;
    IF @actual = @tipo_codigo
        THROW 51133, 'CENTER_SERVICE_TYPE_SEQUENCE_INVALID', 1;
    IF EXISTS (SELECT 1 FROM dbo.residentes WITH (READCOMMITTEDLOCK) WHERE centro_id = @centro_id)
       OR EXISTS (SELECT 1 FROM dbo.familiares WITH (READCOMMITTEDLOCK) WHERE centro_id = @centro_id)
        THROW 51135, 'CENTER_SERVICE_TYPE_NOT_EMPTY', 1;

    DECLARE @numero INT = ISNULL((SELECT MAX(numero) FROM dbo.tipos_servicio_centro WITH (READCOMMITTEDLOCK) WHERE centro_id = @centro_id), 0) + 1;
    DECLARE @fila UNIQUEIDENTIFIER = NEWID(), @ahora DATETIME2(3) = SYSUTCDATETIME();

    INSERT INTO dbo.tipos_servicio_centro (id, centro_id, numero, tipo_codigo, motivo_codigo, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
    VALUES (@fila, @centro_id, @numero, @tipo_codigo, N'ERROR_DE_ALTA', @cuenta_id, N'PLATAFORMA', @ahora);
    UPDATE dbo.centros SET tipo_de_servicio = @tipo_codigo WHERE id = @centro_id;
    INSERT INTO dbo.eventos_auditoria (id, cuenta_id, perfil_activo, centro_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
    VALUES (NEWID(), @cuenta_id, N'PLATAFORMA', @centro_id, N'CENTER_SERVICE_TYPE', @fila, N'CENTER_SERVICE_TYPE_CHANGE', @ahora);

    COMMIT TRANSACTION;
END;
GO

-- Solo el usuario de la aplicación los ejecuta: ni PUBLIC ni residapp_tareas.
GRANT EXECUTE ON OBJECT::dbo.centro_tiene_personas TO residapp_aplicacion;
GRANT EXECUTE ON OBJECT::dbo.corregir_tipo_servicio_centro TO residapp_aplicacion;
GO

COMMIT TRANSACTION;
GO
