/*
 * Aplica la propuesta de renombrado a español de docs/decisiones-arquitectura/modelo-datos-basal-residente.html
 * (Parte 2): traduce a snake_case en español (ASCII, sin tildes/ñ) los nombres de las 22 tablas y sus
 * columnas creadas por 0001_init_sqlserver.sql. 0001 es inmutable (ver
 * docs/decisiones-arquitectura/integridad-sql-basal-legado.md: "cualquier evolución del esquema debe
 * hacerse en 0002 o posterior") — este script no lo modifica, solo actúa sobre una base de datos donde
 * 0001 ya se ejecutó.
 *
 * Fuera de alcance (no cubierto por la propuesta): nombres de constraints/índices (PK_residents,
 * IX_..., etc.) y los valores de catálogo ya almacenados en código (estados, area_code, motivo_code,
 * códigos de perfil/ítem Barthel, claves de los payloads JSON de área) — todos permanecen exactamente
 * igual que en 0001, solo cambian los nombres estructurales de tabla y columna.
 *
 * Enfoque técnico: DROP + CREATE completo (no sp_rename). 0001 tiene 27 triggers y 51 checks cuyo
 * cuerpo T-SQL referencia los nombres de columna actuales como texto literal; sp_rename no reescribe
 * ese texto, así que un renombrado de columna usada por un trigger/check lo dejaría roto (error en
 * tiempo de ejecución) si no se recrea también. No hay datos reales todavía — el tratamiento de datos
 * reales sigue "pendiente de formalizar" (ver docs/producto/roadmap.md) — así que recrear el esquema
 * completo bajo los nuevos nombres es seguro y más fiable que 150+ sp_rename más la reescritura manual
 * de cada trigger/check dependiente.
 *
 * Tras ejecutar este script, actualizar en el mismo cambio: el código C# que referencia estas tablas/
 * columnas (ResidApp.Domain, ResidApp.Application, repositorios Dapper de ResidApp.Infrastructure) y
 * los formularios ya construidos (Residents/Create, Baseline/Sign, Baseline/Direction).
 */

------------------------------------------------------------
-- DROP del esquema en inglés (orden inverso de creación en 0001, por dependencias FK)
------------------------------------------------------------
DROP TABLE IF EXISTS dbo.idempotency_operations;
DROP TABLE IF EXISTS dbo.audit_events;
DROP TABLE IF EXISTS dbo.baseline_supersessions;
DROP TABLE IF EXISTS dbo.resident_current_baselines;
DROP TABLE IF EXISTS dbo.baseline_version_barthel_items;
DROP TABLE IF EXISTS dbo.baseline_version_barthel;
DROP TABLE IF EXISTS dbo.baseline_version_areas;
DROP TABLE IF EXISTS dbo.baseline_versions;
DROP TABLE IF EXISTS dbo.baseline_draft_barthel_items;
DROP TABLE IF EXISTS dbo.baseline_draft_barthel;
DROP TABLE IF EXISTS dbo.baseline_draft_areas;
DROP TABLE IF EXISTS dbo.baseline_drafts;
DROP TABLE IF EXISTS dbo.resident_location_intervals;
DROP TABLE IF EXISTS dbo.resident_center_episodes;
DROP TABLE IF EXISTS dbo.profile_permissions;
DROP TABLE IF EXISTS dbo.profile_resident_scopes;
DROP TABLE IF EXISTS dbo.profile_unit_scopes;
DROP TABLE IF EXISTS dbo.profile_scopes;
DROP TABLE IF EXISTS dbo.residents;
DROP TABLE IF EXISTS dbo.units;
DROP TABLE IF EXISTS dbo.centers;
DROP TABLE IF EXISTS dbo.accounts;
GO

------------------------------------------------------------
-- Identidad y estructura organizativa
------------------------------------------------------------
CREATE TABLE dbo.cuentas (
    id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_accounts PRIMARY KEY,
    sujeto_externo    NVARCHAR(200) NOT NULL,
    estado            NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_accounts_status DEFAULT ('ACTIVE'),
    creado_en         DATETIME2(3) NOT NULL,
    CONSTRAINT CK_accounts_status CHECK (estado IN ('ACTIVE', 'SUSPENDED'))
);
CREATE UNIQUE INDEX UX_accounts_external_subject ON dbo.cuentas (sujeto_externo);
GO

CREATE TABLE dbo.centros (
    id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_centers PRIMARY KEY,
    codigo         NVARCHAR(64) NOT NULL,
    nombre_visible NVARCHAR(200) NOT NULL,
    estado         NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_centers_status DEFAULT ('ACTIVE'),
    creado_en      DATETIME2(3) NOT NULL,
    CONSTRAINT CK_centers_status CHECK (estado IN ('ACTIVE', 'INACTIVE'))
);
CREATE UNIQUE INDEX UX_centers_code ON dbo.centros (codigo);
GO

-- edificio_id/planta_id sin FK: edificios/plantas son verticales fuera de alcance (ver 0001).
CREATE TABLE dbo.unidades (
    id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_units PRIMARY KEY,
    centro_id      UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_units_center REFERENCES dbo.centros(id),
    edificio_id    UNIQUEIDENTIFIER NULL,
    planta_id      UNIQUEIDENTIFIER NULL,
    codigo         NVARCHAR(64) NOT NULL,
    nombre_visible NVARCHAR(200) NOT NULL,
    estado         NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_units_status DEFAULT ('ACTIVE'),
    creado_en      DATETIME2(3) NOT NULL,
    CONSTRAINT CK_units_floor_requires_building CHECK (planta_id IS NULL OR edificio_id IS NOT NULL),
    CONSTRAINT CK_units_status CHECK (estado IN ('ACTIVE', 'INACTIVE'))
);
CREATE UNIQUE INDEX UX_units_center_code ON dbo.unidades (centro_id, codigo);
CREATE UNIQUE INDEX UX_units_center_id ON dbo.unidades (centro_id, id);
GO

CREATE TABLE dbo.residentes (
    id                        UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_residents PRIMARY KEY,
    centro_id                 UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_residents_center REFERENCES dbo.centros(id),
    nombre_visible            NVARCHAR(200) NOT NULL,
    fecha_nacimiento          DATE NOT NULL,
    sexo_documentado_codigo   NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    estado                    NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_residents_status DEFAULT ('ACTIVE'),
    motivo_inactivacion       NVARCHAR(1000) NULL,
    inactivado_en             DATETIME2(3) NULL,
    inactivado_por_cuenta_id  UNIQUEIDENTIFIER NULL CONSTRAINT FK_residents_inactivated_by REFERENCES dbo.cuentas(id),
    inactivado_por_perfil     NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NULL,
    creado_en                 DATETIME2(3) NOT NULL,
    creado_por_cuenta_id      UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_residents_created_by REFERENCES dbo.cuentas(id),
    creado_por_perfil         NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CONSTRAINT CK_residents_sex CHECK (sexo_documentado_codigo IN ('male', 'female', 'other', 'unknown')),
    CONSTRAINT CK_residents_status CHECK (estado IN ('ACTIVE', 'INACTIVE')),
    CONSTRAINT CK_residents_created_profile CHECK (creado_por_perfil IN ('ADMINISTRACION', 'ENFERMERIA')),
    CONSTRAINT CK_residents_inactivation CHECK (
        (estado = 'ACTIVE' AND motivo_inactivacion IS NULL AND inactivado_en IS NULL
            AND inactivado_por_cuenta_id IS NULL AND inactivado_por_perfil IS NULL)
        OR (estado = 'INACTIVE' AND LEN(LTRIM(RTRIM(ISNULL(motivo_inactivacion, '')))) > 0
            AND inactivado_en IS NOT NULL AND inactivado_por_cuenta_id IS NOT NULL
            AND inactivado_por_perfil = 'ADMINISTRACION')
    )
);
CREATE UNIQUE INDEX UX_residents_center_id ON dbo.residentes (centro_id, id);
GO

