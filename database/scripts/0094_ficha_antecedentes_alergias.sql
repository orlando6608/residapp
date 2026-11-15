/*
 * Ficha de antecedentes de salud, fase 1: alergias e intolerancias a medicamentos (CJ, medicina-alergias-y-plazos-cj 2.1 a 2.10 y medicina-ficha-aclaraciones-cj 1.1 y 1.2;
 * diseño en docs/producto/ficha-antecedentes-salud-diseno.md, aprobado por Orlando el 2026-11-15; lectura de dpo-residapp en docs/privacidad/ficha-alergias-enfermedades-medicacion-lectura-dpo.md,
 * apartado 5.1). Las enfermedades y la medicación (fases 2 y 3) reutilizarán estas dos tablas con un script nuevo que amplíe las listas de los CHECK y añada sus columnas.
 *
 * Es una lista informativa del residente, no una administración de medicación: no hay alertas, cruces ni bloqueos. Se pide en el alta pero no bloquea nada: un apartado sin
 * ninguna versión es «pendiente» y no es lo mismo que «No refiere» ni que «No consta» (no se guarda ninguna fila por defecto).
 *
 *   fichas_antecedentes_versiones   Una fila por apartado y por guardado: el estado del apartado, la fuente opcional (lista cerrada, nunca el nombre de quien informó), el motivo del
 *                                   cambio (lista cerrada), quién (cuenta y perfil) y cuándo. Cada guardado es una versión nueva y no se pisa la anterior (Llei 21/2000, art. 9.3): la
 *                                   «última modificación» de cada apartado es la fecha y el perfil de su versión vigente (la de mayor número). La clave única (residente, apartado,
 *                                   versión) hace que dos guardados a la vez no se pisen: el segundo falla y la aplicación se lo dice a quien edita.
 *   fichas_antecedentes_elementos   Una fila por elemento de cada versión: ALERGIA (a un medicamento o grupo; la reacción es opcional) o INTOLERANCIA (a un medicamento; la reacción es
 *                                   obligatoria). Texto corto con límite. Sin columnas «por si llega una base de datos de fármacos».
 *
 * Estados del apartado de alergias: CON_ENTRADAS (hay al menos una ALERGIA), NO_REFIERE («No refiere alergias a medicamentos») y NO_CONSTA («No se sabe o no consta»). Las intolerancias
 * pueden convivir con cualquiera de los tres. Lo que la BD impone: los elementos solo se añaden en la misma transacción que creó su versión (transaccion_id; una versión cerrada no
 * admite elementos nuevos: revisión del DPO, 2026-11-15); numero_elementos acota las filas; un estado distinto de CON_ENTRADAS no admite ALERGIA; las posiciones van de 1 a
 * numero_elementos sin repetirse. Lo que impone la aplicación (el SQL no puede ver un conjunto aún por insertar): que CON_ENTRADAS tenga al menos una ALERGIA y que se inserten
 * exactamente numero_elementos filas, todo en una transacción; al leer, la aplicación comprueba ambas cosas y trata como ficha con error una versión que no las cumpla.
 *
 * Las dos tablas son de solo inserción (INSTEAD OF UPDATE, DELETE; además DENY UPDATE/DELETE al rol de la aplicación) y llevan centro_id con clave foránea compuesta con residentes y
 * seguridad por filas por centro. Las versiones anteriores no se muestran en ninguna pantalla ordinaria ni en el PDF (art. 32 LOPDGDD; el DPO real debe confirmar que basta). Queda para
 * una fase posterior la anulación de una versión por error (datos de otro residente): hoy un error se corrige con una versión nueva de motivo CORRECCION.
 *
 * Permiso nuevo HEALTH_BACKGROUND_EDIT: Enfermería y Medicina solo editan la ficha si Administración se lo concede a su perfil (apagado por defecto, art. 25.2 RGPD). Ver la ficha no
 * lleva permiso: la ven Enfermería y Medicina en su ámbito. No lo pueden tener Auxiliar, Administración, Dirección Clínica ni Plataforma. Dirección Clínica no lee la ficha en esta fase (su
 * lectura exigirá CLINICAL_DETAIL_READ, finalidad declarada y auditoría previa, y llegará con un tipo de recurso clínico nuevo). La lista de permisos y el trigger repiten los de 0076 más
 * HEALTH_BACKGROUND_EDIT.
 *
 * La auditoría de cada cambio (HEALTH_BACKGROUND_SAVE, o HEALTH_BACKGROUND_CORRECT si el motivo es CORRECCION; tipo de recurso HEALTH_BACKGROUND) la escribe la aplicación en
 * dbo.eventos_auditoria con solo identificadores, sin el texto de la ficha.
 */

SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO

CREATE TABLE dbo.fichas_antecedentes_versiones (
    id                    UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_fav PRIMARY KEY,
    centro_id             UNIQUEIDENTIFIER NOT NULL,
    residente_id          UNIQUEIDENTIFIER NOT NULL,
    apartado_codigo       NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    numero_version        INT NOT NULL,
    estado_codigo         NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    numero_elementos      SMALLINT NOT NULL,
    fuente_codigo         NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NULL,
    motivo_codigo         NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NOT NULL,
    creado_por_cuenta_id  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_fav_cuenta REFERENCES dbo.cuentas(id),
    creado_por_perfil     NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    creado_en             DATETIME2(3) NOT NULL,
    transaccion_id        BIGINT NOT NULL CONSTRAINT DF_fav_transaccion DEFAULT CURRENT_TRANSACTION_ID(),
    CONSTRAINT FK_fav_residente FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes(centro_id, id),
    CONSTRAINT CK_fav_apartado CHECK (apartado_codigo IN ('ALERGIAS')),
    CONSTRAINT CK_fav_numero CHECK (numero_version >= 1),
    CONSTRAINT CK_fav_estado CHECK (estado_codigo IN ('CON_ENTRADAS', 'NO_REFIERE', 'NO_CONSTA')),
    CONSTRAINT CK_fav_elementos CHECK (numero_elementos BETWEEN 0 AND 50 AND (estado_codigo <> 'CON_ENTRADAS' OR numero_elementos >= 1)),
    CONSTRAINT CK_fav_fuente CHECK (fuente_codigo IS NULL OR fuente_codigo IN ('INFORME_MEDICO', 'RECETA_ELECTRONICA', 'RESIDENTE', 'FAMILIA_CUIDADORES')),
    CONSTRAINT CK_fav_motivo CHECK (motivo_codigo IN ('REGISTRO_INICIAL', 'ACTUALIZACION', 'CORRECCION')),
    CONSTRAINT CK_fav_motivo_inicial CHECK ((numero_version = 1 AND motivo_codigo = 'REGISTRO_INICIAL') OR (numero_version > 1 AND motivo_codigo <> 'REGISTRO_INICIAL')),
    CONSTRAINT CK_fav_perfil CHECK (creado_por_perfil IN ('ENFERMERIA', 'MEDICINA'))
);
CREATE UNIQUE INDEX UX_fav_version ON dbo.fichas_antecedentes_versiones (residente_id, apartado_codigo, numero_version);
CREATE UNIQUE INDEX UX_fav_scope ON dbo.fichas_antecedentes_versiones (id, residente_id, centro_id);
GO

CREATE TABLE dbo.fichas_antecedentes_elementos (
    id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_fae PRIMARY KEY,
    version_id       UNIQUEIDENTIFIER NOT NULL,
    residente_id     UNIQUEIDENTIFIER NOT NULL,
    centro_id        UNIQUEIDENTIFIER NOT NULL,
    posicion         SMALLINT NOT NULL,
    tipo_codigo      NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    nombre           NVARCHAR(200) NOT NULL,
    reaccion         NVARCHAR(200) NULL,
    CONSTRAINT FK_fae_version FOREIGN KEY (version_id, residente_id, centro_id) REFERENCES dbo.fichas_antecedentes_versiones(id, residente_id, centro_id),
    CONSTRAINT CK_fae_posicion CHECK (posicion BETWEEN 1 AND 50),
    CONSTRAINT CK_fae_tipo CHECK (tipo_codigo IN ('ALERGIA', 'INTOLERANCIA')),
    CONSTRAINT CK_fae_nombre CHECK (LEN(LTRIM(RTRIM(nombre))) > 0),
    CONSTRAINT CK_fae_reaccion CHECK (reaccion IS NULL OR LEN(LTRIM(RTRIM(reaccion))) > 0),
    CONSTRAINT CK_fae_intolerancia_reaccion CHECK (tipo_codigo <> 'INTOLERANCIA' OR reaccion IS NOT NULL)
);
CREATE UNIQUE INDEX UX_fae_posicion ON dbo.fichas_antecedentes_elementos (version_id, posicion);
GO

