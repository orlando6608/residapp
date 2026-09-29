/*
 * Protocolo urgente, bloque 1 (historia 6 de Enfermería y de Medicina: ENF-06/ENF-11, MED-05/MED-13 y
 * DER-04). La derivación a Urgencias con su informe firmado en PDF llegará en el bloque 2 y añadirá su
 * salida.
 *
 * "Activar protocolo urgente" es la cuarta salida de la decisión asistencial y de la conducta médica. El
 * protocolo pertenece al perfil que lo activa, con un estado propio para cada uno, igual que los seguimientos:
 *   Enfermería: EN_VALORACION / EN_SEGUIMIENTO -> PROTOCOLO_URGENTE.
 *   Medicina:   EN_VALORACION_MEDICA / CON_INDICACION_PENDIENTE / EN_SEGUIMIENTO_MEDICO -> PROTOCOLO_URGENTE_MEDICO.
 * Mientras está activo se documentan actuaciones, evolución y contactos con servicios (servicio y hora del
 * contacto, que quedan en la trazabilidad interna y nunca en el informe externo, DER-04); cada registro
 * avanza la revisión del evento. Decisión de producto (2026-09-29): desde el protocolo solo se deriva o se
 * cierra con el cierre común; Enfermería no escala un protocolo. Un protocolo por evento; la valoración del
 * perfil sigue en borrador y se cierra al cerrar el evento.
 *
 * Activar no retrasa la atención: basta un paso con una nota opcional. Ambas tablas son de solo inserción.
 */

ALTER TABLE dbo.eventos_asistenciales DROP CONSTRAINT CK_ea_estado;
ALTER TABLE dbo.eventos_asistenciales ADD CONSTRAINT CK_ea_estado CHECK (estado_codigo IN (
    'PENDIENTE', 'EN_VALORACION', 'EN_SEGUIMIENTO', 'ESCALADO_MEDICINA', 'EN_VALORACION_MEDICA',
    'CON_INDICACION_PENDIENTE', 'EN_SEGUIMIENTO_MEDICO', 'PROTOCOLO_URGENTE', 'PROTOCOLO_URGENTE_MEDICO', 'CERRADO'));
ALTER TABLE dbo.eventos_asistenciales DROP CONSTRAINT CK_ea_inicio_medico;
ALTER TABLE dbo.eventos_asistenciales ADD CONSTRAINT CK_ea_inicio_medico CHECK (
    estado_codigo NOT IN ('EN_VALORACION_MEDICA', 'CON_INDICACION_PENDIENTE', 'EN_SEGUIMIENTO_MEDICO', 'PROTOCOLO_URGENTE_MEDICO')
    OR (valoracion_medica_iniciada_por_cuenta_id IS NOT NULL AND valoracion_medica_iniciada_en IS NOT NULL));
GO

-- Mismas reglas que en 0014, más las entradas y salidas de los dos protocolos urgentes.
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
                       AND i.estado_codigo IN ('EN_SEGUIMIENTO', 'ESCALADO_MEDICINA', 'PROTOCOLO_URGENTE', 'CERRADO'))
                   OR (d.estado_codigo = 'PROTOCOLO_URGENTE' AND i.estado_codigo IN ('PROTOCOLO_URGENTE', 'CERRADO'))
                   OR (d.estado_codigo IN ('ESCALADO_MEDICINA', 'EN_VALORACION_MEDICA') AND i.estado_codigo = 'EN_VALORACION_MEDICA')
                   OR (d.estado_codigo IN ('EN_VALORACION_MEDICA', 'CON_INDICACION_PENDIENTE', 'EN_SEGUIMIENTO_MEDICO')
                       AND i.estado_codigo IN ('CON_INDICACION_PENDIENTE', 'EN_SEGUIMIENTO_MEDICO', 'PROTOCOLO_URGENTE_MEDICO', 'CERRADO'))
                   OR (d.estado_codigo = 'PROTOCOLO_URGENTE_MEDICO' AND i.estado_codigo IN ('PROTOCOLO_URGENTE_MEDICO', 'CERRADO'))))
        THROW 50310, 'CLINICAL_EVENT_TRANSITION_INVALID', 1;
