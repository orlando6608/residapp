/*
 * Historial, bloque 3 (historia 11 de Enfermería, 9 de Medicina): corrección y rectificación (COR-01/COR-02).
 *
 * Política provisional, decidida por el usuario (2026-09-30) a falta de la del centro y el responsable de
 * protección de datos (docs/producto/roadmap.md):
 *   - Solo se corrigen las dos valoraciones del evento, la de Enfermería y la médica. Nunca el basal, la
 *     observación original, las indicaciones, los seguimientos, el protocolo, el informe firmado ni la
 *     comunicación familiar.
 *   - Solo las corrige su autor: la cuenta que guardó la última versión ordinaria (valoraciones_*_versiones).
 *   - Dentro de la ventana (Correccion:VentanaHoras, 6 h, desde ese guardado), una versión corregida con motivo.
 *     La valoración vigente pasa a tener el contenido corregido y cada corrección queda aquí, inmutable. El
 *     original sigue en valoraciones_*_versiones.
 *   - Fuera de la ventana, una rectificación añadida (texto y motivo) que no cambia la valoración.
 *   - Solo cuando la valoración ya no se puede guardar de forma normal (el evento salió de su estado de
 *     valoración). Mientras se puede, se guarda como siempre.
 *
 * TR_ve_guard y TR_vm_guard siguen prohibiendo cambiar una valoración CERRADA, salvo que su nuevo contenido,
 * autor y hora coincidan con una corrección registrada de esa valoración: todo cambio de una valoración
 * cerrada deja su corrección trazable.
 */

-- Las FK compuestas garantizan que la corrección es del mismo evento que su valoración.
CREATE UNIQUE INDEX UX_ve_evento_scope ON dbo.valoraciones_enfermeria (id, evento_id);
CREATE UNIQUE INDEX UX_vm_evento_scope ON dbo.valoraciones_medicas (id, evento_id);
GO

CREATE TABLE dbo.valoraciones_enfermeria_correcciones (
    id                            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_vec PRIMARY KEY,
    valoracion_id                 UNIQUEIDENTIFIER NOT NULL,
    evento_id                     UNIQUEIDENTIFIER NOT NULL,
    hallazgos                     NVARCHAR(2000) NULL,
    valoracion                    NVARCHAR(2000) NULL,
    actuaciones                   NVARCHAR(2000) NULL,
    comunicaciones                NVARCHAR(2000) NULL,
    resultado                     NVARCHAR(2000) NULL,
    temperatura_celsius           DECIMAL(4, 1) NULL,
    tension_sistolica_mmhg        SMALLINT NULL,
    tension_diastolica_mmhg       SMALLINT NULL,
    frecuencia_cardiaca_lpm       SMALLINT NULL,
    frecuencia_respiratoria_rpm   SMALLINT NULL,
    saturacion_o2_pct             SMALLINT NULL,
    soporte_respiratorio_codigo   NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NULL,
    flujo_o2_lpm                  DECIMAL(4, 1) NULL,
    glucemia_mg_dl                SMALLINT NULL,
    otra_constante_nombre         NVARCHAR(100) NULL,
    otra_constante_valor          NVARCHAR(50) NULL,
    otra_constante_unidad         NVARCHAR(30) NULL,
    motivo                        NVARCHAR(500) NOT NULL,
    corregido_por_cuenta_id       UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_vec_corregido_por REFERENCES dbo.cuentas(id),
    corregido_en                  DATETIME2(3) NOT NULL,
    CONSTRAINT FK_vec_valoracion FOREIGN KEY (valoracion_id, evento_id) REFERENCES dbo.valoraciones_enfermeria (id, evento_id),
    CONSTRAINT CK_vec_motivo CHECK (LEN(LTRIM(RTRIM(motivo))) > 0)
);
CREATE INDEX IX_vec_evento ON dbo.valoraciones_enfermeria_correcciones (evento_id);
GO

CREATE TRIGGER dbo.TR_vec_immutable ON dbo.valoraciones_enfermeria_correcciones INSTEAD OF UPDATE, DELETE AS
    THROW 50380, 'NURSING_ASSESSMENT_CORRECTION_IMMUTABLE', 1;
