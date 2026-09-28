/*
 * Valoración médica e indicaciones a Enfermería (historias 2 y 3 de docs/historias-usuarios/medicina.md,
 * MED-04 a MED-09) y su gestión por Enfermería (historia 7 de docs/historias-usuarios/enfermeria.md, ENF-10).
 *
 * Ciclo del evento en Medicina: ESCALADO_MEDICINA (pendiente médica) -> EN_VALORACION_MEDICA al empezar la
 * valoración (registra quién y cuándo, sin propiedad permanente) -> CON_INDICACION_PENDIENTE al registrar
 * la primera indicación. Medicina puede añadir más indicaciones; el resto de la conducta médica (cierre,
 * seguimiento médico, protocolo urgente) llegará con sus historias y añadirá sus transiciones. La
 * valoración médica sigue en borrador hasta el desenlace final y no se edita tras pasar a la conducta.
 *
 * Indicación: texto, fecha prevista o criterio e información adicional, sin prioridad automática. Enfermería
 * de la unidad confirma la lectura (PENDIENTE_LECTURA -> LEIDA) y después registra si fue realizada o no
 * realizada con incidencia (LEIDA -> REALIZADA / NO_REALIZADA). Lectura y realización son hitos distintos
 * y ninguno caduca: la indicación sigue visible hasta resolverse. Cada paso avanza su propia revisión
 * (concurrencia optimista) sin tocar la del evento, para que Enfermería y Medicina no se bloqueen entre sí.
 */

ALTER TABLE dbo.eventos_asistenciales ADD
    valoracion_medica_iniciada_por_cuenta_id  UNIQUEIDENTIFIER NULL CONSTRAINT FK_ea_medica_iniciada_por REFERENCES dbo.cuentas(id),
    valoracion_medica_iniciada_en             DATETIME2(3) NULL;
GO

ALTER TABLE dbo.eventos_asistenciales DROP CONSTRAINT CK_ea_estado;
ALTER TABLE dbo.eventos_asistenciales ADD CONSTRAINT CK_ea_estado CHECK (estado_codigo IN (
    'PENDIENTE', 'EN_VALORACION', 'EN_SEGUIMIENTO', 'ESCALADO_MEDICINA', 'EN_VALORACION_MEDICA',
    'CON_INDICACION_PENDIENTE', 'CERRADO'));
ALTER TABLE dbo.eventos_asistenciales ADD CONSTRAINT CK_ea_inicio_medico CHECK (
    estado_codigo NOT IN ('EN_VALORACION_MEDICA', 'CON_INDICACION_PENDIENTE')
    OR (valoracion_medica_iniciada_por_cuenta_id IS NOT NULL AND valoracion_medica_iniciada_en IS NOT NULL));
GO

-- Mismas reglas que en 0011, más las del ciclo médico: ESCALADO_MEDICINA -> EN_VALORACION_MEDICA,
-- EN_VALORACION_MEDICA -> EN_VALORACION_MEDICA / CON_INDICACION_PENDIENTE y
-- CON_INDICACION_PENDIENTE -> CON_INDICACION_PENDIENTE. Quién empezó la valoración médica no cambia después.
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
           OR (d.valoracion_medica_iniciada_por_cuenta_id IS NOT NULL
               AND (i.valoracion_medica_iniciada_por_cuenta_id <> d.valoracion_medica_iniciada_por_cuenta_id
                    OR i.valoracion_medica_iniciada_en <> d.valoracion_medica_iniciada_en))
           OR NOT ((d.estado_codigo IN ('PENDIENTE', 'EN_VALORACION') AND i.estado_codigo = 'EN_VALORACION')
                   OR (d.estado_codigo IN ('EN_VALORACION', 'EN_SEGUIMIENTO')
                       AND i.estado_codigo IN ('EN_SEGUIMIENTO', 'ESCALADO_MEDICINA', 'CERRADO'))
                   OR (d.estado_codigo IN ('ESCALADO_MEDICINA', 'EN_VALORACION_MEDICA') AND i.estado_codigo = 'EN_VALORACION_MEDICA')
                   OR (d.estado_codigo IN ('EN_VALORACION_MEDICA', 'CON_INDICACION_PENDIENTE') AND i.estado_codigo = 'CON_INDICACION_PENDIENTE')))
        THROW 50310, 'CLINICAL_EVENT_TRANSITION_INVALID', 1;
END;
GO

/*
 * Valoración médica (MED-05): hallazgos y exploración, valoración, actuaciones y constantes opcionales, con
 * las mismas comprobaciones de formato que la de Enfermería. Nunca modifica la observación original ni la
 * valoración de Enfermería, que viven en sus tablas.
 */
