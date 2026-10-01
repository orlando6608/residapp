/*
 * Administración, bloque 1 (historia 1, ADM-03): corrección de la identidad administrativa del residente.
 *
 * Decisión del usuario (2026-10-01): Administración corrige el nombre, la fecha de nacimiento y el sexo documentado
 * con un motivo obligatorio, y cada corrección queda aquí, inmutable, con los valores anteriores y los nuevos.
 * residentes guarda solo la identidad vigente.
 *
 * numero cuenta las correcciones de cada residente (1, 2, ...): UX_ric_numero impide que dos correcciones
 * simultáneas, o un doble envío, registren el mismo número.
 *
 * TR_res_identity_guard: un cambio de nombre, fecha de nacimiento o sexo de residentes solo se acepta si coincide
 * con la última corrección registrada del residente (valores anteriores y nuevos). Así ninguna identidad se
 * sobrescribe sin dejar rastro. INTERSECT compara los NULL como iguales.
 */

CREATE TABLE dbo.residentes_identidad_correcciones (
    id                        UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ric PRIMARY KEY,
    residente_id              UNIQUEIDENTIFIER NOT NULL,
    centro_id                 UNIQUEIDENTIFIER NOT NULL,
    numero                    INT NOT NULL,
    nombre_anterior           NVARCHAR(200) NOT NULL,
    fecha_nacimiento_anterior DATE NOT NULL,
    sexo_anterior_codigo      NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    nombre_nuevo              NVARCHAR(200) NOT NULL,
    fecha_nacimiento_nueva    DATE NOT NULL,
    sexo_nuevo_codigo         NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    motivo                    NVARCHAR(1000) NOT NULL,
    corregido_por_cuenta_id   UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_ric_corregido_por REFERENCES dbo.cuentas(id),
    corregido_por_perfil      NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    corregido_en              DATETIME2(3) NOT NULL,
    CONSTRAINT FK_ric_residente FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes (centro_id, id),
    CONSTRAINT CK_ric_numero CHECK (numero > 0),
    CONSTRAINT CK_ric_sexo CHECK (sexo_anterior_codigo IN ('male', 'female', 'other', 'unknown')
        AND sexo_nuevo_codigo IN ('male', 'female', 'other', 'unknown')),
    CONSTRAINT CK_ric_nombre CHECK (LEN(LTRIM(RTRIM(nombre_nuevo))) > 0),
    CONSTRAINT CK_ric_motivo CHECK (LEN(LTRIM(RTRIM(motivo))) > 0),
    CONSTRAINT CK_ric_cambio CHECK (nombre_nuevo COLLATE Latin1_General_100_BIN2 <> nombre_anterior COLLATE Latin1_General_100_BIN2 OR fecha_nacimiento_nueva <> fecha_nacimiento_anterior
        OR sexo_nuevo_codigo <> sexo_anterior_codigo),
    CONSTRAINT CK_ric_perfil CHECK (corregido_por_perfil = 'ADMINISTRACION')
);
CREATE UNIQUE INDEX UX_ric_numero ON dbo.residentes_identidad_correcciones (residente_id, numero);
GO

CREATE TRIGGER dbo.TR_ric_immutable ON dbo.residentes_identidad_correcciones INSTEAD OF UPDATE, DELETE AS
    THROW 50390, 'RESIDENT_IDENTITY_CORRECTION_IMMUTABLE', 1;
GO

CREATE TRIGGER dbo.TR_res_identity_guard ON dbo.residentes AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE (i.nombre_visible COLLATE Latin1_General_100_BIN2 <> d.nombre_visible COLLATE Latin1_General_100_BIN2
               OR i.fecha_nacimiento <> d.fecha_nacimiento OR i.sexo_documentado_codigo <> d.sexo_documentado_codigo)
          AND NOT EXISTS (
              SELECT d.nombre_visible COLLATE Latin1_General_100_BIN2, d.fecha_nacimiento, d.sexo_documentado_codigo,
                     i.nombre_visible COLLATE Latin1_General_100_BIN2, i.fecha_nacimiento, i.sexo_documentado_codigo
              INTERSECT
              SELECT latest.nombre_anterior COLLATE Latin1_General_100_BIN2, latest.fecha_nacimiento_anterior, latest.sexo_anterior_codigo,
                     latest.nombre_nuevo COLLATE Latin1_General_100_BIN2, latest.fecha_nacimiento_nueva, latest.sexo_nuevo_codigo
                FROM (SELECT TOP 1 c.* FROM dbo.residentes_identidad_correcciones c
                       WHERE c.residente_id = i.id AND c.centro_id = i.centro_id
                       ORDER BY c.numero DESC) latest))
        THROW 50391, 'RESIDENT_IDENTITY_CHANGE_REQUIRES_CORRECTION', 1;
END;
GO
