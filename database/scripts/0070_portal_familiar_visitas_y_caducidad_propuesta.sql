/*
 * Portal Familiar con las respuestas de CJ del 2026-10-25 (docs/pendientes-cj/portal-familiar-confirmaciones-cj y confirmaciones-tercera-tanda-cj).
 *
 *  - portal_familiar_visitas (tercera 6.3): la última vez que cada cuenta de familiar abrió a cada residente. Con ella el portal destaca las novedades
 *    publicadas desde entonces. Solo guarda la última visita: quién leyó qué ya queda en la auditoría (FAMILY_PORTAL_READ, CJ portal 3.1).
 *  - citas_solicitudes.propuesta_en y propuesta_caduca_en (portal 6.4): una propuesta del centro caduca a las 48 horas de hacerla o 24 horas antes de la
 *    cita, lo que llegue antes (AppointmentRequestRules.ProposalExpiresAt; la columna calculada repite la fórmula para las consultas). Después deja de
 *    reservar el hueco y Administración puede volver a proponer: el trigger admite PROPUESTA -> PROPUESTA. Que la propuesta anterior ya caducó lo
 *    comprueba la aplicación en su transacción, como el resto de la propuesta viva (0057).
 */

CREATE TABLE dbo.portal_familiar_visitas (
    centro_id                UNIQUEIDENTIFIER NOT NULL,
    cuenta_id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_pfv_cuenta REFERENCES dbo.cuentas(id),
    residente_id             UNIQUEIDENTIFIER NOT NULL,
    visto_en                 DATETIME2(3) NOT NULL,
    CONSTRAINT PK_portal_familiar_visitas PRIMARY KEY (cuenta_id, residente_id),
    CONSTRAINT FK_pfv_residente FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes(centro_id, id)
);
GO

ALTER TABLE dbo.citas_solicitudes ADD propuesta_en DATETIME2(3) NULL;
GO

-- Relleno de las propuestas que ya existen: cuándo se hicieron está en su último cambio PROPUESTA. El trigger de transiciones no admite tocar una
-- solicitud aceptada; se desactiva solo para el relleno.
DISABLE TRIGGER dbo.TR_citas_solicitudes_update_guard ON dbo.citas_solicitudes;
GO

UPDATE r SET propuesta_en = (
        SELECT TOP (1) c.ocurrido_en FROM dbo.citas_solicitudes_cambios c
         WHERE c.solicitud_id = r.id AND c.accion_codigo = 'PROPUESTA' ORDER BY c.numero DESC)
  FROM dbo.citas_solicitudes r
 WHERE r.estado_codigo IN ('PROPUESTA', 'ACEPTADA');
GO

ENABLE TRIGGER dbo.TR_citas_solicitudes_update_guard ON dbo.citas_solicitudes;
GO

ALTER TABLE dbo.citas_solicitudes DROP CONSTRAINT CK_citas_solicitudes_propuesta;
ALTER TABLE dbo.citas_solicitudes ADD CONSTRAINT CK_citas_solicitudes_propuesta CHECK (
    (estado_codigo IN ('PROPUESTA', 'ACEPTADA') AND propuesta_inicio_en IS NOT NULL AND propuesta_fin_en IS NOT NULL AND propuesta_fin_en > propuesta_inicio_en
        AND propuesta_en IS NOT NULL)
    OR (estado_codigo NOT IN ('PROPUESTA', 'ACEPTADA') AND propuesta_inicio_en IS NULL AND propuesta_fin_en IS NULL AND propuesta_en IS NULL));
ALTER TABLE dbo.citas_solicitudes ADD propuesta_caduca_en AS (
    CASE WHEN DATEADD(HOUR, 48, propuesta_en) < DATEADD(HOUR, -24, propuesta_inicio_en)
         THEN DATEADD(HOUR, 48, propuesta_en) ELSE DATEADD(HOUR, -24, propuesta_inicio_en) END) PERSISTED;
GO

-- Las propuestas que reservan hueco se buscan por equipo y caducidad (AppointmentSql.ReadTakenAsync, con FORCESEEK).
DROP INDEX IX_citas_solicitudes_propuesta ON dbo.citas_solicitudes;
CREATE INDEX IX_citas_solicitudes_propuesta ON dbo.citas_solicitudes (centro_id, equipo_codigo, propuesta_caduca_en)
    INCLUDE (propuesta_inicio_en, propuesta_fin_en) WHERE estado_codigo = 'PROPUESTA';
GO

/* De una solicitud solo cambian el estado, la propuesta y la cita; el residente, el vínculo, quien la envió, el equipo, la modalidad y lo que pidió
   no. PENDIENTE pasa a PROPUESTA, CANCELADA o CERRADA; PROPUESTA, a ACEPTADA, PENDIENTE (otro horario), PROPUESTA (otra propuesta, cuando la
   anterior caducó), CANCELADA o CERRADA; el resto son finales. */
CREATE OR ALTER TRIGGER dbo.TR_citas_solicitudes_update_guard ON dbo.citas_solicitudes AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(id) OR UPDATE(centro_id) OR UPDATE(residente_id) OR UPDATE(vinculo_id) OR UPDATE(cuenta_id) OR UPDATE(equipo_codigo) OR UPDATE(modalidad_codigo)
       OR UPDATE(motivo_codigo) OR UPDATE(comentario) OR UPDATE(disponibilidad_dias) OR UPDATE(disponibilidad_franja) OR UPDATE(operacion_id) OR UPDATE(creada_en)
        THROW 50532, 'APPOINTMENT_REQUEST_IDENTITY_IMMUTABLE', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
         WHERE d.estado_codigo IN ('ACEPTADA', 'CANCELADA', 'CERRADA')
            OR (d.estado_codigo = 'PENDIENTE' AND i.estado_codigo NOT IN ('PROPUESTA', 'CANCELADA', 'CERRADA'))
            OR (d.estado_codigo = 'PROPUESTA' AND i.estado_codigo NOT IN ('ACEPTADA', 'PENDIENTE', 'PROPUESTA', 'CANCELADA', 'CERRADA')))
        THROW 50533, 'APPOINTMENT_REQUEST_TRANSITION_INVALID', 1;
END;
GO

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.portal_familiar_visitas,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.portal_familiar_visitas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.portal_familiar_visitas AFTER UPDATE;
GO
