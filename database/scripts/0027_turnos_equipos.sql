/*
 * Administración, turnos y equipos (historia 4, ADM-14/15/17, ORG-02/04): catálogo de turnos del centro, equipos con nombre dentro de
 * una unidad, sus miembros y la planificación puntual de un equipo en un turno y una fecha. Decisión del usuario (2026-10-01).
 *
 *   turnos_catalogo ...... nombre y horas (hora_fin <= hora_inicio cruza la medianoche). Las horas no cambian nunca (ADM-16: no
 *                          reescribir lo histórico); solo el nombre y el estado. No se borra.
 *   equipos .............. de una sola unidad; el nombre cambia, el resto no. No se borra.
 *   equipos_miembros ..... solo se revoca (como las concesiones de unidad); un miembro vigente por equipo y cuenta.
 *   planificacion_turnos . una fila por equipo, turno y fecha; solo se retira. conflicto_justificacion guarda la decisión de quien
 *                          planificó cuando el servidor avisó de un solapamiento (ADM-17). Planificar no concede acceso.
 *
 * Los conflictos (mismo equipo en turnos que se solapan, o una persona en dos equipos con turnos que se solapan) los calcula el
 * servidor; la BD solo impide la fila duplicada y toda modificación o borrado indebidos.
 */

CREATE TABLE dbo.turnos_catalogo (
    id                   UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_shifts PRIMARY KEY,
    centro_id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_shifts_center REFERENCES dbo.centros(id),
    nombre_visible       NVARCHAR(100) NOT NULL,
    hora_inicio          TIME(0) NOT NULL,
    hora_fin             TIME(0) NOT NULL,
    estado               NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_shifts_status DEFAULT ('ACTIVE'),
    creado_en            DATETIME2(3) NOT NULL,
    creado_por_cuenta_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_shifts_created_by REFERENCES dbo.cuentas(id),
    CONSTRAINT CK_shifts_status CHECK (estado IN ('ACTIVE', 'INACTIVE')),
    CONSTRAINT CK_shifts_name CHECK (LEN(LTRIM(RTRIM(nombre_visible))) > 0)
);
CREATE UNIQUE INDEX UX_shifts_center_id ON dbo.turnos_catalogo (centro_id, id);
CREATE UNIQUE INDEX UX_shifts_center_name ON dbo.turnos_catalogo (centro_id, nombre_visible);
GO
CREATE TRIGGER dbo.TR_shifts_guard ON dbo.turnos_catalogo AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
         WHERE i.centro_id <> d.centro_id OR i.hora_inicio <> d.hora_inicio OR i.hora_fin <> d.hora_fin
            OR i.creado_en <> d.creado_en OR i.creado_por_cuenta_id <> d.creado_por_cuenta_id)
        THROW 50450, 'SHIFT_IMMUTABLE_FIELD', 1;
END;
GO
CREATE TRIGGER dbo.TR_shifts_no_delete ON dbo.turnos_catalogo INSTEAD OF DELETE AS
    THROW 50451, 'SHIFT_DELETE_FORBIDDEN', 1;
GO

CREATE TABLE dbo.equipos (
    id                   UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_teams PRIMARY KEY,
    centro_id            UNIQUEIDENTIFIER NOT NULL,
    unidad_id            UNIQUEIDENTIFIER NOT NULL,
    nombre_visible       NVARCHAR(100) NOT NULL,
    estado               NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_teams_status DEFAULT ('ACTIVE'),
    creado_en            DATETIME2(3) NOT NULL,
    creado_por_cuenta_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_teams_created_by REFERENCES dbo.cuentas(id),
    CONSTRAINT FK_teams_unit FOREIGN KEY (centro_id, unidad_id) REFERENCES dbo.unidades(centro_id, id),
    CONSTRAINT CK_teams_status CHECK (estado IN ('ACTIVE', 'INACTIVE')),
    CONSTRAINT CK_teams_name CHECK (LEN(LTRIM(RTRIM(nombre_visible))) > 0)
);
CREATE UNIQUE INDEX UX_teams_center_id ON dbo.equipos (centro_id, id);
CREATE UNIQUE INDEX UX_teams_unit_id ON dbo.equipos (centro_id, unidad_id, id);
CREATE UNIQUE INDEX UX_teams_unit_name ON dbo.equipos (centro_id, unidad_id, nombre_visible);
GO
CREATE TRIGGER dbo.TR_teams_guard ON dbo.equipos AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
         WHERE i.centro_id <> d.centro_id OR i.unidad_id <> d.unidad_id
            OR i.creado_en <> d.creado_en OR i.creado_por_cuenta_id <> d.creado_por_cuenta_id)
        THROW 50452, 'TEAM_IMMUTABLE_FIELD', 1;
END;
GO
CREATE TRIGGER dbo.TR_teams_no_delete ON dbo.equipos INSTEAD OF DELETE AS
    THROW 50453, 'TEAM_DELETE_FORBIDDEN', 1;
GO