CREATE TABLE dbo.valoraciones_medicas (
    id                            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_vm PRIMARY KEY,
    evento_id                     UNIQUEIDENTIFIER NOT NULL,
    residente_id                  UNIQUEIDENTIFIER NOT NULL,
    centro_id                     UNIQUEIDENTIFIER NOT NULL,
    estado_codigo                 NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_vm_estado DEFAULT 'BORRADOR',
    hallazgos_exploracion         NVARCHAR(2000) NULL,
    valoracion                    NVARCHAR(2000) NULL,
    actuaciones                   NVARCHAR(2000) NULL,
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
    creado_por_cuenta_id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_vm_creado_por REFERENCES dbo.cuentas(id),
    creado_en                     DATETIME2(3) NOT NULL,
    actualizado_por_cuenta_id     UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_vm_actualizado_por REFERENCES dbo.cuentas(id),
    actualizado_en                DATETIME2(3) NOT NULL,
    CONSTRAINT FK_vm_evento FOREIGN KEY (evento_id, residente_id, centro_id) REFERENCES dbo.eventos_asistenciales(id, residente_id, centro_id),
    CONSTRAINT CK_vm_estado CHECK (estado_codigo IN ('BORRADOR', 'CERRADA')),
    CONSTRAINT CK_vm_positivos CHECK (
        (temperatura_celsius IS NULL OR temperatura_celsius > 0)
        AND (tension_sistolica_mmhg IS NULL OR tension_sistolica_mmhg > 0)
        AND (tension_diastolica_mmhg IS NULL OR tension_diastolica_mmhg > 0)
        AND (frecuencia_cardiaca_lpm IS NULL OR frecuencia_cardiaca_lpm > 0)
        AND (frecuencia_respiratoria_rpm IS NULL OR frecuencia_respiratoria_rpm > 0)
        AND (glucemia_mg_dl IS NULL OR glucemia_mg_dl > 0)),
    CONSTRAINT CK_vm_tension CHECK (
        (tension_sistolica_mmhg IS NULL AND tension_diastolica_mmhg IS NULL)
        OR (tension_sistolica_mmhg IS NOT NULL AND tension_diastolica_mmhg IS NOT NULL)),
    CONSTRAINT CK_vm_saturacion CHECK (saturacion_o2_pct IS NULL OR saturacion_o2_pct BETWEEN 0 AND 100),
    CONSTRAINT CK_vm_soporte CHECK (soporte_respiratorio_codigo IS NULL OR soporte_respiratorio_codigo IN ('AIRE_AMBIENTE', 'OXIGENOTERAPIA')),
    CONSTRAINT CK_vm_flujo CHECK (flujo_o2_lpm IS NULL OR (flujo_o2_lpm > 0 AND soporte_respiratorio_codigo = 'OXIGENOTERAPIA')),
    CONSTRAINT CK_vm_otra CHECK (
        (otra_constante_nombre IS NULL AND otra_constante_valor IS NULL AND otra_constante_unidad IS NULL)
        OR (LEN(LTRIM(RTRIM(otra_constante_nombre))) > 0 AND LEN(LTRIM(RTRIM(otra_constante_valor))) > 0))
);
CREATE UNIQUE INDEX UX_vm_evento ON dbo.valoraciones_medicas (evento_id);
GO

CREATE TRIGGER dbo.TR_vm_guard ON dbo.valoraciones_medicas AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE d.estado_codigo <> 'BORRADOR'
           OR i.evento_id <> d.evento_id OR i.residente_id <> d.residente_id OR i.centro_id <> d.centro_id
           OR i.creado_por_cuenta_id <> d.creado_por_cuenta_id OR i.creado_en <> d.creado_en)
        THROW 50330, 'MEDICAL_ASSESSMENT_IMMUTABLE', 1;
END;
GO
CREATE TRIGGER dbo.TR_vm_no_delete ON dbo.valoraciones_medicas INSTEAD OF DELETE AS
    THROW 50331, 'MEDICAL_ASSESSMENT_DELETE_FORBIDDEN', 1;
GO

-- Una copia inmutable por guardado, igual que valoraciones_enfermeria_versiones (0009).
CREATE TABLE dbo.valoraciones_medicas_versiones (
    id                            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_vmv PRIMARY KEY,
    valoracion_id                 UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_vmv_valoracion REFERENCES dbo.valoraciones_medicas(id),
    evento_id                     UNIQUEIDENTIFIER NOT NULL,
    revision_evento               INT NOT NULL,
    hallazgos_exploracion         NVARCHAR(2000) NULL,
    valoracion                    NVARCHAR(2000) NULL,
    actuaciones                   NVARCHAR(2000) NULL,
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
    guardado_por_cuenta_id        UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_vmv_guardado_por REFERENCES dbo.cuentas(id),
    guardado_en                   DATETIME2(3) NOT NULL
);
CREATE UNIQUE INDEX UX_vmv_revision ON dbo.valoraciones_medicas_versiones (valoracion_id, revision_evento);
GO
CREATE TRIGGER dbo.TR_vmv_immutable ON dbo.valoraciones_medicas_versiones INSTEAD OF UPDATE, DELETE AS
    THROW 50332, 'MEDICAL_ASSESSMENT_VERSION_IMMUTABLE', 1;
GO

