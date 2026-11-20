/*
 * Hora prevista opcional junto a la fecha prevista (respuesta de CJ a la guía 1, prueba 10, 2026-11-19; decidido por Orlando el 2026-11-20;
 * lectura del DPO el 2026-11-20: adelante, sin cambio de tratamiento).
 *
 *  - hora_prevista TIME(0) NULL en las cinco tablas con fecha_prevista: seguimientos y seguimiento_acciones (Enfermería, 0010),
 *    indicaciones_medicas (0012), seguimientos_medicos y seguimiento_medico_acciones (0014). Es la hora local del centro (Madrid), sin zona.
 *  - No hay hora sin fecha. Como los CHECK de tipo de acción ya exigen fecha_prevista NULL en las acciones que no planifican, esa regla
 *    cubre también «sin hora» en ellas.
 *  - indicaciones_medicas admite UPDATE de estado y TR_im_guard compara a mano las columnas que no pueden cambiar: se vuelve a crear con
 *    hora_prevista dentro (si no, la hora podría reescribirse después de emitida la indicación).
 *  - Las demás tablas son INSTEAD OF UPDATE, DELETE: basta con añadir la columna. La matriz 0091 va por tabla y el catálogo de expurgo
 *    también: no cambian.
 *  - Qué significa «vencido» lo decide la aplicación: con hora, cuando pasa esa hora (hora de Madrid); sin hora, al día siguiente.
 */

ALTER TABLE dbo.seguimientos ADD hora_prevista TIME(0) NULL;
ALTER TABLE dbo.seguimiento_acciones ADD hora_prevista TIME(0) NULL;
ALTER TABLE dbo.indicaciones_medicas ADD hora_prevista TIME(0) NULL;
ALTER TABLE dbo.seguimientos_medicos ADD hora_prevista TIME(0) NULL;
ALTER TABLE dbo.seguimiento_medico_acciones ADD hora_prevista TIME(0) NULL;
GO

ALTER TABLE dbo.seguimientos ADD CONSTRAINT CK_seg_hora CHECK (hora_prevista IS NULL OR fecha_prevista IS NOT NULL);
ALTER TABLE dbo.seguimiento_acciones ADD CONSTRAINT CK_sa_hora CHECK (hora_prevista IS NULL OR fecha_prevista IS NOT NULL);
ALTER TABLE dbo.indicaciones_medicas ADD CONSTRAINT CK_im_hora CHECK (hora_prevista IS NULL OR fecha_prevista IS NOT NULL);
ALTER TABLE dbo.seguimientos_medicos ADD CONSTRAINT CK_segm_hora CHECK (hora_prevista IS NULL OR fecha_prevista IS NOT NULL);
ALTER TABLE dbo.seguimiento_medico_acciones ADD CONSTRAINT CK_sma_hora CHECK (hora_prevista IS NULL OR fecha_prevista IS NOT NULL);
GO

-- Igual que en 0012, con la hora prevista entre lo que no puede cambiar.
ALTER TRIGGER dbo.TR_im_guard ON dbo.indicaciones_medicas AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE i.evento_id <> d.evento_id OR i.residente_id <> d.residente_id OR i.centro_id <> d.centro_id
           OR i.texto <> d.texto OR ISNULL(i.fecha_prevista, '19000101') <> ISNULL(d.fecha_prevista, '19000101')
           OR (i.hora_prevista IS NULL AND d.hora_prevista IS NOT NULL) OR (i.hora_prevista IS NOT NULL AND d.hora_prevista IS NULL)
           OR i.hora_prevista <> d.hora_prevista
           OR ISNULL(i.criterio, N'') <> ISNULL(d.criterio, N'') OR ISNULL(i.informacion_adicional, N'') <> ISNULL(d.informacion_adicional, N'')
           OR i.emitida_por_cuenta_id <> d.emitida_por_cuenta_id OR i.emitida_en <> d.emitida_en
           OR i.revision <> d.revision + 1
           OR (d.leida_por_cuenta_id IS NOT NULL AND (i.leida_por_cuenta_id <> d.leida_por_cuenta_id OR i.leida_en <> d.leida_en))
           OR NOT ((d.estado_codigo = 'PENDIENTE_LECTURA' AND i.estado_codigo = 'LEIDA')
                   OR (d.estado_codigo = 'LEIDA' AND i.estado_codigo IN ('REALIZADA', 'NO_REALIZADA'))))
        THROW 50333, 'MEDICAL_INDICATION_TRANSITION_INVALID', 1;
END;
GO
