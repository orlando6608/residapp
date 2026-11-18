/*
 * Citas externas de los residentes, PR 4: la nota «al terminar» y la siguiente cita (docs/producto/citas-externas-diseno.md, valor 17; lectura del DPO, apartado 7, y su segunda
 * lectura del 2026-11-18; decisiones de Orlando, 2026-11-18).
 *
 *   citas_externas_notas   Una fila por VERSIÓN de la nota de una cita realizada. No se edita ni se borra: corregir es una versión nueva, y «retirar la nota» es una versión marcada como
 *                          retirada (sin contenido ni siguiente cita). Lleva el resultado (lista cerrada), una línea de 150 caracteres sin saltos de línea, la siguiente cita prevista (una fecha o «en N
 *                          meses», nunca las dos) y la cuenta y la hora de quien la anotó (Llei 21/2000, art. 9.3). La nota es de la historia del residente, no de una unidad: no guarda unidad.
 *                          La escribe cualquier Enfermería con acceso al residente (no hace falta EXTERNAL_APPOINTMENT_MANAGE); la leen Enfermería y Medicina; Administración solo ve la siguiente cita
 *                          prevista de la versión vigente, por una consulta propia que no devuelve el resto. Es contenido clínico: sigue al residente (catálogo de expurgo).
 *   citas_externas.cita_origen_id   De qué cita viene una cita registrada con «Registrar la siguiente cita». NULL en el resto. Vive en la cita NUEVA (la de origen está cerrada y no cambia).
 *                          Inmutable. La de origen ha de estar realizada o no acudida, y ser del mismo residente (clave ajena compuesta).
 *
 * Una cita de origen solo tiene una siguiente viva (índice único filtrado UX_cext_origen_viva; decisión de Orlando, 2026-11-18).
 *
 * Códigos de error nuevos: 50936 EXTERNAL_APPOINTMENT_NOTE_IMMUTABLE, 50937 EXTERNAL_APPOINTMENT_NOTE_INVALID, 50938 EXTERNAL_APPOINTMENT_ORIGIN_INVALID.
 * La auditoría (acciones EXT_APPOINTMENT_NOTE, EXT_APPOINTMENT_NOTE_RETRACT y EXT_APPOINTMENT_NOTE_HIST_READ) lleva solo identificadores y no se añade a las acciones que ve Administración.
 */

SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO

/* ---- La cita nueva guarda de cuál viene ---- */

ALTER TABLE dbo.citas_externas ADD cita_origen_id UNIQUEIDENTIFIER NULL;
GO

ALTER TABLE dbo.citas_externas ADD CONSTRAINT UQ_cext_centro_residente UNIQUE (centro_id, id, residente_id);
GO

-- Con cita_origen_id NULL la clave ajena no se comprueba (columna nula); con valor, la de origen es del mismo centro y del mismo residente.
ALTER TABLE dbo.citas_externas ADD CONSTRAINT FK_cext_origen FOREIGN KEY (centro_id, cita_origen_id, residente_id) REFERENCES dbo.citas_externas (centro_id, id, residente_id);
ALTER TABLE dbo.citas_externas ADD CONSTRAINT CK_cext_origen_distinta CHECK (cita_origen_id IS NULL OR cita_origen_id <> id);
GO

-- Una cita de origen solo puede tener una siguiente VIVA (no cancelada): dos personas a la vez, o un envío directo, no crean dos. Si la primera se cancela, se puede registrar otra.
CREATE UNIQUE INDEX UX_cext_origen_viva ON dbo.citas_externas (centro_id, cita_origen_id) WHERE cita_origen_id IS NOT NULL AND estado_codigo <> 'CANCELADA';
GO

/* Igual que en 0102, más cita_origen_id: una vez registrada, de qué cita viene no cambia. */
ALTER TRIGGER dbo.TR_cext_update_guard ON dbo.citas_externas AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(id) OR UPDATE(centro_id) OR UPDATE(residente_id) OR UPDATE(creada_por_cuenta_id) OR UPDATE(creada_por_perfil) OR UPDATE(creada_en) OR UPDATE(operacion_id) OR UPDATE(cita_origen_id)
        THROW 50932, 'EXTERNAL_APPOINTMENT_IDENTITY_IMMUTABLE', 1;
    IF EXISTS (SELECT 1 FROM deleted WHERE estado_codigo IN ('REALIZADA', 'NO_ACUDIDA', 'CANCELADA'))
        THROW 50933, 'EXTERNAL_APPOINTMENT_CLOSED', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id WHERE i.version <> d.version + 1)
        THROW 50934, 'EXTERNAL_APPOINTMENT_VERSION_INVALID', 1;
END;
GO

/* La cita de origen ha de estar realizada o no acudida: no se registra «la siguiente» de una cita que sigue abierta o se canceló. */
CREATE TRIGGER dbo.TR_cext_origen_estado ON dbo.citas_externas AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
         WHERE i.cita_origen_id IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM dbo.citas_externas o WITH (FORCESEEK)
                            WHERE o.id = i.cita_origen_id AND o.centro_id = i.centro_id AND o.estado_codigo IN ('REALIZADA', 'NO_ACUDIDA')))
        THROW 50938, 'EXTERNAL_APPOINTMENT_ORIGIN_INVALID', 1;
END;
GO

/* ---- Las versiones de la nota «al terminar» ---- */

