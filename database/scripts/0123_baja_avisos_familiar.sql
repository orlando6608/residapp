/*
 * Baja de avisos por familiar (ADR 0012, requisito 5; decisión de Orlando, 2026-11-26; lectura de privacidad, borrador por validar, en
 * docs/privacidad/codigo-acceso-familiar-por-correo.md, apartado «PR 3: baja de avisos»).
 *
 *   1. Dos columnas en dbo.familiares (la baja es de la persona en ese centro, para todos sus residentes): desde cuándo y qué cuenta de Administración la anotó. Ambas nulas
 *      o ambas con valor. No se guarda el motivo, el canal ni quién la pidió. El historial queda en dbo.eventos_auditoria. Ningún disparador la borra: sobrevive a cambiar,
 *      quitar o confirmar el correo. Nadie las rellena aquí: nadie queda de baja por este script.
 *   2. Anonimizar a un familiar la borra: CK_fam_anonimizado (0087) lo exige.
 *   3. La tarea programada (rol residapp_tareas, 0083) lee la fecha para comprobarla justo antes de enviar; no lee quién la anotó.
 *   4. Citas externas (0105): una persona de baja no recibe el aviso (BAJA_DE_AVISOS) y, si se había encargado, no cuenta como alcanzable, para que el aviso llegue a los
 *      demás. El resto del procedimiento es el de 0105.
 * No es idempotente: lo aplica una sola vez database/aplicar-scripts.sh.
 */

SET XACT_ABORT ON;
BEGIN TRANSACTION;

ALTER TABLE dbo.familiares ADD
    avisos_baja_en            DATETIME2(3) NULL,
    avisos_baja_por_cuenta_id UNIQUEIDENTIFIER NULL CONSTRAINT FK_fam_avisos_baja_por REFERENCES dbo.cuentas(id);
GO

ALTER TABLE dbo.familiares ADD CONSTRAINT CK_fam_avisos_baja CHECK (
    (avisos_baja_en IS NULL AND avisos_baja_por_cuenta_id IS NULL)
    OR (avisos_baja_en IS NOT NULL AND avisos_baja_por_cuenta_id IS NOT NULL));
GO

ALTER TABLE dbo.familiares DROP CONSTRAINT CK_fam_anonimizado;
GO
ALTER TABLE dbo.familiares ADD CONSTRAINT CK_fam_anonimizado CHECK (
    (anonimizado_en IS NULL AND anonimizado_por_cuenta_id IS NULL AND anonimizado_motivo_codigo IS NULL)
    OR (anonimizado_en IS NOT NULL AND anonimizado_por_cuenta_id IS NOT NULL
        AND anonimizado_motivo_codigo IN ('PETICION_DEL_INTERESADO', 'PETICION_DE_REPRESENTANTE', 'PETICION_DE_ALLEGADOS_O_HEREDEROS', 'INICIATIVA_DEL_CENTRO')
        AND nombre_visible = N'Persona anonimizada' AND telefono = N'-' AND correo IS NULL
        AND correo_confirmado_en IS NULL AND correo_confirmado_por_cuenta_id IS NULL
        AND avisos_baja_en IS NULL AND avisos_baja_por_cuenta_id IS NULL));
GO

IF DATABASE_PRINCIPAL_ID(N'residapp_tareas') IS NOT NULL
    GRANT SELECT (avisos_baja_en) ON dbo.familiares TO residapp_tareas;
GO

