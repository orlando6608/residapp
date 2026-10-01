/*
 * Administración, estructura del centro (historia 2): edificios, plantas, habitaciones y plazas.
 *
 *   edificios ........ del centro. Se crean, renombran y activan o inactivan; no se borran.
 *   plantas .......... de un edificio. Igual.
 *   habitaciones ..... de una unidad. Igual.
 *   plazas ........... de una habitación (y por tanto de una unidad). Igual.
 *
 * Hasta ahora unidades.edificio_id / planta_id e intervalos_ubicacion_residente.edificio_id / planta_id / habitacion_id / plaza_id existían
 * sin tablas ni claves foráneas (0002). Este script las crea y enlaza:
 *   - una unidad puede tener edificio y planta (la planta exige edificio, CK_units_floor_requires_building). TR_units_guard se recrea
 *     para permitir cambiarlos; centro, código y fecha de creación siguen sin cambiar nunca (0025);
 *   - una ubicación de residente puede llevar edificio, planta, habitación y plaza (CK_rli_hierarchy: la planta exige edificio y la plaza
 *     habitación); las claves foráneas compuestas obligan a que la habitación y la plaza sean de la unidad de la ubicación;
 *   - una plaza solo la ocupa un residente a la vez (UX_rli_place_active).
 *
 * Mover a un residente de habitación o plaza es un traslado y espera a la decisión de producto: aquí solo se elige al dar de alta.
 */

-- Las columnas existían sin claves foráneas: si alguna fila guardó un id que no existe en el catálogo nuevo, este script no sigue.
IF EXISTS (SELECT 1 FROM dbo.unidades WHERE edificio_id IS NOT NULL OR planta_id IS NOT NULL)
    THROW 50460, 'UNITS_WITH_BUILDING_OR_FLOOR_BEFORE_CATALOG', 1;
IF EXISTS (SELECT 1 FROM dbo.intervalos_ubicacion_residente
            WHERE edificio_id IS NOT NULL OR planta_id IS NOT NULL OR habitacion_id IS NOT NULL OR plaza_id IS NOT NULL)
    THROW 50460, 'LOCATIONS_WITH_BUILDING_FLOOR_ROOM_OR_PLACE_BEFORE_CATALOG', 1;
GO

CREATE TABLE dbo.edificios (
    id                   UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_buildings PRIMARY KEY,
    centro_id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_buildings_center REFERENCES dbo.centros(id),
    nombre_visible       NVARCHAR(200) NOT NULL,
    estado               NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_buildings_status DEFAULT ('ACTIVE'),
    creado_en            DATETIME2(3) NOT NULL,
    creado_por_cuenta_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_buildings_created_by REFERENCES dbo.cuentas(id),
    CONSTRAINT CK_buildings_status CHECK (estado IN ('ACTIVE', 'INACTIVE')),
    CONSTRAINT CK_buildings_name CHECK (LEN(LTRIM(RTRIM(nombre_visible))) > 0)
);
CREATE UNIQUE INDEX UX_buildings_center_id ON dbo.edificios (centro_id, id);
CREATE UNIQUE INDEX UX_buildings_center_name ON dbo.edificios (centro_id, nombre_visible);
GO
CREATE TRIGGER dbo.TR_buildings_guard ON dbo.edificios AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
         WHERE i.centro_id <> d.centro_id OR i.creado_en <> d.creado_en OR i.creado_por_cuenta_id <> d.creado_por_cuenta_id)
        THROW 50461, 'BUILDING_IMMUTABLE_FIELD', 1;
END;
GO
CREATE TRIGGER dbo.TR_buildings_no_delete ON dbo.edificios INSTEAD OF DELETE AS
    THROW 50462, 'BUILDING_DELETE_FORBIDDEN', 1;
GO