------------------------------------------------------------
-- Autorización: ámbitos y permisos por cuenta/perfil
------------------------------------------------------------
CREATE TABLE dbo.ambitos_perfil (
    id                      UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_profile_scopes PRIMARY KEY,
    cuenta_id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_ps_account REFERENCES dbo.cuentas(id),
    centro_id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_ps_center REFERENCES dbo.centros(id),
    perfil_codigo           NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    estado                  NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_ps_status DEFAULT ('ACTIVE'),
    concedido_en            DATETIME2(3) NOT NULL,
    concedido_por_cuenta_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_ps_granted_by REFERENCES dbo.cuentas(id),
    revocado_en             DATETIME2(3) NULL,
    revocado_por_cuenta_id  UNIQUEIDENTIFIER NULL CONSTRAINT FK_ps_revoked_by REFERENCES dbo.cuentas(id),
    CONSTRAINT CK_ps_profile CHECK (perfil_codigo IN ('AUXILIAR', 'ENFERMERIA', 'MEDICINA', 'FAMILIAR', 'ADMINISTRACION', 'DIRECCION_CLINICA')),
    CONSTRAINT CK_ps_status CHECK (estado IN ('ACTIVE', 'REVOKED')),
    CONSTRAINT CK_ps_revocation CHECK (
        (estado = 'ACTIVE' AND revocado_en IS NULL AND revocado_por_cuenta_id IS NULL)
        OR (estado = 'REVOKED' AND revocado_en IS NOT NULL AND revocado_por_cuenta_id IS NOT NULL)
    )
);
CREATE UNIQUE INDEX UX_ps_id_center ON dbo.ambitos_perfil (id, centro_id);
-- Índice único condicional: como máximo un perfil ACTIVO por cuenta/centro/perfil (traduce
-- profile_scopes_active_unique del prototipo original).
CREATE UNIQUE INDEX UX_ps_active ON dbo.ambitos_perfil (cuenta_id, centro_id, perfil_codigo) WHERE estado = 'ACTIVE';
CREATE INDEX IX_ps_authorization_lookup ON dbo.ambitos_perfil (centro_id, perfil_codigo, cuenta_id, estado);
GO
-- Revoke-only + no-delete (traduce profile_scopes_revoke_only / profile_scopes_no_delete).
CREATE TRIGGER dbo.TR_ps_revoke_only ON dbo.ambitos_perfil AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE d.estado <> 'ACTIVE' OR i.estado <> 'REVOKED'
           OR i.cuenta_id <> d.cuenta_id OR i.centro_id <> d.centro_id OR i.perfil_codigo <> d.perfil_codigo
           OR i.concedido_en <> d.concedido_en OR i.concedido_por_cuenta_id <> d.concedido_por_cuenta_id)
        THROW 50100, 'PROFILE_SCOPE_REVOKE_ONLY', 1;
END;
GO
CREATE TRIGGER dbo.TR_ps_no_delete ON dbo.ambitos_perfil INSTEAD OF DELETE AS
    THROW 50101, 'PROFILE_SCOPE_DELETE_FORBIDDEN', 1;
GO

CREATE TABLE dbo.ambitos_perfil_unidad (
    id                      UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_pus PRIMARY KEY,
    ambito_perfil_id        UNIQUEIDENTIFIER NOT NULL,
    centro_id               UNIQUEIDENTIFIER NOT NULL,
    unidad_id               UNIQUEIDENTIFIER NOT NULL,
    concedido_en            DATETIME2(3) NOT NULL,
    concedido_por_cuenta_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_pus_granted_by REFERENCES dbo.cuentas(id),
    revocado_en             DATETIME2(3) NULL,
    revocado_por_cuenta_id  UNIQUEIDENTIFIER NULL CONSTRAINT FK_pus_revoked_by REFERENCES dbo.cuentas(id),
    CONSTRAINT FK_pus_scope FOREIGN KEY (ambito_perfil_id, centro_id) REFERENCES dbo.ambitos_perfil(id, centro_id),
    CONSTRAINT FK_pus_unit FOREIGN KEY (centro_id, unidad_id) REFERENCES dbo.unidades(centro_id, id),
    CONSTRAINT CK_pus_revocation CHECK (
        (revocado_en IS NULL AND revocado_por_cuenta_id IS NULL)
        OR (revocado_en IS NOT NULL AND revocado_por_cuenta_id IS NOT NULL)
    )
);
CREATE UNIQUE INDEX UX_pus_active ON dbo.ambitos_perfil_unidad (ambito_perfil_id, unidad_id) WHERE revocado_en IS NULL;
CREATE INDEX IX_pus_lookup ON dbo.ambitos_perfil_unidad (centro_id, unidad_id, ambito_perfil_id, revocado_en);
GO
CREATE TRIGGER dbo.TR_pus_revoke_only ON dbo.ambitos_perfil_unidad AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE d.revocado_en IS NOT NULL OR i.revocado_en IS NULL
           OR i.ambito_perfil_id <> d.ambito_perfil_id OR i.centro_id <> d.centro_id OR i.unidad_id <> d.unidad_id
           OR i.concedido_en <> d.concedido_en OR i.concedido_por_cuenta_id <> d.concedido_por_cuenta_id)
        THROW 50110, 'PROFILE_UNIT_SCOPE_REVOKE_ONLY', 1;
END;
GO
CREATE TRIGGER dbo.TR_pus_no_delete ON dbo.ambitos_perfil_unidad INSTEAD OF DELETE AS
    THROW 50111, 'PROFILE_UNIT_SCOPE_DELETE_FORBIDDEN', 1;
GO

