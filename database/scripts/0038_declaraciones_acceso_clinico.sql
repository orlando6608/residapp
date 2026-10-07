/*
 * Lectura clínica de Dirección: finalidad, justificación y declaración de acceso (decisiones de CJ, 2026-10-06).
 *
 *   - Dirección elige una finalidad de una lista cerrada (continuidad asistencial, incidencia o reclamación asistencial,
 *     trazabilidad documental) y SIEMPRE escribe una justificación breve. La finalidad antigua «Supervisión clínica» ya no se ofrece;
 *     se conserva en la restricción solo para las filas de auditoría que ya existen (sin justificación).
 *   - Una declaración por residente vale 1 hora para todas sus pantallas clínicas autorizadas. Termina al cambiar de residente, de
 *     perfil activo o de ámbito, al cerrar sesión o al cumplirse la hora. Cada apertura sigue comprobando permisos y escribiendo su
 *     propia fila de auditoría ANTES de entregar el contenido; la declaración solo evita repetir finalidad y justificación.
 *
 * Una declaración solo se crea y se termina: no se borra ni se cambia nada más (trigger de guarda).
 */

ALTER TABLE dbo.eventos_auditoria ADD justificacion NVARCHAR(300) NULL;
GO

ALTER TABLE dbo.eventos_auditoria DROP CONSTRAINT CK_audit_direction_read;
GO

-- proposito_codigo IS NOT NULL: sin él, una finalidad nula daba UNKNOWN y un CHECK acepta UNKNOWN (la restricción original tenía ese hueco).
ALTER TABLE dbo.eventos_auditoria ADD CONSTRAINT CK_audit_direction_read CHECK (
    accion_codigo <> 'CLINICAL_DETAIL_READ'
    OR (
        perfil_activo = 'DIRECCION_CLINICA' AND unidad_id IS NOT NULL AND residente_id IS NOT NULL AND proposito_codigo IS NOT NULL
        AND (
            (proposito_codigo = 'SUPERVISION_CLINICA' AND justificacion IS NULL)
            OR (proposito_codigo IN ('CONTINUIDAD_ASISTENCIAL', 'INCIDENCIA_RECLAMACION', 'TRAZABILIDAD_DOCUMENTAL')
                AND justificacion IS NOT NULL AND LEN(LTRIM(RTRIM(justificacion))) > 0)
        )
    )
);
GO

CREATE TABLE dbo.declaraciones_acceso_clinico (
    id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_dac PRIMARY KEY,
    centro_id         UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_dac_center REFERENCES dbo.centros(id),
    cuenta_id         UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_dac_account REFERENCES dbo.cuentas(id),
    ambito_perfil_id  UNIQUEIDENTIFIER NOT NULL,
    residente_id      UNIQUEIDENTIFIER NOT NULL,
    proposito_codigo  NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    justificacion     NVARCHAR(300) NOT NULL,
    creada_en         DATETIME2(3) NOT NULL,
    caduca_en         DATETIME2(3) NOT NULL,
    terminada_en      DATETIME2(3) NULL,
    CONSTRAINT FK_dac_scope FOREIGN KEY (ambito_perfil_id, centro_id) REFERENCES dbo.ambitos_perfil(id, centro_id),
    CONSTRAINT FK_dac_resident FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes(centro_id, id),
    CONSTRAINT CK_dac_purpose CHECK (proposito_codigo IN ('CONTINUIDAD_ASISTENCIAL', 'INCIDENCIA_RECLAMACION', 'TRAZABILIDAD_DOCUMENTAL')),
    CONSTRAINT CK_dac_justification CHECK (LEN(LTRIM(RTRIM(justificacion))) > 0),
    CONSTRAINT CK_dac_expiry CHECK (caduca_en > creada_en),
    CONSTRAINT CK_dac_end CHECK (terminada_en IS NULL OR terminada_en >= creada_en)
);
CREATE INDEX IX_dac_account_open ON dbo.declaraciones_acceso_clinico (cuenta_id, terminada_en, caduca_en);
GO

/* Solo se puede terminar una declaración abierta; ningún otro cambio ni borrado. */
CREATE TRIGGER dbo.TR_dac_guard ON dbo.declaraciones_acceso_clinico INSTEAD OF UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM deleted) RETURN; -- una sentencia que no afecta a ninguna fila
    IF NOT EXISTS (SELECT 1 FROM inserted) -- DELETE
        THROW 50380, 'CLINICAL_ACCESS_DECLARATION_IMMUTABLE', 1;

    IF EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id
         WHERE d.terminada_en IS NOT NULL OR i.terminada_en IS NULL
            OR i.centro_id <> d.centro_id OR i.cuenta_id <> d.cuenta_id OR i.ambito_perfil_id <> d.ambito_perfil_id
            OR i.residente_id <> d.residente_id OR i.proposito_codigo <> d.proposito_codigo OR i.justificacion <> d.justificacion
            OR i.creada_en <> d.creada_en OR i.caduca_en <> d.caduca_en)
        THROW 50380, 'CLINICAL_ACCESS_DECLARATION_IMMUTABLE', 1;

    UPDATE t SET terminada_en = i.terminada_en
      FROM dbo.declaraciones_acceso_clinico t JOIN inserted i ON i.id = t.id;
END;
GO

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.declaraciones_acceso_clinico,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.declaraciones_acceso_clinico AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.declaraciones_acceso_clinico AFTER UPDATE;
GO