CREATE TABLE dbo.plantas (
    id                   UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_floors PRIMARY KEY,
    centro_id            UNIQUEIDENTIFIER NOT NULL,
    edificio_id          UNIQUEIDENTIFIER NOT NULL,
    nombre_visible       NVARCHAR(200) NOT NULL,
    estado               NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_floors_status DEFAULT ('ACTIVE'),
    creado_en            DATETIME2(3) NOT NULL,
    creado_por_cuenta_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_floors_created_by REFERENCES dbo.cuentas(id),
    CONSTRAINT FK_floors_building FOREIGN KEY (centro_id, edificio_id) REFERENCES dbo.edificios(centro_id, id),
    CONSTRAINT CK_floors_status CHECK (estado IN ('ACTIVE', 'INACTIVE')),
    CONSTRAINT CK_floors_name CHECK (LEN(LTRIM(RTRIM(nombre_visible))) > 0)
);
CREATE UNIQUE INDEX UX_floors_building_id ON dbo.plantas (centro_id, edificio_id, id);
CREATE UNIQUE INDEX UX_floors_building_name ON dbo.plantas (centro_id, edificio_id, nombre_visible);
GO
CREATE TRIGGER dbo.TR_floors_guard ON dbo.plantas AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
         WHERE i.centro_id <> d.centro_id OR i.edificio_id <> d.edificio_id
            OR i.creado_en <> d.creado_en OR i.creado_por_cuenta_id <> d.creado_por_cuenta_id)
        THROW 50463, 'FLOOR_IMMUTABLE_FIELD', 1;
END;
GO
CREATE TRIGGER dbo.TR_floors_no_delete ON dbo.plantas INSTEAD OF DELETE AS
    THROW 50464, 'FLOOR_DELETE_FORBIDDEN', 1;
GO

CREATE TABLE dbo.habitaciones (
    id                   UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_rooms PRIMARY KEY,
    centro_id            UNIQUEIDENTIFIER NOT NULL,
    unidad_id            UNIQUEIDENTIFIER NOT NULL,
    nombre_visible       NVARCHAR(200) NOT NULL,
    estado               NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_rooms_status DEFAULT ('ACTIVE'),
    creado_en            DATETIME2(3) NOT NULL,
    creado_por_cuenta_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_rooms_created_by REFERENCES dbo.cuentas(id),
    CONSTRAINT FK_rooms_unit FOREIGN KEY (centro_id, unidad_id) REFERENCES dbo.unidades(centro_id, id),
    CONSTRAINT CK_rooms_status CHECK (estado IN ('ACTIVE', 'INACTIVE')),
    CONSTRAINT CK_rooms_name CHECK (LEN(LTRIM(RTRIM(nombre_visible))) > 0)
);
CREATE UNIQUE INDEX UX_rooms_unit_id ON dbo.habitaciones (centro_id, unidad_id, id);
CREATE UNIQUE INDEX UX_rooms_unit_name ON dbo.habitaciones (centro_id, unidad_id, nombre_visible);
GO
CREATE TRIGGER dbo.TR_rooms_guard ON dbo.habitaciones AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
         WHERE i.centro_id <> d.centro_id OR i.unidad_id <> d.unidad_id
            OR i.creado_en <> d.creado_en OR i.creado_por_cuenta_id <> d.creado_por_cuenta_id)
        THROW 50465, 'ROOM_IMMUTABLE_FIELD', 1;
END;
GO
CREATE TRIGGER dbo.TR_rooms_no_delete ON dbo.habitaciones INSTEAD OF DELETE AS
    THROW 50466, 'ROOM_DELETE_FORBIDDEN', 1;
GO

