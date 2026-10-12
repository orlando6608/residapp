/*
 * Portal Familiar, citas en modo solicitud previa (portal-familiar.md, historia 5; administracion.md, historia 8; CIT-S01 a CIT-S04, CIT-C02, CIT-C03).
 * Suposiciones a confirmar con CJ en portal-familiar-confirmaciones-cj.html, tema 6.
 *
 * La familia envía una solicitud (equipo, modalidad, motivo categorizado, comentario breve y disponibilidad aproximada); Administración propone
 * una fecha y una hora (y puede apuntar un profesional interno, que la familia nunca ve); la familia acepta, pide otro horario o cancela. Solo la
 * aceptación crea una cita Confirmada en dbo.citas (0054): la solicitud no es una cita. No hay chat.
 *
 *  - citas_solicitudes: una fila por solicitud, con su estado (PENDIENTE, PROPUESTA, ACEPTADA, CANCELADA o CERRADA por el centro). A lo sumo una
 *    solicitud abierta (PENDIENTE o PROPUESTA) por residente y equipo (UX_citas_solicitudes_abierta), y la misma operación de la misma cuenta no se
 *    repite (UX_citas_solicitudes_operacion): es la idempotencia de la doble pulsación. Una propuesta viva reserva su hueco: la aplicación la cuenta
 *    como ocupada al calcular huecos libres; la barrera final contra dos citas en el mismo hueco sigue siendo UX_citas_hueco al aceptar.
 *  - citas_solicitudes_cambios: cada paso con su actor, perfil e instante (CIT-C03). Inmutable.
 *
 * Que el servicio esté en solicitud previa, la modalidad, la propuesta viva, el hueco libre y el límite de una cita futura por residente y equipo los
 * comprueba la aplicación dentro de su transacción; aquí quedan la inmutabilidad, las transiciones permitidas, la exclusión de dos solicitudes
 * abiertas, la idempotencia y la coherencia del residente con el vínculo. Las búsquedas de la aplicación llevan FORCESEEK: en una BD recién creada
 * (la del CI) el plan recorre la clave primaria y dos transacciones se bloquean (lección de 0055 y 0056).
 */

CREATE TABLE dbo.citas_solicitudes (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_citas_solicitudes PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_citas_solicitudes_centro REFERENCES dbo.centros(id),
    residente_id             UNIQUEIDENTIFIER NOT NULL,
    vinculo_id               UNIQUEIDENTIFIER NOT NULL,
    cuenta_id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_citas_solicitudes_cuenta REFERENCES dbo.cuentas(id),
    equipo_codigo            NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    modalidad_codigo         NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    motivo_codigo            NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NOT NULL,
    comentario               NVARCHAR(300) NULL,
    disponibilidad_dias      NVARCHAR(13) COLLATE Latin1_General_100_BIN2 NOT NULL,
    disponibilidad_franja    NVARCHAR(12) COLLATE Latin1_General_100_BIN2 NOT NULL,
    estado_codigo            NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    propuesta_inicio_en      DATETIME2(3) NULL,
    propuesta_fin_en         DATETIME2(3) NULL,
    profesional_interno      NVARCHAR(100) NULL,
    cita_id                  UNIQUEIDENTIFIER NULL,
    operacion_id             UNIQUEIDENTIFIER NOT NULL,
    creada_en                DATETIME2(3) NOT NULL,
    CONSTRAINT UQ_citas_solicitudes_centro UNIQUE (centro_id, id),
    CONSTRAINT FK_citas_solicitudes_vinculo FOREIGN KEY (centro_id, residente_id, vinculo_id) REFERENCES dbo.residentes_familiares (centro_id, residente_id, id),
    CONSTRAINT FK_citas_solicitudes_cita FOREIGN KEY (centro_id, cita_id) REFERENCES dbo.citas (centro_id, id),
    CONSTRAINT CK_citas_solicitudes_equipo CHECK (equipo_codigo IN ('MEDICO', 'ENFERMERIA')),
    CONSTRAINT CK_citas_solicitudes_modalidad CHECK (modalidad_codigo IN ('PRESENCIAL', 'TELEFONICA', 'VIDEOCONFERENCIA')),
    CONSTRAINT CK_citas_solicitudes_motivo CHECK (motivo_codigo IN
        ('SEGUIMIENTO_GENERAL', 'RESULTADOS', 'MEDICACION', 'CAMBIO_RECIENTE', 'INFORMACION_CUIDADOS', 'OTRO')),
    CONSTRAINT CK_citas_solicitudes_franja CHECK (disponibilidad_franja IN ('INDIFERENTE', 'MANANA', 'TARDE')),
    CONSTRAINT CK_citas_solicitudes_estado CHECK (estado_codigo IN ('PENDIENTE', 'PROPUESTA', 'ACEPTADA', 'CANCELADA', 'CERRADA')),
    CONSTRAINT CK_citas_solicitudes_propuesta CHECK (
        (estado_codigo IN ('PROPUESTA', 'ACEPTADA') AND propuesta_inicio_en IS NOT NULL AND propuesta_fin_en IS NOT NULL AND propuesta_fin_en > propuesta_inicio_en)
        OR (estado_codigo NOT IN ('PROPUESTA', 'ACEPTADA') AND propuesta_inicio_en IS NULL AND propuesta_fin_en IS NULL)),
    CONSTRAINT CK_citas_solicitudes_cita CHECK ((estado_codigo = 'ACEPTADA' AND cita_id IS NOT NULL) OR (estado_codigo <> 'ACEPTADA' AND cita_id IS NULL)),
    CONSTRAINT CK_citas_solicitudes_comentario CHECK (comentario IS NULL OR LEN(LTRIM(RTRIM(comentario))) > 0)
);
CREATE UNIQUE INDEX UX_citas_solicitudes_abierta ON dbo.citas_solicitudes (centro_id, residente_id, equipo_codigo) WHERE estado_codigo IN ('PENDIENTE', 'PROPUESTA');
CREATE UNIQUE INDEX UX_citas_solicitudes_operacion ON dbo.citas_solicitudes (cuenta_id, operacion_id);
CREATE INDEX IX_citas_solicitudes_centro ON dbo.citas_solicitudes (centro_id, estado_codigo, creada_en);
CREATE INDEX IX_citas_solicitudes_residente ON dbo.citas_solicitudes (centro_id, residente_id, creada_en);
CREATE INDEX IX_citas_solicitudes_propuesta ON dbo.citas_solicitudes (centro_id, equipo_codigo, propuesta_inicio_en) INCLUDE (propuesta_fin_en) WHERE estado_codigo = 'PROPUESTA';
GO