CREATE TABLE dbo.citas_externas_notas (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_cext_notas PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL,
    cita_id                  UNIQUEIDENTIFIER NOT NULL,
    residente_id             UNIQUEIDENTIFIER NOT NULL,
    numero                   INT NOT NULL,
    retirada                 BIT NOT NULL CONSTRAINT DF_cextn_retirada DEFAULT (0),
    resultado_codigo         NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NULL,
    texto                    NVARCHAR(150) NULL,
    siguiente_fecha          DATE NULL,
    siguiente_meses          TINYINT NULL,
    motivo_retirada_codigo   NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NULL,
    actor_cuenta_id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_cextn_cuenta REFERENCES dbo.cuentas(id),
    actor_perfil_codigo      NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    operacion_id             UNIQUEIDENTIFIER NULL,
    ocurrido_en              DATETIME2(3) NOT NULL,
    CONSTRAINT FK_cextn_cita FOREIGN KEY (centro_id, cita_id, residente_id) REFERENCES dbo.citas_externas (centro_id, id, residente_id),
    CONSTRAINT FK_cextn_residente FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes (centro_id, id),
    CONSTRAINT CK_cextn_numero CHECK (numero > 0),
    CONSTRAINT CK_cextn_perfil CHECK (actor_perfil_codigo = 'ENFERMERIA'),
    CONSTRAINT CK_cextn_resultado CHECK (resultado_codigo IS NULL OR resultado_codigo IN
        ('SIN_CAMBIOS', 'NUEVA_CITA', 'PENDIENTE_INFORME', 'CAMBIO_PAUTA', 'TRASLADO', 'OTRA')),
    CONSTRAINT CK_cextn_motivo_retirada CHECK (motivo_retirada_codigo IS NULL OR motivo_retirada_codigo IN ('ERROR_DE_REGISTRO', 'DATOS_DE_OTRA_PERSONA', 'OTRO')),
    -- Una línea de 150: sin saltos de línea, tabuladores ni separadores Unicode de línea, y con algo escrito (DPO, apartado 7, punto 4).
    CONSTRAINT CK_cextn_texto CHECK (texto IS NULL OR (LEN(LTRIM(RTRIM(texto))) > 0
        AND CHARINDEX(NCHAR(10), texto) = 0 AND CHARINDEX(NCHAR(13), texto) = 0 AND CHARINDEX(NCHAR(9), texto) = 0
        AND CHARINDEX(NCHAR(11), texto) = 0 AND CHARINDEX(NCHAR(12), texto) = 0 AND CHARINDEX(NCHAR(133), texto) = 0
        AND CHARINDEX(NCHAR(8232), texto) = 0 AND CHARINDEX(NCHAR(8233), texto) = 0)),
    CONSTRAINT CK_cextn_siguiente_excluyente CHECK (siguiente_fecha IS NULL OR siguiente_meses IS NULL),
    CONSTRAINT CK_cextn_siguiente_meses CHECK (siguiente_meses IS NULL OR siguiente_meses BETWEEN 1 AND 36),
    -- Retirada: sin contenido ni siguiente cita y con motivo. Versión normal: con resultado y sin motivo de retirada.
    CONSTRAINT CK_cextn_retirada CHECK (
        (retirada = 1 AND resultado_codigo IS NULL AND texto IS NULL AND siguiente_fecha IS NULL AND siguiente_meses IS NULL AND motivo_retirada_codigo IS NOT NULL)
        OR (retirada = 0 AND resultado_codigo IS NOT NULL AND motivo_retirada_codigo IS NULL))
);
CREATE UNIQUE INDEX UX_cextn_numero ON dbo.citas_externas_notas (cita_id, numero);
CREATE UNIQUE INDEX UX_cextn_operacion ON dbo.citas_externas_notas (actor_cuenta_id, operacion_id) WHERE operacion_id IS NOT NULL;
GO

/* Ni se edita ni se borra: cada cambio es una versión nueva. */
CREATE TRIGGER dbo.TR_cextn_immutable ON dbo.citas_externas_notas INSTEAD OF UPDATE, DELETE AS
    THROW 50936, 'EXTERNAL_APPOINTMENT_NOTE_IMMUTABLE', 1;
GO

/* Lo que la base garantiza de cada versión nueva: la cita está realizada; los números son correlativos desde 1; la primera no es una retirada y no hay dos retiradas seguidas;
   la siguiente cita prevista es posterior al día de la cita. Quién puede escribir y si el residente está de baja lo comprueba el caso de uso, porque la base no conoce las unidades. */
CREATE TRIGGER dbo.TR_cextn_reglas ON dbo.citas_externas_notas AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
         WHERE NOT EXISTS (SELECT 1 FROM dbo.citas_externas c WITH (FORCESEEK)
                            WHERE c.id = i.cita_id AND c.centro_id = i.centro_id AND c.estado_codigo = 'REALIZADA'
                              AND (i.siguiente_fecha IS NULL OR i.siguiente_fecha > c.fecha))
            OR i.numero <> 1 + (SELECT COUNT(*) FROM dbo.citas_externas_notas n WITH (FORCESEEK) WHERE n.cita_id = i.cita_id AND n.numero < i.numero)
            OR (i.retirada = 1 AND (i.numero = 1 OR EXISTS (SELECT 1 FROM dbo.citas_externas_notas p WITH (FORCESEEK)
                                                             WHERE p.cita_id = i.cita_id AND p.numero = i.numero - 1 AND p.retirada = 1))))
        THROW 50937, 'EXTERNAL_APPOINTMENT_NOTE_INVALID', 1;
END;
GO

-- Seguridad por filas por centro, con el mismo predicado que el resto (0030, 0032, 0090, 0101).
ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas_notas,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas_notas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas_notas AFTER UPDATE;
GO

-- Permisos mínimos del usuario de la aplicación (0091): las versiones solo se insertan.
GRANT INSERT ON dbo.citas_externas_notas TO residapp_aplicacion;
DENY UPDATE, DELETE ON dbo.citas_externas_notas TO residapp_aplicacion;
GO

COMMIT TRANSACTION;
GO
