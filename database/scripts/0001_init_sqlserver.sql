/*
 * Traduce db/migrations/0001_resident_baseline_foundation.sql (SQLite/D1) a T-SQL para SQL Server.
 * Alcance: únicamente las 22 tablas del vertical residente/basal (de las 29 totales del original).
 * Fuera de alcance, deliberadamente sin FK: buildings/floors/rooms/places, barthel_catalog_options,
 * baseline_draft_contributions, center_location_config_versions — pertenecen a verticales todavía no
 * portados. Ver docs/decisiones-arquitectura/ para el detalle de riesgos y decisiones.
 *
 * Convenciones:
 *  - UNIQUEIDENTIFIER para todo ID opaco (generados siempre en C# con Guid.NewGuid(), nunca NEWID()
 *    como DEFAULT de columna de negocio: NEWID() no garantiza los nibbles de versión/variante v4 que
 *    exigen los readonly record struct de ResidApp.Shared).
 *  - COLLATE Latin1_General_100_BIN2 en columnas de código/estado/perfil: SQLite compara bytes
 *    (case-sensitive); la colación por defecto de SQL Server normalmente no lo es.
 *  - DATE para fechas civiles (nacimiento, fecha de información, fecha de valoración Barthel);
 *    DATETIME2(3) para instantes (creado/firmado/ocurrido).
 *  - Los CHECK que en SQLite usan json_valid(...) se traducen a ISJSON(...)=1.
 */

------------------------------------------------------------
-- Identidad y estructura organizativa
------------------------------------------------------------
CREATE TABLE dbo.accounts (
    id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_accounts PRIMARY KEY,
    external_subject  NVARCHAR(200) NOT NULL,
    status            NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_accounts_status DEFAULT ('ACTIVE'),
    created_at        DATETIME2(3) NOT NULL,
    CONSTRAINT CK_accounts_status CHECK (status IN ('ACTIVE', 'SUSPENDED'))
);
CREATE UNIQUE INDEX UX_accounts_external_subject ON dbo.accounts (external_subject);
GO

CREATE TABLE dbo.centers (
    id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_centers PRIMARY KEY,
    code          NVARCHAR(64) NOT NULL,
    display_name  NVARCHAR(200) NOT NULL,
    status        NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_centers_status DEFAULT ('ACTIVE'),
    created_at    DATETIME2(3) NOT NULL,
    CONSTRAINT CK_centers_status CHECK (status IN ('ACTIVE', 'INACTIVE'))
);
CREATE UNIQUE INDEX UX_centers_code ON dbo.centers (code);
GO

-- building_id/floor_id sin FK: buildings/floors son verticales fuera de alcance (ver cabecera).
CREATE TABLE dbo.units (
    id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_units PRIMARY KEY,
    center_id     UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_units_center REFERENCES dbo.centers(id),
    building_id   UNIQUEIDENTIFIER NULL,
    floor_id      UNIQUEIDENTIFIER NULL,
    code          NVARCHAR(64) NOT NULL,
    display_name  NVARCHAR(200) NOT NULL,
    status        NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_units_status DEFAULT ('ACTIVE'),
    created_at    DATETIME2(3) NOT NULL,
    CONSTRAINT CK_units_floor_requires_building CHECK (floor_id IS NULL OR building_id IS NOT NULL),
    CONSTRAINT CK_units_status CHECK (status IN ('ACTIVE', 'INACTIVE'))
);
CREATE UNIQUE INDEX UX_units_center_code ON dbo.units (center_id, code);
CREATE UNIQUE INDEX UX_units_center_id ON dbo.units (center_id, id);
GO

CREATE TABLE dbo.residents (
    id                        UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_residents PRIMARY KEY,
    center_id                 UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_residents_center REFERENCES dbo.centers(id),
    display_name              NVARCHAR(200) NOT NULL,
    birth_date                DATE NOT NULL,
    documented_sex_code       NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    status                    NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_residents_status DEFAULT ('ACTIVE'),
    inactivation_reason       NVARCHAR(1000) NULL,
    inactivated_at            DATETIME2(3) NULL,
    inactivated_by_account_id UNIQUEIDENTIFIER NULL CONSTRAINT FK_residents_inactivated_by REFERENCES dbo.accounts(id),
    inactivated_by_profile    NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NULL,
    created_at                DATETIME2(3) NOT NULL,
    created_by_account_id     UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_residents_created_by REFERENCES dbo.accounts(id),
    created_by_profile        NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CONSTRAINT CK_residents_sex CHECK (documented_sex_code IN ('male', 'female', 'other', 'unknown')),
    CONSTRAINT CK_residents_status CHECK (status IN ('ACTIVE', 'INACTIVE')),
    CONSTRAINT CK_residents_created_profile CHECK (created_by_profile IN ('ADMINISTRACION', 'ENFERMERIA')),
    CONSTRAINT CK_residents_inactivation CHECK (
        (status = 'ACTIVE' AND inactivation_reason IS NULL AND inactivated_at IS NULL
            AND inactivated_by_account_id IS NULL AND inactivated_by_profile IS NULL)
        OR (status = 'INACTIVE' AND LEN(LTRIM(RTRIM(ISNULL(inactivation_reason, '')))) > 0
            AND inactivated_at IS NOT NULL AND inactivated_by_account_id IS NOT NULL
            AND inactivated_by_profile = 'ADMINISTRACION')
    )
);
CREATE UNIQUE INDEX UX_residents_center_id ON dbo.residents (center_id, id);
GO

