/*
 * Añade el cierre de un evento por Enfermería y la decisión de comunicación familiar (historia 3 de
 * docs/historias-usuarios/enfermeria.md, ENF-06/ENF-12; pantallas ENF-06, ENF-07A, ENF-14 y ENF-15 del
 * wireframe), más el histórico de versiones de la valoración.
 *
 * Cerrar es una de las cuatro salidas de la decisión asistencial; solo se añade el estado CERRADO (las
 * otras tres llegan con las historias 4 a 6). El cierre es idempotente (operaciones_idempotencia,
 * CLINICAL_EVENT_CLOSE) y la BD impide cualquier transición desde CERRADO, así que no puede haber dos
 * cierres. Al cerrar, la valoración pasa de BORRADOR a CERRADA y queda inmutable (TR_ve_guard, 0007).
 *
 * Comunicación familiar: al cerrar se decide explícitamente entre NO_COMUNICAR y PREPARAR. Si se prepara,
 * el texto se guarda en dbo.comunicaciones_familiares como PENDIENTE_APROBACION. Su aprobación, audiencia,
 * momento de publicación y publicación pertenecen a los verticales Portal Familiar y Administración, que
 * extenderán esta tabla. Ante la familia la firma es siempre "Equipo asistencial del centro";
 * preparado_por_cuenta_id solo sirve para la trazabilidad interna.
 *
 * Histórico de la valoración: hasta ahora el borrador se sobrescribía en cada guardado. Desde este script,
 * cada guardado deja además una copia inmutable en dbo.valoraciones_enfermeria_versiones. Los borradores
 * ya existentes entran con una sola versión (su contenido actual); sus versiones intermedias anteriores no
 * se conservaron y no pueden recuperarse.
 */

ALTER TABLE dbo.operaciones_idempotencia DROP CONSTRAINT CK_idem_action;
ALTER TABLE dbo.operaciones_idempotencia ADD CONSTRAINT CK_idem_action
    CHECK (accion_codigo IN (
        'RESIDENT_CREATE', 'BASELINE_SIGN', 'CLINICAL_DETAIL_READ',
        'DAILY_CLOSURE_NO_CHANGE', 'DAILY_CLOSURE_NOT_ASSESSABLE', 'DAILY_CLOSURE_CHANGE_REPORTED',
        'BASELINE_DRAFT_CREATE', 'CLINICAL_EVENT_REGISTER', 'CLINICAL_EVENT_CLOSE'));
GO

ALTER TABLE dbo.eventos_asistenciales ADD
    cerrado_por_cuenta_id         UNIQUEIDENTIFIER NULL CONSTRAINT FK_ea_cerrado_por REFERENCES dbo.cuentas(id),
    cerrado_en                    DATETIME2(3) NULL,
    comunicacion_familiar_codigo  NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NULL;
GO

ALTER TABLE dbo.eventos_asistenciales DROP CONSTRAINT CK_ea_estado;
ALTER TABLE dbo.eventos_asistenciales ADD CONSTRAINT CK_ea_estado
    CHECK (estado_codigo IN ('PENDIENTE', 'EN_VALORACION', 'CERRADO'));
ALTER TABLE dbo.eventos_asistenciales ADD CONSTRAINT CK_ea_comunicacion
    CHECK (comunicacion_familiar_codigo IS NULL OR comunicacion_familiar_codigo IN ('NO_COMUNICAR', 'PREPARAR'));
ALTER TABLE dbo.eventos_asistenciales ADD CONSTRAINT CK_ea_cierre CHECK (
    (estado_codigo = 'CERRADO' AND cerrado_por_cuenta_id IS NOT NULL AND cerrado_en IS NOT NULL AND comunicacion_familiar_codigo IS NOT NULL)
    OR (estado_codigo <> 'CERRADO' AND cerrado_por_cuenta_id IS NULL AND cerrado_en IS NULL AND comunicacion_familiar_codigo IS NULL));
