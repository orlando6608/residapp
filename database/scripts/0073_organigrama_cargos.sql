/*
 * Organigrama y cargos (ADM-07) con las respuestas de CJ del 2026-10-25 (docs/pendientes-cj/organigrama-y-cargos-cj):
 *
 *   cargos ............... los crea cada centro (1.2); cada uno dice si es de una persona o de varias (1.3), de qué cargo depende (un superior,
 *                          1.5) y lleva una nota para la coordinación funcional. No se borra: se inactiva (la auditoría guarda su identificador,
 *                          y así sigue resolviendo su nombre). El nombre es único por centro sin mayúsculas ni acentos.
 *   cargos_ocupantes ..... quién ocupa un cargo ahora (1.1): una cuenta de la aplicación o solo un nombre. Solo lo actual (1.7): las filas se
 *                          añaden y se borran, y no se conserva quién ocupó antes un cargo (minimización: no es un registro de personal).
 *                          Una persona puede ocupar varios cargos (1.4). Ocupar un cargo no concede ningún permiso.
 *   cargos_unidades ...... las unidades a las que se liga un cargo (1.6); se añaden y se borran. Los equipos de turnos no tienen relación.
 *
 * Todo es del centro y va bajo seg.pol_centro: la lectura por centro basta para que todos los profesionales lo consulten (1.8). La familia
 * nunca lo ve: no hay pantalla ni consulta del portal para ello.
 */

CREATE TABLE dbo.cargos (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_cargos PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_cargos_centro REFERENCES dbo.centros(id),
    nombre                   NVARCHAR(100) COLLATE Latin1_General_100_CI_AI NOT NULL,
    una_persona              BIT NOT NULL,
    superior_id              UNIQUEIDENTIFIER NULL,
    nota_coordinacion        NVARCHAR(500) NULL,
    estado                   NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_cargos_estado DEFAULT ('ACTIVE'),
    creado_en                DATETIME2(3) NOT NULL,
    creado_por_cuenta_id     UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_cargos_creado_por REFERENCES dbo.cuentas(id),
    modificado_en            DATETIME2(3) NULL,
    modificado_por_cuenta_id UNIQUEIDENTIFIER NULL CONSTRAINT FK_cargos_modificado_por REFERENCES dbo.cuentas(id),
    CONSTRAINT CK_cargos_estado CHECK (estado IN ('ACTIVE', 'INACTIVE')),
    CONSTRAINT CK_cargos_nombre CHECK (LEN(LTRIM(RTRIM(nombre))) > 0),
    CONSTRAINT CK_cargos_nota CHECK (nota_coordinacion IS NULL OR LEN(LTRIM(RTRIM(nota_coordinacion))) > 0),
    CONSTRAINT CK_cargos_superior CHECK (superior_id IS NULL OR superior_id <> id),
    CONSTRAINT CK_cargos_modificacion CHECK (
        (modificado_en IS NULL AND modificado_por_cuenta_id IS NULL)
        OR (modificado_en IS NOT NULL AND modificado_por_cuenta_id IS NOT NULL)
    )
);
CREATE UNIQUE INDEX UX_cargos_centro_id ON dbo.cargos (centro_id, id);
CREATE UNIQUE INDEX UX_cargos_centro_nombre ON dbo.cargos (centro_id, nombre);
CREATE INDEX IX_cargos_superior ON dbo.cargos (superior_id);
GO
ALTER TABLE dbo.cargos ADD CONSTRAINT FK_cargos_superior FOREIGN KEY (centro_id, superior_id) REFERENCES dbo.cargos(centro_id, id);
GO

CREATE TABLE dbo.cargos_ocupantes (
    id                   UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_cargos_ocupantes PRIMARY KEY,
    centro_id            UNIQUEIDENTIFIER NOT NULL,
    cargo_id             UNIQUEIDENTIFIER NOT NULL,
    cuenta_id            UNIQUEIDENTIFIER NULL CONSTRAINT FK_cocu_cuenta REFERENCES dbo.cuentas(id),
    nombre_texto         NVARCHAR(200) COLLATE Latin1_General_100_CI_AI NULL,
    puesto_en            DATETIME2(3) NOT NULL,
    puesto_por_cuenta_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_cocu_puesto_por REFERENCES dbo.cuentas(id),
    CONSTRAINT FK_cocu_cargo FOREIGN KEY (centro_id, cargo_id) REFERENCES dbo.cargos(centro_id, id),
    CONSTRAINT CK_cocu_persona CHECK (
        (cuenta_id IS NOT NULL AND nombre_texto IS NULL)
        OR (cuenta_id IS NULL AND nombre_texto IS NOT NULL AND LEN(LTRIM(RTRIM(nombre_texto))) > 0)
    )
);
CREATE INDEX IX_cocu_cargo ON dbo.cargos_ocupantes (cargo_id);
CREATE INDEX IX_cocu_cuenta ON dbo.cargos_ocupantes (cuenta_id);
CREATE UNIQUE INDEX UX_cocu_cuenta ON dbo.cargos_ocupantes (cargo_id, cuenta_id) WHERE cuenta_id IS NOT NULL;
CREATE UNIQUE INDEX UX_cocu_nombre ON dbo.cargos_ocupantes (cargo_id, nombre_texto) WHERE nombre_texto IS NOT NULL;
GO

