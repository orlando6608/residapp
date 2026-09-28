/*
 * Añade la valoración de Enfermería (historia 2 de docs/historias-usuarios/enfermeria.md, ENF-03 a ENF-05;
 * pantallas ENF-04/ENF-05 del wireframe) sobre una noción común de "evento asistencial".
 *
 * dbo.eventos_asistenciales es el ciclo de vida de un evento, sea cual sea su origen: un cambio que
 * registra Auxiliar (dbo.cierres_cotidianos_residente, tipo CAMBIO_ENVIADO) o un evento que observa
 * Enfermería (dbo.eventos_clinicos). Ambos orígenes siguen siendo inmutables y conservan su autoría real
 * (ENF-16: "no simula autoría de Auxiliar"); lo único mutable es el estado de su gestión. Su id es el mismo
 * que el de la fila de origen, de modo que los enlaces existentes (bandejas, detalle) siguen siendo válidos.
 * El estado deja de vivir en dbo.eventos_clinicos.estado_codigo, que queda fijo en 'PENDIENTE' (0006).
 *
 * Concurrencia (docs/flujos-clinicos/valoracion-escalado-enfermeria.md, paso 2): "empezar valoración" y
 * cada guardado avanzan revision en exactamente 1; quien trabaje con una revisión anterior debe recargar.
 * Empezar registra quién y cuándo, sin crear propiedad permanente sobre el evento.
 *
 * Solo existen los estados PENDIENTE y EN_VALORACION: la decisión asistencial (cerrar, seguimiento,
 * escalar, protocolo urgente) llegará con las historias 3 a 6 y extenderá los CHECK de forma aditiva.
 */

CREATE TABLE dbo.eventos_asistenciales (
    id                                 UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ea PRIMARY KEY,
    residente_id                       UNIQUEIDENTIFIER NOT NULL,
    centro_id                          UNIQUEIDENTIFIER NOT NULL,
    unidad_id                          UNIQUEIDENTIFIER NOT NULL,
    origen_codigo                      NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    cierre_id                          UNIQUEIDENTIFIER NULL CONSTRAINT FK_ea_cierre REFERENCES dbo.cierres_cotidianos_residente(id),
    evento_clinico_id                  UNIQUEIDENTIFIER NULL CONSTRAINT FK_ea_evento_clinico REFERENCES dbo.eventos_clinicos(id),
    clasificacion_codigo               NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    recibido_en                        DATETIME2(3) NOT NULL,
    estado_codigo                      NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_ea_estado DEFAULT 'PENDIENTE',
    revision                           INT NOT NULL CONSTRAINT DF_ea_revision DEFAULT 1,
    valoracion_iniciada_por_cuenta_id  UNIQUEIDENTIFIER NULL CONSTRAINT FK_ea_iniciada_por REFERENCES dbo.cuentas(id),
    valoracion_iniciada_en             DATETIME2(3) NULL,
    CONSTRAINT FK_ea_resident FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes(centro_id, id),
    CONSTRAINT FK_ea_unit FOREIGN KEY (centro_id, unidad_id) REFERENCES dbo.unidades(centro_id, id),
    CONSTRAINT CK_ea_origen CHECK (
        (origen_codigo = 'CAMBIO_AUXILIAR' AND cierre_id = id AND evento_clinico_id IS NULL)
        OR (origen_codigo = 'EVENTO_ENFERMERIA' AND evento_clinico_id = id AND cierre_id IS NULL)),
    CONSTRAINT CK_ea_clasificacion CHECK (clasificacion_codigo IN ('ORDINARIO', 'PRIORITARIO')),
    CONSTRAINT CK_ea_estado CHECK (estado_codigo IN ('PENDIENTE', 'EN_VALORACION')),
    CONSTRAINT CK_ea_revision CHECK (revision >= 1),
    CONSTRAINT CK_ea_inicio CHECK (
        (estado_codigo = 'PENDIENTE' AND valoracion_iniciada_por_cuenta_id IS NULL AND valoracion_iniciada_en IS NULL)
        OR (estado_codigo <> 'PENDIENTE' AND valoracion_iniciada_por_cuenta_id IS NOT NULL AND valoracion_iniciada_en IS NOT NULL))
);
CREATE UNIQUE INDEX UX_ea_scope ON dbo.eventos_asistenciales (id, residente_id, centro_id);
CREATE INDEX IX_ea_bandeja ON dbo.eventos_asistenciales (centro_id, unidad_id, clasificacion_codigo, estado_codigo, recibido_en);
GO

-- Origen, residente, clasificación y hora de recepción son inmutables; toda actualización avanza la
-- revisión en exactamente 1; solo PENDIENTE -> EN_VALORACION o EN_VALORACION -> EN_VALORACION.
CREATE TRIGGER dbo.TR_ea_transition_guard ON dbo.eventos_asistenciales AFTER UPDATE AS
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
           OR i.estado_codigo <> 'EN_VALORACION')
        THROW 50310, 'CLINICAL_EVENT_TRANSITION_INVALID', 1;
END;
GO
CREATE TRIGGER dbo.TR_ea_no_delete ON dbo.eventos_asistenciales INSTEAD OF DELETE AS
    THROW 50311, 'CLINICAL_EVENT_DELETE_FORBIDDEN', 1;
GO