CREATE TABLE dbo.ambitos_perfil_residente (
    id                      UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_prs PRIMARY KEY,
    ambito_perfil_id        UNIQUEIDENTIFIER NOT NULL,
    centro_id               UNIQUEIDENTIFIER NOT NULL,
    residente_id            UNIQUEIDENTIFIER NOT NULL,
    concedido_en            DATETIME2(3) NOT NULL,
    concedido_por_cuenta_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_prs_granted_by REFERENCES dbo.cuentas(id),
    revocado_en             DATETIME2(3) NULL,
    revocado_por_cuenta_id  UNIQUEIDENTIFIER NULL CONSTRAINT FK_prs_revoked_by REFERENCES dbo.cuentas(id),
    CONSTRAINT FK_prs_scope FOREIGN KEY (ambito_perfil_id, centro_id) REFERENCES dbo.ambitos_perfil(id, centro_id),
    CONSTRAINT FK_prs_resident FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes(centro_id, id),
    CONSTRAINT CK_prs_revocation CHECK (
        (revocado_en IS NULL AND revocado_por_cuenta_id IS NULL)
        OR (revocado_en IS NOT NULL AND revocado_por_cuenta_id IS NOT NULL)
    )
);
CREATE UNIQUE INDEX UX_prs_active ON dbo.ambitos_perfil_residente (ambito_perfil_id, residente_id) WHERE revocado_en IS NULL;
CREATE INDEX IX_prs_lookup ON dbo.ambitos_perfil_residente (centro_id, residente_id, ambito_perfil_id, revocado_en);
GO
CREATE TRIGGER dbo.TR_prs_revoke_only ON dbo.ambitos_perfil_residente AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE d.revocado_en IS NOT NULL OR i.revocado_en IS NULL
           OR i.ambito_perfil_id <> d.ambito_perfil_id OR i.centro_id <> d.centro_id OR i.residente_id <> d.residente_id
           OR i.concedido_en <> d.concedido_en OR i.concedido_por_cuenta_id <> d.concedido_por_cuenta_id)
        THROW 50120, 'PROFILE_RESIDENT_SCOPE_REVOKE_ONLY', 1;
END;
GO
CREATE TRIGGER dbo.TR_prs_no_delete ON dbo.ambitos_perfil_residente INSTEAD OF DELETE AS
    THROW 50121, 'PROFILE_RESIDENT_SCOPE_DELETE_FORBIDDEN', 1;
GO

CREATE TABLE dbo.permisos_perfil (
    id                      UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_pp PRIMARY KEY,
    ambito_perfil_id        UNIQUEIDENTIFIER NOT NULL,
    centro_id               UNIQUEIDENTIFIER NOT NULL,
    permiso_codigo          NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    concedido_en            DATETIME2(3) NOT NULL,
    concedido_por_cuenta_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_pp_granted_by REFERENCES dbo.cuentas(id),
    revocado_en             DATETIME2(3) NULL,
    revocado_por_cuenta_id  UNIQUEIDENTIFIER NULL CONSTRAINT FK_pp_revoked_by REFERENCES dbo.cuentas(id),
    CONSTRAINT FK_pp_scope FOREIGN KEY (ambito_perfil_id, centro_id) REFERENCES dbo.ambitos_perfil(id, centro_id),
    -- Se incluye BASELINE_DRAFT_CONTRIBUTE (verticales de contribución compartida) aunque este script no
    -- cree basales_borrador_contribuciones, para no romper el catálogo original de permisos válidos.
    CONSTRAINT CK_pp_code CHECK (permiso_codigo IN
        ('RESIDENT_IDENTITY_CREATE', 'BASELINE_INITIAL_COMPLETE', 'BASELINE_REEVALUATE',
         'BASELINE_DRAFT_CONTRIBUTE', 'CLINICAL_DETAIL_READ')),
    CONSTRAINT CK_pp_revocation CHECK (
        (revocado_en IS NULL AND revocado_por_cuenta_id IS NULL)
        OR (revocado_en IS NOT NULL AND revocado_por_cuenta_id IS NOT NULL)
    )
);
CREATE UNIQUE INDEX UX_pp_active ON dbo.permisos_perfil (ambito_perfil_id, permiso_codigo) WHERE revocado_en IS NULL;
CREATE INDEX IX_pp_lookup ON dbo.permisos_perfil (centro_id, ambito_perfil_id, permiso_codigo, revocado_en);
GO
CREATE TRIGGER dbo.TR_pp_revoke_only ON dbo.permisos_perfil AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE d.revocado_en IS NOT NULL OR i.revocado_en IS NULL
           OR i.ambito_perfil_id <> d.ambito_perfil_id OR i.centro_id <> d.centro_id OR i.permiso_codigo <> d.permiso_codigo
           OR i.concedido_en <> d.concedido_en OR i.concedido_por_cuenta_id <> d.concedido_por_cuenta_id)
        THROW 50130, 'PROFILE_PERMISSION_REVOKE_ONLY', 1;
END;
GO
CREATE TRIGGER dbo.TR_pp_no_delete ON dbo.permisos_perfil INSTEAD OF DELETE AS
    THROW 50131, 'PROFILE_PERMISSION_DELETE_FORBIDDEN', 1;
GO

------------------------------------------------------------
-- Episodios y ubicación del residente
------------------------------------------------------------
CREATE TABLE dbo.episodios_residente_centro (
    id                    UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_rce PRIMARY KEY,
    residente_id          UNIQUEIDENTIFIER NOT NULL,
    centro_id             UNIQUEIDENTIFIER NOT NULL,
    referencia_interna    NVARCHAR(200) NULL,
    vigente_desde         DATETIME2(3) NOT NULL,
    vigente_hasta         DATETIME2(3) NULL,
    creado_en             DATETIME2(3) NOT NULL,
    creado_por_cuenta_id  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_rce_created_by REFERENCES dbo.cuentas(id),
    creado_por_perfil     NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CONSTRAINT FK_rce_resident FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes(centro_id, id),
    CONSTRAINT CK_rce_time CHECK (vigente_hasta IS NULL OR vigente_hasta > vigente_desde),
    CONSTRAINT CK_rce_profile CHECK (creado_por_perfil IN ('ADMINISTRACION', 'ENFERMERIA'))
);
CREATE UNIQUE INDEX UX_rce_scope ON dbo.episodios_residente_centro (id, residente_id, centro_id);
-- Índice único condicional: como máximo un episodio vigente por residente (traduce
-- resident_center_episodes_active_unique). No se traduce resident_center_episodes_no_overlap
-- (validación completa de solapamiento temporal): el único camino de escritura hoy es el alta inicial,
-- que crea un único episodio sin vigente_hasta — ver riesgos en 0001.
CREATE UNIQUE INDEX UX_rce_active ON dbo.episodios_residente_centro (residente_id) WHERE vigente_hasta IS NULL;
CREATE INDEX IX_rce_lookup ON dbo.episodios_residente_centro (centro_id, residente_id, vigente_desde);
GO
-- Close-only + no-delete (traduce resident_center_episodes_close_only / _no_delete).
CREATE TRIGGER dbo.TR_rce_close_only ON dbo.episodios_residente_centro AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE d.vigente_hasta IS NOT NULL OR i.vigente_hasta IS NULL
           OR i.residente_id <> d.residente_id OR i.centro_id <> d.centro_id
           OR ISNULL(i.referencia_interna, '') <> ISNULL(d.referencia_interna, '')
           OR i.vigente_desde <> d.vigente_desde OR i.creado_en <> d.creado_en
           OR i.creado_por_cuenta_id <> d.creado_por_cuenta_id OR i.creado_por_perfil <> d.creado_por_perfil)
        THROW 50140, 'RESIDENT_CENTER_EPISODE_CLOSE_ONLY', 1;
