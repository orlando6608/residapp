/*
 * La baja por fallecimiento cierra sola los episodios abiertos del residente, con una anotación de sistema (CJ, 2026-10-07; documento
 * continuidad-supervision-comunicacion, 3.2: «si el paciente fallece también se cierra»).
 *
 *   - eventos_asistenciales.cierre_sistema_codigo: FALLECIMIENTO cuando el cierre lo hizo el sistema al dar de baja. El cierre lleva como
 *     autor la cuenta de Administración que dio la baja, comunicación familiar NO_COMUNICAR y esta anotación; la pantalla del evento
 *     dice «cerrado por el sistema al dar de baja al residente por fallecimiento».
 *   - TR_ea_transition_guard solo admite ese cierre desde cualquier estado no cerrado si hay una baja por fallecimiento del residente
 *     creada en la MISMA transacción (bajas_residente.transaccion_id = CURRENT_TRANSACTION_ID()): no hay otra forma de cerrar así.
 *   - Las valoraciones en borrador, las indicaciones pendientes y los seguimientos no se tocan: quedan como estaban en el evento cerrado.
 */

ALTER TABLE dbo.eventos_asistenciales ADD cierre_sistema_codigo NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NULL;
GO
ALTER TABLE dbo.eventos_asistenciales ADD CONSTRAINT CK_ea_cierre_sistema CHECK (
    cierre_sistema_codigo IS NULL OR (cierre_sistema_codigo = 'FALLECIMIENTO' AND estado_codigo = 'CERRADO'));
GO
ALTER TABLE dbo.bajas_residente ADD transaccion_id BIGINT NOT NULL CONSTRAINT DF_bjr_transaccion DEFAULT (CURRENT_TRANSACTION_ID());
GO

-- Mismas reglas que en 0040, más el cierre del sistema por fallecimiento.
ALTER TRIGGER dbo.TR_ea_transition_guard ON dbo.eventos_asistenciales AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        CROSS APPLY (SELECT CASE WHEN i.estado_codigo = 'CERRADO' AND d.estado_codigo <> 'CERRADO'
                                      AND i.cierre_sistema_codigo = 'FALLECIMIENTO'
                                      AND EXISTS (SELECT 1 FROM dbo.bajas_residente b
                                                   WHERE b.residente_id = i.residente_id AND b.centro_id = i.centro_id
                                                     AND b.motivo_codigo = 'FALLECIMIENTO' AND b.transaccion_id = CURRENT_TRANSACTION_ID())
                                 THEN 1 ELSE 0 END AS SystemClosure) sc
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
           OR (ISNULL(i.cierre_sistema_codigo, '') <> ISNULL(d.cierre_sistema_codigo, '') AND sc.SystemClosure = 0)
           OR (d.valoracion_medica_iniciada_por_cuenta_id IS NOT NULL
               AND (i.valoracion_medica_iniciada_por_cuenta_id <> d.valoracion_medica_iniciada_por_cuenta_id
                    OR i.valoracion_medica_iniciada_en <> d.valoracion_medica_iniciada_en))
           OR NOT (sc.SystemClosure = 1
                   OR (i.unidad_id <> d.unidad_id AND i.estado_codigo = d.estado_codigo)
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

-- Un evento que nunca llegó a valorarse (PENDIENTE) también se cierra por fallecimiento: el cierre del sistema no inventa un inicio de
-- valoración, así que CK_ea_inicio admite CERRADO con cierre de sistema y sin iniciador.
ALTER TABLE dbo.eventos_asistenciales DROP CONSTRAINT CK_ea_inicio;
ALTER TABLE dbo.eventos_asistenciales ADD CONSTRAINT CK_ea_inicio CHECK (
    (origen_codigo = 'EVENTO_MEDICINA'
        AND valoracion_iniciada_por_cuenta_id IS NULL AND valoracion_iniciada_en IS NULL
        AND estado_codigo IN ('EN_VALORACION_MEDICA', 'CON_INDICACION_PENDIENTE', 'EN_SEGUIMIENTO_MEDICO',
                              'PROTOCOLO_URGENTE_MEDICO', 'CERRADO'))
    OR (origen_codigo <> 'EVENTO_MEDICINA'
        AND ((estado_codigo = 'PENDIENTE' AND valoracion_iniciada_por_cuenta_id IS NULL AND valoracion_iniciada_en IS NULL)
             OR (estado_codigo <> 'PENDIENTE' AND valoracion_iniciada_por_cuenta_id IS NOT NULL AND valoracion_iniciada_en IS NOT NULL)
             OR (estado_codigo = 'CERRADO' AND cierre_sistema_codigo IS NOT NULL))));
GO