------------------------------------------------------------
-- Autorización: ámbitos y permisos por cuenta/perfil
------------------------------------------------------------
CREATE TABLE dbo.profile_scopes (
    id                     UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_profile_scopes PRIMARY KEY,
    account_id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_ps_account REFERENCES dbo.accounts(id),
    center_id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_ps_center REFERENCES dbo.centers(id),
    profile_code           NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    status                 NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_ps_status DEFAULT ('ACTIVE'),
    granted_at             DATETIME2(3) NOT NULL,
    granted_by_account_id  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_ps_granted_by REFERENCES dbo.accounts(id),
    revoked_at             DATETIME2(3) NULL,
    revoked_by_account_id  UNIQUEIDENTIFIER NULL CONSTRAINT FK_ps_revoked_by REFERENCES dbo.accounts(id),
    CONSTRAINT CK_ps_profile CHECK (profile_code IN ('AUXILIAR', 'ENFERMERIA', 'MEDICINA', 'FAMILIAR', 'ADMINISTRACION', 'DIRECCION_CLINICA')),
    CONSTRAINT CK_ps_status CHECK (status IN ('ACTIVE', 'REVOKED')),
    CONSTRAINT CK_ps_revocation CHECK (
        (status = 'ACTIVE' AND revoked_at IS NULL AND revoked_by_account_id IS NULL)
        OR (status = 'REVOKED' AND revoked_at IS NOT NULL AND revoked_by_account_id IS NOT NULL)
    )
);
CREATE UNIQUE INDEX UX_ps_id_center ON dbo.profile_scopes (id, center_id);
-- Índice único condicional: como máximo un perfil ACTIVO por cuenta/centro/perfil (traduce
-- profile_scopes_active_unique).
CREATE UNIQUE INDEX UX_ps_active ON dbo.profile_scopes (account_id, center_id, profile_code) WHERE status = 'ACTIVE';
CREATE INDEX IX_ps_authorization_lookup ON dbo.profile_scopes (center_id, profile_code, account_id, status);
GO
-- Revoke-only + no-delete (traduce profile_scopes_revoke_only / profile_scopes_no_delete).
CREATE TRIGGER dbo.TR_ps_revoke_only ON dbo.profile_scopes AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE d.status <> 'ACTIVE' OR i.status <> 'REVOKED'
           OR i.account_id <> d.account_id OR i.center_id <> d.center_id OR i.profile_code <> d.profile_code
           OR i.granted_at <> d.granted_at OR i.granted_by_account_id <> d.granted_by_account_id)
        THROW 50100, 'PROFILE_SCOPE_REVOKE_ONLY', 1;
END;
GO
CREATE TRIGGER dbo.TR_ps_no_delete ON dbo.profile_scopes INSTEAD OF DELETE AS
    THROW 50101, 'PROFILE_SCOPE_DELETE_FORBIDDEN', 1;
GO

CREATE TABLE dbo.profile_unit_scopes (
    id                     UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_pus PRIMARY KEY,
    profile_scope_id       UNIQUEIDENTIFIER NOT NULL,
    center_id              UNIQUEIDENTIFIER NOT NULL,
    unit_id                UNIQUEIDENTIFIER NOT NULL,
    granted_at             DATETIME2(3) NOT NULL,
    granted_by_account_id  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_pus_granted_by REFERENCES dbo.accounts(id),
    revoked_at             DATETIME2(3) NULL,
    revoked_by_account_id  UNIQUEIDENTIFIER NULL CONSTRAINT FK_pus_revoked_by REFERENCES dbo.accounts(id),
    CONSTRAINT FK_pus_scope FOREIGN KEY (profile_scope_id, center_id) REFERENCES dbo.profile_scopes(id, center_id),
    CONSTRAINT FK_pus_unit FOREIGN KEY (center_id, unit_id) REFERENCES dbo.units(center_id, id),
    CONSTRAINT CK_pus_revocation CHECK (
        (revoked_at IS NULL AND revoked_by_account_id IS NULL)
        OR (revoked_at IS NOT NULL AND revoked_by_account_id IS NOT NULL)
    )
);
CREATE UNIQUE INDEX UX_pus_active ON dbo.profile_unit_scopes (profile_scope_id, unit_id) WHERE revoked_at IS NULL;
CREATE INDEX IX_pus_lookup ON dbo.profile_unit_scopes (center_id, unit_id, profile_scope_id, revoked_at);
GO
CREATE TRIGGER dbo.TR_pus_revoke_only ON dbo.profile_unit_scopes AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE d.revoked_at IS NOT NULL OR i.revoked_at IS NULL
           OR i.profile_scope_id <> d.profile_scope_id OR i.center_id <> d.center_id OR i.unit_id <> d.unit_id
           OR i.granted_at <> d.granted_at OR i.granted_by_account_id <> d.granted_by_account_id)
        THROW 50110, 'PROFILE_UNIT_SCOPE_REVOKE_ONLY', 1;
END;
GO
CREATE TRIGGER dbo.TR_pus_no_delete ON dbo.profile_unit_scopes INSTEAD OF DELETE AS
    THROW 50111, 'PROFILE_UNIT_SCOPE_DELETE_FORBIDDEN', 1;
GO

CREATE TABLE dbo.profile_resident_scopes (
    id                     UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_prs PRIMARY KEY,
    profile_scope_id       UNIQUEIDENTIFIER NOT NULL,
    center_id              UNIQUEIDENTIFIER NOT NULL,
    resident_id            UNIQUEIDENTIFIER NOT NULL,
    granted_at             DATETIME2(3) NOT NULL,
    granted_by_account_id  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_prs_granted_by REFERENCES dbo.accounts(id),
    revoked_at             DATETIME2(3) NULL,
    revoked_by_account_id  UNIQUEIDENTIFIER NULL CONSTRAINT FK_prs_revoked_by REFERENCES dbo.accounts(id),
    CONSTRAINT FK_prs_scope FOREIGN KEY (profile_scope_id, center_id) REFERENCES dbo.profile_scopes(id, center_id),
    CONSTRAINT FK_prs_resident FOREIGN KEY (center_id, resident_id) REFERENCES dbo.residents(center_id, id),
    CONSTRAINT CK_prs_revocation CHECK (
        (revoked_at IS NULL AND revoked_by_account_id IS NULL)
        OR (revoked_at IS NOT NULL AND revoked_by_account_id IS NOT NULL)
    )
);
CREATE UNIQUE INDEX UX_prs_active ON dbo.profile_resident_scopes (profile_scope_id, resident_id) WHERE revoked_at IS NULL;
CREATE INDEX IX_prs_lookup ON dbo.profile_resident_scopes (center_id, resident_id, profile_scope_id, revoked_at);
GO
CREATE TRIGGER dbo.TR_prs_revoke_only ON dbo.profile_resident_scopes AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE d.revoked_at IS NOT NULL OR i.revoked_at IS NULL
           OR i.profile_scope_id <> d.profile_scope_id OR i.center_id <> d.center_id OR i.resident_id <> d.resident_id
           OR i.granted_at <> d.granted_at OR i.granted_by_account_id <> d.granted_by_account_id)
        THROW 50120, 'PROFILE_RESIDENT_SCOPE_REVOKE_ONLY', 1;
END;
GO
CREATE TRIGGER dbo.TR_prs_no_delete ON dbo.profile_resident_scopes INSTEAD OF DELETE AS
    THROW 50121, 'PROFILE_RESIDENT_SCOPE_DELETE_FORBIDDEN', 1;
