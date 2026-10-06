/*
 * centro_id en las diez tablas hijas que no lo tenían (ADR 0008, incremento G1): sin él, la seguridad por filas por centro (0030) no
 * puede proteger su contenido clínico. Siguen la convención del resto del esquema: centro_id NOT NULL y clave foránea compuesta con
 * la tabla padre, de modo que el motor impide que una fila hija apunte a un padre de otro centro.
 *
 *   padre → hija
 *   cierres_cotidianos_cambio_areas → cierres_cotidianos_cambio_area_opciones
 *   informes_derivacion → intentos_llamada_familia
 *   protocolos_urgentes → protocolo_urgente_registros
 *   seguimientos → seguimiento_acciones (y su transferencia_id, autorreferencia)
 *   seguimientos_medicos → seguimiento_medico_acciones (y su transferencia_id)
 *   valoraciones_enfermeria → valoraciones_enfermeria_versiones, valoraciones_enfermeria_correcciones
 *   valoraciones_medicas → valoraciones_medicas_versiones, valoraciones_medicas_correcciones
 *   operaciones_idempotencia: sin padre; clave foránea simple a centros (el centro de la operación).
 *
 * Las filas existentes toman el centro de su padre. Casi todas las hijas son inmutables (INSTEAD OF UPDATE, DELETE): se desactiva su
 * trigger solo para el relleno y se reactiva antes de terminar, todo en una transacción. Para operaciones_idempotencia el centro sale
 * del recurso que guardó la operación (residente, borrador o versión de basal, evento, cierre, informe); si no se puede resolver
 * (operación sin completar o recurso desconocido) se usa el centro reservado «Plataforma» (ADR 0006): ningún centro real lo verá y,
 * con RLS, nadie reutilizará esa operación.
 */

SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO

-- Claves únicas de los padres para las claves foráneas compuestas.
CREATE UNIQUE INDEX UX_ccca_centro_scope ON dbo.cierres_cotidianos_cambio_areas (centro_id, id, area_codigo);
CREATE UNIQUE INDEX UX_id_centro ON dbo.informes_derivacion (centro_id, id);
CREATE UNIQUE INDEX UX_pu_centro ON dbo.protocolos_urgentes (centro_id, id);
CREATE UNIQUE INDEX UX_seg_centro ON dbo.seguimientos (centro_id, id);
CREATE UNIQUE INDEX UX_segm_centro ON dbo.seguimientos_medicos (centro_id, id);
CREATE UNIQUE INDEX UX_ve_centro_scope ON dbo.valoraciones_enfermeria (centro_id, id, evento_id);
CREATE UNIQUE INDEX UX_vm_centro_scope ON dbo.valoraciones_medicas (centro_id, id, evento_id);
GO

ALTER TABLE dbo.cierres_cotidianos_cambio_area_opciones ADD centro_id UNIQUEIDENTIFIER NULL;
ALTER TABLE dbo.intentos_llamada_familia ADD centro_id UNIQUEIDENTIFIER NULL;
ALTER TABLE dbo.protocolo_urgente_registros ADD centro_id UNIQUEIDENTIFIER NULL;
ALTER TABLE dbo.seguimiento_acciones ADD centro_id UNIQUEIDENTIFIER NULL;
ALTER TABLE dbo.seguimiento_medico_acciones ADD centro_id UNIQUEIDENTIFIER NULL;
ALTER TABLE dbo.valoraciones_enfermeria_versiones ADD centro_id UNIQUEIDENTIFIER NULL;
ALTER TABLE dbo.valoraciones_enfermeria_correcciones ADD centro_id UNIQUEIDENTIFIER NULL;
ALTER TABLE dbo.valoraciones_medicas_versiones ADD centro_id UNIQUEIDENTIFIER NULL;
ALTER TABLE dbo.valoraciones_medicas_correcciones ADD centro_id UNIQUEIDENTIFIER NULL;
ALTER TABLE dbo.operaciones_idempotencia ADD centro_id UNIQUEIDENTIFIER NULL;
GO

