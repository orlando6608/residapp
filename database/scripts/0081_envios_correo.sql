/*
 * Registro de envíos de correo (ADR 0012, pieza 1: el aviso de propuesta de cita; lectura de privacidad en docs/privacidad/aviso-cita-por-correo.md, 2.4).
 *
 * Una fila por correo que la aplicación intenta mandar. Sirve solo para dos cosas: no mandar dos veces el mismo aviso y poder explicar si uno salió. Por eso
 * NO guarda la dirección (ni su huella), el asunto, el cuerpo, los enlaces, el nombre de nadie, el residente, la cuenta de Administración ni el texto de una
 * excepción: el residente se llega por la solicitud (referencia_id) y quién propuso consta en citas_solicitudes_cambios.
 *
 *   estado_codigo   PENDIENTE (reservado antes de enviar), ENVIADO (ACS lo aceptó; no quiere decir entregado), FALLIDO u OMITIDO (no había a quién o no era
 *                   ya vigente). motivo_codigo es un valor cerrado, nunca el mensaje de una excepción.
 *   referencia_*    Qué lo provocó: la solicitud y el número de su cambio PROPUESTA (no el día: una solicitud puede recibir otra propuesta el mismo día).
 *                   La clave única (plantilla, referencia, clave) es lo que impide enviarlo dos veces, también con dos peticiones a la vez.
 *
 * Una fila solo puede pasar de PENDIENTE a un estado final, y su identidad no cambia (TR_envios_correo_guard). No se impide borrar: la vía de purga espera
 * el plazo de conservación, que decide el DPO (propuesta de ingeniería: 12 meses desde resuelto_en como tope).
 */

CREATE TABLE dbo.envios_correo (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_envios_correo PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_envios_correo_centro REFERENCES dbo.centros(id),
    plantilla_codigo         NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    plantilla_version        NVARCHAR(8) COLLATE Latin1_General_100_BIN2 NOT NULL,
    idioma                   NVARCHAR(2) COLLATE Latin1_General_100_BIN2 NOT NULL,
    familiar_id              UNIQUEIDENTIFIER NOT NULL,
    referencia_id            UNIQUEIDENTIFIER NOT NULL,
    referencia_clave         NVARCHAR(40) COLLATE Latin1_General_100_BIN2 NOT NULL,
    estado_codigo            NVARCHAR(10) COLLATE Latin1_General_100_BIN2 NOT NULL,
    motivo_codigo            NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NULL,
    acs_operacion_id         NVARCHAR(64) NULL,
    intentos                 TINYINT NOT NULL CONSTRAINT DF_envios_correo_intentos DEFAULT 0,
    creado_en                DATETIME2(3) NOT NULL,
    resuelto_en              DATETIME2(3) NULL,
    CONSTRAINT FK_envios_correo_familiar FOREIGN KEY (centro_id, familiar_id) REFERENCES dbo.familiares (centro_id, id),
    CONSTRAINT CK_envios_correo_plantilla CHECK (plantilla_codigo IN ('AVISO_PROPUESTA_CITA')),
    CONSTRAINT CK_envios_correo_idioma CHECK (idioma IN ('es', 'ca')),
    CONSTRAINT CK_envios_correo_estado CHECK (estado_codigo IN ('PENDIENTE', 'ENVIADO', 'FALLIDO', 'OMITIDO')),
    CONSTRAINT CK_envios_correo_motivo CHECK (motivo_codigo IS NULL OR motivo_codigo IN
        ('SIN_CORREO', 'SIN_ACCESO', 'PROPUESTA_NO_VIGENTE', 'DESTINO_NO_PERMITIDO', 'ACS_RECHAZADO', 'ACS_NO_DISPONIBLE', 'TIEMPO_AGOTADO')),
    CONSTRAINT CK_envios_correo_resolucion CHECK (
        (estado_codigo = 'PENDIENTE' AND motivo_codigo IS NULL AND resuelto_en IS NULL)
        OR (estado_codigo = 'ENVIADO' AND motivo_codigo IS NULL AND resuelto_en IS NOT NULL)
        OR (estado_codigo IN ('FALLIDO', 'OMITIDO') AND motivo_codigo IS NOT NULL AND resuelto_en IS NOT NULL))
);
GO
CREATE UNIQUE INDEX UX_envios_correo_referencia ON dbo.envios_correo (plantilla_codigo, referencia_id, referencia_clave);
CREATE INDEX IX_envios_correo_centro ON dbo.envios_correo (centro_id, creado_en);
GO

CREATE TRIGGER dbo.TR_envios_correo_guard ON dbo.envios_correo AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(id) OR UPDATE(centro_id) OR UPDATE(plantilla_codigo) OR UPDATE(plantilla_version) OR UPDATE(idioma) OR UPDATE(familiar_id)
       OR UPDATE(referencia_id) OR UPDATE(referencia_clave) OR UPDATE(creado_en)
        THROW 50590, 'EMAIL_LOG_IDENTITY_IMMUTABLE', 1;
    IF EXISTS (SELECT 1 FROM deleted d WHERE d.estado_codigo <> 'PENDIENTE')
        THROW 50591, 'EMAIL_LOG_ALREADY_RESOLVED', 1;
END;
GO

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.envios_correo,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.envios_correo AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.envios_correo AFTER UPDATE;
GO
