/*
 * Rangos de referencia de constantes por centro, para el aviso visual de la valoración de Enfermería
 * (ENF-05). Decisiones de producto (2026-09-28):
 *  - El aviso es solo visual y no bloquea el guardado; no cambia la clasificación, el orden de las bandejas
 *    ni el desenlace del evento (docs/producto/alcance.md excluye el triaje y las recomendaciones
 *    automáticas; wireframe ENF-06: "el sistema no decide clínicamente ni infiere prioridad").
 *  - Los rangos se fijan desde una pantalla propia con el permiso REFERENCE_RANGES_MANAGE, que solo puede
 *    concederse a perfiles clínicos (MEDICINA o DIRECCION_CLINICA) y nunca a Administración: el wireframe
 *    ADM-29 prohíbe a Administración modificar umbrales clínicos. CJ decide a quién se concede en cada centro.
 *  - Todo cambio queda en un historial inmutable (quién, con qué perfil, cuándo, valor anterior y nuevo).
 *  - Los rangos individuales por residente quedan para una segunda fase.
 *
 * Este script no siembra ningún valor: un centro sin rangos configurados no muestra ningún aviso.
 * Cada constante admite un mínimo, un máximo o ambos. La tensión arterial se configura por separado
 * (sistólica y diastólica). El flujo de O2 y la "otra constante" libre no tienen rango.
 */

CREATE TABLE dbo.rangos_referencia_constantes (
    centro_id         UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_rrc_centro REFERENCES dbo.centros(id),
    constante_codigo  NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    minimo            DECIMAL(6, 1) NULL,
    maximo            DECIMAL(6, 1) NULL,
    CONSTRAINT PK_rrc PRIMARY KEY (centro_id, constante_codigo),
    CONSTRAINT CK_rrc_constante CHECK (constante_codigo IN (
        'TEMPERATURA', 'TENSION_SISTOLICA', 'TENSION_DIASTOLICA', 'FRECUENCIA_CARDIACA',
        'FRECUENCIA_RESPIRATORIA', 'SATURACION_O2', 'GLUCEMIA')),
    CONSTRAINT CK_rrc_limites CHECK (
        (minimo IS NOT NULL OR maximo IS NOT NULL)
        AND (minimo IS NULL OR maximo IS NULL OR minimo < maximo))
);
GO

-- Historial append-only: una fila por constante que cambia. Nuevo mínimo y máximo a NULL significa que
-- se quitó el rango; anterior mínimo y máximo a NULL, que no había ninguno.
CREATE TABLE dbo.rangos_referencia_constantes_historial (
    id                      UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_rrch PRIMARY KEY,
    centro_id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_rrch_centro REFERENCES dbo.centros(id),
    constante_codigo        NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    minimo_anterior         DECIMAL(6, 1) NULL,
    maximo_anterior         DECIMAL(6, 1) NULL,
    minimo_nuevo            DECIMAL(6, 1) NULL,
    maximo_nuevo            DECIMAL(6, 1) NULL,
    cambiado_por_cuenta_id  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_rrch_cuenta REFERENCES dbo.cuentas(id),
    cambiado_por_perfil     NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    cambiado_en             DATETIME2(3) NOT NULL,
    CONSTRAINT CK_rrch_constante CHECK (constante_codigo IN (
        'TEMPERATURA', 'TENSION_SISTOLICA', 'TENSION_DIASTOLICA', 'FRECUENCIA_CARDIACA',
        'FRECUENCIA_RESPIRATORIA', 'SATURACION_O2', 'GLUCEMIA')),
    CONSTRAINT CK_rrch_perfil CHECK (cambiado_por_perfil IN ('MEDICINA', 'DIRECCION_CLINICA'))
);
CREATE INDEX IX_rrch_centro_fecha ON dbo.rangos_referencia_constantes_historial (centro_id, cambiado_en);
GO
CREATE TRIGGER dbo.TR_rrch_immutable ON dbo.rangos_referencia_constantes_historial INSTEAD OF UPDATE, DELETE AS
    THROW 50320, 'REFERENCE_RANGE_HISTORY_IMMUTABLE', 1;
GO

ALTER TABLE dbo.permisos_perfil DROP CONSTRAINT CK_pp_code;
ALTER TABLE dbo.permisos_perfil ADD CONSTRAINT CK_pp_code CHECK (permiso_codigo IN
    ('RESIDENT_IDENTITY_CREATE', 'BASELINE_INITIAL_COMPLETE', 'BASELINE_REEVALUATE',
     'BASELINE_DRAFT_CONTRIBUTE', 'CLINICAL_DETAIL_READ', 'REFERENCE_RANGES_MANAGE'));
GO

-- REFERENCE_RANGES_MANAGE solo para perfiles clínicos: nunca Administración (ADM-29), ni perfiles que no
-- tienen responsabilidad clínica sobre umbrales.
CREATE TRIGGER dbo.TR_pp_reference_ranges_profile ON dbo.permisos_perfil AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
          JOIN dbo.ambitos_perfil p ON p.id = i.ambito_perfil_id
         WHERE i.permiso_codigo = 'REFERENCE_RANGES_MANAGE' AND p.perfil_codigo NOT IN ('MEDICINA', 'DIRECCION_CLINICA'))
        THROW 50321, 'REFERENCE_RANGES_PERMISSION_PROFILE_INVALID', 1;
END;
GO