-- Relleno. Los triggers de inmutabilidad se desactivan solo aquí (la transacción los deja reactivados o no cambia nada).
DISABLE TRIGGER dbo.TR_cccao_immutable ON dbo.cierres_cotidianos_cambio_area_opciones;
DISABLE TRIGGER dbo.TR_ilf_immutable ON dbo.intentos_llamada_familia;
DISABLE TRIGGER dbo.TR_pur_immutable ON dbo.protocolo_urgente_registros;
DISABLE TRIGGER dbo.TR_sa_immutable ON dbo.seguimiento_acciones;
DISABLE TRIGGER dbo.TR_sma_immutable ON dbo.seguimiento_medico_acciones;
DISABLE TRIGGER dbo.TR_vev_immutable ON dbo.valoraciones_enfermeria_versiones;
DISABLE TRIGGER dbo.TR_vec_immutable ON dbo.valoraciones_enfermeria_correcciones;
DISABLE TRIGGER dbo.TR_vmv_immutable ON dbo.valoraciones_medicas_versiones;
DISABLE TRIGGER dbo.TR_vmc_immutable ON dbo.valoraciones_medicas_correcciones;
DISABLE TRIGGER dbo.TR_idem_transition_guard ON dbo.operaciones_idempotencia;
GO

UPDATE child SET centro_id = parent.centro_id
  FROM dbo.cierres_cotidianos_cambio_area_opciones child JOIN dbo.cierres_cotidianos_cambio_areas parent ON parent.id = child.area_id;
UPDATE child SET centro_id = parent.centro_id
  FROM dbo.intentos_llamada_familia child JOIN dbo.informes_derivacion parent ON parent.id = child.informe_id;
UPDATE child SET centro_id = parent.centro_id
  FROM dbo.protocolo_urgente_registros child JOIN dbo.protocolos_urgentes parent ON parent.id = child.protocolo_id;
UPDATE child SET centro_id = parent.centro_id
  FROM dbo.seguimiento_acciones child JOIN dbo.seguimientos parent ON parent.id = child.seguimiento_id;
UPDATE child SET centro_id = parent.centro_id
  FROM dbo.seguimiento_medico_acciones child JOIN dbo.seguimientos_medicos parent ON parent.id = child.seguimiento_id;
UPDATE child SET centro_id = parent.centro_id
  FROM dbo.valoraciones_enfermeria_versiones child JOIN dbo.valoraciones_enfermeria parent ON parent.id = child.valoracion_id;
UPDATE child SET centro_id = parent.centro_id
  FROM dbo.valoraciones_enfermeria_correcciones child JOIN dbo.valoraciones_enfermeria parent ON parent.id = child.valoracion_id;
UPDATE child SET centro_id = parent.centro_id
  FROM dbo.valoraciones_medicas_versiones child JOIN dbo.valoraciones_medicas parent ON parent.id = child.valoracion_id;
UPDATE child SET centro_id = parent.centro_id
  FROM dbo.valoraciones_medicas_correcciones child JOIN dbo.valoraciones_medicas parent ON parent.id = child.valoracion_id;

-- Idempotencia: el recurso resultado es el id de una fila con centro_id (residente, borrador o versión de basal, evento, cierre, informe).
UPDATE operation SET centro_id = resource.centro_id
  FROM dbo.operaciones_idempotencia operation
  JOIN (SELECT id, centro_id FROM dbo.residentes
        UNION ALL SELECT id, centro_id FROM dbo.basales_borrador
        UNION ALL SELECT id, centro_id FROM dbo.basales_version
        UNION ALL SELECT id, centro_id FROM dbo.eventos_asistenciales
        UNION ALL SELECT id, centro_id FROM dbo.eventos_clinicos
        UNION ALL SELECT id, centro_id FROM dbo.cierres_cotidianos_residente
        UNION ALL SELECT id, centro_id FROM dbo.informes_derivacion) resource ON resource.id = operation.recurso_resultado_id;
