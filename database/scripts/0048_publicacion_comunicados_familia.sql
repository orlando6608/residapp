/*
 * Publicación de los comunicados a la familia (CJ, 2026-10-07; continuidad-supervision-comunicacion 5.1 y 5.2).
 *
 * Quien prepara el comunicado lo aprueba al guardarlo (0009: comunicaciones_familiares, inmutable; su estado_codigo conserva el valor
 * histórico PENDIENTE_APROBACION y ya no significa «sin aprobar»). Tras 1 hora de margen se publica solo a una hora fija del día: esa
 * regla vive en el dominio (FamilyCommunicationSchedule) porque depende de la zona horaria, así que no se guarda ninguna fecha.
 * Administración puede publicarlo antes de esa hora: esta tabla guarda cada publicación anticipada (una por comunicado, inmutable).
 * Que la comunicación esté publicada es «llegó su hora o tiene una fila aquí».
 *
 * Que el comunicado sea de una unidad del ámbito de quien lo publica y que no esté ya publicado lo comprueba la aplicación dentro de su
 * transacción; la base de datos impide duplicar la publicación y cambiarla.
 */

CREATE UNIQUE INDEX UX_cf_id_centro ON dbo.comunicaciones_familiares (id, centro_id);
GO

CREATE TABLE dbo.comunicaciones_familiares_publicacion_anticipada (
    id                      UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_cfpa PRIMARY KEY,
    comunicacion_id         UNIQUEIDENTIFIER NOT NULL,
    centro_id               UNIQUEIDENTIFIER NOT NULL,
    publicada_por_cuenta_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_cfpa_cuenta REFERENCES dbo.cuentas(id),
    publicada_en            DATETIME2(3) NOT NULL,
    CONSTRAINT FK_cfpa_comunicacion FOREIGN KEY (comunicacion_id, centro_id) REFERENCES dbo.comunicaciones_familiares (id, centro_id)
);
CREATE UNIQUE INDEX UX_cfpa_comunicacion ON dbo.comunicaciones_familiares_publicacion_anticipada (comunicacion_id);
GO

CREATE TRIGGER dbo.TR_cfpa_immutable ON dbo.comunicaciones_familiares_publicacion_anticipada INSTEAD OF UPDATE, DELETE AS
    THROW 50480, 'FAMILY_COMMUNICATION_PUBLICATION_IMMUTABLE', 1;
GO

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.comunicaciones_familiares_publicacion_anticipada,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.comunicaciones_familiares_publicacion_anticipada AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.comunicaciones_familiares_publicacion_anticipada AFTER UPDATE;
GO
