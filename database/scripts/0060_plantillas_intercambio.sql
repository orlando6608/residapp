/*
 * Importación y exportación por ficheros (ADR 0010, estudio de mercado R1): plantillas de emparejamiento de columnas por centro.
 *
 *  - Una plantilla dice, para un fichero de otra aplicación (identificado por la firma de sus cabeceras), qué columna va a qué campo de
 *    ResidApp, el formato de las fechas y, al importar, qué significa cada valor de la columna de sexo. Al importar se aplica sola cuando
 *    llega un fichero con la misma firma; al exportar se elige de una lista.
 *  - No guarda datos personales: solo nombres de columnas, campos y códigos. El fichero importado o exportado no se guarda nunca.
 *  - Guardar con un nombre que ya existe (mismo centro, dirección y entidad) sustituye la plantilla; el rastro de quién la guardó, y
 *    cuándo, queda en dbo.eventos_auditoria (EXCHANGE_TEMPLATE_SAVE).
 *
 * Sin trigger: la clave foránea y la seguridad por filas protegen el centro.
 */

CREATE TABLE dbo.plantillas_intercambio (
    id                      UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_plantillas_intercambio PRIMARY KEY,
    centro_id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_plantillas_intercambio_centro REFERENCES dbo.centros (id),
    direccion               NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    entidad                 NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    nombre                  NVARCHAR(100) NOT NULL,
    firma_cabeceras         CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    mapeo_json              NVARCHAR(MAX) NOT NULL,
    guardada_por_cuenta_id  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_plantillas_intercambio_cuenta REFERENCES dbo.cuentas (id),
    guardada_en             DATETIME2(3) NOT NULL,
    CONSTRAINT CK_plantillas_intercambio_direccion CHECK (direccion IN ('IMPORT', 'EXPORT')),
    CONSTRAINT CK_plantillas_intercambio_entidad CHECK (entidad IN ('STRUCTURE', 'RESIDENTS')),
    CONSTRAINT CK_plantillas_intercambio_nombre CHECK (LEN(LTRIM(RTRIM(nombre))) > 0),
    CONSTRAINT CK_plantillas_intercambio_mapeo CHECK (ISJSON(mapeo_json) = 1)
);
CREATE UNIQUE INDEX UX_plantillas_intercambio_nombre ON dbo.plantillas_intercambio (centro_id, direccion, entidad, nombre);
CREATE INDEX IX_plantillas_intercambio_firma ON dbo.plantillas_intercambio (centro_id, direccion, entidad, firma_cabeceras);
GO

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.plantillas_intercambio,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.plantillas_intercambio AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.plantillas_intercambio AFTER UPDATE;
GO
