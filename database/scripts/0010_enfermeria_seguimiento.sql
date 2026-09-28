/*
 * Añade el seguimiento de un evento por Enfermería y su transferencia entre turnos (historia 4 de
 * docs/historias-usuarios/enfermeria.md, ENF-06 a ENF-08; pantallas ENF-07B, ENF-08 y ENF-09 del wireframe).
 *
 * Iniciar seguimiento es la segunda salida de la decisión asistencial: el evento pasa de EN_VALORACION a
 * EN_SEGUIMIENTO, sale de las bandejas de ordinarios y prioritarios y entra en la bandeja compartida de
 * seguimientos. Cada acción sobre el seguimiento avanza la revisión del evento (concurrencia optimista).
 * "Resolver" vuelve a la decisión asistencial: desde EN_SEGUIMIENTO se puede cerrar (0009). La valoración
 * sigue en BORRADOR durante el seguimiento y se cierra al cerrar el evento.
 *
 * Equipo responsable: hasta que exista el vertical Administración (turnos y equipos reales), es la
 * Enfermería de la unidad del evento, la misma que ya comparte sus bandejas, así que no se guarda. La
 * transferencia registra el equipo o turno entrante como texto libre y una nota de continuidad; queda
 * pendiente de recepción hasta que alguien la confirma, y el seguimiento sigue visible entretanto.
 *
 * Ambas tablas son de solo inserción: cada actuación conserva su autoría y el plan vigente (fecha prevista
 * o criterio) es el del seguimiento o el de su última reprogramación. Un seguimiento vencido nunca se
 * cierra ni se oculta: el vencimiento se calcula al leer.
 */

ALTER TABLE dbo.eventos_asistenciales DROP CONSTRAINT CK_ea_estado;
ALTER TABLE dbo.eventos_asistenciales ADD CONSTRAINT CK_ea_estado
    CHECK (estado_codigo IN ('PENDIENTE', 'EN_VALORACION', 'EN_SEGUIMIENTO', 'CERRADO'));
GO

-- Mismas reglas que en 0009, con las transiciones del seguimiento: EN_VALORACION -> EN_SEGUIMIENTO,
-- EN_SEGUIMIENTO -> EN_SEGUIMIENTO (cada acción) y EN_SEGUIMIENTO -> CERRADO. Nada sale de CERRADO.
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
                   OR (d.estado_codigo IN ('EN_VALORACION', 'EN_SEGUIMIENTO') AND i.estado_codigo IN ('EN_SEGUIMIENTO', 'CERRADO'))))
        THROW 50310, 'CLINICAL_EVENT_TRANSITION_INVALID', 1;
END;
GO

CREATE TABLE dbo.seguimientos (
    id                          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_seg PRIMARY KEY,
    evento_id                   UNIQUEIDENTIFIER NOT NULL,
    residente_id                UNIQUEIDENTIFIER NOT NULL,
    centro_id                   UNIQUEIDENTIFIER NOT NULL,
    fecha_prevista              DATE NULL,
    criterio                    NVARCHAR(1000) NULL,
    indicaciones_continuidad    NVARCHAR(2000) NULL,
    iniciado_por_cuenta_id      UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_seg_iniciado_por REFERENCES dbo.cuentas(id),
    iniciado_en                 DATETIME2(3) NOT NULL,
    CONSTRAINT FK_seg_evento FOREIGN KEY (evento_id, residente_id, centro_id) REFERENCES dbo.eventos_asistenciales(id, residente_id, centro_id),
    CONSTRAINT CK_seg_plan CHECK (fecha_prevista IS NOT NULL OR LEN(LTRIM(RTRIM(ISNULL(criterio, '')))) > 0)
);
CREATE UNIQUE INDEX UX_seg_evento ON dbo.seguimientos (evento_id);
GO

CREATE TRIGGER dbo.TR_seg_immutable ON dbo.seguimientos INSTEAD OF UPDATE, DELETE AS
    THROW 50316, 'FOLLOW_UP_IMMUTABLE', 1;
GO

/*
 * Acciones sobre un seguimiento (ENF-08/ENF-09), cada una con su autoría:
 *   ACTUACION       texto de la actuación.
 *   REPROGRAMACION  nueva fecha prevista y/o criterio, con la justificación en texto.
 *   TRANSFERENCIA   equipo o turno entrante, con la nota de continuidad en texto.
 *   RECEPCION       confirmación de una transferencia concreta (una sola por transferencia).
 */
CREATE TABLE dbo.seguimiento_acciones (
    id                         UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_sa PRIMARY KEY,
    seguimiento_id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_sa_seguimiento REFERENCES dbo.seguimientos(id),
    tipo_codigo                NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    texto                      NVARCHAR(2000) NULL,
    fecha_prevista             DATE NULL,
    criterio                   NVARCHAR(1000) NULL,
    equipo_entrante            NVARCHAR(100) NULL,
    transferencia_id           UNIQUEIDENTIFIER NULL CONSTRAINT FK_sa_transferencia REFERENCES dbo.seguimiento_acciones(id),
    registrado_por_cuenta_id   UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_sa_registrado_por REFERENCES dbo.cuentas(id),
    registrado_en              DATETIME2(3) NOT NULL,
    CONSTRAINT CK_sa_tipo CHECK (
        (tipo_codigo = 'ACTUACION' AND LEN(LTRIM(RTRIM(ISNULL(texto, '')))) > 0
            AND fecha_prevista IS NULL AND criterio IS NULL AND equipo_entrante IS NULL AND transferencia_id IS NULL)
        OR (tipo_codigo = 'REPROGRAMACION' AND LEN(LTRIM(RTRIM(ISNULL(texto, '')))) > 0
            AND (fecha_prevista IS NOT NULL OR LEN(LTRIM(RTRIM(ISNULL(criterio, '')))) > 0)
            AND equipo_entrante IS NULL AND transferencia_id IS NULL)
        OR (tipo_codigo = 'TRANSFERENCIA' AND LEN(LTRIM(RTRIM(ISNULL(equipo_entrante, '')))) > 0
            AND fecha_prevista IS NULL AND criterio IS NULL AND transferencia_id IS NULL)
        OR (tipo_codigo = 'RECEPCION' AND transferencia_id IS NOT NULL
            AND texto IS NULL AND fecha_prevista IS NULL AND criterio IS NULL AND equipo_entrante IS NULL))
);
CREATE UNIQUE INDEX UX_sa_recepcion ON dbo.seguimiento_acciones (transferencia_id) WHERE transferencia_id IS NOT NULL;
CREATE INDEX IX_sa_seguimiento ON dbo.seguimiento_acciones (seguimiento_id, registrado_en);
GO

CREATE TRIGGER dbo.TR_sa_immutable ON dbo.seguimiento_acciones INSTEAD OF UPDATE, DELETE AS
    THROW 50317, 'FOLLOW_UP_ACTION_IMMUTABLE', 1;
GO