CREATE TABLE dbo.citas_solicitudes_cambios (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_citas_solicitudes_cambios PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL,
    solicitud_id             UNIQUEIDENTIFIER NOT NULL,
    numero                   INT NOT NULL,
    accion_codigo            NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    actor_cuenta_id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_citas_solicitudes_cambios_cuenta REFERENCES dbo.cuentas(id),
    actor_perfil_codigo      NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NOT NULL,
    inicio_propuesto_en      DATETIME2(3) NULL,
    detalle                  NVARCHAR(500) NULL,
    ocurrido_en              DATETIME2(3) NOT NULL,
    CONSTRAINT FK_citas_solicitudes_cambios_solicitud FOREIGN KEY (centro_id, solicitud_id) REFERENCES dbo.citas_solicitudes (centro_id, id),
    CONSTRAINT CK_citas_solicitudes_cambios_accion CHECK (accion_codigo IN ('CREADA', 'PROPUESTA', 'OTRO_HORARIO', 'ACEPTADA', 'CANCELADA', 'CERRADA')),
    CONSTRAINT CK_citas_solicitudes_cambios_perfil CHECK (actor_perfil_codigo IN ('FAMILIAR', 'ADMINISTRACION')),
    CONSTRAINT CK_citas_solicitudes_cambios_numero CHECK (numero > 0)
);
CREATE UNIQUE INDEX UX_citas_solicitudes_cambios_numero ON dbo.citas_solicitudes_cambios (solicitud_id, numero);
GO

CREATE TRIGGER dbo.TR_citas_solicitudes_cambios_immutable ON dbo.citas_solicitudes_cambios INSTEAD OF UPDATE, DELETE AS
    THROW 50530, 'APPOINTMENT_REQUEST_CHANGE_IMMUTABLE', 1;
GO

CREATE TRIGGER dbo.TR_citas_solicitudes_no_delete ON dbo.citas_solicitudes INSTEAD OF DELETE AS
    THROW 50531, 'APPOINTMENT_REQUEST_NO_DELETE', 1;
GO

/* Una solicitud nace pendiente. */
CREATE TRIGGER dbo.TR_citas_solicitudes_insert_guard ON dbo.citas_solicitudes AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted WHERE estado_codigo <> 'PENDIENTE')
        THROW 50534, 'APPOINTMENT_REQUEST_TRANSITION_INVALID', 1;
END;
GO

/* De una solicitud solo cambian el estado, la propuesta y la cita; el residente, el vínculo, quien la envió, el equipo, la modalidad y lo que pidió
   no. PENDIENTE pasa a PROPUESTA, CANCELADA o CERRADA; PROPUESTA, a ACEPTADA, PENDIENTE (otro horario), CANCELADA o CERRADA; el resto son finales. */
CREATE TRIGGER dbo.TR_citas_solicitudes_update_guard ON dbo.citas_solicitudes AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(id) OR UPDATE(centro_id) OR UPDATE(residente_id) OR UPDATE(vinculo_id) OR UPDATE(cuenta_id) OR UPDATE(equipo_codigo) OR UPDATE(modalidad_codigo)
       OR UPDATE(motivo_codigo) OR UPDATE(comentario) OR UPDATE(disponibilidad_dias) OR UPDATE(disponibilidad_franja) OR UPDATE(operacion_id) OR UPDATE(creada_en)
        THROW 50532, 'APPOINTMENT_REQUEST_IDENTITY_IMMUTABLE', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
         WHERE d.estado_codigo IN ('ACEPTADA', 'CANCELADA', 'CERRADA')
            OR (d.estado_codigo = 'PENDIENTE' AND i.estado_codigo NOT IN ('PROPUESTA', 'CANCELADA', 'CERRADA'))
            OR (d.estado_codigo = 'PROPUESTA' AND i.estado_codigo NOT IN ('ACEPTADA', 'PENDIENTE', 'CANCELADA', 'CERRADA')))
        THROW 50533, 'APPOINTMENT_REQUEST_TRANSITION_INVALID', 1;
END;
GO

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_solicitudes,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_solicitudes AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_solicitudes AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_solicitudes_cambios,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_solicitudes_cambios AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_solicitudes_cambios AFTER UPDATE;
GO