CREATE TRIGGER dbo.TR_fav_immutable ON dbo.fichas_antecedentes_versiones INSTEAD OF UPDATE, DELETE AS
    THROW 50920, 'HEALTH_BACKGROUND_IMMUTABLE', 1;
GO

CREATE TRIGGER dbo.TR_fae_immutable ON dbo.fichas_antecedentes_elementos INSTEAD OF UPDATE, DELETE AS
    THROW 50921, 'HEALTH_BACKGROUND_IMMUTABLE', 1;
GO

-- Un elemento solo se añade en la misma transacción que creó su versión (transaccion_id): una versión ya cerrada no admite elementos nuevos. Además, su posición no pasa
-- de numero_elementos y un estado distinto de CON_ENTRADAS no admite alergias.
CREATE TRIGGER dbo.TR_fae_coherence ON dbo.fichas_antecedentes_elementos AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
          JOIN dbo.fichas_antecedentes_versiones v ON v.id = i.version_id
         WHERE v.transaccion_id <> CURRENT_TRANSACTION_ID()
            OR i.posicion > v.numero_elementos
            OR (i.tipo_codigo = 'ALERGIA' AND v.estado_codigo <> 'CON_ENTRADAS'))
        THROW 50922, 'HEALTH_BACKGROUND_ELEMENT_INCOHERENT', 1;
END;
GO

-- Seguridad por filas por centro, con el mismo predicado que el resto (0030, 0032, 0090).
ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.fichas_antecedentes_versiones,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.fichas_antecedentes_versiones AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.fichas_antecedentes_versiones AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.fichas_antecedentes_elementos,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.fichas_antecedentes_elementos AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.fichas_antecedentes_elementos AFTER UPDATE;
GO

-- Permisos mínimos del usuario de la aplicación (0091): solo inserta; no actualiza ni borra.
GRANT INSERT ON dbo.fichas_antecedentes_versiones TO residapp_aplicacion;
DENY UPDATE, DELETE ON dbo.fichas_antecedentes_versiones TO residapp_aplicacion;
GRANT INSERT ON dbo.fichas_antecedentes_elementos TO residapp_aplicacion;
DENY UPDATE, DELETE ON dbo.fichas_antecedentes_elementos TO residapp_aplicacion;
GO

-- Catálogo de permisos: HEALTH_BACKGROUND_EDIT, solo para Enfermería y Medicina.
ALTER TABLE dbo.permisos_perfil DROP CONSTRAINT CK_pp_code;
ALTER TABLE dbo.permisos_perfil ADD CONSTRAINT CK_pp_code CHECK (permiso_codigo IN
    ('RESIDENT_IDENTITY_CREATE', 'BASELINE_INITIAL_COMPLETE', 'BASELINE_REEVALUATE',
     'BASELINE_DRAFT_CONTRIBUTE', 'CLINICAL_DETAIL_READ', 'REFERENCE_RANGES_MANAGE', 'PROCESS_DEADLINES_MANAGE', 'RESIDENT_PHOTO_MANAGE',
     'QUALITY_REVIEW', 'HEALTH_BACKGROUND_EDIT'));
GO

ALTER TRIGGER dbo.TR_pp_profile_catalog ON dbo.permisos_perfil AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
          JOIN dbo.ambitos_perfil p ON p.id = i.ambito_perfil_id
         WHERE (i.permiso_codigo IN ('RESIDENT_IDENTITY_CREATE', 'RESIDENT_PHOTO_MANAGE') AND p.perfil_codigo <> 'ENFERMERIA')
            OR (i.permiso_codigo IN ('BASELINE_INITIAL_COMPLETE', 'BASELINE_REEVALUATE', 'BASELINE_DRAFT_CONTRIBUTE', 'HEALTH_BACKGROUND_EDIT')
                AND p.perfil_codigo NOT IN ('ENFERMERIA', 'MEDICINA'))
            OR (i.permiso_codigo IN ('CLINICAL_DETAIL_READ', 'PROCESS_DEADLINES_MANAGE', 'QUALITY_REVIEW') AND p.perfil_codigo <> 'DIRECCION_CLINICA'))
        THROW 50420, 'PROFILE_PERMISSION_NOT_ALLOWED', 1;
END;
GO

COMMIT TRANSACTION;
GO
