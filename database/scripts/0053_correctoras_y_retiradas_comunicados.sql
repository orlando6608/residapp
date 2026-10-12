/*
 * Portal Familiar, bloque 2: lo que se hace con un comunicado ya publicado (portal-familiar.md, historia 3; suposición a confirmar con CJ en
 * portal-familiar-confirmaciones-cj.html, tema 4).
 *
 * Un comunicado publicado es inmutable: un error se corrige con una publicación correctora vinculada, nunca cambiando el texto original, y una
 * retirada excepcional oculta el contenido a la familia sin borrarlo. Las correcciones de 0049 siguen siendo solo para antes de publicar; estas dos
 * tablas son para después.
 *
 *  - comunicaciones_familiares_correctoras: cada fila es una publicación nueva, vinculada al comunicado que corrige, que sale en el momento de
 *    guardarla (quien la guarda es Administración o Dirección Clínica, que la aprueban al hacerlo) con su propio tipo y su propia ventana.
 *  - comunicaciones_familiares_retiradas: a lo sumo una por comunicado, con el motivo, que es interno y la familia no ve. Retirar un comunicado
 *    retira también sus correctoras: la familia solo ve que ese contenido ya no está disponible.
 *
 * Que el comunicado esté publicado, que sea del ámbito de quien actúa y la concurrencia lo comprueba la aplicación dentro de su transacción
 * (la publicación depende de la hora y de la zona horaria); aquí quedan la inmutabilidad, que el residente sea el del comunicado, que no se
 * corrija un comunicado retirado y que no se retire dos veces.
 */

CREATE TABLE dbo.comunicaciones_familiares_correctoras (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_cfco PRIMARY KEY,
    comunicacion_id          UNIQUEIDENTIFIER NOT NULL,
    centro_id                UNIQUEIDENTIFIER NOT NULL,
    residente_id             UNIQUEIDENTIFIER NOT NULL,
    tipo_codigo              NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    texto                    NVARCHAR(2000) NOT NULL,
    publicada_por_cuenta_id  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_cfco_cuenta REFERENCES dbo.cuentas(id),
    publicada_en             DATETIME2(3) NOT NULL,
    CONSTRAINT FK_cfco_comunicacion FOREIGN KEY (comunicacion_id, centro_id) REFERENCES dbo.comunicaciones_familiares (id, centro_id),
    CONSTRAINT CK_cfco_tipo CHECK (tipo_codigo IN ('ORDINARIA', 'RELEVANTE')),
    CONSTRAINT CK_cfco_texto CHECK (LEN(LTRIM(RTRIM(texto))) > 0)
);
CREATE INDEX IX_cfco_comunicacion ON dbo.comunicaciones_familiares_correctoras (comunicacion_id);
CREATE INDEX IX_cfco_residente ON dbo.comunicaciones_familiares_correctoras (centro_id, residente_id, publicada_en);
GO

CREATE TABLE dbo.comunicaciones_familiares_retiradas (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_cfr PRIMARY KEY,
    comunicacion_id          UNIQUEIDENTIFIER NOT NULL,
    centro_id                UNIQUEIDENTIFIER NOT NULL,
    residente_id             UNIQUEIDENTIFIER NOT NULL,
    motivo                   NVARCHAR(500) NOT NULL,
    retirada_por_cuenta_id   UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_cfr_cuenta REFERENCES dbo.cuentas(id),
    retirada_en              DATETIME2(3) NOT NULL,
    CONSTRAINT FK_cfr_comunicacion FOREIGN KEY (comunicacion_id, centro_id) REFERENCES dbo.comunicaciones_familiares (id, centro_id),
    CONSTRAINT CK_cfr_motivo CHECK (LEN(LTRIM(RTRIM(motivo))) > 0)
);
CREATE UNIQUE INDEX UX_cfr_comunicacion ON dbo.comunicaciones_familiares_retiradas (comunicacion_id);
CREATE INDEX IX_cfr_residente ON dbo.comunicaciones_familiares_retiradas (centro_id, residente_id);
GO

CREATE TRIGGER dbo.TR_cfco_immutable ON dbo.comunicaciones_familiares_correctoras INSTEAD OF UPDATE, DELETE AS
    THROW 50510, 'FAMILY_COMMUNICATION_CORRECTIVE_IMMUTABLE', 1;
GO

CREATE TRIGGER dbo.TR_cfr_immutable ON dbo.comunicaciones_familiares_retiradas INSTEAD OF UPDATE, DELETE AS
    THROW 50511, 'FAMILY_COMMUNICATION_WITHDRAWAL_IMMUTABLE', 1;
GO

/* El residente es el del comunicado, y no se publica una correctora sobre un comunicado retirado. */
CREATE TRIGGER dbo.TR_cfco_valid ON dbo.comunicaciones_familiares_correctoras AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
          LEFT JOIN dbo.comunicaciones_familiares family ON family.id = i.comunicacion_id AND family.residente_id = i.residente_id
         WHERE family.id IS NULL)
        THROW 50512, 'FAMILY_COMMUNICATION_CORRECTIVE_RESIDENT_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN dbo.comunicaciones_familiares_retiradas withdrawal ON withdrawal.comunicacion_id = i.comunicacion_id)
        THROW 50513, 'FAMILY_COMMUNICATION_WITHDRAWN', 1;
END;
GO

CREATE TRIGGER dbo.TR_cfr_valid ON dbo.comunicaciones_familiares_retiradas AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
          LEFT JOIN dbo.comunicaciones_familiares family ON family.id = i.comunicacion_id AND family.residente_id = i.residente_id
         WHERE family.id IS NULL)
        THROW 50514, 'FAMILY_COMMUNICATION_WITHDRAWAL_RESIDENT_INVALID', 1;
END;
GO

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.comunicaciones_familiares_correctoras,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.comunicaciones_familiares_correctoras AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.comunicaciones_familiares_correctoras AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.comunicaciones_familiares_retiradas,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.comunicaciones_familiares_retiradas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.comunicaciones_familiares_retiradas AFTER UPDATE;
GO
