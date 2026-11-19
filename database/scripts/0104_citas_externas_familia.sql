/*
 * Citas externas de los residentes, PR 5: la familia solo ve (docs/tareas/alta-prioridad/pendientes-citas-externas.md, apartado 10; docs/privacidad/citas-externas-lectura-dpo.md;
 * decisiones de Orlando, 2026-11-18). Dos interruptores, apagados por defecto (RGPD art. 25.2): uno por centro y otro por residente. La familia ve una cita solo si el centro está
 * encendido, el residente no está restringido y la cita no es OCULTA (esa regla la aplica la lectura de la familia, en cada petición).
 *
 *   citas_externas_familia_centro      Una fila por CAMBIO del interruptor del centro; el estado vigente es el de la fila de mayor numero (patrón de 0098: no hay una tabla de estado y otra
 *                                      de historial que puedan discrepar). Sin fila = apagado. Al ENCENDER, Administración confirma tres cosas (columnas bit, las tres a 1): que el centro ya
 *                                      informó a residentes y familias, que tiene el procedimiento de voluntad del residente y que actualizó su EIPD. Apagar no pide confirmaciones.
 *   citas_externas_familia_residente   Una fila por CAMBIO del interruptor del residente («la familia no ve las citas externas de esta persona»); vigente = la de mayor numero. Sin fila = la
 *                                      familia las ve. Lo cambian Administración y Enfermería (esta, sin el permiso EXTERNAL_APPOINTMENT_MANAGE: es una herramienta de privacidad, no de gestión).
 *
 * Las dos son de solo añadir (INSTEAD OF UPDATE, DELETE): cambiar el interruptor es insertar la fila siguiente, y el número hace de versión (dos personas a la vez: la segunda choca con el
 * índice único y ve el aviso de «alguien cambió esto antes»). Una fila siempre cambia el valor vigente (la primera es un encendido o una restricción; las demás, lo contrario de la anterior).
 * Sin texto libre: ni motivo ni comentario. La cuenta es un dato personal seudonimizado, igual que en las demás tablas de historial. Plazo de conservación: sin decidir
 * (docs/privacidad/antes-de-datos-reales.md); la del residente es historia del residente y sigue su categoría en el catálogo de expurgo (docs/privacidad/expurgo-y-supresion.md).
 *
 * Códigos de error nuevos: 50939 EXTERNAL_APPOINTMENT_FAMILY_IMMUTABLE, 50940 EXTERNAL_APPOINTMENT_FAMILY_SEQUENCE_INVALID, 50941 EXTERNAL_APPOINTMENT_FAMILY_CENTER_INVALID.
 * Auditoría (acciones EXT_APPOINTMENT_FAMILY_ON y _OFF, EXT_APPOINTMENT_RESIDENT_HIDE y _SHOW): solo identificadores; el recurso es la fila, de la que se lee el valor anterior y el nuevo.
 */

SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO

/* ---- El interruptor del centro ---- */

CREATE TABLE dbo.citas_externas_familia_centro (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_cextfc PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_cextfc_centro REFERENCES dbo.centros(id),
    numero                   INT NOT NULL,
    familia_ve               BIT NOT NULL,
    confirma_informada       BIT NOT NULL,
    confirma_voluntad        BIT NOT NULL,
    confirma_eipd            BIT NOT NULL,
    cambiado_por_cuenta_id   UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_cextfc_cuenta REFERENCES dbo.cuentas(id),
    cambiado_por_perfil      NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    cambiado_en              DATETIME2(3) NOT NULL,
    CONSTRAINT CK_cextfc_numero CHECK (numero >= 1),
    CONSTRAINT CK_cextfc_perfil CHECK (cambiado_por_perfil = 'ADMINISTRACION'),
    -- Encender exige las tres confirmaciones; apagar no pide ninguna.
    CONSTRAINT CK_cextfc_confirmaciones CHECK (familia_ve = 0 OR (confirma_informada = 1 AND confirma_voluntad = 1 AND confirma_eipd = 1))
);
CREATE UNIQUE INDEX UX_cextfc_numero ON dbo.citas_externas_familia_centro (centro_id, numero);
GO