UPDATE dbo.operaciones_idempotencia SET centro_id = '5F3A1C00-0000-4000-8000-000000000001' WHERE centro_id IS NULL;
GO

ENABLE TRIGGER dbo.TR_cccao_immutable ON dbo.cierres_cotidianos_cambio_area_opciones;
ENABLE TRIGGER dbo.TR_ilf_immutable ON dbo.intentos_llamada_familia;
ENABLE TRIGGER dbo.TR_pur_immutable ON dbo.protocolo_urgente_registros;
ENABLE TRIGGER dbo.TR_sa_immutable ON dbo.seguimiento_acciones;
ENABLE TRIGGER dbo.TR_sma_immutable ON dbo.seguimiento_medico_acciones;
ENABLE TRIGGER dbo.TR_vev_immutable ON dbo.valoraciones_enfermeria_versiones;
ENABLE TRIGGER dbo.TR_vec_immutable ON dbo.valoraciones_enfermeria_correcciones;
ENABLE TRIGGER dbo.TR_vmv_immutable ON dbo.valoraciones_medicas_versiones;
ENABLE TRIGGER dbo.TR_vmc_immutable ON dbo.valoraciones_medicas_correcciones;
ENABLE TRIGGER dbo.TR_idem_transition_guard ON dbo.operaciones_idempotencia;
GO

ALTER TABLE dbo.cierres_cotidianos_cambio_area_opciones ALTER COLUMN centro_id UNIQUEIDENTIFIER NOT NULL;
ALTER TABLE dbo.intentos_llamada_familia ALTER COLUMN centro_id UNIQUEIDENTIFIER NOT NULL;
ALTER TABLE dbo.protocolo_urgente_registros ALTER COLUMN centro_id UNIQUEIDENTIFIER NOT NULL;
ALTER TABLE dbo.seguimiento_acciones ALTER COLUMN centro_id UNIQUEIDENTIFIER NOT NULL;
ALTER TABLE dbo.seguimiento_medico_acciones ALTER COLUMN centro_id UNIQUEIDENTIFIER NOT NULL;
ALTER TABLE dbo.valoraciones_enfermeria_versiones ALTER COLUMN centro_id UNIQUEIDENTIFIER NOT NULL;
ALTER TABLE dbo.valoraciones_enfermeria_correcciones ALTER COLUMN centro_id UNIQUEIDENTIFIER NOT NULL;
ALTER TABLE dbo.valoraciones_medicas_versiones ALTER COLUMN centro_id UNIQUEIDENTIFIER NOT NULL;
ALTER TABLE dbo.valoraciones_medicas_correcciones ALTER COLUMN centro_id UNIQUEIDENTIFIER NOT NULL;
ALTER TABLE dbo.operaciones_idempotencia ALTER COLUMN centro_id UNIQUEIDENTIFIER NOT NULL;
GO

-- Claves únicas de las hijas autorreferenciadas (transferencia_id), antes de sustituir sus claves foráneas.
CREATE UNIQUE INDEX UX_sa_centro ON dbo.seguimiento_acciones (centro_id, id);
CREATE UNIQUE INDEX UX_sma_centro ON dbo.seguimiento_medico_acciones (centro_id, id);
GO

-- Las claves foráneas simples al padre se sustituyen por claves compuestas con centro_id.
ALTER TABLE dbo.cierres_cotidianos_cambio_area_opciones DROP CONSTRAINT FK_cccao_area;
ALTER TABLE dbo.cierres_cotidianos_cambio_area_opciones ADD CONSTRAINT FK_cccao_area
    FOREIGN KEY (centro_id, area_id, area_codigo) REFERENCES dbo.cierres_cotidianos_cambio_areas (centro_id, id, area_codigo);