CREATE TABLE dbo.plazas (
    id                   UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_places PRIMARY KEY,
    centro_id            UNIQUEIDENTIFIER NOT NULL,
    unidad_id            UNIQUEIDENTIFIER NOT NULL,
    habitacion_id        UNIQUEIDENTIFIER NOT NULL,
    nombre_visible       NVARCHAR(200) NOT NULL,
    estado               NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_places_status DEFAULT ('ACTIVE'),
    creado_en            DATETIME2(3) NOT NULL,
    creado_por_cuenta_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_places_created_by REFERENCES dbo.cuentas(id),
    CONSTRAINT FK_places_room FOREIGN KEY (centro_id, unidad_id, habitacion_id) REFERENCES dbo.habitaciones(centro_id, unidad_id, id),
    CONSTRAINT CK_places_status CHECK (estado IN ('ACTIVE', 'INACTIVE')),
    CONSTRAINT CK_places_name CHECK (LEN(LTRIM(RTRIM(nombre_visible))) > 0)
);
CREATE UNIQUE INDEX UX_places_room_id ON dbo.plazas (centro_id, unidad_id, habitacion_id, id);
CREATE UNIQUE INDEX UX_places_room_name ON dbo.plazas (habitacion_id, nombre_visible);
GO
CREATE TRIGGER dbo.TR_places_guard ON dbo.plazas AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
         WHERE i.centro_id <> d.centro_id OR i.unidad_id <> d.unidad_id OR i.habitacion_id <> d.habitacion_id
            OR i.creado_en <> d.creado_en OR i.creado_por_cuenta_id <> d.creado_por_cuenta_id)
        THROW 50467, 'PLACE_IMMUTABLE_FIELD', 1;
END;
GO
CREATE TRIGGER dbo.TR_places_no_delete ON dbo.plazas INSTEAD OF DELETE AS
    THROW 50468, 'PLACE_DELETE_FORBIDDEN', 1;
GO

-- Unidades: edificio y planta ahora son del catálogo y se pueden cambiar (el resto de la identidad no, como en 0025).
DROP TRIGGER dbo.TR_units_guard;
GO
CREATE TRIGGER dbo.TR_units_guard ON dbo.unidades AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
         WHERE i.centro_id <> d.centro_id OR i.creado_en <> d.creado_en
            OR i.codigo COLLATE Latin1_General_100_BIN2 <> d.codigo COLLATE Latin1_General_100_BIN2)
        THROW 50430, 'UNIT_IMMUTABLE_FIELD', 1;
END;
GO
ALTER TABLE dbo.unidades ADD CONSTRAINT FK_units_building FOREIGN KEY (centro_id, edificio_id) REFERENCES dbo.edificios(centro_id, id);
ALTER TABLE dbo.unidades ADD CONSTRAINT FK_units_floor FOREIGN KEY (centro_id, edificio_id, planta_id) REFERENCES dbo.plantas(centro_id, edificio_id, id);
GO

-- Ubicaciones de residentes: la habitación y la plaza son de la unidad de la ubicación; una plaza, un residente vigente.
ALTER TABLE dbo.intervalos_ubicacion_residente ADD CONSTRAINT FK_rli_building FOREIGN KEY (centro_id, edificio_id) REFERENCES dbo.edificios(centro_id, id);
ALTER TABLE dbo.intervalos_ubicacion_residente ADD CONSTRAINT FK_rli_floor FOREIGN KEY (centro_id, edificio_id, planta_id) REFERENCES dbo.plantas(centro_id, edificio_id, id);
ALTER TABLE dbo.intervalos_ubicacion_residente ADD CONSTRAINT FK_rli_room FOREIGN KEY (centro_id, unidad_id, habitacion_id) REFERENCES dbo.habitaciones(centro_id, unidad_id, id);
ALTER TABLE dbo.intervalos_ubicacion_residente ADD CONSTRAINT FK_rli_place FOREIGN KEY (centro_id, unidad_id, habitacion_id, plaza_id) REFERENCES dbo.plazas(centro_id, unidad_id, habitacion_id, id);
CREATE UNIQUE INDEX UX_rli_place_active ON dbo.intervalos_ubicacion_residente (plaza_id) WHERE vigente_hasta IS NULL AND plaza_id IS NOT NULL;
GO
