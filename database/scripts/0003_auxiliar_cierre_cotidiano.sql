/*
 * Añade el cierre cotidiano del Auxiliar (grupo A2 del plan de implementación: AUX-04 "Sin cambios" y
 * AUX-05 "No valorable"). Sin precedente en 0001/0002 ni en el prototipo legado: ese vertical nunca llegó
 * a migrarse en `docs/legado-cloudflare/db/migrations/`, así que este es diseño nuevo, no una traducción.
 *
 * A diferencia de 0002 (DROP + CREATE completo de todo el esquema), este script es puramente aditivo:
 * 0001/0002 ya tienen datos de desarrollo sembrados (ver database/seed/) que no deben perderse. Solo se
 * añade una tabla nueva y se extiende un CHECK existente (CK_idem_action) con dos códigos de acción
 * nuevos para la idempotencia de este vertical.
 *
 * "Registrar cambio" (AUX-06 a AUX-12, grupo A3) queda fuera de este script: se diseñará cuando llegue
 * ese grupo, probablemente extendiendo tipo_codigo con un tercer valor sobre esta misma tabla.
 */

------------------------------------------------------------
-- Extiende la idempotencia existente con los dos nuevos códigos de acción
------------------------------------------------------------
ALTER TABLE dbo.operaciones_idempotencia DROP CONSTRAINT CK_idem_action;
ALTER TABLE dbo.operaciones_idempotencia ADD CONSTRAINT CK_idem_action
    CHECK (accion_codigo IN ('RESIDENT_CREATE', 'BASELINE_SIGN', 'CLINICAL_DETAIL_READ', 'DAILY_CLOSURE_NO_CHANGE', 'DAILY_CLOSURE_NOT_ASSESSABLE'));
GO

------------------------------------------------------------
-- Cierre cotidiano del Auxiliar (AUX-04/AUX-05). Append-only, igual que el resto del esquema clínico:
-- un cierre registrado nunca se edita ni se borra, solo se crean nuevos.
------------------------------------------------------------
CREATE TABLE dbo.cierres_cotidianos_residente (
    id                        UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ccr PRIMARY KEY,
    residente_id              UNIQUEIDENTIFIER NOT NULL,
    centro_id                 UNIQUEIDENTIFIER NOT NULL,
    unidad_id                 UNIQUEIDENTIFIER NOT NULL,
    tipo_codigo               NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    motivo_no_valorable       NVARCHAR(500) NULL,
    registrado_por_cuenta_id  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_ccr_account REFERENCES dbo.cuentas(id),
    registrado_por_perfil     NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    ocurrido_en               DATETIME2(3) NOT NULL,
    CONSTRAINT FK_ccr_resident FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes(centro_id, id),
    CONSTRAINT FK_ccr_unit FOREIGN KEY (centro_id, unidad_id) REFERENCES dbo.unidades(centro_id, id),
    CONSTRAINT CK_ccr_type CHECK (tipo_codigo IN ('SIN_CAMBIOS', 'NO_VALORABLE')),
    CONSTRAINT CK_ccr_profile CHECK (registrado_por_perfil = 'AUXILIAR'),
    -- Regla de negocio "No valorable no puede guardarse sin motivo" repetida como CHECK, no solo en C#
    -- (docs/flujos-clinicos/registro-cotidiano-auxiliar.md): defensa en profundidad, mismo criterio que
    -- el resto del esquema (ver docs/decisiones-arquitectura/integridad-sql-basal-legado.md).
    CONSTRAINT CK_ccr_reason CHECK (
        (tipo_codigo = 'NO_VALORABLE' AND LEN(LTRIM(RTRIM(ISNULL(motivo_no_valorable, '')))) > 0)
        OR (tipo_codigo = 'SIN_CAMBIOS' AND motivo_no_valorable IS NULL)
    )
);
CREATE INDEX IX_ccr_resident_time ON dbo.cierres_cotidianos_residente (centro_id, residente_id, ocurrido_en);
GO

CREATE TRIGGER dbo.TR_ccr_immutable ON dbo.cierres_cotidianos_residente INSTEAD OF UPDATE, DELETE AS
    THROW 50300, 'DAILY_CLOSURE_IMMUTABLE', 1;
GO
