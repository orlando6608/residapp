/*
 * Baja, reactivación y suspensión del residente (CJ, 2026-10-07; documento traslado-y-baja-residente).
 *
 * BAJA (fallecimiento, alta voluntaria, traslado a otro centro, otro con motivo escrito). Los datos no se borran y se conservan 5 años
 * (conservar_hasta). La baja cierra el episodio y la ubicación vigentes y pone residentes.estado en INACTIVE, que la autorización y
 * las listas ya tratan como «no activo»; Administración lo ve en una lista aparte, en solo lectura, y puede reactivarlo (episodio y
 * ubicación nuevos). dbo.bajas_residente guarda cada baja y solo admite rellenar una vez los campos de la reactivación.
 *
 * SUSPENSIÓN (ingreso hospitalario prolongado). El residente sigue activo, pero no se puede actuar sobre él: ni cambios cotidianos ni
 * eventos ni constantes ni basales ni comunicados a familias. dbo.suspensiones_residente guarda cada suspensión (una abierta como
 * máximo por residente). Los disparadores TR_*_suspension_guard impiden las escrituras clínicas mientras haya una abierta.
 */

CREATE TABLE dbo.bajas_residente (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_bjr PRIMARY KEY,
    residente_id             UNIQUEIDENTIFIER NOT NULL,
    centro_id                UNIQUEIDENTIFIER NOT NULL,
    episodio_id              UNIQUEIDENTIFIER NOT NULL,
    unidad_id                UNIQUEIDENTIFIER NOT NULL,
    motivo_codigo            NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    motivo_texto             NVARCHAR(500) NULL,
    conservar_hasta          DATE NOT NULL,
    dada_por_cuenta_id       UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_bjr_by REFERENCES dbo.cuentas(id),
    dada_en                  DATETIME2(3) NOT NULL,
    reactivada_en            DATETIME2(3) NULL,
    reactivada_por_cuenta_id UNIQUEIDENTIFIER NULL CONSTRAINT FK_bjr_reactivated_by REFERENCES dbo.cuentas(id),
    CONSTRAINT FK_bjr_resident FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes(centro_id, id),
    CONSTRAINT FK_bjr_episode FOREIGN KEY (episodio_id, residente_id, centro_id) REFERENCES dbo.episodios_residente_centro(id, residente_id, centro_id),
    CONSTRAINT FK_bjr_unit FOREIGN KEY (centro_id, unidad_id) REFERENCES dbo.unidades(centro_id, id),
    CONSTRAINT CK_bjr_motivo CHECK (motivo_codigo IN ('FALLECIMIENTO', 'ALTA_VOLUNTARIA', 'TRASLADO_OTRO_CENTRO', 'OTRO')),
    CONSTRAINT CK_bjr_motivo_texto CHECK (
        (motivo_codigo = 'OTRO' AND LEN(LTRIM(RTRIM(ISNULL(motivo_texto, '')))) > 0)
        OR (motivo_codigo <> 'OTRO' AND (motivo_texto IS NULL OR LEN(LTRIM(RTRIM(motivo_texto))) > 0))),
    CONSTRAINT CK_bjr_reactivacion CHECK (
        (reactivada_en IS NULL AND reactivada_por_cuenta_id IS NULL)
        OR (reactivada_en IS NOT NULL AND reactivada_por_cuenta_id IS NOT NULL AND reactivada_en >= dada_en))
);
CREATE UNIQUE INDEX UX_bjr_open ON dbo.bajas_residente (residente_id) WHERE reactivada_en IS NULL;
CREATE INDEX IX_bjr_lookup ON dbo.bajas_residente (centro_id, unidad_id, dada_en);
GO
CREATE TRIGGER dbo.TR_bjr_guard ON dbo.bajas_residente AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE d.reactivada_en IS NOT NULL OR i.reactivada_en IS NULL
           OR i.residente_id <> d.residente_id OR i.centro_id <> d.centro_id OR i.episodio_id <> d.episodio_id
           OR i.unidad_id <> d.unidad_id OR i.motivo_codigo <> d.motivo_codigo OR ISNULL(i.motivo_texto, '') <> ISNULL(d.motivo_texto, '')
           OR i.conservar_hasta <> d.conservar_hasta OR i.dada_por_cuenta_id <> d.dada_por_cuenta_id OR i.dada_en <> d.dada_en)
        THROW 50450, 'RESIDENT_DISCHARGE_REACTIVATE_ONLY', 1;
END;
GO
CREATE TRIGGER dbo.TR_bjr_no_delete ON dbo.bajas_residente INSTEAD OF DELETE AS
    THROW 50451, 'RESIDENT_DISCHARGE_DELETE_FORBIDDEN', 1;
GO

