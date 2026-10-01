/*
 * Administración, bloque 2 (historia 3, ADM-03, FAM-01): familiares, autorizaciones de acceso y contacto urgente.
 *
 * Decisiones del usuario (2026-10-01):
 *   - Familiar y autorización son objetos distintos (FAM-01): crear y vincular un familiar no le da acceso a nada; la
 *     autorización se abre aparte y solo el estado ACTIVA permitirá entrar al Portal Familiar cuando exista.
 *   - Estados del PRD: PENDIENTE -> ACTIVA -> SUSPENDIDA / REVOCADA / CADUCADA. CADUCADA no se guarda: es una ACTIVA
 *     con valida_hasta ya pasada (día local), que la aplicación calcula al leer.
 *   - El contacto urgente designado es uno de los familiares vinculados al residente; uno vigente, con historial.
 *
 * familiares: datos de contacto del familiar en el centro. Solo cambian nombre, teléfono y correo (con auditoría).
 * residentes_familiares: el vínculo con un residente y la relación (texto libre). Solo cambia la relación.
 * familiares_autorizaciones_cambios: solo inserción. Cada fila es un cambio de estado de la autorización de un
 *   vínculo; el estado vigente es el del número más alto. UX_fac_numero impide que dos cambios simultáneos, o un doble
 *   envío, registren el mismo número. TR_fac_transition valida cada cambio contra el anterior.
 * residentes_contacto_urgente: solo inserción. Cada fila designa el contacto vigente (vinculo_id) o lo quita (NULL);
 *   el vigente es el del número más alto. FK_rcu_vinculo garantiza que el vínculo es del mismo residente.
 */

CREATE TABLE dbo.familiares (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_familiares PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_fam_centro REFERENCES dbo.centros(id),
    nombre_visible           NVARCHAR(200) NOT NULL,
    telefono                 NVARCHAR(32) NOT NULL,
    correo                   NVARCHAR(254) NULL,
    creado_por_cuenta_id     UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_fam_creado_por REFERENCES dbo.cuentas(id),
    creado_en                DATETIME2(3) NOT NULL,
    CONSTRAINT UQ_fam_centro UNIQUE (centro_id, id),
    CONSTRAINT CK_fam_nombre CHECK (LEN(LTRIM(RTRIM(nombre_visible))) > 0),
    CONSTRAINT CK_fam_telefono CHECK (LEN(LTRIM(RTRIM(telefono))) > 0),
    CONSTRAINT CK_fam_correo CHECK (correo IS NULL OR LEN(LTRIM(RTRIM(correo))) > 0)
);
GO

CREATE TRIGGER dbo.TR_fam_update_guard ON dbo.familiares AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE i.centro_id <> d.centro_id OR i.creado_por_cuenta_id <> d.creado_por_cuenta_id OR i.creado_en <> d.creado_en)
        THROW 50400, 'FAMILY_MEMBER_UPDATE_INVALID', 1;
END;
GO

CREATE TRIGGER dbo.TR_fam_no_delete ON dbo.familiares INSTEAD OF DELETE AS
    THROW 50401, 'FAMILY_MEMBER_DELETE_FORBIDDEN', 1;
GO

CREATE TABLE dbo.residentes_familiares (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_residentes_familiares PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL,
    residente_id             UNIQUEIDENTIFIER NOT NULL,
    familiar_id              UNIQUEIDENTIFIER NOT NULL,
    relacion                 NVARCHAR(100) NOT NULL,
    vinculado_por_cuenta_id  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_rfa_vinculado_por REFERENCES dbo.cuentas(id),
    vinculado_en             DATETIME2(3) NOT NULL,
    CONSTRAINT FK_rfa_residente FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes (centro_id, id),
    CONSTRAINT FK_rfa_familiar FOREIGN KEY (centro_id, familiar_id) REFERENCES dbo.familiares (centro_id, id),
    CONSTRAINT UQ_rfa_centro UNIQUE (centro_id, id),
    CONSTRAINT UQ_rfa_residente UNIQUE (centro_id, residente_id, id),
    CONSTRAINT CK_rfa_relacion CHECK (LEN(LTRIM(RTRIM(relacion))) > 0)
);
CREATE UNIQUE INDEX UX_rfa_par ON dbo.residentes_familiares (residente_id, familiar_id);
GO

CREATE TRIGGER dbo.TR_rfa_update_guard ON dbo.residentes_familiares AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE i.centro_id <> d.centro_id OR i.residente_id <> d.residente_id OR i.familiar_id <> d.familiar_id
           OR i.vinculado_por_cuenta_id <> d.vinculado_por_cuenta_id OR i.vinculado_en <> d.vinculado_en)
        THROW 50402, 'RESIDENT_FAMILY_LINK_UPDATE_INVALID', 1;
END;
GO

CREATE TRIGGER dbo.TR_rfa_no_delete ON dbo.residentes_familiares INSTEAD OF DELETE AS
    THROW 50403, 'RESIDENT_FAMILY_LINK_DELETE_FORBIDDEN', 1;
