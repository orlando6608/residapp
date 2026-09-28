/*
 * Rangos de referencia de constantes por centro, para el aviso visual de la valoración de Enfermería
 * (ENF-05). Decisión de producto (2026-09-28): el aviso es solo visual y no bloquea el guardado; no cambia
 * la clasificación, el orden de las bandejas ni el desenlace del evento (docs/producto/alcance.md excluye
 * el triaje y las recomendaciones automáticas; wireframe ENF-06: "el sistema no decide clínicamente ni
 * infiere prioridad").
 *
 * Los valores los fija CJ (responsable clínico del producto) mediante un script versionado, no desde
 * ninguna pantalla: los wireframes de Administración (ADM-29) y Dirección (DIR-11) prohíben expresamente
 * modificar umbrales clínicos. Este script solo crea la tabla, sin valores: un centro sin rangos
 * configurados no muestra ningún aviso. Los rangos individuales por residente quedan para una segunda fase.
 *
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