GO

-- Mismas reglas que en 0007 (inmutabilidad y revisión + 1), con las transiciones permitidas explícitas:
-- PENDIENTE -> EN_VALORACION, EN_VALORACION -> EN_VALORACION y EN_VALORACION -> CERRADO. Nada sale de CERRADO.
ALTER TRIGGER dbo.TR_ea_transition_guard ON dbo.eventos_asistenciales AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE i.residente_id <> d.residente_id OR i.centro_id <> d.centro_id OR i.unidad_id <> d.unidad_id
           OR i.origen_codigo <> d.origen_codigo
           OR ISNULL(i.cierre_id, '00000000-0000-0000-0000-000000000000') <> ISNULL(d.cierre_id, '00000000-0000-0000-0000-000000000000')
           OR ISNULL(i.evento_clinico_id, '00000000-0000-0000-0000-000000000000') <> ISNULL(d.evento_clinico_id, '00000000-0000-0000-0000-000000000000')
           OR i.clasificacion_codigo <> d.clasificacion_codigo OR i.recibido_en <> d.recibido_en
           OR i.revision <> d.revision + 1
           OR NOT ((d.estado_codigo IN ('PENDIENTE', 'EN_VALORACION') AND i.estado_codigo = 'EN_VALORACION')
                   OR (d.estado_codigo = 'EN_VALORACION' AND i.estado_codigo = 'CERRADO')))
        THROW 50310, 'CLINICAL_EVENT_TRANSITION_INVALID', 1;
END;
GO

ALTER TABLE dbo.valoraciones_enfermeria DROP CONSTRAINT CK_ve_estado;
ALTER TABLE dbo.valoraciones_enfermeria ADD CONSTRAINT CK_ve_estado CHECK (estado_codigo IN ('BORRADOR', 'CERRADA'));
GO

/*
 * Una fila por cada guardado de la valoración, con el contenido tal como quedó y la revisión del evento
 * que produjo ese guardado. Copia de una fila ya validada de dbo.valoraciones_enfermeria, por eso no repite
 * sus CHECK. Solo inserción.
 */
CREATE TABLE dbo.valoraciones_enfermeria_versiones (
    id                            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_vev PRIMARY KEY,
    valoracion_id                 UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_vev_valoracion REFERENCES dbo.valoraciones_enfermeria(id),
    evento_id                     UNIQUEIDENTIFIER NOT NULL,
    revision_evento               INT NOT NULL,
    hallazgos                     NVARCHAR(2000) NULL,
    valoracion                    NVARCHAR(2000) NULL,
    actuaciones                   NVARCHAR(2000) NULL,
    comunicaciones                NVARCHAR(2000) NULL,
    resultado                     NVARCHAR(2000) NULL,
    temperatura_celsius           DECIMAL(4, 1) NULL,
    tension_sistolica_mmhg        SMALLINT NULL,
    tension_diastolica_mmhg       SMALLINT NULL,
    frecuencia_cardiaca_lpm       SMALLINT NULL,
    frecuencia_respiratoria_rpm   SMALLINT NULL,
    saturacion_o2_pct             SMALLINT NULL,
    soporte_respiratorio_codigo   NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NULL,
    flujo_o2_lpm                  DECIMAL(4, 1) NULL,
    glucemia_mg_dl                SMALLINT NULL,
    otra_constante_nombre         NVARCHAR(100) NULL,
    otra_constante_valor          NVARCHAR(50) NULL,
    otra_constante_unidad         NVARCHAR(30) NULL,
    guardado_por_cuenta_id        UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_vev_guardado_por REFERENCES dbo.cuentas(id),
    guardado_en                   DATETIME2(3) NOT NULL
);
CREATE UNIQUE INDEX UX_vev_revision ON dbo.valoraciones_enfermeria_versiones (valoracion_id, revision_evento);
GO