GO

CREATE TABLE dbo.profile_permissions (
    id                     UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_pp PRIMARY KEY,
    profile_scope_id       UNIQUEIDENTIFIER NOT NULL,
    center_id              UNIQUEIDENTIFIER NOT NULL,
    permission_code        NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    granted_at             DATETIME2(3) NOT NULL,
    granted_by_account_id  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_pp_granted_by REFERENCES dbo.accounts(id),
    revoked_at             DATETIME2(3) NULL,
    revoked_by_account_id  UNIQUEIDENTIFIER NULL CONSTRAINT FK_pp_revoked_by REFERENCES dbo.accounts(id),
    CONSTRAINT FK_pp_scope FOREIGN KEY (profile_scope_id, center_id) REFERENCES dbo.profile_scopes(id, center_id),
    -- Se incluye BASELINE_DRAFT_CONTRIBUTE (verticales de contribución compartida) aunque este script no
    -- cree baseline_draft_contributions, para no romper el catálogo original de permisos válidos.
    CONSTRAINT CK_pp_code CHECK (permission_code IN
        ('RESIDENT_IDENTITY_CREATE', 'BASELINE_INITIAL_COMPLETE', 'BASELINE_REEVALUATE',
         'BASELINE_DRAFT_CONTRIBUTE', 'CLINICAL_DETAIL_READ')),
    CONSTRAINT CK_pp_revocation CHECK (
        (revoked_at IS NULL AND revoked_by_account_id IS NULL)
        OR (revoked_at IS NOT NULL AND revoked_by_account_id IS NOT NULL)
    )
);
CREATE UNIQUE INDEX UX_pp_active ON dbo.profile_permissions (profile_scope_id, permission_code) WHERE revoked_at IS NULL;
CREATE INDEX IX_pp_lookup ON dbo.profile_permissions (center_id, profile_scope_id, permission_code, revoked_at);
GO
CREATE TRIGGER dbo.TR_pp_revoke_only ON dbo.profile_permissions AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE d.revoked_at IS NOT NULL OR i.revoked_at IS NULL
           OR i.profile_scope_id <> d.profile_scope_id OR i.center_id <> d.center_id OR i.permission_code <> d.permission_code
           OR i.granted_at <> d.granted_at OR i.granted_by_account_id <> d.granted_by_account_id)
        THROW 50130, 'PROFILE_PERMISSION_REVOKE_ONLY', 1;
END;
GO
CREATE TRIGGER dbo.TR_pp_no_delete ON dbo.profile_permissions INSTEAD OF DELETE AS
    THROW 50131, 'PROFILE_PERMISSION_DELETE_FORBIDDEN', 1;
GO

------------------------------------------------------------
-- Episodios y ubicación del residente
------------------------------------------------------------
CREATE TABLE dbo.resident_center_episodes (
    id                      UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_rce PRIMARY KEY,
    resident_id             UNIQUEIDENTIFIER NOT NULL,
    center_id               UNIQUEIDENTIFIER NOT NULL,
    internal_reference      NVARCHAR(200) NULL,
    valid_from              DATETIME2(3) NOT NULL,
    valid_until             DATETIME2(3) NULL,
    created_at              DATETIME2(3) NOT NULL,
    created_by_account_id   UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_rce_created_by REFERENCES dbo.accounts(id),
    created_by_profile      NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CONSTRAINT FK_rce_resident FOREIGN KEY (center_id, resident_id) REFERENCES dbo.residents(center_id, id),
    CONSTRAINT CK_rce_time CHECK (valid_until IS NULL OR valid_until > valid_from),
    CONSTRAINT CK_rce_profile CHECK (created_by_profile IN ('ADMINISTRACION', 'ENFERMERIA'))
);
CREATE UNIQUE INDEX UX_rce_scope ON dbo.resident_center_episodes (id, resident_id, center_id);
-- Índice único condicional: como máximo un episodio vigente por residente (traduce
-- resident_center_episodes_active_unique). No se traduce resident_center_episodes_no_overlap (validación
-- completa de solapamiento temporal): el único camino de escritura hoy es el alta inicial, que crea un
-- único episodio sin valid_until — ver riesgos.
CREATE UNIQUE INDEX UX_rce_active ON dbo.resident_center_episodes (resident_id) WHERE valid_until IS NULL;
CREATE INDEX IX_rce_lookup ON dbo.resident_center_episodes (center_id, resident_id, valid_from);
GO
-- Close-only + no-delete (traduce resident_center_episodes_close_only / _no_delete).
CREATE TRIGGER dbo.TR_rce_close_only ON dbo.resident_center_episodes AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE d.valid_until IS NOT NULL OR i.valid_until IS NULL
           OR i.resident_id <> d.resident_id OR i.center_id <> d.center_id
           OR ISNULL(i.internal_reference, '') <> ISNULL(d.internal_reference, '')
           OR i.valid_from <> d.valid_from OR i.created_at <> d.created_at
           OR i.created_by_account_id <> d.created_by_account_id OR i.created_by_profile <> d.created_by_profile)
        THROW 50140, 'RESIDENT_CENTER_EPISODE_CLOSE_ONLY', 1;
END;
GO
CREATE TRIGGER dbo.TR_rce_no_delete ON dbo.resident_center_episodes INSTEAD OF DELETE AS
    THROW 50141, 'RESIDENT_CENTER_EPISODE_DELETE_FORBIDDEN', 1;
GO

