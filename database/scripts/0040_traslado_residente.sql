/*
 * Traslado del residente (CJ, 2026-10-07; documento traslado-y-baja-residente).
 *
 *   - Solo entre unidades del mismo centro (también vale para cambiar de habitación o plaza dentro de la unidad).
 *   - Los episodios abiertos del residente pasan a la unidad de destino y la de origen deja de verlos: se actualiza
 *     eventos_asistenciales.unidad_id de los eventos no cerrados. Los eventos cerrados no se tocan; los hijos (valoraciones,
 *     seguimientos, ...) cuelgan del evento por (id, residente, centro) y no repiten la unidad.
 *   - Un borrador de basal a medias en la unidad de origen se cancela y el basal se rehace en la de destino.
 *   - Lo hace Administración (la parte de Enfermería con permiso específico queda para cuando un centro la pida).
 *
 * dbo.traslados_residente guarda cada traslado, inmutable. TR_ea_transition_guard solo deja cambiar la unidad de un evento no
 * cerrado, sin cambiar su estado, si hay un traslado de ese residente (de esa unidad a esa) creado en la MISMA transacción
 * (transaccion_id = CURRENT_TRANSACTION_ID()): no hay otra forma de mover un evento.
 */

CREATE TABLE dbo.traslados_residente (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_trr PRIMARY KEY,
    residente_id             UNIQUEIDENTIFIER NOT NULL,
    centro_id                UNIQUEIDENTIFIER NOT NULL,
    episodio_id              UNIQUEIDENTIFIER NOT NULL,
    unidad_origen_id         UNIQUEIDENTIFIER NOT NULL,
    unidad_destino_id        UNIQUEIDENTIFIER NOT NULL,
    intervalo_origen_id      UNIQUEIDENTIFIER NOT NULL,
    intervalo_destino_id     UNIQUEIDENTIFIER NOT NULL,
    eventos_trasladados      INT NOT NULL,
    borrador_basal_cancelado BIT NOT NULL,
    trasladado_por_cuenta_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_trr_by REFERENCES dbo.cuentas(id),
    trasladado_por_perfil    NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    trasladado_en            DATETIME2(3) NOT NULL,
    transaccion_id           BIGINT NOT NULL CONSTRAINT DF_trr_transaccion DEFAULT (CURRENT_TRANSACTION_ID()),
    CONSTRAINT FK_trr_resident FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes(centro_id, id),
    CONSTRAINT FK_trr_episode FOREIGN KEY (episodio_id, residente_id, centro_id) REFERENCES dbo.episodios_residente_centro(id, residente_id, centro_id),
    CONSTRAINT FK_trr_origin FOREIGN KEY (centro_id, unidad_origen_id) REFERENCES dbo.unidades(centro_id, id),
    CONSTRAINT FK_trr_destination FOREIGN KEY (centro_id, unidad_destino_id) REFERENCES dbo.unidades(centro_id, id),
    CONSTRAINT FK_trr_interval_origin FOREIGN KEY (intervalo_origen_id) REFERENCES dbo.intervalos_ubicacion_residente(id),
    CONSTRAINT FK_trr_interval_destination FOREIGN KEY (intervalo_destino_id) REFERENCES dbo.intervalos_ubicacion_residente(id),
    CONSTRAINT CK_trr_events CHECK (eventos_trasladados >= 0),
    CONSTRAINT CK_trr_profile CHECK (trasladado_por_perfil IN ('ADMINISTRACION', 'ENFERMERIA'))
);
CREATE UNIQUE INDEX UX_trr_scope ON dbo.traslados_residente (id, residente_id, centro_id);
CREATE INDEX IX_trr_resident ON dbo.traslados_residente (centro_id, residente_id, trasladado_en);
GO
CREATE TRIGGER dbo.TR_trr_immutable ON dbo.traslados_residente INSTEAD OF UPDATE, DELETE AS
    THROW 50440, 'RESIDENT_TRANSFER_IMMUTABLE', 1;
GO

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.traslados_residente,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.traslados_residente AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.traslados_residente AFTER UPDATE;
GO