ALTER TABLE dbo.intentos_llamada_familia DROP CONSTRAINT FK_ilf_informe;
ALTER TABLE dbo.intentos_llamada_familia ADD CONSTRAINT FK_ilf_informe
    FOREIGN KEY (centro_id, informe_id) REFERENCES dbo.informes_derivacion (centro_id, id);

ALTER TABLE dbo.protocolo_urgente_registros DROP CONSTRAINT FK_pur_protocolo;
ALTER TABLE dbo.protocolo_urgente_registros ADD CONSTRAINT FK_pur_protocolo
    FOREIGN KEY (centro_id, protocolo_id) REFERENCES dbo.protocolos_urgentes (centro_id, id);

ALTER TABLE dbo.seguimiento_acciones DROP CONSTRAINT FK_sa_seguimiento;
ALTER TABLE dbo.seguimiento_acciones DROP CONSTRAINT FK_sa_transferencia;
ALTER TABLE dbo.seguimiento_acciones ADD CONSTRAINT FK_sa_seguimiento
    FOREIGN KEY (centro_id, seguimiento_id) REFERENCES dbo.seguimientos (centro_id, id);
ALTER TABLE dbo.seguimiento_acciones ADD CONSTRAINT FK_sa_transferencia
    FOREIGN KEY (centro_id, transferencia_id) REFERENCES dbo.seguimiento_acciones (centro_id, id);

ALTER TABLE dbo.seguimiento_medico_acciones DROP CONSTRAINT FK_sma_seguimiento;
ALTER TABLE dbo.seguimiento_medico_acciones DROP CONSTRAINT FK_sma_transferencia;
ALTER TABLE dbo.seguimiento_medico_acciones ADD CONSTRAINT FK_sma_seguimiento
    FOREIGN KEY (centro_id, seguimiento_id) REFERENCES dbo.seguimientos_medicos (centro_id, id);
ALTER TABLE dbo.seguimiento_medico_acciones ADD CONSTRAINT FK_sma_transferencia
    FOREIGN KEY (centro_id, transferencia_id) REFERENCES dbo.seguimiento_medico_acciones (centro_id, id);

ALTER TABLE dbo.valoraciones_enfermeria_versiones DROP CONSTRAINT FK_vev_valoracion;
ALTER TABLE dbo.valoraciones_enfermeria_versiones ADD CONSTRAINT FK_vev_valoracion
    FOREIGN KEY (centro_id, valoracion_id, evento_id) REFERENCES dbo.valoraciones_enfermeria (centro_id, id, evento_id);

ALTER TABLE dbo.valoraciones_enfermeria_correcciones DROP CONSTRAINT FK_vec_valoracion;
ALTER TABLE dbo.valoraciones_enfermeria_correcciones ADD CONSTRAINT FK_vec_valoracion
    FOREIGN KEY (centro_id, valoracion_id, evento_id) REFERENCES dbo.valoraciones_enfermeria (centro_id, id, evento_id);

ALTER TABLE dbo.valoraciones_medicas_versiones DROP CONSTRAINT FK_vmv_valoracion;
ALTER TABLE dbo.valoraciones_medicas_versiones ADD CONSTRAINT FK_vmv_valoracion
    FOREIGN KEY (centro_id, valoracion_id, evento_id) REFERENCES dbo.valoraciones_medicas (centro_id, id, evento_id);

ALTER TABLE dbo.valoraciones_medicas_correcciones DROP CONSTRAINT FK_vmc_valoracion;
ALTER TABLE dbo.valoraciones_medicas_correcciones ADD CONSTRAINT FK_vmc_valoracion
    FOREIGN KEY (centro_id, valoracion_id, evento_id) REFERENCES dbo.valoraciones_medicas (centro_id, id, evento_id);

ALTER TABLE dbo.operaciones_idempotencia ADD CONSTRAINT FK_idem_centro FOREIGN KEY (centro_id) REFERENCES dbo.centros (id);
GO

COMMIT TRANSACTION;
GO