-- building_id/floor_id/room_id/place_id sin FK: verticales fuera de alcance (ver cabecera).
CREATE TABLE dbo.resident_location_intervals (
    id                      UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_rli PRIMARY KEY,
    resident_id             UNIQUEIDENTIFIER NOT NULL,
    center_id               UNIQUEIDENTIFIER NOT NULL,
    episode_id              UNIQUEIDENTIFIER NOT NULL,
    unit_id                 UNIQUEIDENTIFIER NOT NULL,
    building_id             UNIQUEIDENTIFIER NULL,
    floor_id                UNIQUEIDENTIFIER NULL,
    room_id                 UNIQUEIDENTIFIER NULL,
    place_id                UNIQUEIDENTIFIER NULL,
    valid_from              DATETIME2(3) NOT NULL,
    valid_until             DATETIME2(3) NULL,
    changed_at              DATETIME2(3) NOT NULL,
    changed_by_account_id   UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_rli_changed_by REFERENCES dbo.accounts(id),
    changed_by_profile      NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CONSTRAINT FK_rli_episode FOREIGN KEY (episode_id, resident_id, center_id) REFERENCES dbo.resident_center_episodes(id, resident_id, center_id),
    CONSTRAINT FK_rli_unit FOREIGN KEY (center_id, unit_id) REFERENCES dbo.units(center_id, id),
    CONSTRAINT CK_rli_time CHECK (valid_until IS NULL OR valid_until > valid_from),
    CONSTRAINT CK_rli_hierarchy CHECK (
        (floor_id IS NULL OR building_id IS NOT NULL) AND (place_id IS NULL OR room_id IS NOT NULL)
    ),
    CONSTRAINT CK_rli_profile CHECK (changed_by_profile IN ('ADMINISTRACION', 'ENFERMERIA'))
);
-- Índice único condicional: como máximo una ubicación vigente por residente (traduce
-- resident_location_intervals_active_unique). No se traduce la validación completa de
-- resident_location_intervals_validate_insert (config de centro para habitación/plaza obligatoria) —
-- ver riesgos.
CREATE UNIQUE INDEX UX_rli_active ON dbo.resident_location_intervals (resident_id) WHERE valid_until IS NULL;
CREATE INDEX IX_rli_current_lookup ON dbo.resident_location_intervals (center_id, unit_id, resident_id, valid_until);
GO
CREATE TRIGGER dbo.TR_rli_close_only ON dbo.resident_location_intervals AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE d.valid_until IS NOT NULL OR i.valid_until IS NULL
           OR i.resident_id <> d.resident_id OR i.center_id <> d.center_id OR i.episode_id <> d.episode_id
           OR i.unit_id <> d.unit_id OR ISNULL(i.building_id,'00000000-0000-0000-0000-000000000000') <> ISNULL(d.building_id,'00000000-0000-0000-0000-000000000000')
           OR ISNULL(i.floor_id,'00000000-0000-0000-0000-000000000000') <> ISNULL(d.floor_id,'00000000-0000-0000-0000-000000000000')
           OR ISNULL(i.room_id,'00000000-0000-0000-0000-000000000000') <> ISNULL(d.room_id,'00000000-0000-0000-0000-000000000000')
           OR ISNULL(i.place_id,'00000000-0000-0000-0000-000000000000') <> ISNULL(d.place_id,'00000000-0000-0000-0000-000000000000')
           OR i.valid_from <> d.valid_from)
        THROW 50150, 'RESIDENT_LOCATION_INTERVAL_CLOSE_ONLY', 1;
END;
GO
CREATE TRIGGER dbo.TR_rli_no_delete ON dbo.resident_location_intervals INSTEAD OF DELETE AS
    THROW 50151, 'RESIDENT_LOCATION_INTERVAL_DELETE_FORBIDDEN', 1;
GO

------------------------------------------------------------
-- Basal: borrador (mutable mientras ACTIVE; concurrencia optimista por draft_revision)
------------------------------------------------------------
CREATE TABLE dbo.baseline_drafts (
    id                                     UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_bd PRIMARY KEY,
    resident_id                            UNIQUEIDENTIFIER NOT NULL,
    center_id                              UNIQUEIDENTIFIER NOT NULL,
    created_in_unit_id                     UNIQUEIDENTIFIER NOT NULL,
    status                                 NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_bd_status DEFAULT ('ACTIVE'),
    reason_code                            NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NULL,
    common_information_source_code         NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NULL,
    common_information_source_other_text   NVARCHAR(500) NULL,
    common_information_date                DATE NULL,
    created_by_account_id                  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_bd_created_by REFERENCES dbo.accounts(id),
    created_by_profile                     NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_at                             DATETIME2(3) NOT NULL,
    updated_by_account_id                  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_bd_updated_by REFERENCES dbo.accounts(id),
    updated_by_profile                     NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    updated_at                             DATETIME2(3) NOT NULL,
    draft_revision                         INT NOT NULL CONSTRAINT DF_bd_revision DEFAULT (1),
    cancelled_at                           DATETIME2(3) NULL,
    cancelled_by_account_id                UNIQUEIDENTIFIER NULL CONSTRAINT FK_bd_cancelled_by REFERENCES dbo.accounts(id),
    cancelled_by_profile                   NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NULL,
    cancellation_reason                    NVARCHAR(1000) NULL,
    CONSTRAINT FK_bd_resident FOREIGN KEY (center_id, resident_id) REFERENCES dbo.residents(center_id, id),
    CONSTRAINT FK_bd_unit FOREIGN KEY (center_id, created_in_unit_id) REFERENCES dbo.units(center_id, id),
    CONSTRAINT CK_bd_status CHECK (status IN ('ACTIVE', 'CANCELLED', 'SIGNED')),
    CONSTRAINT CK_bd_reason CHECK (reason_code IS NULL OR reason_code IN ('ALTA', 'REVISION_PROGRAMADA', 'CAMBIO_FUNCIONAL_CONSOLIDADO')),
    CONSTRAINT CK_bd_source CHECK (common_information_source_code IS NULL OR common_information_source_code IN
        ('VALORACION_DIRECTA', 'HISTORIA_O_INFORME_CLINICO', 'PERSONAL_DEL_CENTRO', 'FAMILIAR_O_CUIDADOR', 'FUENTES_COMBINADAS', 'OTRA', 'NO_DOCUMENTADO')),
    CONSTRAINT CK_bd_source_other CHECK (
        (common_information_source_code = 'OTRA' AND LEN(LTRIM(RTRIM(ISNULL(common_information_source_other_text, '')))) > 0)
        OR (ISNULL(common_information_source_code, '') <> 'OTRA' AND LEN(LTRIM(RTRIM(ISNULL(common_information_source_other_text, '')))) = 0)
    ),
    CONSTRAINT CK_bd_profiles CHECK (created_by_profile IN ('ENFERMERIA', 'MEDICINA') AND updated_by_profile IN ('ENFERMERIA', 'MEDICINA')),
    CONSTRAINT CK_bd_revision CHECK (draft_revision >= 1),
    CONSTRAINT CK_bd_cancellation CHECK (
        (status <> 'CANCELLED' AND cancelled_at IS NULL AND cancelled_by_account_id IS NULL
            AND cancelled_by_profile IS NULL AND cancellation_reason IS NULL)
        OR (status = 'CANCELLED' AND cancelled_at IS NOT NULL
            AND cancelled_by_account_id = created_by_account_id AND cancelled_by_profile = created_by_profile
            AND LEN(LTRIM(RTRIM(ISNULL(cancellation_reason, '')))) > 0)
    )
);
CREATE UNIQUE INDEX UX_bd_scope ON dbo.baseline_drafts (id, resident_id, center_id);
-- Índice único condicional: como máximo un borrador ACTIVE por residente (traduce
-- baseline_drafts_active_unique) — es la base física de la concurrencia optimista de SignBaseline.
CREATE UNIQUE INDEX UX_bd_active ON dbo.baseline_drafts (resident_id) WHERE status = 'ACTIVE';
CREATE INDEX IX_bd_scope_status ON dbo.baseline_drafts (center_id, created_in_unit_id, resident_id, status);
GO
-- Transition guard + no-delete (traduce baseline_drafts_transition_guard / _no_delete). Es el trigger más
-- importante de este script: es la garantía física, a nivel de base de datos, de la concurrencia
-- optimista que SqlBaselineRepository ya comprueba en aplicación con RowsAffected==1.
CREATE TRIGGER dbo.TR_bd_transition_guard ON dbo.baseline_drafts AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE d.status <> 'ACTIVE'
           OR i.resident_id <> d.resident_id OR i.center_id <> d.center_id OR i.created_in_unit_id <> d.created_in_unit_id
           OR i.created_by_account_id <> d.created_by_account_id OR i.created_by_profile <> d.created_by_profile
           OR i.created_at <> d.created_at
           OR i.status NOT IN ('ACTIVE', 'CANCELLED', 'SIGNED')
           OR (i.status = 'ACTIVE' AND i.draft_revision <> d.draft_revision + 1)
           OR (i.status <> 'ACTIVE' AND i.draft_revision <> d.draft_revision)
           OR (i.status = 'SIGNED' AND NOT EXISTS (SELECT 1 FROM dbo.baseline_versions v WHERE v.source_draft_id = d.id)))
        THROW 50160, 'BASELINE_DRAFT_TRANSITION_INVALID', 1;
