/*
 * Cierre médico del evento (historia 4 de docs/historias-usuarios/medicina.md, MED-15 a MED-17).
 *
 * Medicina cierra desde EN_VALORACION_MEDICA (con la valoración médica guardada) o desde
 * CON_INDICACION_PENDIENTE, con el mismo cierre idempotente y la misma decisión de comunicación familiar que
 * Enfermería (0009: CERRADO, cerrado_por_cuenta_id, cerrado_en, comunicacion_familiar_codigo y
 * comunicaciones_familiares). Enfermería no hace un segundo cierre: un evento escalado solo sale de
 * Medicina, así que "cerrado por Medicina" es un evento CERRADO con escalado.
 *
 * Decisión de producto (2026-09-28): se puede cerrar con indicaciones todavía pendientes. La pantalla de
 * cierre las muestra y siguen en la bandeja de Enfermería hasta resolverse (no caducan), por eso este script
 * no toca dbo.indicaciones_medicas.
 */

-- Mismas reglas que en 0012, más EN_VALORACION_MEDICA / CON_INDICACION_PENDIENTE -> CERRADO.
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
                       AND i.estado_codigo IN ('EN_SEGUIMIENTO', 'ESCALADO_MEDICINA', 'CERRADO'))
                   OR (d.estado_codigo IN ('ESCALADO_MEDICINA', 'EN_VALORACION_MEDICA') AND i.estado_codigo = 'EN_VALORACION_MEDICA')
                   OR (d.estado_codigo IN ('EN_VALORACION_MEDICA', 'CON_INDICACION_PENDIENTE')
                       AND i.estado_codigo IN ('CON_INDICACION_PENDIENTE', 'CERRADO'))))
        THROW 50310, 'CLINICAL_EVENT_TRANSITION_INVALID', 1;
END;
GO