CREATE TRIGGER dbo.TR_vev_immutable ON dbo.valoraciones_enfermeria_versiones INSTEAD OF UPDATE, DELETE AS
    THROW 50314, 'NURSING_ASSESSMENT_VERSION_IMMUTABLE', 1;
GO

INSERT INTO dbo.valoraciones_enfermeria_versiones
    (id, valoracion_id, evento_id, revision_evento, hallazgos, valoracion, actuaciones, comunicaciones, resultado,
     temperatura_celsius, tension_sistolica_mmhg, tension_diastolica_mmhg, frecuencia_cardiaca_lpm, frecuencia_respiratoria_rpm,
     saturacion_o2_pct, soporte_respiratorio_codigo, flujo_o2_lpm, glucemia_mg_dl,
     otra_constante_nombre, otra_constante_valor, otra_constante_unidad, guardado_por_cuenta_id, guardado_en)
SELECT NEWID(), v.id, v.evento_id, ea.revision, v.hallazgos, v.valoracion, v.actuaciones, v.comunicaciones, v.resultado,
       v.temperatura_celsius, v.tension_sistolica_mmhg, v.tension_diastolica_mmhg, v.frecuencia_cardiaca_lpm, v.frecuencia_respiratoria_rpm,
       v.saturacion_o2_pct, v.soporte_respiratorio_codigo, v.flujo_o2_lpm, v.glucemia_mg_dl,
       v.otra_constante_nombre, v.otra_constante_valor, v.otra_constante_unidad, v.actualizado_por_cuenta_id, v.actualizado_en
  FROM dbo.valoraciones_enfermeria v
  JOIN dbo.eventos_asistenciales ea ON ea.id = v.evento_id;
GO

/*
 * Comunicación familiar preparada al cerrar un evento (ENF-14/ENF-15): tipo decidido por el profesional
 * (FAM-06: la relevancia la decide un profesional, nunca el sistema) y texto comprensible, sin datos
 * internos. Una por evento. Hoy solo existe PENDIENTE_APROBACION y no se modifica; Portal Familiar
 * sustituirá el trigger por una guarda de transiciones cuando construya la aprobación.
 */
CREATE TABLE dbo.comunicaciones_familiares (
    id                         UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_cf PRIMARY KEY,
    evento_id                  UNIQUEIDENTIFIER NOT NULL,
    residente_id               UNIQUEIDENTIFIER NOT NULL,
    centro_id                  UNIQUEIDENTIFIER NOT NULL,
    tipo_codigo                NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    texto                      NVARCHAR(2000) NOT NULL,
    estado_codigo              NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_cf_estado DEFAULT 'PENDIENTE_APROBACION',
    preparado_por_cuenta_id    UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_cf_preparado_por REFERENCES dbo.cuentas(id),
    preparado_en               DATETIME2(3) NOT NULL,
    CONSTRAINT FK_cf_evento FOREIGN KEY (evento_id, residente_id, centro_id) REFERENCES dbo.eventos_asistenciales(id, residente_id, centro_id),
    CONSTRAINT CK_cf_tipo CHECK (tipo_codigo IN ('ORDINARIA', 'RELEVANTE')),
    CONSTRAINT CK_cf_texto CHECK (LEN(LTRIM(RTRIM(texto))) > 0),
    CONSTRAINT CK_cf_estado CHECK (estado_codigo = 'PENDIENTE_APROBACION')
);
CREATE UNIQUE INDEX UX_cf_evento ON dbo.comunicaciones_familiares (evento_id);
CREATE INDEX IX_cf_bandeja ON dbo.comunicaciones_familiares (centro_id, estado_codigo, preparado_en);
GO

CREATE TRIGGER dbo.TR_cf_immutable ON dbo.comunicaciones_familiares INSTEAD OF UPDATE, DELETE AS
    THROW 50315, 'FAMILY_COMMUNICATION_IMMUTABLE', 1;
GO