END;
GO
CREATE TRIGGER dbo.TR_bd_no_delete ON dbo.baseline_drafts INSTEAD OF DELETE AS
    THROW 50161, 'BASELINE_DRAFT_DELETE_FORBIDDEN', 1;
GO

------------------------------------------------------------
-- Basal: contenido del borrador (áreas, Barthel)
------------------------------------------------------------
CREATE TABLE dbo.baseline_draft_areas (
    id                                        UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_bda PRIMARY KEY,
    draft_id                                  UNIQUEIDENTIFIER NOT NULL,
    resident_id                               UNIQUEIDENTIFIER NOT NULL,
    center_id                                 UNIQUEIDENTIFIER NOT NULL,
    area_code                                 NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    catalog_version_code                      NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    answer_payload                            NVARCHAR(MAX) NOT NULL,
    observation                               NVARCHAR(1000) NULL,
    information_source_override_code          NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NULL,
    information_source_override_other_text    NVARCHAR(500) NULL,
    information_date_override                 DATE NULL,
    recorded_by_account_id                    UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_bda_recorded_by REFERENCES dbo.accounts(id),
    recorded_by_profile                       NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    recorded_at                               DATETIME2(3) NOT NULL,
    CONSTRAINT FK_bda_draft FOREIGN KEY (draft_id, resident_id, center_id) REFERENCES dbo.baseline_drafts(id, resident_id, center_id),
    CONSTRAINT CK_bda_area CHECK (area_code IN
        ('MOVILIDAD', 'ALIMENTACION', 'CONTINENCIA', 'ASEO_HIGIENE', 'COGNICION', 'COMUNICACION', 'CONDUCTA', 'SUENO', 'AYUDAS_HABITUALES')),
    CONSTRAINT CK_bda_catalog CHECK (catalog_version_code = 'BASAL_AREAS_V0_1'),
    CONSTRAINT CK_bda_json CHECK (ISJSON(answer_payload) = 1),
    CONSTRAINT CK_bda_profile CHECK (recorded_by_profile IN ('ENFERMERIA', 'MEDICINA')),
    -- Traduce baseline_draft_areas_payload_validate_*: solo las dos reglas expresables como CHECK simple
    -- sobre JSON_VALUE; el resto de reglas cruzadas de validation.ts viven en los records de
    -- ResidApp.Domain.Baseline.Answers, que ya validan antes de llegar aquí.
    CONSTRAINT CK_bda_feeding_enteral CHECK (
        area_code <> 'ALIMENTACION'
        OR ((ISNULL(JSON_VALUE(answer_payload, '$.foodTextureCode'), '') <> 'NO_APLICA'
             AND ISNULL(JSON_VALUE(answer_payload, '$.liquidConsistencyCode'), '') <> 'NO_APLICA')
            OR JSON_VALUE(answer_payload, '$.routeCode') = 'ENTERAL')
    )
);
CREATE UNIQUE INDEX UX_bda_area ON dbo.baseline_draft_areas (draft_id, area_code);
CREATE INDEX IX_bda_scope ON dbo.baseline_draft_areas (center_id, resident_id, draft_id, area_code);
GO
-- Mutable solo mientras el borrador padre está ACTIVE (traduce las 3 triggers
-- *_mutable_only_while_active_* / *_insert_only_while_active en una sola).
CREATE TRIGGER dbo.TR_bda_active_guard ON dbo.baseline_draft_areas AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i WHERE NOT EXISTS (SELECT 1 FROM dbo.baseline_drafts d WHERE d.id = i.draft_id AND d.status = 'ACTIVE'))
        THROW 50170, 'BASELINE_DRAFT_AREA_PARENT_NOT_ACTIVE', 1;
    IF EXISTS (SELECT 1 FROM deleted d WHERE NOT EXISTS (SELECT 1 FROM dbo.baseline_drafts bd WHERE bd.id = d.draft_id AND bd.status = 'ACTIVE'))
        THROW 50171, 'BASELINE_DRAFT_AREA_IMMUTABLE', 1;
END;
GO

