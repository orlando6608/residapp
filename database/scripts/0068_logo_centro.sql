/*
 * Logo propio de cada centro (residencia). Con la sesión de un centro, el encabezado, la impresión del navegador y el PDF del informe de
 * derivación llevan el logo y el nombre del centro en vez de los de ResidApp (guia-diseno-sistema-visual.md, sección 4).
 *
 *  - logos_centro: a lo sumo un logo por centro (la clave primaria es el centro). Es un PNG o un JPEG ya reducido por el navegador (512 px
 *    como máximo por lado) y sin metadatos (CenterLogo.Normalize), de 512 KB como máximo. Lo suben Plataforma al dar de alta el centro y la
 *    Administración del propio centro después; el nombre del centro lo cambia solo su Administración (dbo.centros.nombre_visible, sin cambios
 *    de esquema).
 *  - Cambiar el logo sobrescribe el anterior y quitarlo borra la fila: no se conserva ningún logo antiguo. El rastro de quién lo puso o lo
 *    quitó, y cuándo, queda en dbo.eventos_auditoria (CENTER_LOGO_SET y CENTER_LOGO_REMOVE), sin la imagen; el cambio de nombre, en
 *    CENTER_RENAME.
 *  - Sin seguridad por filas, como dbo.centros: el alta de un centro por Plataforma escribe el logo desde el ámbito del centro reservado, que
 *    ese predicado bloquearía. El logo no es un dato clínico ni personal. Las lecturas de la aplicación llegan siempre por el centro del
 *    ámbito ya comprobado de la sesión (SqlCenterBrandingDirectory).
 */

CREATE TABLE dbo.logos_centro (
    centro_id                 UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_logos_centro PRIMARY KEY
                                  CONSTRAINT FK_logos_centro_centro REFERENCES dbo.centros(id),
    tipo                      NVARCHAR(4) COLLATE Latin1_General_100_BIN2 NOT NULL,
    contenido                 VARBINARY(MAX) NOT NULL,
    actualizado_por_cuenta_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_logos_centro_cuenta REFERENCES dbo.cuentas(id),
    actualizado_por_perfil    NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    actualizado_en            DATETIME2(3) NOT NULL,
    CONSTRAINT CK_logos_centro_tipo CHECK (tipo IN ('PNG', 'JPEG')),
    CONSTRAINT CK_logos_centro_perfil CHECK (actualizado_por_perfil IN ('PLATAFORMA', 'ADMINISTRACION')),
    CONSTRAINT CK_logos_centro_contenido CHECK (DATALENGTH(contenido) BETWEEN 4 AND 524288)
);
GO