CREATE TABLE dbo.suspensiones_residente (
    id                          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_spr PRIMARY KEY,
    residente_id                UNIQUEIDENTIFIER NOT NULL,
    centro_id                   UNIQUEIDENTIFIER NOT NULL,
    unidad_id                   UNIQUEIDENTIFIER NOT NULL,
    nota                        NVARCHAR(500) NULL,
    iniciada_por_cuenta_id      UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_spr_by REFERENCES dbo.cuentas(id),
    iniciada_en                 DATETIME2(3) NOT NULL,
    finalizada_en               DATETIME2(3) NULL,
    finalizada_por_cuenta_id    UNIQUEIDENTIFIER NULL CONSTRAINT FK_spr_ended_by REFERENCES dbo.cuentas(id),
    CONSTRAINT FK_spr_resident FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes(centro_id, id),
    CONSTRAINT FK_spr_unit FOREIGN KEY (centro_id, unidad_id) REFERENCES dbo.unidades(centro_id, id),
    CONSTRAINT CK_spr_nota CHECK (nota IS NULL OR LEN(LTRIM(RTRIM(nota))) > 0),
    CONSTRAINT CK_spr_fin CHECK (
        (finalizada_en IS NULL AND finalizada_por_cuenta_id IS NULL)
        OR (finalizada_en IS NOT NULL AND finalizada_por_cuenta_id IS NOT NULL AND finalizada_en >= iniciada_en))
);
CREATE UNIQUE INDEX UX_spr_open ON dbo.suspensiones_residente (residente_id) WHERE finalizada_en IS NULL;
CREATE INDEX IX_spr_lookup ON dbo.suspensiones_residente (centro_id, residente_id, iniciada_en);
GO
CREATE TRIGGER dbo.TR_spr_guard ON dbo.suspensiones_residente AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
        WHERE d.finalizada_en IS NOT NULL OR i.finalizada_en IS NULL
           OR i.residente_id <> d.residente_id OR i.centro_id <> d.centro_id OR i.unidad_id <> d.unidad_id
           OR ISNULL(i.nota, '') <> ISNULL(d.nota, '') OR i.iniciada_por_cuenta_id <> d.iniciada_por_cuenta_id OR i.iniciada_en <> d.iniciada_en)
        THROW 50452, 'RESIDENT_SUSPENSION_END_ONLY', 1;
END;
GO
CREATE TRIGGER dbo.TR_spr_no_delete ON dbo.suspensiones_residente INSTEAD OF DELETE AS
    THROW 50453, 'RESIDENT_SUSPENSION_DELETE_FORBIDDEN', 1;
GO

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.bajas_residente,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.bajas_residente AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.bajas_residente AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.suspensiones_residente,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.suspensiones_residente AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.suspensiones_residente AFTER UPDATE;
GO

/* Mientras haya una suspensión abierta, no se escribe nada clínico sobre el residente. */
CREATE TRIGGER dbo.TR_cc_suspension_guard ON dbo.cierres_cotidianos_residente AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.suspensiones_residente s
                ON s.residente_id = i.residente_id AND s.centro_id = i.centro_id AND s.finalizada_en IS NULL)
        THROW 50454, 'RESIDENT_SUSPENDED', 1;
END;
GO
CREATE TRIGGER dbo.TR_ec_suspension_guard ON dbo.eventos_clinicos AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.suspensiones_residente s
                ON s.residente_id = i.residente_id AND s.centro_id = i.centro_id AND s.finalizada_en IS NULL)
        THROW 50454, 'RESIDENT_SUSPENDED', 1;
END;
GO
CREATE TRIGGER dbo.TR_bd_suspension_guard ON dbo.basales_borrador AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.suspensiones_residente s
                ON s.residente_id = i.residente_id AND s.centro_id = i.centro_id AND s.finalizada_en IS NULL)
        THROW 50454, 'RESIDENT_SUSPENDED', 1;
END;
GO
CREATE TRIGGER dbo.TR_cf_suspension_guard ON dbo.comunicaciones_familiares AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.suspensiones_residente s
                ON s.residente_id = i.residente_id AND s.centro_id = i.centro_id AND s.finalizada_en IS NULL)
        THROW 50454, 'RESIDENT_SUSPENDED', 1;
END;
GO
/* Toda acción sobre un evento (valoración, constantes, indicación, seguimiento, cierre, protocolo) avanza su revisión: se frena aquí.
   Mover la unidad del evento por un traslado no cambia nada clínico y se deja pasar. */
CREATE TRIGGER dbo.TR_ea_suspension_guard ON dbo.eventos_asistenciales AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
                JOIN dbo.suspensiones_residente s
                  ON s.residente_id = i.residente_id AND s.centro_id = i.centro_id AND s.finalizada_en IS NULL
                WHERE i.unidad_id = d.unidad_id)
        THROW 50454, 'RESIDENT_SUSPENDED', 1;
END;
GO