CREATE TABLE dbo.baseline_draft_barthel (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_bdb PRIMARY KEY,
    draft_id                 UNIQUEIDENTIFIER NOT NULL,
    resident_id              UNIQUEIDENTIFIER NOT NULL,
    center_id                UNIQUEIDENTIFIER NOT NULL,
    instrument_version_code  NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_bdb_instrument DEFAULT ('BARTHEL_COMUN_V0_1'),
    assessment_date          DATE NULL,
    total_score              INT NULL,
    recorded_by_account_id   UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_bdb_recorded_by REFERENCES dbo.accounts(id),
    recorded_by_profile      NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    recorded_at              DATETIME2(3) NOT NULL,
    CONSTRAINT FK_bdb_draft FOREIGN KEY (draft_id, resident_id, center_id) REFERENCES dbo.baseline_drafts(id, resident_id, center_id),
    CONSTRAINT CK_bdb_instrument CHECK (instrument_version_code = 'BARTHEL_COMUN_V0_1'),
    CONSTRAINT CK_bdb_total CHECK (total_score IS NULL OR total_score BETWEEN 0 AND 100),
    CONSTRAINT CK_bdb_profile CHECK (recorded_by_profile IN ('ENFERMERIA', 'MEDICINA'))
);
CREATE UNIQUE INDEX UX_bdb_draft ON dbo.baseline_draft_barthel (draft_id);
CREATE UNIQUE INDEX UX_bdb_scope ON dbo.baseline_draft_barthel (id, draft_id, resident_id, center_id, instrument_version_code);
GO
CREATE TRIGGER dbo.TR_bdb_active_guard ON dbo.baseline_draft_barthel AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i WHERE NOT EXISTS (SELECT 1 FROM dbo.baseline_drafts d WHERE d.id = i.draft_id AND d.status = 'ACTIVE'))
        THROW 50172, 'BASELINE_DRAFT_BARTHEL_PARENT_NOT_ACTIVE', 1;
    IF EXISTS (SELECT 1 FROM deleted d WHERE NOT EXISTS (SELECT 1 FROM dbo.baseline_drafts bd WHERE bd.id = d.draft_id AND bd.status = 'ACTIVE'))
        THROW 50173, 'BASELINE_DRAFT_BARTHEL_IMMUTABLE', 1;
END;
GO

-- Sin FK a barthel_catalog_options (fuera de alcance); solo CHECK de ítem/score.
CREATE TABLE dbo.baseline_draft_barthel_items (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_bdbi PRIMARY KEY,
    barthel_id               UNIQUEIDENTIFIER NOT NULL,
    draft_id                 UNIQUEIDENTIFIER NOT NULL,
    resident_id              UNIQUEIDENTIFIER NOT NULL,
    center_id                UNIQUEIDENTIFIER NOT NULL,
    instrument_version_code  NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    item_code                NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    selected_option_code     NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    awarded_score            INT NOT NULL,
    CONSTRAINT FK_bdbi_barthel FOREIGN KEY (barthel_id, draft_id, resident_id, center_id, instrument_version_code)
        REFERENCES dbo.baseline_draft_barthel(id, draft_id, resident_id, center_id, instrument_version_code),
    CONSTRAINT CK_bdbi_item CHECK (item_code IN
        ('COMER', 'LAVARSE', 'VESTIRSE', 'ARREGLARSE', 'DEPOSICION', 'MICCION', 'USO_RETRETE', 'TRASLADO_CAMA_SILLON', 'DEAMBULACION', 'ESCALERAS')),
    CONSTRAINT CK_bdbi_score CHECK (awarded_score IN (0, 5, 10, 15))
);
CREATE UNIQUE INDEX UX_bdbi_item ON dbo.baseline_draft_barthel_items (barthel_id, item_code);
GO
CREATE TRIGGER dbo.TR_bdbi_active_guard ON dbo.baseline_draft_barthel_items AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i WHERE NOT EXISTS (SELECT 1 FROM dbo.baseline_drafts d WHERE d.id = i.draft_id AND d.status = 'ACTIVE'))
        THROW 50174, 'BASELINE_DRAFT_BARTHEL_ITEM_PARENT_NOT_ACTIVE', 1;
    IF EXISTS (SELECT 1 FROM deleted d WHERE NOT EXISTS (SELECT 1 FROM dbo.baseline_drafts bd WHERE bd.id = d.draft_id AND bd.status = 'ACTIVE'))
        THROW 50175, 'BASELINE_DRAFT_BARTHEL_ITEM_IMMUTABLE', 1;
END;
GO

------------------------------------------------------------
-- Basal: versiones firmadas (append-only)
------------------------------------------------------------
CREATE TABLE dbo.baseline_versions (
    id                                     UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_bv PRIMARY KEY,
    source_draft_id                        UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_bv_draft REFERENCES dbo.baseline_drafts(id),
    resident_id                            UNIQUEIDENTIFIER NOT NULL,
    center_id                              UNIQUEIDENTIFIER NOT NULL,
    created_in_unit_id                     UNIQUEIDENTIFIER NOT NULL,
    version_number                         INT NOT NULL,
    reason_code                            NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    common_information_source_code         NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    common_information_source_other_text   NVARCHAR(500) NULL,
    common_information_date                DATE NOT NULL,
    created_by_account_id                  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_bv_created_by REFERENCES dbo.accounts(id),
    created_by_profile                     NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_at                             DATETIME2(3) NOT NULL,
    signed_by_account_id                   UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_bv_signed_by REFERENCES dbo.accounts(id),
    signed_by_profile                      NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    signed_at                              DATETIME2(3) NOT NULL,
    valid_from                             DATETIME2(3) NOT NULL,
    activation_operation_id                UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT FK_bv_resident FOREIGN KEY (center_id, resident_id) REFERENCES dbo.residents(center_id, id),
    CONSTRAINT FK_bv_unit FOREIGN KEY (center_id, created_in_unit_id) REFERENCES dbo.units(center_id, id),
    CONSTRAINT CK_bv_number CHECK (version_number >= 1),
    CONSTRAINT CK_bv_reason CHECK (reason_code IN ('ALTA', 'REVISION_PROGRAMADA', 'CAMBIO_FUNCIONAL_CONSOLIDADO')),
    CONSTRAINT CK_bv_signer CHECK (created_by_account_id = signed_by_account_id AND created_by_profile = signed_by_profile
        AND signed_by_profile IN ('ENFERMERIA', 'MEDICINA')),
    CONSTRAINT CK_bv_time CHECK (signed_at >= created_at AND valid_from = signed_at)
);
CREATE UNIQUE INDEX UX_bv_source_draft ON dbo.baseline_versions (source_draft_id);
CREATE UNIQUE INDEX UX_bv_resident_number ON dbo.baseline_versions (resident_id, version_number);
CREATE UNIQUE INDEX UX_bv_activation_operation ON dbo.baseline_versions (activation_operation_id);
CREATE UNIQUE INDEX UX_bv_scope ON dbo.baseline_versions (id, resident_id, center_id);
CREATE INDEX IX_bv_scope_time ON dbo.baseline_versions (center_id, resident_id, version_number, signed_at);
GO
-- Append-only estricto (traduce baseline_versions_no_update/no_delete). No se traduce el trigger
-- baseline_versions_validate_insert (~230 líneas, revalida campo a campo contra el borrador origen):
-- SqlBaselineRepository copia esos campos literalmente desde la fila de baseline_drafts ya leída dentro
-- de la misma transacción, así que la revalidación sería tautológica (ver riesgos del plan).
CREATE TRIGGER dbo.TR_bv_immutable ON dbo.baseline_versions INSTEAD OF UPDATE, DELETE AS
    THROW 50180, 'BASELINE_VERSION_IMMUTABLE', 1;