END;
GO

CREATE TABLE dbo.protocolos_urgentes (
    id                        UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_pu PRIMARY KEY,
    evento_id                 UNIQUEIDENTIFIER NOT NULL,
    residente_id              UNIQUEIDENTIFIER NOT NULL,
    centro_id                 UNIQUEIDENTIFIER NOT NULL,
    perfil_codigo             NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    nota_activacion           NVARCHAR(2000) NULL,
    activado_por_cuenta_id    UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_pu_activado_por REFERENCES dbo.cuentas(id),
    activado_en               DATETIME2(3) NOT NULL,
    CONSTRAINT FK_pu_evento FOREIGN KEY (evento_id, residente_id, centro_id) REFERENCES dbo.eventos_asistenciales(id, residente_id, centro_id),
    CONSTRAINT CK_pu_perfil CHECK (perfil_codigo IN ('ENFERMERIA', 'MEDICINA')),
    CONSTRAINT CK_pu_nota CHECK (nota_activacion IS NULL OR LEN(LTRIM(RTRIM(nota_activacion))) > 0)
);
CREATE UNIQUE INDEX UX_pu_evento ON dbo.protocolos_urgentes (evento_id);
GO

CREATE TRIGGER dbo.TR_pu_immutable ON dbo.protocolos_urgentes INSTEAD OF UPDATE, DELETE AS
    THROW 50350, 'URGENT_PROTOCOL_IMMUTABLE', 1;
GO

/*
 * Registros del protocolo (ENF-11/MED-13), cada uno con su autoría:
 *   ACTUACION  actuación realizada, en texto.
 *   EVOLUCION  evolución del residente, en texto.
 *   CONTACTO   servicio contactado y hora del contacto (la escribe el profesional: puede documentarse
 *              después, pero nunca en el futuro), con una nota opcional. Trazabilidad interna (DER-04).
 */
CREATE TABLE dbo.protocolo_urgente_registros (
    id                         UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_pur PRIMARY KEY,
    protocolo_id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_pur_protocolo REFERENCES dbo.protocolos_urgentes(id),
    tipo_codigo                NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    texto                      NVARCHAR(2000) NULL,
    servicio_contactado        NVARCHAR(200) NULL,
    contactado_en              DATETIME2(3) NULL,
    registrado_por_cuenta_id   UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_pur_registrado_por REFERENCES dbo.cuentas(id),
    registrado_en              DATETIME2(3) NOT NULL,
    CONSTRAINT CK_pur_tipo CHECK (
        (tipo_codigo IN ('ACTUACION', 'EVOLUCION') AND LEN(LTRIM(RTRIM(ISNULL(texto, '')))) > 0
            AND servicio_contactado IS NULL AND contactado_en IS NULL)
        OR (tipo_codigo = 'CONTACTO' AND LEN(LTRIM(RTRIM(ISNULL(servicio_contactado, '')))) > 0 AND contactado_en IS NOT NULL
            AND (texto IS NULL OR LEN(LTRIM(RTRIM(texto))) > 0))),
    CONSTRAINT CK_pur_contacto_no_futuro CHECK (contactado_en IS NULL OR contactado_en <= DATEADD(MINUTE, 5, registrado_en))
);
CREATE INDEX IX_pur_protocolo ON dbo.protocolo_urgente_registros (protocolo_id, registrado_en);
GO

CREATE TRIGGER dbo.TR_pur_immutable ON dbo.protocolo_urgente_registros INSTEAD OF UPDATE, DELETE AS
    THROW 50351, 'URGENT_PROTOCOL_ENTRY_IMMUTABLE', 1;
GO