CREATE TABLE dbo.cargos_unidades (
    cargo_id             UNIQUEIDENTIFIER NOT NULL,
    unidad_id            UNIQUEIDENTIFIER NOT NULL,
    centro_id            UNIQUEIDENTIFIER NOT NULL,
    ligada_en            DATETIME2(3) NOT NULL,
    ligada_por_cuenta_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_cuni_ligada_por REFERENCES dbo.cuentas(id),
    CONSTRAINT PK_cargos_unidades PRIMARY KEY (cargo_id, unidad_id),
    CONSTRAINT FK_cuni_cargo FOREIGN KEY (centro_id, cargo_id) REFERENCES dbo.cargos(centro_id, id),
    CONSTRAINT FK_cuni_unidad FOREIGN KEY (centro_id, unidad_id) REFERENCES dbo.unidades(centro_id, id)
);
GO

-- Lo que no cambia nunca de un cargo; no se puede inactivar con ocupantes ni dejar de ser «de una persona» con más de una.
CREATE TRIGGER dbo.TR_cargos_guard ON dbo.cargos AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
         WHERE i.centro_id <> d.centro_id OR i.creado_en <> d.creado_en OR i.creado_por_cuenta_id <> d.creado_por_cuenta_id)
        THROW 50550, 'POSITION_IMMUTABLE_FIELD', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
         WHERE i.estado = 'INACTIVE' AND d.estado = 'ACTIVE'
           AND EXISTS (SELECT 1 FROM dbo.cargos_ocupantes o WITH (FORCESEEK) WHERE o.cargo_id = i.id))
        THROW 50553, 'POSITION_HAS_OCCUPANTS', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
         WHERE i.una_persona = 1 AND d.una_persona = 0
           AND (SELECT COUNT(*) FROM dbo.cargos_ocupantes o WITH (FORCESEEK) WHERE o.cargo_id = i.id) > 1)
        THROW 50552, 'POSITION_SINGLE_OCCUPANT', 1;
END;
GO

CREATE TRIGGER dbo.TR_cargos_no_delete ON dbo.cargos INSTEAD OF DELETE AS
    THROW 50551, 'POSITION_DELETE_FORBIDDEN', 1;
GO

-- Un cargo no puede depender de sí mismo ni de uno de sus subordinados (directos o no).
CREATE TRIGGER dbo.TR_cargos_no_cycle ON dbo.cargos AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted WHERE superior_id IS NOT NULL)
        RETURN;
    DECLARE @ciclo BIT = 0;
    WITH ancestros (origen_id, ancestro_id) AS (
        SELECT i.id, i.superior_id FROM inserted i WHERE i.superior_id IS NOT NULL
        UNION ALL
        SELECT a.origen_id, c.superior_id
          FROM ancestros a
          JOIN dbo.cargos c WITH (FORCESEEK) ON c.id = a.ancestro_id
         WHERE c.superior_id IS NOT NULL AND a.ancestro_id <> a.origen_id
    )
    SELECT @ciclo = 1 FROM ancestros WHERE ancestro_id = origen_id OPTION (MAXRECURSION 100);
    IF @ciclo = 1
        THROW 50554, 'POSITION_CYCLE', 1;
END;
GO

-- Las personas de un cargo: solo se añaden o se quitan; en un cargo de una persona no cabe una segunda, ni en uno inactivo.
CREATE TRIGGER dbo.TR_cargos_ocupantes_no_update ON dbo.cargos_ocupantes INSTEAD OF UPDATE AS
    THROW 50555, 'POSITION_OCCUPANT_IMMUTABLE', 1;
GO

CREATE TRIGGER dbo.TR_cargos_ocupantes_rules ON dbo.cargos_ocupantes AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN dbo.cargos c WITH (FORCESEEK) ON c.id = i.cargo_id
         WHERE c.estado <> 'ACTIVE')
        THROW 50556, 'POSITION_INACTIVE', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN dbo.cargos c WITH (FORCESEEK) ON c.id = i.cargo_id
         WHERE c.una_persona = 1
           AND (SELECT COUNT(*) FROM dbo.cargos_ocupantes o WITH (FORCESEEK) WHERE o.cargo_id = i.cargo_id) > 1)
        THROW 50552, 'POSITION_SINGLE_OCCUPANT', 1;
END;
GO

CREATE TRIGGER dbo.TR_cargos_unidades_no_update ON dbo.cargos_unidades INSTEAD OF UPDATE AS
    THROW 50557, 'POSITION_UNIT_IMMUTABLE', 1;
GO

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.cargos,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.cargos AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.cargos AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.cargos_ocupantes,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.cargos_ocupantes AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.cargos_ocupantes AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.cargos_unidades,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.cargos_unidades AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.cargos_unidades AFTER UPDATE;
GO