GO

CREATE TABLE dbo.valoraciones_medicas_correcciones (
    id                            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_vmc PRIMARY KEY,
    valoracion_id                 UNIQUEIDENTIFIER NOT NULL,
    evento_id                     UNIQUEIDENTIFIER NOT NULL,
    hallazgos_exploracion         NVARCHAR(2000) NULL,
    valoracion                    NVARCHAR(2000) NULL,
    actuaciones                   NVARCHAR(2000) NULL,
    temperatura_celsius           DECIMAL(4, 1) NULL,
    tension_sistolica_mmhg        SMALLINT NULL,
    tension_diastolica_mmhg       SMALLINT NULL,
    frecuencia_cardiaca_lpm       SMALLINT NULL,
    frecuencia_respiratoria_rpm   SMALLINT NULL,
    saturacion_o2_pct             SMALLINT NULL,
    soporte_respiratorio_codigo   NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NULL,
    flujo_o2_lpm                  DECIMAL(4, 1) NULL,
    glucemia_mg_dl                SMALLINT NULL,
    otra_constante_nombre         NVARCHAR(100) NULL,
    otra_constante_valor          NVARCHAR(50) NULL,
    otra_constante_unidad         NVARCHAR(30) NULL,
    motivo                        NVARCHAR(500) NOT NULL,
    corregido_por_cuenta_id       UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_vmc_corregido_por REFERENCES dbo.cuentas(id),
    corregido_en                  DATETIME2(3) NOT NULL,
    CONSTRAINT FK_vmc_valoracion FOREIGN KEY (valoracion_id, evento_id) REFERENCES dbo.valoraciones_medicas (id, evento_id),
    CONSTRAINT CK_vmc_motivo CHECK (LEN(LTRIM(RTRIM(motivo))) > 0)
);
CREATE INDEX IX_vmc_evento ON dbo.valoraciones_medicas_correcciones (evento_id);
GO

CREATE TRIGGER dbo.TR_vmc_immutable ON dbo.valoraciones_medicas_correcciones INSTEAD OF UPDATE, DELETE AS
    THROW 50381, 'MEDICAL_ASSESSMENT_CORRECTION_IMMUTABLE', 1;
GO

-- perfil_codigo dice qué valoración se rectifica: la de Enfermería o la médica del evento.
CREATE TABLE dbo.valoraciones_rectificaciones (
    id                        UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_vr PRIMARY KEY,
    evento_id                 UNIQUEIDENTIFIER NOT NULL,
    residente_id              UNIQUEIDENTIFIER NOT NULL,
    centro_id                 UNIQUEIDENTIFIER NOT NULL,
    perfil_codigo             NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    texto                     NVARCHAR(2000) NOT NULL,
    motivo                    NVARCHAR(500) NOT NULL,
    registrado_por_cuenta_id  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_vr_registrado_por REFERENCES dbo.cuentas(id),
    registrado_en             DATETIME2(3) NOT NULL,
    CONSTRAINT FK_vr_evento FOREIGN KEY (evento_id, residente_id, centro_id) REFERENCES dbo.eventos_asistenciales (id, residente_id, centro_id),
    CONSTRAINT CK_vr_perfil CHECK (perfil_codigo IN ('ENFERMERIA', 'MEDICINA')),
    CONSTRAINT CK_vr_texto CHECK (LEN(LTRIM(RTRIM(texto))) > 0),
    CONSTRAINT CK_vr_motivo CHECK (LEN(LTRIM(RTRIM(motivo))) > 0)
);
CREATE INDEX IX_vr_evento ON dbo.valoraciones_rectificaciones (evento_id, perfil_codigo);
GO

CREATE TRIGGER dbo.TR_vr_immutable ON dbo.valoraciones_rectificaciones INSTEAD OF UPDATE, DELETE AS
    THROW 50382, 'ASSESSMENT_RECTIFICATION_IMMUTABLE', 1;
GO