/* ---- El interruptor del residente ---- */

CREATE TABLE dbo.citas_externas_familia_residente (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_cextfr PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL,
    residente_id             UNIQUEIDENTIFIER NOT NULL,
    numero                   INT NOT NULL,
    familia_no_ve            BIT NOT NULL,
    cambiado_por_cuenta_id   UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_cextfr_cuenta REFERENCES dbo.cuentas(id),
    cambiado_por_perfil      NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    cambiado_en              DATETIME2(3) NOT NULL,
    CONSTRAINT FK_cextfr_residente FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes (centro_id, id),
    CONSTRAINT CK_cextfr_numero CHECK (numero >= 1),
    CONSTRAINT CK_cextfr_perfil CHECK (cambiado_por_perfil IN ('ADMINISTRACION', 'ENFERMERIA'))
);
CREATE UNIQUE INDEX UX_cextfr_numero ON dbo.citas_externas_familia_residente (centro_id, residente_id, numero);
GO

/* ---- Solo se añade ---- */

CREATE TRIGGER dbo.TR_cextfc_immutable ON dbo.citas_externas_familia_centro INSTEAD OF UPDATE, DELETE AS
    THROW 50939, 'EXTERNAL_APPOINTMENT_FAMILY_IMMUTABLE', 1;
GO

CREATE TRIGGER dbo.TR_cextfr_immutable ON dbo.citas_externas_familia_residente INSTEAD OF UPDATE, DELETE AS
    THROW 50939, 'EXTERNAL_APPOINTMENT_FAMILY_IMMUTABLE', 1;
GO

/* No se enciende nada en el centro reservado de Plataforma; la primera fila es un encendido y cada una cambia el valor de la anterior (no hay filas «que no cambian nada»). */
CREATE TRIGGER dbo.TR_cextfc_reglas ON dbo.citas_externas_familia_centro AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.centros c ON c.id = i.centro_id WHERE c.codigo = 'PLATAFORMA')
        THROW 50941, 'EXTERNAL_APPOINTMENT_FAMILY_CENTER_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i
          LEFT JOIN dbo.citas_externas_familia_centro previous ON previous.centro_id = i.centro_id AND previous.numero = i.numero - 1
         WHERE (i.numero = 1 AND i.familia_ve = 0)
            OR (i.numero > 1 AND (previous.id IS NULL OR previous.familia_ve = i.familia_ve)))
        THROW 50940, 'EXTERNAL_APPOINTMENT_FAMILY_SEQUENCE_INVALID', 1;
END;
GO

CREATE TRIGGER dbo.TR_cextfr_reglas ON dbo.citas_externas_familia_residente AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
          LEFT JOIN dbo.citas_externas_familia_residente previous
                 ON previous.centro_id = i.centro_id AND previous.residente_id = i.residente_id AND previous.numero = i.numero - 1
         WHERE (i.numero = 1 AND i.familia_no_ve = 0)
            OR (i.numero > 1 AND (previous.id IS NULL OR previous.familia_no_ve = i.familia_no_ve)))
        THROW 50940, 'EXTERNAL_APPOINTMENT_FAMILY_SEQUENCE_INVALID', 1;
END;
GO

-- Seguridad por filas por centro, con el mismo predicado que el resto (0030, 0032, 0090, 0101, 0103).
ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas_familia_centro,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas_familia_centro AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas_familia_centro AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas_familia_residente,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas_familia_residente AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas_familia_residente AFTER UPDATE;
GO

-- Permisos mínimos del usuario de la aplicación (0091): las filas solo se insertan.
GRANT INSERT ON dbo.citas_externas_familia_centro TO residapp_aplicacion;
DENY UPDATE, DELETE ON dbo.citas_externas_familia_centro TO residapp_aplicacion;
GRANT INSERT ON dbo.citas_externas_familia_residente TO residapp_aplicacion;
DENY UPDATE, DELETE ON dbo.citas_externas_familia_residente TO residapp_aplicacion;
GO

COMMIT TRANSACTION;
GO
