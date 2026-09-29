/*
 * Evento propio de Medicina (historia 7 de Medicina: MED-11 del PRD, pantalla MED-18). Medicina registra un
 * evento que observa directamente y sigue el mismo ciclo que un escalado desde la valoración médica
 * (docs/flujos-clinicos/valoracion-conducta-medicina.md, paso 6), sin simular un escalado desde Enfermería.
 *
 * Decisión de producto (2026-09-29): el evento nace ya en EN_VALORACION_MEDICA, con quien lo registra como
 * iniciador de la valoración médica, y no hay estado nuevo. Por eso TR_ea_transition_guard no cambia: solo
 * protege los UPDATE, y desde EN_VALORACION_MEDICA ya están permitidas todas las salidas médicas.
 *
 * - eventos_clinicos admite el perfil MEDICINA (lo anunciaba el comentario de CK_ec_profile en 0006).
 * - eventos_asistenciales admite el origen EVENTO_MEDICINA, con el mismo vínculo que EVENTO_ENFERMERIA.
 * - CK_ea_inicio: un evento de Medicina nunca tiene inicio de valoración de Enfermería y solo puede estar en
 *   los estados médicos o cerrado, así que nunca entra en las bandejas ni en la valoración de Enfermería. El
 *   resto de orígenes conserva la regla de 0007. CK_ea_inicio_medico (0015) ya exige el iniciador médico.
 */

ALTER TABLE dbo.eventos_clinicos DROP CONSTRAINT CK_ec_profile;
ALTER TABLE dbo.eventos_clinicos ADD CONSTRAINT CK_ec_profile CHECK (registrado_por_perfil IN ('ENFERMERIA', 'MEDICINA'));

ALTER TABLE dbo.eventos_asistenciales DROP CONSTRAINT CK_ea_origen;
ALTER TABLE dbo.eventos_asistenciales ADD CONSTRAINT CK_ea_origen CHECK (
    (origen_codigo = 'CAMBIO_AUXILIAR' AND cierre_id = id AND evento_clinico_id IS NULL)
    OR (origen_codigo IN ('EVENTO_ENFERMERIA', 'EVENTO_MEDICINA') AND evento_clinico_id = id AND cierre_id IS NULL));

ALTER TABLE dbo.eventos_asistenciales DROP CONSTRAINT CK_ea_inicio;
ALTER TABLE dbo.eventos_asistenciales ADD CONSTRAINT CK_ea_inicio CHECK (
    (origen_codigo = 'EVENTO_MEDICINA'
        AND valoracion_iniciada_por_cuenta_id IS NULL AND valoracion_iniciada_en IS NULL
        AND estado_codigo IN ('EN_VALORACION_MEDICA', 'CON_INDICACION_PENDIENTE', 'EN_SEGUIMIENTO_MEDICO',
                              'PROTOCOLO_URGENTE_MEDICO', 'CERRADO'))
    OR (origen_codigo <> 'EVENTO_MEDICINA'
        AND ((estado_codigo = 'PENDIENTE' AND valoracion_iniciada_por_cuenta_id IS NULL AND valoracion_iniciada_en IS NULL)
             OR (estado_codigo <> 'PENDIENTE' AND valoracion_iniciada_por_cuenta_id IS NOT NULL AND valoracion_iniciada_en IS NOT NULL))));
GO
