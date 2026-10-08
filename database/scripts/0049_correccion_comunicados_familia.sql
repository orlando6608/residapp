/*
 * Corrección del texto de un comunicado a la familia durante su margen (CJ, 2026-10-07; continuidad-supervision-comunicacion 5.2: «un margen
 * de 1 h para corregir cualquier error»).
 *
 * comunicaciones_familiares (0009) es inmutable: cada corrección es una fila nueva de esta tabla y el contenido vigente de un comunicado es el
 * de su última corrección, o el original si no tiene ninguna. La corrección conserva siempre el tipo (ordinaria o relevante) y el texto
 * completos. Que sea dentro del margen, que Enfermería del ámbito la haga y que la versión esperada sea la vigente lo comprueba la aplicación
 * dentro de su transacción; aquí quedan la numeración sin huecos, que no se corrija un comunicado ya publicado antes de su hora, que no se
 * corrija a un residente suspendido y la inmutabilidad.
 */

CREATE TABLE dbo.comunicaciones_familiares_correcciones (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_cfc PRIMARY KEY,
    comunicacion_id          UNIQUEIDENTIFIER NOT NULL,
    centro_id                UNIQUEIDENTIFIER NOT NULL,
    residente_id             UNIQUEIDENTIFIER NOT NULL,
    numero                   INT NOT NULL,
    tipo_codigo              NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    texto                    NVARCHAR(2000) NOT NULL,
    corregida_por_cuenta_id  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_cfc_cuenta REFERENCES dbo.cuentas(id),
    corregida_en             DATETIME2(3) NOT NULL,
    CONSTRAINT FK_cfc_comunicacion FOREIGN KEY (comunicacion_id, centro_id) REFERENCES dbo.comunicaciones_familiares (id, centro_id),
    CONSTRAINT CK_cfc_numero CHECK (numero > 0),
    CONSTRAINT CK_cfc_tipo CHECK (tipo_codigo IN ('ORDINARIA', 'RELEVANTE')),
    CONSTRAINT CK_cfc_texto CHECK (LEN(LTRIM(RTRIM(texto))) > 0)
);
CREATE UNIQUE INDEX UX_cfc_numero ON dbo.comunicaciones_familiares_correcciones (comunicacion_id, numero);
GO

CREATE TRIGGER dbo.TR_cfc_immutable ON dbo.comunicaciones_familiares_correcciones INSTEAD OF UPDATE, DELETE AS
    THROW 50490, 'FAMILY_COMMUNICATION_CORRECTION_IMMUTABLE', 1;
GO

/* La primera corrección es la número 1 y cada una sigue a la anterior; el residente es el del comunicado; no se corrige tras la publicación
   anticipada ni a un residente suspendido. */
CREATE TRIGGER dbo.TR_cfc_valid ON dbo.comunicaciones_familiares_correcciones AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
          LEFT JOIN dbo.comunicaciones_familiares_correcciones previous
                 ON previous.comunicacion_id = i.comunicacion_id AND previous.numero = i.numero - 1
         WHERE i.numero > 1 AND previous.id IS NULL)
        THROW 50491, 'FAMILY_COMMUNICATION_CORRECTION_SEQUENCE_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i
          JOIN dbo.comunicaciones_familiares_publicacion_anticipada early ON early.comunicacion_id = i.comunicacion_id)
        THROW 50492, 'FAMILY_COMMUNICATION_CORRECTION_CLOSED', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i
          LEFT JOIN dbo.comunicaciones_familiares family ON family.id = i.comunicacion_id AND family.residente_id = i.residente_id
         WHERE family.id IS NULL)
        THROW 50493, 'FAMILY_COMMUNICATION_CORRECTION_RESIDENT_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN dbo.suspensiones_residente s
                ON s.residente_id = i.residente_id AND s.centro_id = i.centro_id AND s.finalizada_en IS NULL)
        THROW 50494, 'RESIDENT_SUSPENDED', 1;
END;
GO

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.comunicaciones_familiares_correcciones,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.comunicaciones_familiares_correcciones AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.comunicaciones_familiares_correcciones AFTER UPDATE;
GO