GO

CREATE TABLE dbo.familiares_autorizaciones_cambios (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_fac PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL,
    vinculo_id               UNIQUEIDENTIFIER NOT NULL,
    numero                   INT NOT NULL,
    estado_codigo            NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    valida_hasta             DATE NULL,
    motivo                   NVARCHAR(1000) NULL,
    registrado_por_cuenta_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_fac_registrado_por REFERENCES dbo.cuentas(id),
    registrado_por_perfil    NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    registrado_en            DATETIME2(3) NOT NULL,
    CONSTRAINT FK_fac_vinculo FOREIGN KEY (centro_id, vinculo_id) REFERENCES dbo.residentes_familiares (centro_id, id),
    CONSTRAINT CK_fac_numero CHECK (numero > 0),
    CONSTRAINT CK_fac_estado CHECK (estado_codigo IN ('PENDIENTE', 'ACTIVA', 'SUSPENDIDA', 'REVOCADA')),
    CONSTRAINT CK_fac_valida_hasta CHECK (valida_hasta IS NULL OR estado_codigo = 'ACTIVA'),
    CONSTRAINT CK_fac_motivo CHECK (
        (estado_codigo IN ('SUSPENDIDA', 'REVOCADA') AND motivo IS NOT NULL AND LEN(LTRIM(RTRIM(motivo))) > 0)
        OR (estado_codigo IN ('PENDIENTE', 'ACTIVA') AND motivo IS NULL)),
    CONSTRAINT CK_fac_perfil CHECK (registrado_por_perfil = 'ADMINISTRACION')
);
CREATE UNIQUE INDEX UX_fac_numero ON dbo.familiares_autorizaciones_cambios (vinculo_id, numero);
GO

CREATE TRIGGER dbo.TR_fac_immutable ON dbo.familiares_autorizaciones_cambios INSTEAD OF UPDATE, DELETE AS
    THROW 50404, 'FAMILY_AUTHORIZATION_CHANGE_IMMUTABLE', 1;
GO

/* El primer cambio abre la autorización en PENDIENTE; cada uno de los siguientes sigue al anterior (sin huecos en la
 * numeración) con una transición permitida. ACTIVA -> ACTIVA es la renovación de una autorización caducada: la
 * caducidad depende del día local, así que la comprueba la aplicación. REVOCADA es final. */
CREATE TRIGGER dbo.TR_fac_transition ON dbo.familiares_autorizaciones_cambios AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
        LEFT JOIN dbo.familiares_autorizaciones_cambios previous
               ON previous.vinculo_id = i.vinculo_id AND previous.centro_id = i.centro_id AND previous.numero = i.numero - 1
        CROSS APPLY (SELECT COALESCE(previous.estado_codigo, N'') AS estado) prev  -- sin anterior: '' (evita NULL en el NOT)
        WHERE NOT (
            (i.numero = 1 AND i.estado_codigo = 'PENDIENTE')
            OR (prev.estado = 'PENDIENTE' AND i.estado_codigo IN ('ACTIVA', 'REVOCADA'))
            OR (prev.estado = 'ACTIVA' AND i.estado_codigo IN ('ACTIVA', 'SUSPENDIDA', 'REVOCADA'))
            OR (prev.estado = 'SUSPENDIDA' AND i.estado_codigo IN ('ACTIVA', 'REVOCADA'))))
        THROW 50405, 'FAMILY_AUTHORIZATION_TRANSITION_INVALID', 1;
END;
GO

CREATE TABLE dbo.residentes_contacto_urgente (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_rcu PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL,
    residente_id             UNIQUEIDENTIFIER NOT NULL,
    numero                   INT NOT NULL,
    vinculo_id               UNIQUEIDENTIFIER NULL,
    designado_por_cuenta_id  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_rcu_designado_por REFERENCES dbo.cuentas(id),
    designado_por_perfil     NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    designado_en             DATETIME2(3) NOT NULL,
    CONSTRAINT FK_rcu_residente FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes (centro_id, id),
    -- Con vinculo_id NULL (contacto quitado) la clave no se comprueba.
    CONSTRAINT FK_rcu_vinculo FOREIGN KEY (centro_id, residente_id, vinculo_id)
        REFERENCES dbo.residentes_familiares (centro_id, residente_id, id),
    CONSTRAINT CK_rcu_numero CHECK (numero > 0),
    CONSTRAINT CK_rcu_perfil CHECK (designado_por_perfil = 'ADMINISTRACION')
);
CREATE UNIQUE INDEX UX_rcu_numero ON dbo.residentes_contacto_urgente (residente_id, numero);
GO

CREATE TRIGGER dbo.TR_rcu_immutable ON dbo.residentes_contacto_urgente INSTEAD OF UPDATE, DELETE AS
    THROW 50406, 'EMERGENCY_CONTACT_DESIGNATION_IMMUTABLE', 1;
GO