CREATE TABLE dbo.indicaciones_medicas (
    id                         UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_im PRIMARY KEY,
    evento_id                  UNIQUEIDENTIFIER NOT NULL,
    residente_id               UNIQUEIDENTIFIER NOT NULL,
    centro_id                  UNIQUEIDENTIFIER NOT NULL,
    texto                      NVARCHAR(2000) NOT NULL,
    fecha_prevista             DATE NULL,
    criterio                   NVARCHAR(1000) NULL,
    informacion_adicional      NVARCHAR(2000) NULL,
    emitida_por_cuenta_id      UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_im_emitida_por REFERENCES dbo.cuentas(id),
    emitida_en                 DATETIME2(3) NOT NULL,
    estado_codigo              NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_im_estado DEFAULT 'PENDIENTE_LECTURA',
    revision                   INT NOT NULL CONSTRAINT DF_im_revision DEFAULT 1,
    leida_por_cuenta_id        UNIQUEIDENTIFIER NULL CONSTRAINT FK_im_leida_por REFERENCES dbo.cuentas(id),
    leida_en                   DATETIME2(3) NULL,
    resuelta_por_cuenta_id     UNIQUEIDENTIFIER NULL CONSTRAINT FK_im_resuelta_por REFERENCES dbo.cuentas(id),
    resuelta_en                DATETIME2(3) NULL,
    incidencia                 NVARCHAR(2000) NULL,
    CONSTRAINT FK_im_evento FOREIGN KEY (evento_id, residente_id, centro_id) REFERENCES dbo.eventos_asistenciales(id, residente_id, centro_id),
    CONSTRAINT CK_im_texto CHECK (LEN(LTRIM(RTRIM(texto))) > 0),
    CONSTRAINT CK_im_plan CHECK (fecha_prevista IS NOT NULL OR LEN(LTRIM(RTRIM(ISNULL(criterio, '')))) > 0),
    CONSTRAINT CK_im_estado CHECK (estado_codigo IN ('PENDIENTE_LECTURA', 'LEIDA', 'REALIZADA', 'NO_REALIZADA')),
    CONSTRAINT CK_im_lectura CHECK (
        (estado_codigo = 'PENDIENTE_LECTURA' AND leida_por_cuenta_id IS NULL AND leida_en IS NULL)
        OR (estado_codigo <> 'PENDIENTE_LECTURA' AND leida_por_cuenta_id IS NOT NULL AND leida_en IS NOT NULL)),
    CONSTRAINT CK_im_resolucion CHECK (
        (estado_codigo IN ('PENDIENTE_LECTURA', 'LEIDA') AND resuelta_por_cuenta_id IS NULL AND resuelta_en IS NULL AND incidencia IS NULL)
        OR (estado_codigo = 'REALIZADA' AND resuelta_por_cuenta_id IS NOT NULL AND resuelta_en IS NOT NULL AND incidencia IS NULL)
        OR (estado_codigo = 'NO_REALIZADA' AND resuelta_por_cuenta_id IS NOT NULL AND resuelta_en IS NOT NULL
            AND LEN(LTRIM(RTRIM(ISNULL(incidencia, '')))) > 0))
);
CREATE INDEX IX_im_evento ON dbo.indicaciones_medicas (evento_id);
CREATE INDEX IX_im_bandeja ON dbo.indicaciones_medicas (centro_id, estado_codigo, emitida_en);
GO

-- Solo PENDIENTE_LECTURA -> LEIDA -> REALIZADA / NO_REALIZADA, revisión + 1 en cada paso; el contenido de
-- la indicación y los hitos ya registrados no cambian.
CREATE TRIGGER dbo.TR_im_guard ON dbo.indicaciones_medicas AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE i.evento_id <> d.evento_id OR i.residente_id <> d.residente_id OR i.centro_id <> d.centro_id
           OR i.texto <> d.texto OR ISNULL(i.fecha_prevista, '19000101') <> ISNULL(d.fecha_prevista, '19000101')
           OR ISNULL(i.criterio, N'') <> ISNULL(d.criterio, N'') OR ISNULL(i.informacion_adicional, N'') <> ISNULL(d.informacion_adicional, N'')
           OR i.emitida_por_cuenta_id <> d.emitida_por_cuenta_id OR i.emitida_en <> d.emitida_en
           OR i.revision <> d.revision + 1
           OR (d.leida_por_cuenta_id IS NOT NULL AND (i.leida_por_cuenta_id <> d.leida_por_cuenta_id OR i.leida_en <> d.leida_en))
           OR NOT ((d.estado_codigo = 'PENDIENTE_LECTURA' AND i.estado_codigo = 'LEIDA')
                   OR (d.estado_codigo = 'LEIDA' AND i.estado_codigo IN ('REALIZADA', 'NO_REALIZADA'))))
        THROW 50333, 'MEDICAL_INDICATION_TRANSITION_INVALID', 1;
END;
GO
CREATE TRIGGER dbo.TR_im_no_delete ON dbo.indicaciones_medicas INSTEAD OF DELETE AS
    THROW 50334, 'MEDICAL_INDICATION_DELETE_FORBIDDEN', 1;
GO
