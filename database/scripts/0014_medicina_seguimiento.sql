/*
 * Seguimiento médico y continuidad entre turnos (historia 5 de docs/historias-usuarios/medicina.md, MED-07 a
 * MED-09; pantallas MED-10 a MED-12 del wireframe).
 *
 * "Iniciar seguimiento médico" es una salida de la conducta médica: desde EN_VALORACION_MEDICA (con la
 * valoración médica guardada) o desde CON_INDICACION_PENDIENTE, el evento pasa a EN_SEGUIMIENTO_MEDICO, sale
 * de la bandeja de escalados y entra en la bandeja compartida de seguimientos médicos. Cada acción sobre el
 * seguimiento avanza la revisión del evento. "Resolver" vuelve a la conducta: cerrar (0013) o registrar una
 * indicación, que pasa el evento a CON_INDICACION_PENDIENTE y da el seguimiento por terminado. Decisión de
 * producto (2026-09-29): un solo seguimiento médico por evento.
 *
 * Equipo responsable: como en Enfermería (0010), hasta que existan turnos y equipos reales es Medicina de la
 * unidad del evento, así que no se guarda. Al terminar el turno, el médico decide explícitamente entre
 * transferir al equipo o turno entrante (texto libre y nota) o conservarlo para su próxima revisión (nota
 * opcional). La recepción de una transferencia se puede confirmar, pero no es necesaria: el seguimiento sigue
 * visible y trazable sin ella (MED-12).
 *
 * Tablas propias en vez de dbo.seguimientos: un evento que Enfermería escaló desde un seguimiento ya tiene su
 * fila allí (UX_seg_evento). Ambas son de solo inserción; el vencimiento se calcula al leer y un seguimiento
 * vencido nunca se cierra ni se oculta.
 */

ALTER TABLE dbo.eventos_asistenciales DROP CONSTRAINT CK_ea_estado;
ALTER TABLE dbo.eventos_asistenciales ADD CONSTRAINT CK_ea_estado CHECK (estado_codigo IN (
    'PENDIENTE', 'EN_VALORACION', 'EN_SEGUIMIENTO', 'ESCALADO_MEDICINA', 'EN_VALORACION_MEDICA',
    'CON_INDICACION_PENDIENTE', 'EN_SEGUIMIENTO_MEDICO', 'CERRADO'));
ALTER TABLE dbo.eventos_asistenciales DROP CONSTRAINT CK_ea_inicio_medico;
ALTER TABLE dbo.eventos_asistenciales ADD CONSTRAINT CK_ea_inicio_medico CHECK (
    estado_codigo NOT IN ('EN_VALORACION_MEDICA', 'CON_INDICACION_PENDIENTE', 'EN_SEGUIMIENTO_MEDICO')
    OR (valoracion_medica_iniciada_por_cuenta_id IS NOT NULL AND valoracion_medica_iniciada_en IS NOT NULL));
GO

-- Mismas reglas que en 0013, más EN_VALORACION_MEDICA / CON_INDICACION_PENDIENTE -> EN_SEGUIMIENTO_MEDICO y
-- EN_SEGUIMIENTO_MEDICO -> EN_SEGUIMIENTO_MEDICO (cada acción) / CON_INDICACION_PENDIENTE / CERRADO.
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
                   OR (d.estado_codigo IN ('EN_VALORACION_MEDICA', 'CON_INDICACION_PENDIENTE', 'EN_SEGUIMIENTO_MEDICO')
                       AND i.estado_codigo IN ('CON_INDICACION_PENDIENTE', 'EN_SEGUIMIENTO_MEDICO', 'CERRADO'))))
        THROW 50310, 'CLINICAL_EVENT_TRANSITION_INVALID', 1;
END;
GO