ALTER PROCEDURE dbo.calcular_avisos_citas_externas
    @hoy DATE,
    @cita UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    CREATE TABLE #candidatos (
        centro_id UNIQUEIDENTIFIER NOT NULL, cita_id UNIQUEIDENTIFIER NOT NULL, residente_id UNIQUEIDENTIFIER NOT NULL, fecha DATE NOT NULL, tramo NVARCHAR(2) COLLATE Latin1_General_100_BIN2 NOT NULL,
        transporte_pendiente_familia BIT NOT NULL, acompanante_pendiente_familia BIT NOT NULL,
        vinculo_id UNIQUEIDENTIFIER NOT NULL, familiar_id UNIQUEIDENTIFIER NOT NULL, correo NVARCHAR(254) NULL, correo_confirmado_en DATETIME2(3) NULL, tiene_acceso BIT NOT NULL, baja_general BIT NOT NULL);

    INSERT INTO #candidatos
    SELECT appointment.centro_id, appointment.id, appointment.residente_id, appointment.fecha, step.tramo,
           CASE WHEN appointment.transporte_modo_codigo = 'FAMILIA' AND appointment.transporte_estado_codigo = 'PENDIENTE' THEN 1 ELSE 0 END,
           CASE WHEN appointment.acompanante_modo_codigo = 'FAMILIAR' AND appointment.acompanante_estado_codigo = 'PENDIENTE' THEN 1 ELSE 0 END,
           link.id, link.familiar_id, fam.correo, fam.correo_confirmado_en, CASE WHEN access.vinculo_id IS NULL THEN 0 ELSE 1 END,
           CASE WHEN fam.avisos_baja_en IS NULL THEN 0 ELSE 1 END
      FROM dbo.citas_externas appointment
      JOIN (SELECT CAST('7D' AS NVARCHAR(2)) COLLATE Latin1_General_100_BIN2 AS tramo, DATEADD(DAY, 7, @hoy) AS fecha
            UNION ALL SELECT '1D', DATEADD(DAY, 1, @hoy) UNION ALL SELECT '2D', DATEADD(DAY, 2, @hoy)) step ON step.fecha = appointment.fecha
      JOIN dbo.residentes_familiares link ON link.centro_id = appointment.centro_id AND link.residente_id = appointment.residente_id AND link.desvinculado_en IS NULL
      JOIN dbo.familiares fam ON fam.id = link.familiar_id AND fam.centro_id = link.centro_id
      OUTER APPLY (SELECT a.vinculo_id FROM dbo.fn_familiares_con_acceso(link.id, appointment.fecha) a) access
     WHERE appointment.estado_codigo IN ('PROGRAMADA', 'CONFIRMADA_HOSPITAL') AND appointment.visibilidad_familia_codigo <> 'OCULTA'
       AND (@cita IS NULL OR appointment.id = @cita);

    -- Quién tiene hoy cada parte (una sola vez) y, de ellos, quién puede recibir el correo: acceso a la fecha de la cita, dirección confirmada y sin baja (ni la general ni la de citas externas).
    CREATE TABLE #encargos (cita_id UNIQUEIDENTIFIER NOT NULL, ambito_codigo NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL, vinculo_id UNIQUEIDENTIFIER NOT NULL, alcanzable BIT NOT NULL);
    -- Primero los encargos vigentes (pocos), y solo después se mira el acceso de cada uno: unidos en una sola consulta, el plan calculaba el acceso de todo el sistema.
    SELECT held.cita_id, held.centro_id, held.ambito_codigo, held.vinculo_id INTO #vigentes FROM dbo.fn_citas_externas_encargos_vigentes(@hoy) held;
    INSERT INTO #encargos
    SELECT held.cita_id, held.ambito_codigo, held.vinculo_id,
           CASE WHEN reachable.vinculo_id IS NOT NULL AND reachable.correo IS NOT NULL AND reachable.correo_confirmado_en IS NOT NULL
                 AND NOT EXISTS (SELECT 1 FROM dbo.familiares opted WHERE opted.id = reachable.familiar_id AND opted.centro_id = reachable.centro_id AND opted.avisos_baja_en IS NOT NULL)
                 AND NOT EXISTS (SELECT 1 FROM dbo.citas_externas_correo_familiar opt_out
                                  WHERE opt_out.centro_id = reachable.centro_id AND opt_out.familiar_id = reachable.familiar_id AND opt_out.sin_avisos = 1
                                    AND opt_out.numero = (SELECT MAX(latest.numero) FROM dbo.citas_externas_correo_familiar latest WHERE latest.centro_id = opt_out.centro_id AND latest.familiar_id = opt_out.familiar_id))
                THEN 1 ELSE 0 END
      FROM #vigentes held
      JOIN dbo.citas_externas held_appointment ON held_appointment.id = held.cita_id AND held_appointment.centro_id = held.centro_id
      OUTER APPLY dbo.fn_familiares_con_acceso(held.vinculo_id, held_appointment.fecha) reachable
     WHERE @cita IS NULL OR held.cita_id = @cita
    OPTION (RECOMPILE);

    SELECT c.centro_id AS centro_id, c.cita_id AS cita_id, c.fecha AS fecha, c.tramo AS tramo, c.vinculo_id AS vinculo_id, c.familiar_id AS familiar_id,
           c.correo AS correo, c.correo_confirmado_en AS correo_confirmado_en,
           CAST(CASE
               WHEN NOT EXISTS (SELECT 1 FROM dbo.citas_externas_familia_centro center_switch
                                 WHERE center_switch.centro_id = c.centro_id AND center_switch.familia_ve = 1
                                   AND center_switch.numero = (SELECT MAX(latest.numero) FROM dbo.citas_externas_familia_centro latest WHERE latest.centro_id = center_switch.centro_id))
                 OR EXISTS (SELECT 1 FROM dbo.citas_externas_familia_residente resident_switch
                             WHERE resident_switch.centro_id = c.centro_id AND resident_switch.residente_id = c.residente_id AND resident_switch.familia_no_ve = 1
                               AND resident_switch.numero = (SELECT MAX(latest.numero) FROM dbo.citas_externas_familia_residente latest
                                                              WHERE latest.centro_id = resident_switch.centro_id AND latest.residente_id = resident_switch.residente_id))
                   THEN 'FAMILIA_NO_VE'
               WHEN NOT EXISTS (SELECT 1 FROM dbo.citas_externas_correo_centro mail_switch
                                 WHERE mail_switch.centro_id = c.centro_id AND mail_switch.correo_activo = 1
                                   AND mail_switch.numero = (SELECT MAX(latest.numero) FROM dbo.citas_externas_correo_centro latest WHERE latest.centro_id = mail_switch.centro_id))
                   THEN 'AVISOS_DESACTIVADOS'
               WHEN c.tiene_acceso = 0 THEN 'SIN_ACCESO'
               WHEN c.baja_general = 1 OR EXISTS (SELECT 1 FROM dbo.citas_externas_correo_familiar opt_out
                             WHERE opt_out.centro_id = c.centro_id AND opt_out.familiar_id = c.familiar_id AND opt_out.sin_avisos = 1
                               AND opt_out.numero = (SELECT MAX(latest.numero) FROM dbo.citas_externas_correo_familiar latest WHERE latest.centro_id = opt_out.centro_id AND latest.familiar_id = opt_out.familiar_id))
                   THEN 'BAJA_DE_AVISOS'
               -- A 7 días y la víspera: a quien tiene un encargo y PUEDE recibir el correo; si nadie lo tiene (o quien lo tiene no puede), a todos.
               WHEN c.tramo IN ('7D', '1D') AND EXISTS (SELECT 1 FROM #encargos e WHERE e.cita_id = c.cita_id AND e.alcanzable = 1)
                    AND NOT EXISTS (SELECT 1 FROM #encargos e WHERE e.cita_id = c.cita_id AND e.vinculo_id = c.vinculo_id)
                   THEN 'YA_CUBIERTA'
               -- A 2 días: solo si la familia aún tiene algo que cubrir, y no a quien ya se encargó.
               WHEN c.tramo = '2D'
                    AND (EXISTS (SELECT 1 FROM #encargos e WHERE e.cita_id = c.cita_id AND e.vinculo_id = c.vinculo_id)
                         OR NOT ((c.transporte_pendiente_familia = 1 AND NOT EXISTS (SELECT 1 FROM #encargos e WHERE e.cita_id = c.cita_id AND e.ambito_codigo = 'TRANSPORTE'))
                              OR (c.acompanante_pendiente_familia = 1 AND NOT EXISTS (SELECT 1 FROM #encargos e WHERE e.cita_id = c.cita_id AND e.ambito_codigo = 'ACOMPANANTE'))))
                   THEN 'YA_CUBIERTA'
               WHEN c.tramo = '2D'
                    AND EXISTS (SELECT 1 FROM dbo.citas_externas_no_puedo refusal
                                 WHERE refusal.cita_id = c.cita_id AND refusal.vinculo_id = c.vinculo_id AND refusal.no_puede = 1
                                   AND refusal.numero = (SELECT MAX(latest.numero) FROM dbo.citas_externas_no_puedo latest WHERE latest.cita_id = refusal.cita_id AND latest.vinculo_id = refusal.vinculo_id))
                   THEN 'NO_PUEDE'
               ELSE NULL
           END AS NVARCHAR(24)) AS motivo_codigo
      FROM #candidatos c;
END;
GO

COMMIT TRANSACTION;
GO