GO

CREATE TABLE dbo.baseline_version_areas (
    id                                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_bva PRIMARY KEY,
    baseline_version_id                      UNIQUEIDENTIFIER NOT NULL,
    resident_id                              UNIQUEIDENTIFIER NOT NULL,
    center_id                                UNIQUEIDENTIFIER NOT NULL,
    area_code                                NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    catalog_version_code                     NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    answer_payload                           NVARCHAR(MAX) NOT NULL,
    observation                              NVARCHAR(1000) NULL,
    information_source_override_code         NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NULL,
    information_source_override_other_text   NVARCHAR(500) NULL,
    information_date_override                DATE NULL,
    recorded_by_account_id                   UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_bva_recorded_by REFERENCES dbo.accounts(id),
    recorded_by_profile                      NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    recorded_at                              DATETIME2(3) NOT NULL,
    CONSTRAINT FK_bva_version FOREIGN KEY (baseline_version_id, resident_id, center_id) REFERENCES dbo.baseline_versions(id, resident_id, center_id),
    CONSTRAINT CK_bva_area CHECK (area_code IN
        ('MOVILIDAD', 'ALIMENTACION', 'CONTINENCIA', 'ASEO_HIGIENE', 'COGNICION', 'COMUNICACION', 'CONDUCTA', 'SUENO', 'AYUDAS_HABITUALES')),
    CONSTRAINT CK_bva_json CHECK (ISJSON(answer_payload) = 1)
);
CREATE UNIQUE INDEX UX_bva_area ON dbo.baseline_version_areas (baseline_version_id, area_code);
GO
-- No se traduce baseline_version_areas_copy_guard (revalida que la fila copiada coincide con el
-- borrador origen) por el mismo motivo tautológico que TR_bv_immutable; sí su inmutabilidad.
CREATE TRIGGER dbo.TR_bva_immutable ON dbo.baseline_version_areas INSTEAD OF UPDATE, DELETE AS
    THROW 50181, 'BASELINE_VERSION_AREA_IMMUTABLE', 1;
GO

CREATE TABLE dbo.baseline_version_barthel (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_bvb PRIMARY KEY,
    baseline_version_id      UNIQUEIDENTIFIER NOT NULL,
    resident_id              UNIQUEIDENTIFIER NOT NULL,
    center_id                UNIQUEIDENTIFIER NOT NULL,
    instrument_version_code  NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    assessment_date          DATE NOT NULL,
    total_score               INT NOT NULL,
    recorded_by_account_id    UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_bvb_recorded_by REFERENCES dbo.accounts(id),
    recorded_by_profile       NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    recorded_at                DATETIME2(3) NOT NULL,
    CONSTRAINT FK_bvb_version FOREIGN KEY (baseline_version_id, resident_id, center_id) REFERENCES dbo.baseline_versions(id, resident_id, center_id),
    CONSTRAINT CK_bvb_total CHECK (total_score BETWEEN 0 AND 100)
);
CREATE UNIQUE INDEX UX_bvb_version ON dbo.baseline_version_barthel (baseline_version_id);
CREATE UNIQUE INDEX UX_bvb_scope ON dbo.baseline_version_barthel (id, baseline_version_id, resident_id, center_id, instrument_version_code);
GO
CREATE TRIGGER dbo.TR_bvb_immutable ON dbo.baseline_version_barthel INSTEAD OF UPDATE, DELETE AS
    THROW 50182, 'BASELINE_VERSION_BARTHEL_IMMUTABLE', 1;
GO

CREATE TABLE dbo.baseline_version_barthel_items (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_bvbi PRIMARY KEY,
    barthel_id               UNIQUEIDENTIFIER NOT NULL,
    baseline_version_id      UNIQUEIDENTIFIER NOT NULL,
    resident_id               UNIQUEIDENTIFIER NOT NULL,
    center_id                 UNIQUEIDENTIFIER NOT NULL,
    instrument_version_code   NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    item_code                 NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    selected_option_code      NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    awarded_score              INT NOT NULL,
    CONSTRAINT FK_bvbi_barthel FOREIGN KEY (barthel_id, baseline_version_id, resident_id, center_id, instrument_version_code)
        REFERENCES dbo.baseline_version_barthel(id, baseline_version_id, resident_id, center_id, instrument_version_code),
    CONSTRAINT CK_bvbi_score CHECK (awarded_score IN (0, 5, 10, 15))
);
CREATE UNIQUE INDEX UX_bvbi_item ON dbo.baseline_version_barthel_items (barthel_id, item_code);
GO
CREATE TRIGGER dbo.TR_bvbi_immutable ON dbo.baseline_version_barthel_items INSTEAD OF UPDATE, DELETE AS
    THROW 50183, 'BASELINE_VERSION_BARTHEL_ITEM_IMMUTABLE', 1;
GO

CREATE TABLE dbo.resident_current_baselines (
    resident_id           UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_rcb PRIMARY KEY,
    center_id             UNIQUEIDENTIFIER NOT NULL,
    baseline_version_id   UNIQUEIDENTIFIER NOT NULL,
    activated_at          DATETIME2(3) NOT NULL,
    CONSTRAINT FK_rcb_resident FOREIGN KEY (center_id, resident_id) REFERENCES dbo.residents(center_id, id),
    CONSTRAINT FK_rcb_version FOREIGN KEY (baseline_version_id, resident_id, center_id) REFERENCES dbo.baseline_versions(id, resident_id, center_id)
);
CREATE UNIQUE INDEX UX_rcb_version ON dbo.resident_current_baselines (baseline_version_id);
CREATE INDEX IX_rcb_scope ON dbo.resident_current_baselines (center_id, resident_id);
GO
-- Traduce resident_current_baselines_validate_insert/_update (solo baseline_version_id/activated_at
-- pueden cambiar en un update, y solo hacia una versión más nueva del mismo residente — ya lo garantiza
-- la FK compuesta a baseline_versions(id, resident_id, center_id) más este guard) y _no_delete.
CREATE TRIGGER dbo.TR_rcb_update_guard ON dbo.resident_current_baselines AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.resident_id = d.resident_id
        WHERE i.center_id <> d.center_id)
        THROW 50190, 'BASELINE_CURRENT_UPDATE_INVALID', 1;