CREATE TABLE dbo.seguimientos_medicos (
    id                          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_segm PRIMARY KEY,
    evento_id                   UNIQUEIDENTIFIER NOT NULL,
    residente_id                UNIQUEIDENTIFIER NOT NULL,
    centro_id                   UNIQUEIDENTIFIER NOT NULL,
    fecha_prevista              DATE NULL,
    criterio                    NVARCHAR(1000) NULL,
    objetivo                    NVARCHAR(1000) NOT NULL,
    iniciado_por_cuenta_id      UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_segm_iniciado_por REFERENCES dbo.cuentas(id),
    iniciado_en                 DATETIME2(3) NOT NULL,
    CONSTRAINT FK_segm_evento FOREIGN KEY (evento_id, residente_id, centro_id) REFERENCES dbo.eventos_asistenciales(id, residente_id, centro_id),
    CONSTRAINT CK_segm_plan CHECK (fecha_prevista IS NOT NULL OR LEN(LTRIM(RTRIM(ISNULL(criterio, '')))) > 0),
    CONSTRAINT CK_segm_objetivo CHECK (LEN(LTRIM(RTRIM(objetivo))) > 0)
);
CREATE UNIQUE INDEX UX_segm_evento ON dbo.seguimientos_medicos (evento_id);
GO

CREATE TRIGGER dbo.TR_segm_immutable ON dbo.seguimientos_medicos INSTEAD OF UPDATE, DELETE AS
    THROW 50340, 'MEDICAL_FOLLOW_UP_IMMUTABLE', 1;
GO

/*
 * Acciones sobre un seguimiento médico (MED-11/MED-12), cada una con su autoría:
 *   ACTUACION       revisión registrada, en texto.
 *   REPROGRAMACION  nueva fecha prevista y/o criterio, con la justificación en texto.
 *   TRANSFERENCIA   equipo o turno entrante, con la nota de continuidad en texto.
 *   CONSERVACION    el médico lo conserva para su próxima revisión, con una nota opcional.
 *   RECEPCION       confirmación de una transferencia concreta (una sola por transferencia).
 */
CREATE TABLE dbo.seguimiento_medico_acciones (
    id                         UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_sma PRIMARY KEY,
    seguimiento_id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_sma_seguimiento REFERENCES dbo.seguimientos_medicos(id),
    tipo_codigo                NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    texto                      NVARCHAR(2000) NULL,
    fecha_prevista             DATE NULL,
    criterio                   NVARCHAR(1000) NULL,
    equipo_entrante            NVARCHAR(100) NULL,
    transferencia_id           UNIQUEIDENTIFIER NULL CONSTRAINT FK_sma_transferencia REFERENCES dbo.seguimiento_medico_acciones(id),
    registrado_por_cuenta_id   UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_sma_registrado_por REFERENCES dbo.cuentas(id),
    registrado_en              DATETIME2(3) NOT NULL,
    CONSTRAINT CK_sma_tipo CHECK (
        (tipo_codigo = 'ACTUACION' AND LEN(LTRIM(RTRIM(ISNULL(texto, '')))) > 0
            AND fecha_prevista IS NULL AND criterio IS NULL AND equipo_entrante IS NULL AND transferencia_id IS NULL)
        OR (tipo_codigo = 'REPROGRAMACION' AND LEN(LTRIM(RTRIM(ISNULL(texto, '')))) > 0
            AND (fecha_prevista IS NOT NULL OR LEN(LTRIM(RTRIM(ISNULL(criterio, '')))) > 0)
            AND equipo_entrante IS NULL AND transferencia_id IS NULL)
        OR (tipo_codigo = 'TRANSFERENCIA' AND LEN(LTRIM(RTRIM(ISNULL(equipo_entrante, '')))) > 0
            AND fecha_prevista IS NULL AND criterio IS NULL AND transferencia_id IS NULL)
        OR (tipo_codigo = 'CONSERVACION'
            AND fecha_prevista IS NULL AND criterio IS NULL AND equipo_entrante IS NULL AND transferencia_id IS NULL)
        OR (tipo_codigo = 'RECEPCION' AND transferencia_id IS NOT NULL
            AND texto IS NULL AND fecha_prevista IS NULL AND criterio IS NULL AND equipo_entrante IS NULL))
);
CREATE UNIQUE INDEX UX_sma_recepcion ON dbo.seguimiento_medico_acciones (transferencia_id) WHERE transferencia_id IS NOT NULL;
CREATE INDEX IX_sma_seguimiento ON dbo.seguimiento_medico_acciones (seguimiento_id, registrado_en);
GO

CREATE TRIGGER dbo.TR_sma_immutable ON dbo.seguimiento_medico_acciones INSTEAD OF UPDATE, DELETE AS
    THROW 50341, 'MEDICAL_FOLLOW_UP_ACTION_IMMUTABLE', 1;
GO
