/*
 * Historial, bloque 1 (historia 11 de Enfermería, 9 de Medicina): HIS-03 exige que cada evento conserve la
 * referencia al basal y a la ubicación aplicables cuando ocurrió, sin reconstruir el pasado desde los valores
 * actuales.
 *
 * Decisión del usuario (2026-09-30): guardar una instantánea. dbo.eventos_contexto, de solo inserción, guarda
 * para cada evento la versión del basal vigente y el intervalo de ubicación vigente al crearse. La escriben
 * SqlClinicalEventRepository y SqlDailyClosureRepository en la misma transacción que eventos_asistenciales.
 * NULL significa que el residente no tenía basal firmado o ubicación registrada en ese momento.
 *
 * Los eventos anteriores a este script reciben su instantánea deducida del histórico, que es inmutable y por
 * eso exacto: basales_version.vigente_desde es la hora de firma y cada versión sigue vigente hasta la firma de
 * la siguiente; los intervalos de ubicación solo se cierran (TR_rli_close_only).
 */

-- La FK compuesta garantiza que el intervalo es del mismo residente y centro que el evento.
CREATE UNIQUE INDEX UX_rli_scope ON dbo.intervalos_ubicacion_residente (id, residente_id, centro_id);

-- Lista de eventos cerrados por residente (IX_ea_bandeja no incluye residente_id).
CREATE INDEX IX_ea_residente ON dbo.eventos_asistenciales (centro_id, residente_id, estado_codigo);
GO

CREATE TABLE dbo.eventos_contexto (
    evento_id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ectx PRIMARY KEY,
    residente_id            UNIQUEIDENTIFIER NOT NULL,
    centro_id               UNIQUEIDENTIFIER NOT NULL,
    version_basal_id        UNIQUEIDENTIFIER NULL,
    intervalo_ubicacion_id  UNIQUEIDENTIFIER NULL,
    capturado_en            DATETIME2(3) NOT NULL,
    CONSTRAINT FK_ectx_evento FOREIGN KEY (evento_id, residente_id, centro_id)
        REFERENCES dbo.eventos_asistenciales (id, residente_id, centro_id),
    CONSTRAINT FK_ectx_basal FOREIGN KEY (version_basal_id, residente_id, centro_id)
        REFERENCES dbo.basales_version (id, residente_id, centro_id),
    CONSTRAINT FK_ectx_ubicacion FOREIGN KEY (intervalo_ubicacion_id, residente_id, centro_id)
        REFERENCES dbo.intervalos_ubicacion_residente (id, residente_id, centro_id)
);
GO

CREATE TRIGGER dbo.TR_ectx_immutable ON dbo.eventos_contexto INSTEAD OF UPDATE, DELETE AS
    THROW 50370, 'CLINICAL_EVENT_CONTEXT_IMMUTABLE', 1;
GO

INSERT INTO dbo.eventos_contexto (evento_id, residente_id, centro_id, version_basal_id, intervalo_ubicacion_id, capturado_en)
SELECT ea.id, ea.residente_id, ea.centro_id,
       (SELECT TOP 1 v.id FROM dbo.basales_version v
         WHERE v.residente_id = ea.residente_id AND v.centro_id = ea.centro_id AND v.vigente_desde <= ea.recibido_en
         ORDER BY v.vigente_desde DESC),
       (SELECT TOP 1 i.id FROM dbo.intervalos_ubicacion_residente i
         WHERE i.residente_id = ea.residente_id AND i.centro_id = ea.centro_id AND i.vigente_desde <= ea.recibido_en
           AND (i.vigente_hasta IS NULL OR i.vigente_hasta > ea.recibido_en)
         ORDER BY i.vigente_desde DESC),
       ea.recibido_en
  FROM dbo.eventos_asistenciales ea;
GO
