/*
 * Cierre excepcional de un episodio con indicaciones médicas que nadie va a cumplir (CJ, tercera tanda 5.1: «solo Dirección (Clínica),
 * indicando un motivo que queda en la auditoría»; el nombre de quien cierra también). Hasta ahora ese episodio no se podía cerrar
 * (MEDICAL_INDICATIONS_PENDING) y se quedaba abierto para siempre.
 *
 *   - Las indicaciones NO se tocan (decisión de Orlando, 2026-10-26): siguen «pendiente de lectura» o «leída» y los indicadores las cuentan
 *     como sin resolver. Nadie afirma que se hicieron ni que no se hicieron.
 *   - cierres_excepcionales_direccion: una fila inmutable por episodio cerrado así, con el motivo, la cuenta de Dirección Clínica, la hora y
 *     cuántas indicaciones quedaban abiertas. El motivo no sale en ningún listado de la familia.
 *   - eventos_asistenciales.cierre_sistema_codigo admite un valor más, DIRECCION_EXCEPCIONAL. Como el cierre por fallecimiento (0042), el cierre
 *     lleva comunicación familiar NO_COMUNICAR y no necesita iniciador de valoración (CK_ea_inicio ya lo admite con un cierre de sistema).
 *   - TR_ea_transition_guard solo admite ese cierre desde cualquier estado no cerrado si hay una fila de cierres_excepcionales_direccion de
 *     ESE episodio creada en la MISMA transacción (transaccion_id = CURRENT_TRANSACTION_ID()) y el episodio tiene alguna indicación abierta:
 *     no hay otra forma de cerrar así y no es un cierre libre.
 *
 * Que quien actúa sea Dirección Clínica con el episodio en su ámbito y el motivo lo comprueba la aplicación dentro de su transacción.
 */

CREATE TABLE dbo.cierres_excepcionales_direccion (
    evento_id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ced PRIMARY KEY,
    centro_id              UNIQUEIDENTIFIER NOT NULL,
    residente_id           UNIQUEIDENTIFIER NOT NULL,
    motivo                 NVARCHAR(300) NOT NULL,
    indicaciones_abiertas  INT NOT NULL,
    cerrado_por_cuenta_id  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_ced_cuenta REFERENCES dbo.cuentas(id),
    cerrado_en             DATETIME2(3) NOT NULL,
    transaccion_id         BIGINT NOT NULL CONSTRAINT DF_ced_transaccion DEFAULT (CURRENT_TRANSACTION_ID()),
    CONSTRAINT FK_ced_evento FOREIGN KEY (evento_id, residente_id, centro_id) REFERENCES dbo.eventos_asistenciales (id, residente_id, centro_id),
    CONSTRAINT CK_ced_motivo CHECK (LEN(LTRIM(RTRIM(motivo))) > 0),
    CONSTRAINT CK_ced_indicaciones CHECK (indicaciones_abiertas > 0)
);
CREATE INDEX IX_ced_residente ON dbo.cierres_excepcionales_direccion (centro_id, residente_id, cerrado_en);
GO

CREATE TRIGGER dbo.TR_ced_immutable ON dbo.cierres_excepcionales_direccion INSTEAD OF UPDATE, DELETE AS
    THROW 50560, 'EXCEPTIONAL_CLOSURE_IMMUTABLE', 1;
GO

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.cierres_excepcionales_direccion,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.cierres_excepcionales_direccion AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.cierres_excepcionales_direccion AFTER UPDATE;
GO

ALTER TABLE dbo.eventos_asistenciales DROP CONSTRAINT CK_ea_cierre_sistema;
ALTER TABLE dbo.eventos_asistenciales ADD CONSTRAINT CK_ea_cierre_sistema CHECK (
    cierre_sistema_codigo IS NULL
    OR (cierre_sistema_codigo IN ('FALLECIMIENTO', 'DIRECCION_EXCEPCIONAL') AND estado_codigo = 'CERRADO'));
GO

-- Mismas reglas que en 0042, más el cierre excepcional de Dirección.
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
                                                            WHERE im.evento_id = i.id AND im.estado_codigo IN ('PENDIENTE_LECTURA', 'LEIDA'))))
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