END;
GO
CREATE TRIGGER dbo.TR_rcb_no_delete ON dbo.resident_current_baselines INSTEAD OF DELETE AS
    THROW 50191, 'BASELINE_CURRENT_DELETE_FORBIDDEN', 1;
GO

CREATE TABLE dbo.baseline_supersessions (
    previous_version_id   UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_bs PRIMARY KEY,
    new_version_id        UNIQUEIDENTIFIER NOT NULL,
    resident_id           UNIQUEIDENTIFIER NOT NULL,
    center_id             UNIQUEIDENTIFIER NOT NULL,
    superseded_at         DATETIME2(3) NOT NULL,
    CONSTRAINT FK_bs_previous FOREIGN KEY (previous_version_id, resident_id, center_id) REFERENCES dbo.baseline_versions(id, resident_id, center_id),
    CONSTRAINT FK_bs_new FOREIGN KEY (new_version_id, resident_id, center_id) REFERENCES dbo.baseline_versions(id, resident_id, center_id),
    CONSTRAINT CK_bs_distinct CHECK (previous_version_id <> new_version_id)
);
CREATE UNIQUE INDEX UX_bs_new ON dbo.baseline_supersessions (new_version_id);
CREATE INDEX IX_bs_scope ON dbo.baseline_supersessions (center_id, resident_id, superseded_at);
GO
CREATE TRIGGER dbo.TR_bs_immutable ON dbo.baseline_supersessions INSTEAD OF UPDATE, DELETE AS
    THROW 50192, 'BASELINE_SUPERSESSION_IMMUTABLE', 1;
GO

------------------------------------------------------------
-- Auditoría e idempotencia
------------------------------------------------------------
CREATE TABLE dbo.audit_events (
    id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_audit_events PRIMARY KEY,
    account_id       UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_audit_account REFERENCES dbo.accounts(id),
    active_profile   NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    center_id        UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_audit_center REFERENCES dbo.centers(id),
    unit_id          UNIQUEIDENTIFIER NULL,
    resident_id      UNIQUEIDENTIFIER NULL,
    resource_type    NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    resource_id      UNIQUEIDENTIFIER NOT NULL,
    action_code      NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    purpose_code     NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NULL,
    occurred_at      DATETIME2(3) NOT NULL,
    CONSTRAINT FK_audit_unit FOREIGN KEY (center_id, unit_id) REFERENCES dbo.units(center_id, id),
    CONSTRAINT FK_audit_resident FOREIGN KEY (center_id, resident_id) REFERENCES dbo.residents(center_id, id),
    CONSTRAINT CK_audit_profile CHECK (active_profile IN ('AUXILIAR', 'ENFERMERIA', 'MEDICINA', 'FAMILIAR', 'ADMINISTRACION', 'DIRECCION_CLINICA')),
    CONSTRAINT CK_audit_direction_read CHECK (
        action_code <> 'CLINICAL_DETAIL_READ'
        OR (active_profile = 'DIRECCION_CLINICA' AND purpose_code = 'SUPERVISION_CLINICA' AND unit_id IS NOT NULL AND resident_id IS NOT NULL)
    )
);
CREATE INDEX IX_audit_scope_time ON dbo.audit_events (center_id, unit_id, resident_id, occurred_at);
GO
CREATE TRIGGER dbo.TR_audit_immutable ON dbo.audit_events INSTEAD OF UPDATE, DELETE AS
    THROW 50200, 'AUDIT_EVENT_IMMUTABLE', 1;
GO

CREATE TABLE dbo.idempotency_operations (
    id                     UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_idem PRIMARY KEY,
    account_id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_idem_account REFERENCES dbo.accounts(id),
    action_code            NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    operation_id           UNIQUEIDENTIFIER NOT NULL,
    request_hash           CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    status                 NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_idem_status DEFAULT ('IN_PROGRESS'),
    result_resource_id     UNIQUEIDENTIFIER NULL,
    result_json            NVARCHAR(MAX) NULL,
    created_at             DATETIME2(3) NOT NULL,
    completed_at           DATETIME2(3) NULL,
    CONSTRAINT CK_idem_action CHECK (action_code IN ('RESIDENT_CREATE', 'BASELINE_SIGN', 'CLINICAL_DETAIL_READ')),
    CONSTRAINT CK_idem_status CHECK (status IN ('IN_PROGRESS', 'SUCCEEDED')),
    CONSTRAINT CK_idem_result CHECK (
        (status = 'IN_PROGRESS' AND result_resource_id IS NULL AND result_json IS NULL AND completed_at IS NULL)
        OR (status = 'SUCCEEDED' AND result_resource_id IS NOT NULL AND result_json IS NOT NULL
            AND ISJSON(result_json) = 1 AND completed_at IS NOT NULL)
    )
);
CREATE UNIQUE INDEX UX_idem_operation ON dbo.idempotency_operations (account_id, action_code, operation_id);
GO
-- Traduce idempotency_operations_transition_guard (solo IN_PROGRESS -> SUCCEEDED, resto de campos
-- inmutables) y _no_delete.
CREATE TRIGGER dbo.TR_idem_transition_guard ON dbo.idempotency_operations AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE d.status <> 'IN_PROGRESS' OR i.account_id <> d.account_id OR i.action_code <> d.action_code
           OR i.operation_id <> d.operation_id OR i.request_hash <> d.request_hash OR i.created_at <> d.created_at
           OR i.status <> 'SUCCEEDED')
        THROW 50210, 'IDEMPOTENCY_OPERATION_IMMUTABLE', 1;
END;
GO
CREATE TRIGGER dbo.TR_idem_no_delete ON dbo.idempotency_operations INSTEAD OF DELETE AS
    THROW 50211, 'IDEMPOTENCY_OPERATION_IMMUTABLE', 1;
GO
