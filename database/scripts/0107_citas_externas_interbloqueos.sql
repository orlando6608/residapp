/*
 * Interbloqueos (error 1205) de las acciones de la familia en las citas externas, vistos en la CI (PR #347 el 2026-11-19 y PR #357 el 2026-11-20) y reproducidos en local con una base nueva y
 * sin datos («Cómo reproducir el CI en local», lecciones técnicas): cientos por vuelta, con el grafo de system_health sobre citas_externas_no_puedo (UX_cextr_numero) y
 * citas_externas_encargos (PK_cexta y UX_cexta_numero).
 *
 * Causa, en este script: los dos triggers de secuencia buscaban la fila anterior con un LEFT JOIN y, con las tablas casi vacías, el plan recorría la clave primaria en vez de buscar por
 * (cita, ámbito|vínculo, número): lecturas compartidas sobre PK_cexta contra la inserción de otra transacción (el mismo caso que 0056). WITH (FORCESEEK) obliga a buscar por clave.
 * Misma lógica y mismos códigos de error (50943, 50944, 50948); no cambia qué se guarda, quién lo ve, los permisos ni la seguridad por filas.
 *
 * La otra mitad de la causa no es de esquema: dos lecturas de SqlFamilyExternalAppointments.Encargos llevaban HOLDLOCK (bloqueos de rango sobre UX_cexta_numero y UX_cextr_numero, que
 * chocaban entre citas vecinas del índice). Ya no lo llevan: cada acción bloquea la fila de su cita (UPDLOCK) y el índice único decide quién gana.
 */

ALTER TRIGGER dbo.TR_cexta_reglas ON dbo.citas_externas_encargos AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
          JOIN dbo.citas_externas c ON c.centro_id = i.centro_id AND c.id = i.cita_id
          JOIN dbo.residentes_familiares link ON link.centro_id = i.centro_id AND link.id = i.vinculo_id
         WHERE link.residente_id <> c.residente_id)
        THROW 50944, 'EXTERNAL_APPOINTMENT_LINK_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i
          LEFT JOIN dbo.citas_externas_encargos previous WITH (FORCESEEK) ON previous.cita_id = i.cita_id AND previous.ambito_codigo = i.ambito_codigo AND previous.numero = i.numero - 1
         WHERE (i.numero = 1 AND i.accion_codigo <> 'ME_ENCARGO')
            OR (i.numero > 1 AND (previous.id IS NULL
                OR (i.accion_codigo = 'ME_ENCARGO' AND previous.accion_codigo = 'ME_ENCARGO')
                OR (i.accion_codigo IN ('SOLTAR', 'LIBERADO_AUTOMATICO') AND (previous.accion_codigo <> 'ME_ENCARGO' OR previous.vinculo_id <> i.vinculo_id)))))
        THROW 50943, 'EXTERNAL_APPOINTMENT_ARRANGEMENT_SEQUENCE_INVALID', 1;
END;
GO

ALTER TRIGGER dbo.TR_cextr_reglas ON dbo.citas_externas_no_puedo AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
          JOIN dbo.citas_externas c ON c.centro_id = i.centro_id AND c.id = i.cita_id
          JOIN dbo.residentes_familiares link ON link.centro_id = i.centro_id AND link.id = i.vinculo_id
         WHERE link.residente_id <> c.residente_id)
        THROW 50944, 'EXTERNAL_APPOINTMENT_LINK_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i
          LEFT JOIN dbo.citas_externas_no_puedo previous WITH (FORCESEEK) ON previous.cita_id = i.cita_id AND previous.vinculo_id = i.vinculo_id AND previous.numero = i.numero - 1
         WHERE (i.numero = 1 AND i.no_puede = 0)
            OR (i.numero > 1 AND (previous.id IS NULL OR previous.no_puede = i.no_puede)))
        THROW 50948, 'EXTERNAL_APPOINTMENT_REFUSAL_SEQUENCE_INVALID', 1;
END;
GO
