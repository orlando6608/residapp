/*
 * Respuestas de CJ del 2026-10-28 a «Dirección y traslados» (direccion-y-traslados-confirmaciones-cj).
 *
 *  1. 3.2 B: el motivo de la petición del historial tras un traslado pasa a lista cerrada (las tres finalidades de Dirección más «Otro») con texto corto opcional,
 *     obligatorio en «Otro». declaraciones_historial_traslado.proposito_codigo (NULL en las peticiones anteriores, que no se pueden tocar). motivo sigue siendo
 *     NOT NULL: guarda el texto escrito o, sin texto, la etiqueta de la finalidad.
 *  2. 3.3 B: eventos_auditoria.declaracion_id enlaza cada lectura con su petición, para contar las lecturas de cada una sin inferirlas. Solo lo rellenan las lecturas
 *     nuevas: la auditoría es inmutable y las anteriores quedan sin dato.
 *  3. 1.1: el cierre excepcional de Dirección (0075) no vale para un episodio que viene de una derivación a Urgencias (protocolo urgente o informe de derivación): ahí
 *     la comunicación relevante a la familia es obligatoria y NO_COMUNICAR se la saltaría. TR_ea_transition_guard repite la guarda de 0075 con esa condición.
 */

ALTER TABLE dbo.declaraciones_historial_traslado ADD proposito_codigo NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NULL;
GO
ALTER TABLE dbo.declaraciones_historial_traslado ADD CONSTRAINT CK_dht_proposito CHECK (
    proposito_codigo IS NULL OR proposito_codigo IN ('CONTINUIDAD_ASISTENCIAL', 'INCIDENCIA_RECLAMACION', 'TRAZABILIDAD_DOCUMENTAL', 'OTRO'));
GO

/* Mismas reglas que en 0077; el propósito tampoco cambia. */
ALTER TRIGGER dbo.TR_dht_guard ON dbo.declaraciones_historial_traslado INSTEAD OF UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM deleted) RETURN; -- una sentencia que no afecta a ninguna fila
    IF NOT EXISTS (SELECT 1 FROM inserted) -- DELETE
        THROW 50570, 'TRANSFER_HISTORY_DECLARATION_IMMUTABLE', 1;

    IF EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
         WHERE d.terminada_en IS NOT NULL OR i.terminada_en IS NULL
            OR i.centro_id <> d.centro_id OR i.cuenta_id <> d.cuenta_id OR i.ambito_perfil_id <> d.ambito_perfil_id
            OR i.residente_id <> d.residente_id OR i.motivo <> d.motivo OR ISNULL(i.proposito_codigo, N'') <> ISNULL(d.proposito_codigo, N'')
            OR i.creada_en <> d.creada_en OR i.caduca_en <> d.caduca_en)
        THROW 50570, 'TRANSFER_HISTORY_DECLARATION_IMMUTABLE', 1;

    UPDATE t SET terminada_en = i.terminada_en
      FROM dbo.declaraciones_historial_traslado t WITH (FORCESEEK) JOIN inserted i ON i.id = t.id;
END;
GO

ALTER TABLE dbo.eventos_auditoria ADD declaracion_id UNIQUEIDENTIFIER NULL;
GO
CREATE INDEX IX_audit_declaracion ON dbo.eventos_auditoria (declaracion_id) WHERE declaracion_id IS NOT NULL;
GO

/* Mismas reglas que en 0075, más: el cierre excepcional no vale con protocolo urgente ni informe de derivación. */
ALTER TRIGGER dbo.TR_ea_transition_guard ON dbo.eventos_asistenciales AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        CROSS APPLY (SELECT CASE WHEN i.estado_codigo = 'CERRADO' AND d.estado_codigo <> 'CERRADO'
                                      AND ((i.cierre_sistema_codigo = 'FALLECIMIENTO'
                                            AND EXISTS (SELECT 1 FROM dbo.bajas_residente b
                                                         WHERE b.residente_id = i.residente_id AND b.centro_id = i.centro_id
                                                           AND b.motivo_codigo = 'FALLECIMIENTO' AND b.transaccion_id = CURRENT_TRANSACTION_ID()))
                                           OR (i.cierre_sistema_codigo = 'DIRECCION_EXCEPCIONAL'
                                               AND EXISTS (SELECT 1 FROM dbo.cierres_excepcionales_direccion c WITH (FORCESEEK)
                                                            WHERE c.evento_id = i.id AND c.transaccion_id = CURRENT_TRANSACTION_ID())
                                               AND EXISTS (SELECT 1 FROM dbo.indicaciones_medicas im WITH (FORCESEEK)
                                                            WHERE im.evento_id = i.id AND im.estado_codigo IN ('PENDIENTE_LECTURA', 'LEIDA'))
                                               AND NOT EXISTS (SELECT 1 FROM dbo.protocolos_urgentes pu WITH (FORCESEEK) WHERE pu.evento_id = i.id)
                                               AND NOT EXISTS (SELECT 1 FROM dbo.informes_derivacion rd WITH (FORCESEEK) WHERE rd.evento_id = i.id)))
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