END;
GO
CREATE TRIGGER dbo.TR_rce_no_delete ON dbo.episodios_residente_centro INSTEAD OF DELETE AS
    THROW 50141, 'RESIDENT_CENTER_EPISODE_DELETE_FORBIDDEN', 1;
GO

-- edificio_id/planta_id/habitacion_id/plaza_id sin FK: verticales fuera de alcance (ver 0001).
CREATE TABLE dbo.intervalos_ubicacion_residente (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_rli PRIMARY KEY,
    residente_id             UNIQUEIDENTIFIER NOT NULL,
    centro_id                UNIQUEIDENTIFIER NOT NULL,
    episodio_id              UNIQUEIDENTIFIER NOT NULL,
    unidad_id                UNIQUEIDENTIFIER NOT NULL,
    edificio_id              UNIQUEIDENTIFIER NULL,
    planta_id                UNIQUEIDENTIFIER NULL,
    habitacion_id            UNIQUEIDENTIFIER NULL,
    plaza_id                 UNIQUEIDENTIFIER NULL,
    vigente_desde            DATETIME2(3) NOT NULL,
    vigente_hasta            DATETIME2(3) NULL,
    modificado_en            DATETIME2(3) NOT NULL,
    modificado_por_cuenta_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_rli_changed_by REFERENCES dbo.cuentas(id),
    modificado_por_perfil    NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CONSTRAINT FK_rli_episode FOREIGN KEY (episodio_id, residente_id, centro_id) REFERENCES dbo.episodios_residente_centro(id, residente_id, centro_id),
    CONSTRAINT FK_rli_unit FOREIGN KEY (centro_id, unidad_id) REFERENCES dbo.unidades(centro_id, id),
    CONSTRAINT CK_rli_time CHECK (vigente_hasta IS NULL OR vigente_hasta > vigente_desde),
    CONSTRAINT CK_rli_hierarchy CHECK (
        (planta_id IS NULL OR edificio_id IS NOT NULL) AND (plaza_id IS NULL OR habitacion_id IS NOT NULL)
    ),
    CONSTRAINT CK_rli_profile CHECK (modificado_por_perfil IN ('ADMINISTRACION', 'ENFERMERIA'))
);
-- Índice único condicional: como máximo una ubicación vigente por residente (traduce
-- resident_location_intervals_active_unique). No se traduce la validación completa de
-- resident_location_intervals_validate_insert (config de centro para habitación/plaza obligatoria) —
-- ver riesgos en 0001.
CREATE UNIQUE INDEX UX_rli_active ON dbo.intervalos_ubicacion_residente (residente_id) WHERE vigente_hasta IS NULL;
CREATE INDEX IX_rli_current_lookup ON dbo.intervalos_ubicacion_residente (centro_id, unidad_id, residente_id, vigente_hasta);
GO
CREATE TRIGGER dbo.TR_rli_close_only ON dbo.intervalos_ubicacion_residente AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE d.vigente_hasta IS NOT NULL OR i.vigente_hasta IS NULL
           OR i.residente_id <> d.residente_id OR i.centro_id <> d.centro_id OR i.episodio_id <> d.episodio_id
           OR i.unidad_id <> d.unidad_id OR ISNULL(i.edificio_id,'00000000-0000-0000-0000-000000000000') <> ISNULL(d.edificio_id,'00000000-0000-0000-0000-000000000000')
           OR ISNULL(i.planta_id,'00000000-0000-0000-0000-000000000000') <> ISNULL(d.planta_id,'00000000-0000-0000-0000-000000000000')
           OR ISNULL(i.habitacion_id,'00000000-0000-0000-0000-000000000000') <> ISNULL(d.habitacion_id,'00000000-0000-0000-0000-000000000000')
           OR ISNULL(i.plaza_id,'00000000-0000-0000-0000-000000000000') <> ISNULL(d.plaza_id,'00000000-0000-0000-0000-000000000000')
           OR i.vigente_desde <> d.vigente_desde)
        THROW 50150, 'RESIDENT_LOCATION_INTERVAL_CLOSE_ONLY', 1;
END;
GO
CREATE TRIGGER dbo.TR_rli_no_delete ON dbo.intervalos_ubicacion_residente INSTEAD OF DELETE AS
    THROW 50151, 'RESIDENT_LOCATION_INTERVAL_DELETE_FORBIDDEN', 1;
GO