-- Los eventos ya existentes entran en el ciclo como PENDIENTE.
INSERT INTO dbo.eventos_asistenciales
    (id, residente_id, centro_id, unidad_id, origen_codigo, cierre_id, evento_clinico_id, clasificacion_codigo, recibido_en)
SELECT c.id, c.residente_id, c.centro_id, c.unidad_id, 'CAMBIO_AUXILIAR', c.id, NULL, c.clasificacion_codigo, c.ocurrido_en
  FROM dbo.cierres_cotidianos_residente c
 WHERE c.tipo_codigo = 'CAMBIO_ENVIADO';

INSERT INTO dbo.eventos_asistenciales
    (id, residente_id, centro_id, unidad_id, origen_codigo, cierre_id, evento_clinico_id, clasificacion_codigo, recibido_en)
SELECT e.id, e.residente_id, e.centro_id, e.unidad_id, 'EVENTO_ENFERMERIA', NULL, e.id, e.clasificacion_codigo, e.ocurrido_en
  FROM dbo.eventos_clinicos e;
GO

/*
 * Valoración de Enfermería (ENF-05): hallazgos, valoración, actuaciones, comunicaciones, resultado y
 * constantes opcionales. Mientras está en BORRADOR la edita cualquier profesional de Enfermería con acceso
 * al evento (sin propiedad permanente); quién la creó y quién la modificó por última vez quedan
 * registrados, y cada guardado deja además una fila en dbo.eventos_auditoria. Solo se validan formato y
 * coherencia (valores positivos, SpO2 en %, flujo solo con oxigenoterapia); no hay rangos clínicos de
 * alarma, que el sistema no debe inferir.
 */
CREATE TABLE dbo.valoraciones_enfermeria (
    id                            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ve PRIMARY KEY,
    evento_id                     UNIQUEIDENTIFIER NOT NULL,
    residente_id                  UNIQUEIDENTIFIER NOT NULL,
    centro_id                     UNIQUEIDENTIFIER NOT NULL,
    estado_codigo                 NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_ve_estado DEFAULT 'BORRADOR',
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
    creado_por_cuenta_id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_ve_creado_por REFERENCES dbo.cuentas(id),
    creado_en                     DATETIME2(3) NOT NULL,
    actualizado_por_cuenta_id     UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_ve_actualizado_por REFERENCES dbo.cuentas(id),
    actualizado_en                DATETIME2(3) NOT NULL,
    CONSTRAINT FK_ve_evento FOREIGN KEY (evento_id, residente_id, centro_id) REFERENCES dbo.eventos_asistenciales(id, residente_id, centro_id),
    CONSTRAINT CK_ve_estado CHECK (estado_codigo = 'BORRADOR'),
    CONSTRAINT CK_ve_positivos CHECK (
        (temperatura_celsius IS NULL OR temperatura_celsius > 0)
        AND (tension_sistolica_mmhg IS NULL OR tension_sistolica_mmhg > 0)
        AND (tension_diastolica_mmhg IS NULL OR tension_diastolica_mmhg > 0)
        AND (frecuencia_cardiaca_lpm IS NULL OR frecuencia_cardiaca_lpm > 0)
        AND (frecuencia_respiratoria_rpm IS NULL OR frecuencia_respiratoria_rpm > 0)
        AND (glucemia_mg_dl IS NULL OR glucemia_mg_dl > 0)),
    CONSTRAINT CK_ve_tension CHECK (
        (tension_sistolica_mmhg IS NULL AND tension_diastolica_mmhg IS NULL)
        OR (tension_sistolica_mmhg IS NOT NULL AND tension_diastolica_mmhg IS NOT NULL)),
    CONSTRAINT CK_ve_saturacion CHECK (saturacion_o2_pct IS NULL OR saturacion_o2_pct BETWEEN 0 AND 100),
    CONSTRAINT CK_ve_soporte CHECK (soporte_respiratorio_codigo IS NULL OR soporte_respiratorio_codigo IN ('AIRE_AMBIENTE', 'OXIGENOTERAPIA')),
    CONSTRAINT CK_ve_flujo CHECK (flujo_o2_lpm IS NULL OR (flujo_o2_lpm > 0 AND soporte_respiratorio_codigo = 'OXIGENOTERAPIA')),
    CONSTRAINT CK_ve_otra CHECK (
        (otra_constante_nombre IS NULL AND otra_constante_valor IS NULL AND otra_constante_unidad IS NULL)
        OR (LEN(LTRIM(RTRIM(otra_constante_nombre))) > 0 AND LEN(LTRIM(RTRIM(otra_constante_valor))) > 0))
);
CREATE UNIQUE INDEX UX_ve_borrador ON dbo.valoraciones_enfermeria (evento_id) WHERE estado_codigo = 'BORRADOR';
GO

CREATE TRIGGER dbo.TR_ve_guard ON dbo.valoraciones_enfermeria AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE d.estado_codigo <> 'BORRADOR'
           OR i.evento_id <> d.evento_id OR i.residente_id <> d.residente_id OR i.centro_id <> d.centro_id
           OR i.creado_por_cuenta_id <> d.creado_por_cuenta_id OR i.creado_en <> d.creado_en)
        THROW 50312, 'NURSING_ASSESSMENT_IMMUTABLE', 1;
END;
GO
CREATE TRIGGER dbo.TR_ve_no_delete ON dbo.valoraciones_enfermeria INSTEAD OF DELETE AS
    THROW 50313, 'NURSING_ASSESSMENT_DELETE_FORBIDDEN', 1;
GO
