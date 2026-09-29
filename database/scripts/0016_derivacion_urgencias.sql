/*
 * Derivación a Urgencias, bloque 2 de la historia 6 de Enfermería y de Medicina (ENF-12/ENF-14,
 * MED-14/MED-16, DER-01 a DER-06). Es la salida "Derivar a Urgencias" del protocolo urgente (script 0015).
 *
 * Decisiones de producto (2026-09-29):
 *   - La derivación es un documento firmado vinculado al evento, no un estado: el evento sigue en el
 *     protocolo urgente de su perfil (firmar y registrar una llamada avanzan la revisión con la transición
 *     PROTOCOLO_URGENTE[_MEDICO] -> el mismo estado, que ya admite TR_ea_transition_guard) y se cierra
 *     después con el cierre común. Un informe por evento, sin corrección ni nueva versión.
 *   - El informe se firma tras una vista previa obligatoria. Sus datos automáticos (identificación, basal,
 *     observación, valoraciones, constantes, actuaciones, evolución) no se editan; el profesional escribe
 *     el motivo de la derivación y una información adicional opcional. contenido_json guarda las secciones
 *     tal como se firmaron y huella_contenido su SHA-256; pdf guarda el documento generado al firmar y
 *     huella_pdf su SHA-256. Firma electrónica simple: cuenta autenticada, fecha y hora, y la huella; sin
 *     certificado cualificado. El informe nunca incluye CFS ni los servicios contactados (DER-04).
 *   - Toda derivación exige documentar el intento de llamada al contacto familiar (a quién en texto libre,
 *     hasta que Administración tenga el contacto designado; hora, resultado y nota) y una actualización
 *     relevante para la familia. Las dos se exigen al cerrar el evento (ClinicalEventCloser): documentar
 *     no retrasa la atención.
 *
 * Ambas tablas son de solo inserción. La firma es idempotente (REFERRAL_REPORT_SIGN).
 */

ALTER TABLE dbo.operaciones_idempotencia DROP CONSTRAINT CK_idem_action;
ALTER TABLE dbo.operaciones_idempotencia ADD CONSTRAINT CK_idem_action
    CHECK (accion_codigo IN (
        'RESIDENT_CREATE', 'BASELINE_SIGN', 'CLINICAL_DETAIL_READ',
        'DAILY_CLOSURE_NO_CHANGE', 'DAILY_CLOSURE_NOT_ASSESSABLE', 'DAILY_CLOSURE_CHANGE_REPORTED',
        'BASELINE_DRAFT_CREATE', 'CLINICAL_EVENT_REGISTER', 'CLINICAL_EVENT_CLOSE', 'REFERRAL_REPORT_SIGN'));
GO

CREATE TABLE dbo.informes_derivacion (
    id                        UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_id PRIMARY KEY,
    evento_id                 UNIQUEIDENTIFIER NOT NULL,
    residente_id              UNIQUEIDENTIFIER NOT NULL,
    centro_id                 UNIQUEIDENTIFIER NOT NULL,
    perfil_codigo             NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    motivo                    NVARCHAR(2000) NOT NULL,
    informacion_adicional     NVARCHAR(4000) NULL,
    contenido_json            NVARCHAR(MAX) NOT NULL,
    huella_contenido          CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    pdf                       VARBINARY(MAX) NOT NULL,
    huella_pdf                CHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    firmado_por_cuenta_id     UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_id_firmado_por REFERENCES dbo.cuentas(id),
    firmado_en                DATETIME2(3) NOT NULL,
    CONSTRAINT FK_id_evento FOREIGN KEY (evento_id, residente_id, centro_id) REFERENCES dbo.eventos_asistenciales(id, residente_id, centro_id),
    CONSTRAINT CK_id_perfil CHECK (perfil_codigo IN ('ENFERMERIA', 'MEDICINA')),
    CONSTRAINT CK_id_motivo CHECK (LEN(LTRIM(RTRIM(motivo))) > 0),
    CONSTRAINT CK_id_informacion CHECK (informacion_adicional IS NULL OR LEN(LTRIM(RTRIM(informacion_adicional))) > 0),
    CONSTRAINT CK_id_contenido CHECK (ISJSON(contenido_json) = 1),
    CONSTRAINT CK_id_pdf CHECK (DATALENGTH(pdf) > 0)
);
CREATE UNIQUE INDEX UX_id_evento ON dbo.informes_derivacion (evento_id);
GO

CREATE TRIGGER dbo.TR_id_immutable ON dbo.informes_derivacion INSTEAD OF UPDATE, DELETE AS
    THROW 50360, 'REFERRAL_REPORT_IMMUTABLE', 1;
GO

/*
 * Intentos de llamada al contacto familiar tras la derivación (DER-06), cada uno con su autoría. Puede
 * haber varios; la hora la escribe el profesional porque puede documentarse después, pero nunca en el
 * futuro (se admiten unos minutos de desfase entre relojes, como en protocolo_urgente_registros).
 */
CREATE TABLE dbo.intentos_llamada_familia (
    id                         UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ilf PRIMARY KEY,
    informe_id                 UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_ilf_informe REFERENCES dbo.informes_derivacion(id),
    evento_id                  UNIQUEIDENTIFIER NOT NULL,
    contacto                   NVARCHAR(200) NOT NULL,
    llamado_en                 DATETIME2(3) NOT NULL,
    resultado_codigo           NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    nota                       NVARCHAR(2000) NULL,
    registrado_por_cuenta_id   UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_ilf_registrado_por REFERENCES dbo.cuentas(id),
    registrado_en              DATETIME2(3) NOT NULL,
    CONSTRAINT CK_ilf_contacto CHECK (LEN(LTRIM(RTRIM(contacto))) > 0),
    CONSTRAINT CK_ilf_resultado CHECK (resultado_codigo IN ('CONTACTADO', 'NO_CONTESTA', 'NUMERO_ERRONEO')),
    CONSTRAINT CK_ilf_nota CHECK (nota IS NULL OR LEN(LTRIM(RTRIM(nota))) > 0),
    CONSTRAINT CK_ilf_no_futuro CHECK (llamado_en <= DATEADD(MINUTE, 5, registrado_en))
);
CREATE INDEX IX_ilf_evento ON dbo.intentos_llamada_familia (evento_id, registrado_en);
GO

CREATE TRIGGER dbo.TR_ilf_immutable ON dbo.intentos_llamada_familia INSTEAD OF UPDATE, DELETE AS
    THROW 50361, 'FAMILY_CALL_ATTEMPT_IMMUTABLE', 1;
GO