-- Mismas reglas que en 0007, más la corrección de una valoración CERRADA. INTERSECT compara los NULL como
-- iguales: el contenido nuevo tiene que ser exactamente el de una corrección de esa cuenta a esa hora.
ALTER TRIGGER dbo.TR_ve_guard ON dbo.valoraciones_enfermeria AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE i.evento_id <> d.evento_id OR i.residente_id <> d.residente_id OR i.centro_id <> d.centro_id
           OR i.creado_por_cuenta_id <> d.creado_por_cuenta_id OR i.creado_en <> d.creado_en
           OR (d.estado_codigo <> 'BORRADOR'
               AND (i.estado_codigo <> d.estado_codigo OR i.actualizado_en = d.actualizado_en
                    OR NOT EXISTS (
                        SELECT i.hallazgos, i.valoracion, i.actuaciones, i.comunicaciones, i.resultado,
                               i.temperatura_celsius, i.tension_sistolica_mmhg, i.tension_diastolica_mmhg,
                               i.frecuencia_cardiaca_lpm, i.frecuencia_respiratoria_rpm, i.saturacion_o2_pct,
                               i.soporte_respiratorio_codigo, i.flujo_o2_lpm, i.glucemia_mg_dl,
                               i.otra_constante_nombre, i.otra_constante_valor, i.otra_constante_unidad,
                               i.actualizado_por_cuenta_id, i.actualizado_en
                        INTERSECT
                        SELECT c.hallazgos, c.valoracion, c.actuaciones, c.comunicaciones, c.resultado,
                               c.temperatura_celsius, c.tension_sistolica_mmhg, c.tension_diastolica_mmhg,
                               c.frecuencia_cardiaca_lpm, c.frecuencia_respiratoria_rpm, c.saturacion_o2_pct,
                               c.soporte_respiratorio_codigo, c.flujo_o2_lpm, c.glucemia_mg_dl,
                               c.otra_constante_nombre, c.otra_constante_valor, c.otra_constante_unidad,
                               c.corregido_por_cuenta_id, c.corregido_en
                          FROM dbo.valoraciones_enfermeria_correcciones c
                         WHERE c.valoracion_id = i.id))))
        THROW 50312, 'NURSING_ASSESSMENT_IMMUTABLE', 1;
END;
GO

-- Mismas reglas que en 0012, más la corrección de una valoración médica CERRADA.
ALTER TRIGGER dbo.TR_vm_guard ON dbo.valoraciones_medicas AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE i.evento_id <> d.evento_id OR i.residente_id <> d.residente_id OR i.centro_id <> d.centro_id
           OR i.creado_por_cuenta_id <> d.creado_por_cuenta_id OR i.creado_en <> d.creado_en
           OR (d.estado_codigo <> 'BORRADOR'
               AND (i.estado_codigo <> d.estado_codigo OR i.actualizado_en = d.actualizado_en
                    OR NOT EXISTS (
                        SELECT i.hallazgos_exploracion, i.valoracion, i.actuaciones,
                               i.temperatura_celsius, i.tension_sistolica_mmhg, i.tension_diastolica_mmhg,
                               i.frecuencia_cardiaca_lpm, i.frecuencia_respiratoria_rpm, i.saturacion_o2_pct,
                               i.soporte_respiratorio_codigo, i.flujo_o2_lpm, i.glucemia_mg_dl,
                               i.otra_constante_nombre, i.otra_constante_valor, i.otra_constante_unidad,
                               i.actualizado_por_cuenta_id, i.actualizado_en
                        INTERSECT
                        SELECT c.hallazgos_exploracion, c.valoracion, c.actuaciones,
                               c.temperatura_celsius, c.tension_sistolica_mmhg, c.tension_diastolica_mmhg,
                               c.frecuencia_cardiaca_lpm, c.frecuencia_respiratoria_rpm, c.saturacion_o2_pct,
                               c.soporte_respiratorio_codigo, c.flujo_o2_lpm, c.glucemia_mg_dl,
                               c.otra_constante_nombre, c.otra_constante_valor, c.otra_constante_unidad,
                               c.corregido_por_cuenta_id, c.corregido_en
                          FROM dbo.valoraciones_medicas_correcciones c
                         WHERE c.valoracion_id = i.id))))
        THROW 50330, 'MEDICAL_ASSESSMENT_IMMUTABLE', 1;
END;
GO