------------------------------------------------------------
-- Basal: borrador (mutable mientras ACTIVE; concurrencia optimista por revision_borrador)
------------------------------------------------------------
CREATE TABLE dbo.basales_borrador (
    id                                   UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_bd PRIMARY KEY,
    residente_id                         UNIQUEIDENTIFIER NOT NULL,
    centro_id                            UNIQUEIDENTIFIER NOT NULL,
    creado_en_unidad_id                  UNIQUEIDENTIFIER NOT NULL,
    estado                               NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_bd_status DEFAULT ('ACTIVE'),
    motivo_codigo                        NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NULL,
    fuente_informacion_comun_codigo      NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NULL,
    fuente_informacion_comun_otro_texto  NVARCHAR(500) NULL,
    fecha_informacion_comun              DATE NULL,
    creado_por_cuenta_id                 UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_bd_created_by REFERENCES dbo.cuentas(id),
    creado_por_perfil                    NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    creado_en                            DATETIME2(3) NOT NULL,
    actualizado_por_cuenta_id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_bd_updated_by REFERENCES dbo.cuentas(id),
    actualizado_por_perfil               NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    actualizado_en                       DATETIME2(3) NOT NULL,
    revision_borrador                    INT NOT NULL CONSTRAINT DF_bd_revision DEFAULT (1),
    cancelado_en                         DATETIME2(3) NULL,
    cancelado_por_cuenta_id              UNIQUEIDENTIFIER NULL CONSTRAINT FK_bd_cancelled_by REFERENCES dbo.cuentas(id),
    cancelado_por_perfil                 NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NULL,
    motivo_cancelacion                   NVARCHAR(1000) NULL,
    CONSTRAINT FK_bd_resident FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes(centro_id, id),
    CONSTRAINT FK_bd_unit FOREIGN KEY (centro_id, creado_en_unidad_id) REFERENCES dbo.unidades(centro_id, id),
    CONSTRAINT CK_bd_status CHECK (estado IN ('ACTIVE', 'CANCELLED', 'SIGNED')),
    CONSTRAINT CK_bd_reason CHECK (motivo_codigo IS NULL OR motivo_codigo IN ('ALTA', 'REVISION_PROGRAMADA', 'CAMBIO_FUNCIONAL_CONSOLIDADO')),
    CONSTRAINT CK_bd_source CHECK (fuente_informacion_comun_codigo IS NULL OR fuente_informacion_comun_codigo IN
        ('VALORACION_DIRECTA', 'HISTORIA_O_INFORME_CLINICO', 'PERSONAL_DEL_CENTRO', 'FAMILIAR_O_CUIDADOR', 'FUENTES_COMBINADAS', 'OTRA', 'NO_DOCUMENTADO')),
    CONSTRAINT CK_bd_source_other CHECK (
        (fuente_informacion_comun_codigo = 'OTRA' AND LEN(LTRIM(RTRIM(ISNULL(fuente_informacion_comun_otro_texto, '')))) > 0)
        OR (ISNULL(fuente_informacion_comun_codigo, '') <> 'OTRA' AND LEN(LTRIM(RTRIM(ISNULL(fuente_informacion_comun_otro_texto, '')))) = 0)
    ),
    CONSTRAINT CK_bd_profiles CHECK (creado_por_perfil IN ('ENFERMERIA', 'MEDICINA') AND actualizado_por_perfil IN ('ENFERMERIA', 'MEDICINA')),
    CONSTRAINT CK_bd_revision CHECK (revision_borrador >= 1),
    CONSTRAINT CK_bd_cancellation CHECK (
        (estado <> 'CANCELLED' AND cancelado_en IS NULL AND cancelado_por_cuenta_id IS NULL
            AND cancelado_por_perfil IS NULL AND motivo_cancelacion IS NULL)
        OR (estado = 'CANCELLED' AND cancelado_en IS NOT NULL
            AND cancelado_por_cuenta_id = creado_por_cuenta_id AND cancelado_por_perfil = creado_por_perfil
            AND LEN(LTRIM(RTRIM(ISNULL(motivo_cancelacion, '')))) > 0)
    )
);
CREATE UNIQUE INDEX UX_bd_scope ON dbo.basales_borrador (id, residente_id, centro_id);
-- Índice único condicional: como máximo un borrador ACTIVE por residente (traduce
-- baseline_drafts_active_unique) — es la base física de la concurrencia optimista de SignBaseline.
CREATE UNIQUE INDEX UX_bd_active ON dbo.basales_borrador (residente_id) WHERE estado = 'ACTIVE';
CREATE INDEX IX_bd_scope_status ON dbo.basales_borrador (centro_id, creado_en_unidad_id, residente_id, estado);
GO
-- Transition guard + no-delete (traduce baseline_drafts_transition_guard / _no_delete). Es el trigger
-- más importante de este script: es la garantía física, a nivel de base de datos, de la concurrencia
-- optimista que SqlBaselineRepository ya comprueba en aplicación con RowsAffected==1.
CREATE TRIGGER dbo.TR_bd_transition_guard ON dbo.basales_borrador AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE d.estado <> 'ACTIVE'
           OR i.residente_id <> d.residente_id OR i.centro_id <> d.centro_id OR i.creado_en_unidad_id <> d.creado_en_unidad_id
           OR i.creado_por_cuenta_id <> d.creado_por_cuenta_id OR i.creado_por_perfil <> d.creado_por_perfil
           OR i.creado_en <> d.creado_en
           OR i.estado NOT IN ('ACTIVE', 'CANCELLED', 'SIGNED')
           OR (i.estado = 'ACTIVE' AND i.revision_borrador <> d.revision_borrador + 1)
           OR (i.estado <> 'ACTIVE' AND i.revision_borrador <> d.revision_borrador)
           OR (i.estado = 'SIGNED' AND NOT EXISTS (SELECT 1 FROM dbo.basales_version v WHERE v.borrador_origen_id = d.id)))
        THROW 50160, 'BASELINE_DRAFT_TRANSITION_INVALID', 1;
END;
GO
CREATE TRIGGER dbo.TR_bd_no_delete ON dbo.basales_borrador INSTEAD OF DELETE AS
    THROW 50161, 'BASELINE_DRAFT_DELETE_FORBIDDEN', 1;
GO

------------------------------------------------------------
-- Basal: contenido del borrador (áreas, Barthel)
------------------------------------------------------------
CREATE TABLE dbo.basales_borrador_areas (
    id                                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_bda PRIMARY KEY,
    borrador_id                              UNIQUEIDENTIFIER NOT NULL,
    residente_id                             UNIQUEIDENTIFIER NOT NULL,
    centro_id                                UNIQUEIDENTIFIER NOT NULL,
    area_codigo                              NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    catalogo_version_codigo                  NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    respuestas_json                          NVARCHAR(MAX) NOT NULL,
    observacion                              NVARCHAR(1000) NULL,
    fuente_informacion_sustituta_codigo      NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NULL,
    fuente_informacion_sustituta_otro_texto  NVARCHAR(500) NULL,
    fecha_informacion_sustituta              DATE NULL,
    registrado_por_cuenta_id                 UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_bda_recorded_by REFERENCES dbo.cuentas(id),
    registrado_por_perfil                    NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    registrado_en                            DATETIME2(3) NOT NULL,
    CONSTRAINT FK_bda_draft FOREIGN KEY (borrador_id, residente_id, centro_id) REFERENCES dbo.basales_borrador(id, residente_id, centro_id),
    CONSTRAINT CK_bda_area CHECK (area_codigo IN
        ('MOVILIDAD', 'ALIMENTACION', 'CONTINENCIA', 'ASEO_HIGIENE', 'COGNICION', 'COMUNICACION', 'CONDUCTA', 'SUENO', 'AYUDAS_HABITUALES')),
    CONSTRAINT CK_bda_catalog CHECK (catalogo_version_codigo = 'BASAL_AREAS_V0_1'),
    CONSTRAINT CK_bda_json CHECK (ISJSON(respuestas_json) = 1),
    CONSTRAINT CK_bda_profile CHECK (registrado_por_perfil IN ('ENFERMERIA', 'MEDICINA')),
    -- Traduce baseline_draft_areas_payload_validate_*: solo las dos reglas expresables como CHECK simple
    -- sobre JSON_VALUE; el resto de reglas cruzadas viven en ResidApp.Domain.Baseline.Answers. Las claves
    -- del JSON (foodTextureCode, etc.) son propiedades C# serializadas, no columnas — fuera de alcance
    -- de este renombrado.
    CONSTRAINT CK_bda_feeding_enteral CHECK (
        area_codigo <> 'ALIMENTACION'
        OR ((ISNULL(JSON_VALUE(respuestas_json, '$.foodTextureCode'), '') <> 'NO_APLICA'
             AND ISNULL(JSON_VALUE(respuestas_json, '$.liquidConsistencyCode'), '') <> 'NO_APLICA')
            OR JSON_VALUE(respuestas_json, '$.routeCode') = 'ENTERAL')
    )
);
CREATE UNIQUE INDEX UX_bda_area ON dbo.basales_borrador_areas (borrador_id, area_codigo);
CREATE INDEX IX_bda_scope ON dbo.basales_borrador_areas (centro_id, residente_id, borrador_id, area_codigo);
GO
-- Mutable solo mientras el borrador padre está ACTIVE (traduce las 3 triggers
-- *_mutable_only_while_active_* / *_insert_only_while_active en una sola).
CREATE TRIGGER dbo.TR_bda_active_guard ON dbo.basales_borrador_areas AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i WHERE NOT EXISTS (SELECT 1 FROM dbo.basales_borrador d WHERE d.id = i.borrador_id AND d.estado = 'ACTIVE'))
        THROW 50170, 'BASELINE_DRAFT_AREA_PARENT_NOT_ACTIVE', 1;
    IF EXISTS (SELECT 1 FROM deleted d WHERE NOT EXISTS (SELECT 1 FROM dbo.basales_borrador bd WHERE bd.id = d.borrador_id AND bd.estado = 'ACTIVE'))
        THROW 50171, 'BASELINE_DRAFT_AREA_IMMUTABLE', 1;
