/*
 * Añade "Registrar un evento observado por Enfermería" (ENF-16, grupo E3 del plan de implementación),
 * en su alcance mínimo: solo registrar y guardar el evento con estado inicial PENDIENTE. Las bandejas
 * (E4), la valoración (E5) y las cuatro salidas de la decisión asistencial (E6/E7) llegan en grupos
 * posteriores y añadirán sus propias columnas/estados de forma aditiva, igual que 0003->0004 hizo con
 * el cierre cotidiano del Auxiliar.
 *
 * dbo.eventos_clinicos es una entidad nueva, deliberadamente distinta de dbo.cierres_cotidianos_residente
 * (que sigue siendo exclusivamente del Auxiliar, CK_ccr_profile = 'AUXILIAR'): ENF-16 exige explícitamente
 * "no simula autoría de Auxiliar ni se reenvía artificialmente a Enfermería"
 * (docs/bocetos-pantallas/wireframes-funcionales/enfermeria.md), así que un evento propio de Enfermería no
 * puede aparentar venir de Auxiliar compartiendo esa tabla. Append-only, mismo criterio que el resto del
 * esquema clínico: la observación original de un evento no se edita una vez registrada
 * (docs/flujos-clinicos/valoracion-escalado-enfermeria.md, "Reglas de negocio").
 */

ALTER TABLE dbo.operaciones_idempotencia DROP CONSTRAINT CK_idem_action;
ALTER TABLE dbo.operaciones_idempotencia ADD CONSTRAINT CK_idem_action
    CHECK (accion_codigo IN (
        'RESIDENT_CREATE', 'BASELINE_SIGN', 'CLINICAL_DETAIL_READ',
        'DAILY_CLOSURE_NO_CHANGE', 'DAILY_CLOSURE_NOT_ASSESSABLE', 'DAILY_CLOSURE_CHANGE_REPORTED',
        'BASELINE_DRAFT_CREATE', 'CLINICAL_EVENT_REGISTER'));
GO

CREATE TABLE dbo.eventos_clinicos (
    id                          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ec PRIMARY KEY,
    residente_id                UNIQUEIDENTIFIER NOT NULL,
    centro_id                   UNIQUEIDENTIFIER NOT NULL,
    unidad_id                   UNIQUEIDENTIFIER NOT NULL,
    estado_codigo               NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_ec_estado DEFAULT 'PENDIENTE',
    observacion                 NVARCHAR(2000) NOT NULL,
    clasificacion_codigo        NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    datos_clinicos_pertinentes  NVARCHAR(2000) NULL,
    registrado_por_cuenta_id    UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_ec_account REFERENCES dbo.cuentas(id),
    registrado_por_perfil       NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    ocurrido_en                 DATETIME2(3) NOT NULL,
    CONSTRAINT FK_ec_resident FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes(centro_id, id),
    CONSTRAINT FK_ec_unit FOREIGN KEY (centro_id, unidad_id) REFERENCES dbo.unidades(centro_id, id),
    -- Solo Enfermería puede registrar su propio evento en este alcance mínimo; Medicina (MED-18, "igual que
    -- ENF-16, para Medicina") extenderá este CHECK cuando llegue ese grupo, igual que CK_ccr_type se fue
    -- extendiendo por grupo.
    CONSTRAINT CK_ec_profile CHECK (registrado_por_perfil = 'ENFERMERIA'),
    -- Un único estado posible por ahora: E4/E5/E6 añadirán las transiciones (EN_VALORACION, EN_SEGUIMIENTO,
    -- ESCALADO_MEDICINA, PROTOCOLO_URGENTE, CERRADO_ENFERMERIA) cuando existan.
    CONSTRAINT CK_ec_estado CHECK (estado_codigo = 'PENDIENTE'),
    CONSTRAINT CK_ec_observacion CHECK (LEN(LTRIM(RTRIM(observacion))) > 0),
    CONSTRAINT CK_ec_clasificacion CHECK (clasificacion_codigo IN ('ORDINARIO', 'PRIORITARIO'))
);
CREATE INDEX IX_ec_resident_time ON dbo.eventos_clinicos (centro_id, residente_id, ocurrido_en);
GO

CREATE TRIGGER dbo.TR_ec_immutable ON dbo.eventos_clinicos INSTEAD OF UPDATE, DELETE AS
    THROW 50302, 'CLINICAL_EVENT_IMMUTABLE', 1;
GO