-- Mismas reglas que en 0015, más el traslado: la unidad de un evento no cerrado puede cambiar, con el estado igual, si hay un
-- traslado del residente de esa unidad a la nueva creado en esta transacción. Seguir avanzando la revisión en 1.
ALTER TRIGGER dbo.TR_ea_transition_guard ON dbo.eventos_asistenciales AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE i.residente_id <> d.residente_id OR i.centro_id <> d.centro_id
           OR (i.unidad_id <> d.unidad_id
               AND NOT (i.estado_codigo = d.estado_codigo AND d.estado_codigo <> 'CERRADO'
                        AND EXISTS (SELECT 1 FROM dbo.traslados_residente t
                                     WHERE t.residente_id = i.residente_id AND t.centro_id = i.centro_id
                                       AND t.unidad_origen_id = d.unidad_id AND t.unidad_destino_id = i.unidad_id
                                       AND t.transaccion_id = CURRENT_TRANSACTION_ID())))
           OR i.origen_codigo <> d.origen_codigo
           OR ISNULL(i.cierre_id, '00000000-0000-0000-0000-000000000000') <> ISNULL(d.cierre_id, '00000000-0000-0000-0000-000000000000')
           OR ISNULL(i.evento_clinico_id, '00000000-0000-0000-0000-000000000000') <> ISNULL(d.evento_clinico_id, '00000000-0000-0000-0000-000000000000')
           OR i.clasificacion_codigo <> d.clasificacion_codigo OR i.recibido_en <> d.recibido_en
           OR i.revision <> d.revision + 1
           OR (d.valoracion_medica_iniciada_por_cuenta_id IS NOT NULL
               AND (i.valoracion_medica_iniciada_por_cuenta_id <> d.valoracion_medica_iniciada_por_cuenta_id
                    OR i.valoracion_medica_iniciada_en <> d.valoracion_medica_iniciada_en))
           OR NOT ((i.unidad_id <> d.unidad_id AND i.estado_codigo = d.estado_codigo)
                   OR (d.estado_codigo IN ('PENDIENTE', 'EN_VALORACION') AND i.estado_codigo = 'EN_VALORACION')
                   OR (d.estado_codigo IN ('EN_VALORACION', 'EN_SEGUIMIENTO')
                       AND i.estado_codigo IN ('EN_SEGUIMIENTO', 'ESCALADO_MEDICINA', 'PROTOCOLO_URGENTE', 'CERRADO'))
                   OR (d.estado_codigo = 'PROTOCOLO_URGENTE' AND i.estado_codigo IN ('PROTOCOLO_URGENTE', 'CERRADO'))
                   OR (d.estado_codigo IN ('ESCALADO_MEDICINA', 'EN_VALORACION_MEDICA') AND i.estado_codigo = 'EN_VALORACION_MEDICA')
                   OR (d.estado_codigo IN ('EN_VALORACION_MEDICA', 'CON_INDICACION_PENDIENTE', 'EN_SEGUIMIENTO_MEDICO')
                       AND i.estado_codigo IN ('CON_INDICACION_PENDIENTE', 'EN_SEGUIMIENTO_MEDICO', 'PROTOCOLO_URGENTE_MEDICO', 'CERRADO'))
                   OR (d.estado_codigo = 'PROTOCOLO_URGENTE_MEDICO' AND i.estado_codigo IN ('PROTOCOLO_URGENTE_MEDICO', 'CERRADO'))))
        THROW 50310, 'CLINICAL_EVENT_TRANSITION_INVALID', 1;
END;
GO

-- Un borrador de basal también puede cancelarlo el traslado del residente (quien traslada, no el autor del borrador).
ALTER TABLE dbo.basales_borrador ADD cancelado_por_traslado_id UNIQUEIDENTIFIER NULL;
GO
ALTER TABLE dbo.basales_borrador ADD CONSTRAINT FK_bd_cancelled_by_transfer
    FOREIGN KEY (cancelado_por_traslado_id, residente_id, centro_id) REFERENCES dbo.traslados_residente(id, residente_id, centro_id);
GO
ALTER TABLE dbo.basales_borrador DROP CONSTRAINT CK_bd_cancellation;
ALTER TABLE dbo.basales_borrador ADD CONSTRAINT CK_bd_cancellation CHECK (
    (estado <> 'CANCELLED' AND cancelado_en IS NULL AND cancelado_por_cuenta_id IS NULL
        AND cancelado_por_perfil IS NULL AND motivo_cancelacion IS NULL AND cancelado_por_traslado_id IS NULL)
    OR (estado = 'CANCELLED' AND cancelado_en IS NOT NULL AND LEN(LTRIM(RTRIM(ISNULL(motivo_cancelacion, '')))) > 0
        AND ((cancelado_por_traslado_id IS NULL AND cancelado_por_cuenta_id = creado_por_cuenta_id AND cancelado_por_perfil = creado_por_perfil)
             OR (cancelado_por_traslado_id IS NOT NULL AND cancelado_por_cuenta_id IS NOT NULL AND cancelado_por_perfil IS NOT NULL))));
GO