END;
GO

CREATE TABLE dbo.basales_borrador_barthel (
    id                          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_bdb PRIMARY KEY,
    borrador_id                 UNIQUEIDENTIFIER NOT NULL,
    residente_id                UNIQUEIDENTIFIER NOT NULL,
    centro_id                   UNIQUEIDENTIFIER NOT NULL,
    instrumento_version_codigo  NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_bdb_instrument DEFAULT ('BARTHEL_COMUN_V0_1'),
    fecha_valoracion            DATE NULL,
    puntuacion_total            INT NULL,
    registrado_por_cuenta_id    UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_bdb_recorded_by REFERENCES dbo.cuentas(id),
    registrado_por_perfil       NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    registrado_en               DATETIME2(3) NOT NULL,
    CONSTRAINT FK_bdb_draft FOREIGN KEY (borrador_id, residente_id, centro_id) REFERENCES dbo.basales_borrador(id, residente_id, centro_id),
    CONSTRAINT CK_bdb_instrument CHECK (instrumento_version_codigo = 'BARTHEL_COMUN_V0_1'),
    CONSTRAINT CK_bdb_total CHECK (puntuacion_total IS NULL OR puntuacion_total BETWEEN 0 AND 100),
    CONSTRAINT CK_bdb_profile CHECK (registrado_por_perfil IN ('ENFERMERIA', 'MEDICINA'))
);
CREATE UNIQUE INDEX UX_bdb_draft ON dbo.basales_borrador_barthel (borrador_id);
CREATE UNIQUE INDEX UX_bdb_scope ON dbo.basales_borrador_barthel (id, borrador_id, residente_id, centro_id, instrumento_version_codigo);
GO
CREATE TRIGGER dbo.TR_bdb_active_guard ON dbo.basales_borrador_barthel AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i WHERE NOT EXISTS (SELECT 1 FROM dbo.basales_borrador d WHERE d.id = i.borrador_id AND d.estado = 'ACTIVE'))
        THROW 50172, 'BASELINE_DRAFT_BARTHEL_PARENT_NOT_ACTIVE', 1;
    IF EXISTS (SELECT 1 FROM deleted d WHERE NOT EXISTS (SELECT 1 FROM dbo.basales_borrador bd WHERE bd.id = d.borrador_id AND bd.estado = 'ACTIVE'))
        THROW 50173, 'BASELINE_DRAFT_BARTHEL_IMMUTABLE', 1;
END;
GO

-- Sin FK a barthel_catalog_options (fuera de alcance); solo CHECK de ítem/score.
CREATE TABLE dbo.basales_borrador_barthel_items (
    id                          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_bdbi PRIMARY KEY,
    barthel_id                  UNIQUEIDENTIFIER NOT NULL,
    borrador_id                 UNIQUEIDENTIFIER NOT NULL,
    residente_id                UNIQUEIDENTIFIER NOT NULL,
    centro_id                   UNIQUEIDENTIFIER NOT NULL,
    instrumento_version_codigo  NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    item_codigo                 NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    opcion_seleccionada_codigo  NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    puntuacion_otorgada         INT NOT NULL,
    CONSTRAINT FK_bdbi_barthel FOREIGN KEY (barthel_id, borrador_id, residente_id, centro_id, instrumento_version_codigo)
        REFERENCES dbo.basales_borrador_barthel(id, borrador_id, residente_id, centro_id, instrumento_version_codigo),
    CONSTRAINT CK_bdbi_item CHECK (item_codigo IN
        ('COMER', 'LAVARSE', 'VESTIRSE', 'ARREGLARSE', 'DEPOSICION', 'MICCION', 'USO_RETRETE', 'TRASLADO_CAMA_SILLON', 'DEAMBULACION', 'ESCALERAS')),
    CONSTRAINT CK_bdbi_score CHECK (puntuacion_otorgada IN (0, 5, 10, 15))
);
CREATE UNIQUE INDEX UX_bdbi_item ON dbo.basales_borrador_barthel_items (barthel_id, item_codigo);
GO
CREATE TRIGGER dbo.TR_bdbi_active_guard ON dbo.basales_borrador_barthel_items AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i WHERE NOT EXISTS (SELECT 1 FROM dbo.basales_borrador d WHERE d.id = i.borrador_id AND d.estado = 'ACTIVE'))
        THROW 50174, 'BASELINE_DRAFT_BARTHEL_ITEM_PARENT_NOT_ACTIVE', 1;
    IF EXISTS (SELECT 1 FROM deleted d WHERE NOT EXISTS (SELECT 1 FROM dbo.basales_borrador bd WHERE bd.id = d.borrador_id AND bd.estado = 'ACTIVE'))
        THROW 50175, 'BASELINE_DRAFT_BARTHEL_ITEM_IMMUTABLE', 1;
END;
GO

------------------------------------------------------------
-- Basal: versiones firmadas (append-only)
------------------------------------------------------------
CREATE TABLE dbo.basales_version (
    id                                   UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_bv PRIMARY KEY,
    borrador_origen_id                   UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_bv_draft REFERENCES dbo.basales_borrador(id),
    residente_id                         UNIQUEIDENTIFIER NOT NULL,
    centro_id                            UNIQUEIDENTIFIER NOT NULL,
    creado_en_unidad_id                  UNIQUEIDENTIFIER NOT NULL,
    numero_version                       INT NOT NULL,
    motivo_codigo                        NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    fuente_informacion_comun_codigo      NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    fuente_informacion_comun_otro_texto  NVARCHAR(500) NULL,
    fecha_informacion_comun              DATE NOT NULL,
    creado_por_cuenta_id                 UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_bv_created_by REFERENCES dbo.cuentas(id),
    creado_por_perfil                    NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    creado_en                            DATETIME2(3) NOT NULL,
    firmado_por_cuenta_id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_bv_signed_by REFERENCES dbo.cuentas(id),
    firmado_por_perfil                   NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    firmado_en                           DATETIME2(3) NOT NULL,
    vigente_desde                        DATETIME2(3) NOT NULL,
    operacion_activacion_id              UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT FK_bv_resident FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes(centro_id, id),
    CONSTRAINT FK_bv_unit FOREIGN KEY (centro_id, creado_en_unidad_id) REFERENCES dbo.unidades(centro_id, id),
    CONSTRAINT CK_bv_number CHECK (numero_version >= 1),
    CONSTRAINT CK_bv_reason CHECK (motivo_codigo IN ('ALTA', 'REVISION_PROGRAMADA', 'CAMBIO_FUNCIONAL_CONSOLIDADO')),
    CONSTRAINT CK_bv_signer CHECK (creado_por_cuenta_id = firmado_por_cuenta_id AND creado_por_perfil = firmado_por_perfil
        AND firmado_por_perfil IN ('ENFERMERIA', 'MEDICINA')),
    CONSTRAINT CK_bv_time CHECK (firmado_en >= creado_en AND vigente_desde = firmado_en)
);
CREATE UNIQUE INDEX UX_bv_source_draft ON dbo.basales_version (borrador_origen_id);
CREATE UNIQUE INDEX UX_bv_resident_number ON dbo.basales_version (residente_id, numero_version);
CREATE UNIQUE INDEX UX_bv_activation_operation ON dbo.basales_version (operacion_activacion_id);
CREATE UNIQUE INDEX UX_bv_scope ON dbo.basales_version (id, residente_id, centro_id);
CREATE INDEX IX_bv_scope_time ON dbo.basales_version (centro_id, residente_id, numero_version, firmado_en);
GO
-- Append-only estricto (traduce baseline_versions_no_update/no_delete). No se traduce el trigger
-- baseline_versions_validate_insert (~230 líneas, revalida campo a campo contra el borrador origen):
-- SqlBaselineRepository copia esos campos literalmente desde la fila de basales_borrador ya leída
-- dentro de la misma transacción, así que la revalidación sería tautológica (ver riesgos en 0001).
CREATE TRIGGER dbo.TR_bv_immutable ON dbo.basales_version INSTEAD OF UPDATE, DELETE AS
    THROW 50180, 'BASELINE_VERSION_IMMUTABLE', 1;
