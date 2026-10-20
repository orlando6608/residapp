/*
 * Foto de identificación del residente (estudio de mercado y UX, recomendación R2: foto y habitación en la lista de residentes).
 * Suposiciones a confirmar con CJ en docs/pendientes-cj/foto-residente-confirmaciones-cj.html.
 *
 *  - fotos_residente: a lo sumo una foto por residente (la clave primaria es el residente). Es un JPEG ya reducido por el navegador y sin
 *    metadatos (ResidentPhoto.Normalize), de 1 MB como máximo. La ponen Administración y Enfermería; la ven Auxiliar, Enfermería, Medicina y
 *    Administración, nunca la familia.
 *  - Cambiar la foto sobrescribe la anterior y quitarla borra la fila: por minimización de datos no se conserva ninguna foto antigua. El
 *    rastro de quién la puso o la quitó, y cuándo, queda en dbo.eventos_auditoria (RESIDENT_PHOTO_SET y RESIDENT_PHOTO_REMOVE).
 *  - Las listas leen solo actualizada_en (para la dirección de la imagen, que cambia con cada foto nueva); el índice la lleva para que no
 *    tengan que leer la fila con la imagen.
 *
 * Sin trigger: la aplicación solo cambia el contenido y quién y cuándo lo cambió; la clave foránea compuesta y la seguridad por filas
 * protegen el centro.
 */

CREATE TABLE dbo.fotos_residente (
    residente_id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_fotos_residente PRIMARY KEY,
    centro_id                 UNIQUEIDENTIFIER NOT NULL,
    contenido                 VARBINARY(MAX) NOT NULL,
    actualizada_por_cuenta_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_fotos_residente_cuenta REFERENCES dbo.cuentas(id),
    actualizada_por_perfil    NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    actualizada_en            DATETIME2(3) NOT NULL,
    CONSTRAINT FK_fotos_residente_residente FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes (centro_id, id),
    CONSTRAINT CK_fotos_residente_perfil CHECK (actualizada_por_perfil IN ('ADMINISTRACION', 'ENFERMERIA')),
    CONSTRAINT CK_fotos_residente_contenido CHECK (DATALENGTH(contenido) BETWEEN 4 AND 1048576)
);
CREATE INDEX IX_fotos_residente_version ON dbo.fotos_residente (centro_id, residente_id) INCLUDE (actualizada_en);
GO

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.fotos_residente,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.fotos_residente AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.fotos_residente AFTER UPDATE;
GO