CREATE TABLE dbo.equipos_miembros (
    id                      UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_team_members PRIMARY KEY,
    centro_id               UNIQUEIDENTIFIER NOT NULL,
    equipo_id               UNIQUEIDENTIFIER NOT NULL,
    cuenta_id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_tm_account REFERENCES dbo.cuentas(id),
    concedido_en            DATETIME2(3) NOT NULL,
    concedido_por_cuenta_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_tm_granted_by REFERENCES dbo.cuentas(id),
    revocado_en             DATETIME2(3) NULL,
    revocado_por_cuenta_id  UNIQUEIDENTIFIER NULL CONSTRAINT FK_tm_revoked_by REFERENCES dbo.cuentas(id),
    CONSTRAINT FK_tm_team FOREIGN KEY (centro_id, equipo_id) REFERENCES dbo.equipos(centro_id, id),
    CONSTRAINT CK_tm_revocation CHECK (
        (revocado_en IS NULL AND revocado_por_cuenta_id IS NULL)
        OR (revocado_en IS NOT NULL AND revocado_por_cuenta_id IS NOT NULL)
    )
);
CREATE UNIQUE INDEX UX_tm_active ON dbo.equipos_miembros (equipo_id, cuenta_id) WHERE revocado_en IS NULL;
CREATE INDEX IX_tm_account ON dbo.equipos_miembros (cuenta_id, revocado_en);
GO
CREATE TRIGGER dbo.TR_tm_revoke_only ON dbo.equipos_miembros AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
         WHERE d.revocado_en IS NOT NULL OR i.revocado_en IS NULL
            OR i.centro_id <> d.centro_id OR i.equipo_id <> d.equipo_id OR i.cuenta_id <> d.cuenta_id
            OR i.concedido_en <> d.concedido_en OR i.concedido_por_cuenta_id <> d.concedido_por_cuenta_id)
        THROW 50454, 'TEAM_MEMBER_REVOKE_ONLY', 1;
END;
GO
CREATE TRIGGER dbo.TR_tm_no_delete ON dbo.equipos_miembros INSTEAD OF DELETE AS
    THROW 50455, 'TEAM_MEMBER_DELETE_FORBIDDEN', 1;
GO

CREATE TABLE dbo.planificacion_turnos (
    id                      UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_schedule PRIMARY KEY,
    centro_id               UNIQUEIDENTIFIER NOT NULL,
    unidad_id               UNIQUEIDENTIFIER NOT NULL,
    equipo_id               UNIQUEIDENTIFIER NOT NULL,
    turno_id                UNIQUEIDENTIFIER NOT NULL,
    fecha                   DATE NOT NULL,
    lote_id                 UNIQUEIDENTIFIER NOT NULL,
    creado_en               DATETIME2(3) NOT NULL,
    creado_por_cuenta_id    UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_sch_created_by REFERENCES dbo.cuentas(id),
    conflicto_justificacion NVARCHAR(500) NULL,
    retirado_en             DATETIME2(3) NULL,
    retirado_por_cuenta_id  UNIQUEIDENTIFIER NULL CONSTRAINT FK_sch_retired_by REFERENCES dbo.cuentas(id),
    CONSTRAINT FK_sch_team FOREIGN KEY (centro_id, unidad_id, equipo_id) REFERENCES dbo.equipos(centro_id, unidad_id, id),
    CONSTRAINT FK_sch_shift FOREIGN KEY (centro_id, turno_id) REFERENCES dbo.turnos_catalogo(centro_id, id),
    CONSTRAINT CK_sch_justification CHECK (conflicto_justificacion IS NULL OR LEN(LTRIM(RTRIM(conflicto_justificacion))) > 0),
    CONSTRAINT CK_sch_retirement CHECK (
        (retirado_en IS NULL AND retirado_por_cuenta_id IS NULL)
        OR (retirado_en IS NOT NULL AND retirado_por_cuenta_id IS NOT NULL)
    )
);
CREATE UNIQUE INDEX UX_sch_active ON dbo.planificacion_turnos (equipo_id, turno_id, fecha) WHERE retirado_en IS NULL;
CREATE INDEX IX_sch_unit_date ON dbo.planificacion_turnos (centro_id, unidad_id, fecha, retirado_en);
CREATE INDEX IX_sch_team_date ON dbo.planificacion_turnos (equipo_id, fecha, retirado_en);
CREATE INDEX IX_sch_batch ON dbo.planificacion_turnos (lote_id);
GO
CREATE TRIGGER dbo.TR_sch_retire_only ON dbo.planificacion_turnos AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
         WHERE d.retirado_en IS NOT NULL OR i.retirado_en IS NULL
            OR i.centro_id <> d.centro_id OR i.unidad_id <> d.unidad_id OR i.equipo_id <> d.equipo_id OR i.turno_id <> d.turno_id
            OR i.fecha <> d.fecha OR i.lote_id <> d.lote_id OR i.creado_en <> d.creado_en OR i.creado_por_cuenta_id <> d.creado_por_cuenta_id
            OR NOT EXISTS (SELECT i.conflicto_justificacion INTERSECT SELECT d.conflicto_justificacion))
        THROW 50456, 'SCHEDULE_RETIRE_ONLY', 1;
END;
GO
CREATE TRIGGER dbo.TR_sch_no_delete ON dbo.planificacion_turnos INSTEAD OF DELETE AS
    THROW 50457, 'SCHEDULE_DELETE_FORBIDDEN', 1;
GO