GO

CREATE TABLE dbo.basales_version_areas (
    id                                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_bva PRIMARY KEY,
    version_basal_id                         UNIQUEIDENTIFIER NOT NULL,
    residente_id                             UNIQUEIDENTIFIER NOT NULL,
    centro_id                                UNIQUEIDENTIFIER NOT NULL,
    area_codigo                              NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    catalogo_version_codigo                  NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    respuestas_json                          NVARCHAR(MAX) NOT NULL,
    observacion                              NVARCHAR(1000) NULL,
    fuente_informacion_sustituta_codigo      NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NULL,
    fuente_informacion_sustituta_otro_texto  NVARCHAR(500) NULL,
    fecha_informacion_sustituta              DATE NULL,
    registrado_por_cuenta_id                 UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_bva_recorded_by REFERENCES dbo.cuentas(id),
    registrado_por_perfil                    NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    registrado_en                            DATETIME2(3) NOT NULL,
    CONSTRAINT FK_bva_version FOREIGN KEY (version_basal_id, residente_id, centro_id) REFERENCES dbo.basales_version(id, residente_id, centro_id),
    CONSTRAINT CK_bva_area CHECK (area_codigo IN
        ('MOVILIDAD', 'ALIMENTACION', 'CONTINENCIA', 'ASEO_HIGIENE', 'COGNICION', 'COMUNICACION', 'CONDUCTA', 'SUENO', 'AYUDAS_HABITUALES')),
    CONSTRAINT CK_bva_json CHECK (ISJSON(respuestas_json) = 1)
);
CREATE UNIQUE INDEX UX_bva_area ON dbo.basales_version_areas (version_basal_id, area_codigo);
GO
-- No se traduce baseline_version_areas_copy_guard (revalida que la fila copiada coincide con el
-- borrador origen) por el mismo motivo tautológico que TR_bv_immutable; sí su inmutabilidad.
CREATE TRIGGER dbo.TR_bva_immutable ON dbo.basales_version_areas INSTEAD OF UPDATE, DELETE AS
    THROW 50181, 'BASELINE_VERSION_AREA_IMMUTABLE', 1;
GO

CREATE TABLE dbo.basales_version_barthel (
    id                          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_bvb PRIMARY KEY,
    version_basal_id            UNIQUEIDENTIFIER NOT NULL,
    residente_id                UNIQUEIDENTIFIER NOT NULL,
    centro_id                   UNIQUEIDENTIFIER NOT NULL,
    instrumento_version_codigo  NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    fecha_valoracion            DATE NOT NULL,
    puntuacion_total            INT NOT NULL,
    registrado_por_cuenta_id    UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_bvb_recorded_by REFERENCES dbo.cuentas(id),
    registrado_por_perfil       NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    registrado_en               DATETIME2(3) NOT NULL,
    CONSTRAINT FK_bvb_version FOREIGN KEY (version_basal_id, residente_id, centro_id) REFERENCES dbo.basales_version(id, residente_id, centro_id),
    CONSTRAINT CK_bvb_total CHECK (puntuacion_total BETWEEN 0 AND 100)
);
CREATE UNIQUE INDEX UX_bvb_version ON dbo.basales_version_barthel (version_basal_id);
CREATE UNIQUE INDEX UX_bvb_scope ON dbo.basales_version_barthel (id, version_basal_id, residente_id, centro_id, instrumento_version_codigo);
GO
CREATE TRIGGER dbo.TR_bvb_immutable ON dbo.basales_version_barthel INSTEAD OF UPDATE, DELETE AS
    THROW 50182, 'BASELINE_VERSION_BARTHEL_IMMUTABLE', 1;
GO

CREATE TABLE dbo.basales_version_barthel_items (
    id                          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_bvbi PRIMARY KEY,
    barthel_id                  UNIQUEIDENTIFIER NOT NULL,
    version_basal_id            UNIQUEIDENTIFIER NOT NULL,
    residente_id                UNIQUEIDENTIFIER NOT NULL,
    centro_id                   UNIQUEIDENTIFIER NOT NULL,
    instrumento_version_codigo  NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    item_codigo                 NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    opcion_seleccionada_codigo  NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    puntuacion_otorgada         INT NOT NULL,
    CONSTRAINT FK_bvbi_barthel FOREIGN KEY (barthel_id, version_basal_id, residente_id, centro_id, instrumento_version_codigo)
        REFERENCES dbo.basales_version_barthel(id, version_basal_id, residente_id, centro_id, instrumento_version_codigo),
    CONSTRAINT CK_bvbi_score CHECK (puntuacion_otorgada IN (0, 5, 10, 15))
);
CREATE UNIQUE INDEX UX_bvbi_item ON dbo.basales_version_barthel_items (barthel_id, item_codigo);
GO
CREATE TRIGGER dbo.TR_bvbi_immutable ON dbo.basales_version_barthel_items INSTEAD OF UPDATE, DELETE AS
    THROW 50183, 'BASELINE_VERSION_BARTHEL_ITEM_IMMUTABLE', 1;
GO

