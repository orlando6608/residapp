/*
 * Añade el escalado de un evento de Enfermería a Medicina (historia 5 de docs/historias-usuarios/enfermeria.md,
 * ENF-06/ENF-09; pantalla ENF-10 del wireframe) y lo deja legible desde Medicina (historia 1 de
 * docs/historias-usuarios/medicina.md, MED-01 a MED-03, en solo lectura).
 *
 * Escalar es la tercera salida de la decisión asistencial: se escala desde EN_VALORACION o desde
 * EN_SEGUIMIENTO y el evento pasa a ESCALADO_MEDICINA. Escalar no cierra el evento: su desenlace es de
 * Medicina, cuyo vertical (valoración médica y conducta) añadirá las transiciones que salgan de este estado.
 * La valoración de Enfermería se cierra al escalar (CERRADA e inmutable, TR_ve_guard), de modo que Medicina
 * la recibe exactamente como se escaló.
 *
 * El escalado solo guarda el motivo: la observación, el basal vigente, la valoración, las constantes y las
 * actuaciones ya viven en sus tablas y se leen de ahí. No hay resumen automático de ningún tipo.
 */

ALTER TABLE dbo.eventos_asistenciales DROP CONSTRAINT CK_ea_estado;
ALTER TABLE dbo.eventos_asistenciales ADD CONSTRAINT CK_ea_estado
    CHECK (estado_codigo IN ('PENDIENTE', 'EN_VALORACION', 'EN_SEGUIMIENTO', 'ESCALADO_MEDICINA', 'CERRADO'));
GO

-- Mismas reglas que en 0010, más EN_VALORACION/EN_SEGUIMIENTO -> ESCALADO_MEDICINA. Nada sale de
-- ESCALADO_MEDICINA ni de CERRADO.
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
           OR NOT ((d.estado_codigo IN ('PENDIENTE', 'EN_VALORACION') AND i.estado_codigo = 'EN_VALORACION')
                   OR (d.estado_codigo IN ('EN_VALORACION', 'EN_SEGUIMIENTO')
                       AND i.estado_codigo IN ('EN_SEGUIMIENTO', 'ESCALADO_MEDICINA', 'CERRADO'))))
        THROW 50310, 'CLINICAL_EVENT_TRANSITION_INVALID', 1;
END;
GO

CREATE TABLE dbo.escalados_medicina (
    id                        UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_esc PRIMARY KEY,
    evento_id                 UNIQUEIDENTIFIER NOT NULL,
    residente_id              UNIQUEIDENTIFIER NOT NULL,
    centro_id                 UNIQUEIDENTIFIER NOT NULL,
    motivo                    NVARCHAR(2000) NOT NULL,
    escalado_por_cuenta_id    UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_esc_escalado_por REFERENCES dbo.cuentas(id),
    escalado_en               DATETIME2(3) NOT NULL,
    CONSTRAINT FK_esc_evento FOREIGN KEY (evento_id, residente_id, centro_id) REFERENCES dbo.eventos_asistenciales(id, residente_id, centro_id),
    CONSTRAINT CK_esc_motivo CHECK (LEN(LTRIM(RTRIM(motivo))) > 0)
);
CREATE UNIQUE INDEX UX_esc_evento ON dbo.escalados_medicina (evento_id);
GO

CREATE TRIGGER dbo.TR_esc_immutable ON dbo.escalados_medicina INSTEAD OF UPDATE, DELETE AS
    THROW 50318, 'CLINICAL_EVENT_ESCALATION_IMMUTABLE', 1;
GO

/*
 * valoraciones_enfermeria solo tenía un índice por evento filtrado a BORRADOR (UX_ve_borrador, 0007), que
 * no sirve para cerrar la valoración (el UPDATE cambia justo la columna del filtro) ni para leerla ya
 * cerrada. Esas operaciones recorrían toda la tabla bloqueando filas de otros eventos, y dos cierres o
 * escalados simultáneos podían acabar en interbloqueo (visto en SQL Server con la suite en paralelo).
 */
CREATE INDEX IX_ve_evento ON dbo.valoraciones_enfermeria (evento_id);
GO