CREATE TABLE dbo.basales_vigentes_residente (
    residente_id       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_rcb PRIMARY KEY,
    centro_id          UNIQUEIDENTIFIER NOT NULL,
    version_basal_id   UNIQUEIDENTIFIER NOT NULL,
    activado_en        DATETIME2(3) NOT NULL,
    CONSTRAINT FK_rcb_resident FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes(centro_id, id),
    CONSTRAINT FK_rcb_version FOREIGN KEY (version_basal_id, residente_id, centro_id) REFERENCES dbo.basales_version(id, residente_id, centro_id)
);
CREATE UNIQUE INDEX UX_rcb_version ON dbo.basales_vigentes_residente (version_basal_id);
CREATE INDEX IX_rcb_scope ON dbo.basales_vigentes_residente (centro_id, residente_id);
GO
-- Traduce resident_current_baselines_validate_insert/_update (solo version_basal_id/activado_en
-- pueden cambiar en un update, y solo hacia una versión más nueva del mismo residente — ya lo garantiza
-- la FK compuesta a basales_version(id, residente_id, centro_id) más este guard) y _no_delete.
CREATE TRIGGER dbo.TR_rcb_update_guard ON dbo.basales_vigentes_residente AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.residente_id = d.residente_id
        WHERE i.centro_id <> d.centro_id)
        THROW 50190, 'BASELINE_CURRENT_UPDATE_INVALID', 1;
END;
GO
CREATE TRIGGER dbo.TR_rcb_no_delete ON dbo.basales_vigentes_residente INSTEAD OF DELETE AS
    THROW 50191, 'BASELINE_CURRENT_DELETE_FORBIDDEN', 1;
GO

CREATE TABLE dbo.basales_sustituciones (
    version_anterior_id  UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_bs PRIMARY KEY,
    version_nueva_id     UNIQUEIDENTIFIER NOT NULL,
    residente_id         UNIQUEIDENTIFIER NOT NULL,
    centro_id            UNIQUEIDENTIFIER NOT NULL,
    sustituido_en        DATETIME2(3) NOT NULL,
    CONSTRAINT FK_bs_previous FOREIGN KEY (version_anterior_id, residente_id, centro_id) REFERENCES dbo.basales_version(id, residente_id, centro_id),
    CONSTRAINT FK_bs_new FOREIGN KEY (version_nueva_id, residente_id, centro_id) REFERENCES dbo.basales_version(id, residente_id, centro_id),
    CONSTRAINT CK_bs_distinct CHECK (version_anterior_id <> version_nueva_id)
);
CREATE UNIQUE INDEX UX_bs_new ON dbo.basales_sustituciones (version_nueva_id);
CREATE INDEX IX_bs_scope ON dbo.basales_sustituciones (centro_id, residente_id, sustituido_en);
GO
CREATE TRIGGER dbo.TR_bs_immutable ON dbo.basales_sustituciones INSTEAD OF UPDATE, DELETE AS
    THROW 50192, 'BASELINE_SUPERSESSION_IMMUTABLE', 1;
GO

------------------------------------------------------------
-- Auditoría e idempotencia
------------------------------------------------------------
CREATE TABLE dbo.eventos_auditoria (
    id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_audit_events PRIMARY KEY,
    cuenta_id         UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_audit_account REFERENCES dbo.cuentas(id),
    perfil_activo     NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    centro_id         UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_audit_center REFERENCES dbo.centros(id),
    unidad_id         UNIQUEIDENTIFIER NULL,
    residente_id      UNIQUEIDENTIFIER NULL,
    tipo_recurso      NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    recurso_id        UNIQUEIDENTIFIER NOT NULL,
    accion_codigo     NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    proposito_codigo  NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NULL,
    ocurrido_en       DATETIME2(3) NOT NULL,
    CONSTRAINT FK_audit_unit FOREIGN KEY (centro_id, unidad_id) REFERENCES dbo.unidades(centro_id, id),
    CONSTRAINT FK_audit_resident FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes(centro_id, id),
    CONSTRAINT CK_audit_profile CHECK (perfil_activo IN ('AUXILIAR', 'ENFERMERIA', 'MEDICINA', 'FAMILIAR', 'ADMINISTRACION', 'DIRECCION_CLINICA')),
    CONSTRAINT CK_audit_direction_read CHECK (
        accion_codigo <> 'CLINICAL_DETAIL_READ'
        OR (perfil_activo = 'DIRECCION_CLINICA' AND proposito_codigo = 'SUPERVISION_CLINICA' AND unidad_id IS NOT NULL AND residente_id IS NOT NULL)
    )
);
CREATE INDEX IX_audit_scope_time ON dbo.eventos_auditoria (centro_id, unidad_id, residente_id, ocurrido_en);
GO
CREATE TRIGGER dbo.TR_audit_immutable ON dbo.eventos_auditoria INSTEAD OF UPDATE, DELETE AS
    THROW 50200, 'AUDIT_EVENT_IMMUTABLE', 1;
GO

CREATE TABLE dbo.operaciones_idempotencia (
    id                    UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_idem PRIMARY KEY,
    cuenta_id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_idem_account REFERENCES dbo.cuentas(id),
    accion_codigo         NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    operacion_id          UNIQUEIDENTIFIER NOT NULL,
    hash_solicitud        CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    estado                NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_idem_status DEFAULT ('IN_PROGRESS'),
    recurso_resultado_id  UNIQUEIDENTIFIER NULL,
    resultado_json        NVARCHAR(MAX) NULL,
    creado_en             DATETIME2(3) NOT NULL,
    completado_en         DATETIME2(3) NULL,
    CONSTRAINT CK_idem_action CHECK (accion_codigo IN ('RESIDENT_CREATE', 'BASELINE_SIGN', 'CLINICAL_DETAIL_READ')),
    CONSTRAINT CK_idem_status CHECK (estado IN ('IN_PROGRESS', 'SUCCEEDED')),
    CONSTRAINT CK_idem_result CHECK (
        (estado = 'IN_PROGRESS' AND recurso_resultado_id IS NULL AND resultado_json IS NULL AND completado_en IS NULL)
        OR (estado = 'SUCCEEDED' AND recurso_resultado_id IS NOT NULL AND resultado_json IS NOT NULL
            AND ISJSON(resultado_json) = 1 AND completado_en IS NOT NULL)
    )
);
CREATE UNIQUE INDEX UX_idem_operation ON dbo.operaciones_idempotencia (cuenta_id, accion_codigo, operacion_id);
GO
-- Traduce idempotency_operations_transition_guard (solo IN_PROGRESS -> SUCCEEDED, resto de campos
-- inmutables) y _no_delete.
CREATE TRIGGER dbo.TR_idem_transition_guard ON dbo.operaciones_idempotencia AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE d.estado <> 'IN_PROGRESS' OR i.cuenta_id <> d.cuenta_id OR i.accion_codigo <> d.accion_codigo
           OR i.operacion_id <> d.operacion_id OR i.hash_solicitud <> d.hash_solicitud OR i.creado_en <> d.creado_en
           OR i.estado <> 'SUCCEEDED')
        THROW 50210, 'IDEMPOTENCY_OPERATION_IMMUTABLE', 1;
END;
GO
CREATE TRIGGER dbo.TR_idem_no_delete ON dbo.operaciones_idempotencia INSTEAD OF DELETE AS
    THROW 50211, 'IDEMPOTENCY_OPERATION_IMMUTABLE', 1;
GO
