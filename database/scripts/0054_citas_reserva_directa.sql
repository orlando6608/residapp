/*
 * Portal Familiar, citas con reserva directa (portal-familiar.md, historia 4; administracion.md, historia 7; CIT-01 a CIT-C03). Suposiciones a
 * confirmar con CJ en portal-familiar-confirmaciones-cj.html, tema 5.
 *
 * Administración configura el servicio del centro (desactivado, reserva directa o, más adelante, solicitud previa), qué modalidades admite cada
 * equipo (médico o de Enfermería), las franjas recurrentes con la duración de cada cita, y los bloqueos puntuales. La familia reserva un hueco
 * libre. Los huecos no se guardan: se calculan en el dominio (AppointmentSlots) con la configuración, los bloqueos y las citas, porque dependen de
 * la hora local y del momento en que se mira.
 *
 *  - citas_configuracion_centro: una fila por centro con servicio; sin fila el servicio está desactivado. La versión ordena dos ediciones
 *    simultáneas. SOLICITUD_PREVIA se admite en el modelo, pero todavía no hay flujo para ella.
 *  - citas_modalidades_habilitadas / citas_franjas / citas_bloqueos: lo que Administración configura. Las franjas y los bloqueos no se borran: se
 *    retiran (retirada_en).
 *  - citas: una cita por hueco. El hueco es exclusivo por equipo y hora de inicio (UX_citas_hueco): dos reservas simultáneas del mismo hueco no
 *    pueden convivir, pase lo que pase en la aplicación. La misma operación de la misma cuenta no se repite (UX_citas_operacion): es la
 *    idempotencia de la doble pulsación. Una cita cancelada libera el hueco; una finalizada no.
 *  - citas_cambios: cada creación, reprogramación, cancelación y finalización con su actor, perfil e instante (CIT-C03). Inmutable.
 *
 * Que el hueco exista, que no se solape con otra cita de otra duración, la antelación, el horizonte, la modalidad y el límite por residente lo
 * comprueba la aplicación dentro de su transacción; aquí quedan la exclusión del hueco, la idempotencia, la inmutabilidad y la coherencia del
 * residente con el vínculo.
 */

CREATE TABLE dbo.citas_configuracion_centro (
    centro_id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_citas_config PRIMARY KEY CONSTRAINT FK_citas_config_centro REFERENCES dbo.centros(id),
    modo_codigo              NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NOT NULL,
    antelacion_minima_horas  INT NOT NULL,
    horizonte_maximo_dias    INT NOT NULL,
    version                  INT NOT NULL,
    CONSTRAINT CK_citas_config_modo CHECK (modo_codigo IN ('DESACTIVADO', 'RESERVA_DIRECTA', 'SOLICITUD_PREVIA')),
    CONSTRAINT CK_citas_config_antelacion CHECK (antelacion_minima_horas BETWEEN 0 AND 720),
    CONSTRAINT CK_citas_config_horizonte CHECK (horizonte_maximo_dias BETWEEN 1 AND 180),
    CONSTRAINT CK_citas_config_version CHECK (version > 0)
);
GO

CREATE TABLE dbo.citas_modalidades_habilitadas (
    centro_id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_citas_modalidades_centro REFERENCES dbo.centros(id),
    equipo_codigo            NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    modalidad_codigo         NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CONSTRAINT PK_citas_modalidades PRIMARY KEY (centro_id, equipo_codigo, modalidad_codigo),
    CONSTRAINT CK_citas_modalidades_equipo CHECK (equipo_codigo IN ('MEDICO', 'ENFERMERIA')),
    CONSTRAINT CK_citas_modalidades_modalidad CHECK (modalidad_codigo IN ('PRESENCIAL', 'TELEFONICA', 'VIDEOCONFERENCIA'))
);
GO

CREATE TABLE dbo.citas_franjas (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_citas_franjas PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_citas_franjas_centro REFERENCES dbo.centros(id),
    equipo_codigo            NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    dia_semana               TINYINT NOT NULL,
    hora_inicio              TIME(0) NOT NULL,
    hora_fin                 TIME(0) NOT NULL,
    duracion_minutos         INT NOT NULL,
    creada_por_cuenta_id     UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_citas_franjas_cuenta REFERENCES dbo.cuentas(id),
    creada_en                DATETIME2(3) NOT NULL,
    retirada_en              DATETIME2(3) NULL,
    CONSTRAINT CK_citas_franjas_equipo CHECK (equipo_codigo IN ('MEDICO', 'ENFERMERIA')),
    CONSTRAINT CK_citas_franjas_dia CHECK (dia_semana BETWEEN 1 AND 7),
    CONSTRAINT CK_citas_franjas_horas CHECK (hora_fin > hora_inicio),
    CONSTRAINT CK_citas_franjas_duracion CHECK (duracion_minutos BETWEEN 5 AND 240)
);
CREATE INDEX IX_citas_franjas_centro ON dbo.citas_franjas (centro_id, equipo_codigo, dia_semana) WHERE retirada_en IS NULL;
GO

CREATE TABLE dbo.citas_bloqueos (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_citas_bloqueos PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_citas_bloqueos_centro REFERENCES dbo.centros(id),
    equipo_codigo            NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    modalidad_codigo         NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NULL,
    inicio_en                DATETIME2(3) NOT NULL,
    fin_en                   DATETIME2(3) NOT NULL,
    motivo                   NVARCHAR(200) NOT NULL,
    creado_por_cuenta_id     UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_citas_bloqueos_cuenta REFERENCES dbo.cuentas(id),
    creado_en                DATETIME2(3) NOT NULL,
    retirado_en              DATETIME2(3) NULL,
    CONSTRAINT CK_citas_bloqueos_equipo CHECK (equipo_codigo IN ('MEDICO', 'ENFERMERIA')),
    CONSTRAINT CK_citas_bloqueos_modalidad CHECK (modalidad_codigo IS NULL OR modalidad_codigo IN ('PRESENCIAL', 'TELEFONICA', 'VIDEOCONFERENCIA')),
    CONSTRAINT CK_citas_bloqueos_rango CHECK (fin_en > inicio_en),
    CONSTRAINT CK_citas_bloqueos_motivo CHECK (LEN(LTRIM(RTRIM(motivo))) > 0)
);
CREATE INDEX IX_citas_bloqueos_centro ON dbo.citas_bloqueos (centro_id, equipo_codigo, inicio_en) WHERE retirado_en IS NULL;
GO

CREATE TABLE dbo.citas (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_citas PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_citas_centro REFERENCES dbo.centros(id),
    residente_id             UNIQUEIDENTIFIER NOT NULL,
    vinculo_id               UNIQUEIDENTIFIER NOT NULL,
    cuenta_id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_citas_cuenta REFERENCES dbo.cuentas(id),
    equipo_codigo            NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    modalidad_codigo         NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    inicio_en                DATETIME2(3) NOT NULL,
    fin_en                   DATETIME2(3) NOT NULL,
    estado_codigo            NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    operacion_id             UNIQUEIDENTIFIER NOT NULL,
    creada_en                DATETIME2(3) NOT NULL,
    CONSTRAINT UQ_citas_centro UNIQUE (centro_id, id),
    CONSTRAINT FK_citas_vinculo FOREIGN KEY (centro_id, residente_id, vinculo_id) REFERENCES dbo.residentes_familiares (centro_id, residente_id, id),
    CONSTRAINT CK_citas_equipo CHECK (equipo_codigo IN ('MEDICO', 'ENFERMERIA')),
    CONSTRAINT CK_citas_modalidad CHECK (modalidad_codigo IN ('PRESENCIAL', 'TELEFONICA', 'VIDEOCONFERENCIA')),
    CONSTRAINT CK_citas_estado CHECK (estado_codigo IN ('CONFIRMADA', 'CANCELADA', 'FINALIZADA')),
    CONSTRAINT CK_citas_rango CHECK (fin_en > inicio_en)
);
CREATE UNIQUE INDEX UX_citas_hueco ON dbo.citas (centro_id, equipo_codigo, inicio_en) WHERE estado_codigo <> 'CANCELADA';
CREATE UNIQUE INDEX UX_citas_operacion ON dbo.citas (cuenta_id, operacion_id);
CREATE INDEX IX_citas_residente ON dbo.citas (centro_id, residente_id, inicio_en);
CREATE INDEX IX_citas_agenda ON dbo.citas (centro_id, inicio_en);
GO

CREATE TABLE dbo.citas_cambios (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_citas_cambios PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL,
    cita_id                  UNIQUEIDENTIFIER NOT NULL,
    numero                   INT NOT NULL,
    accion_codigo            NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    actor_cuenta_id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_citas_cambios_cuenta REFERENCES dbo.cuentas(id),
    actor_perfil_codigo      NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NOT NULL,
    inicio_anterior_en       DATETIME2(3) NULL,
    inicio_nuevo_en          DATETIME2(3) NULL,
    motivo                   NVARCHAR(500) NULL,
    ocurrido_en              DATETIME2(3) NOT NULL,
    CONSTRAINT FK_citas_cambios_cita FOREIGN KEY (centro_id, cita_id) REFERENCES dbo.citas (centro_id, id),
    CONSTRAINT CK_citas_cambios_accion CHECK (accion_codigo IN ('CREADA', 'REPROGRAMADA', 'CANCELADA', 'FINALIZADA')),
    CONSTRAINT CK_citas_cambios_perfil CHECK (actor_perfil_codigo IN ('FAMILIAR', 'ADMINISTRACION')),
    CONSTRAINT CK_citas_cambios_numero CHECK (numero > 0)
);
CREATE UNIQUE INDEX UX_citas_cambios_numero ON dbo.citas_cambios (cita_id, numero);
GO

CREATE TRIGGER dbo.TR_citas_cambios_immutable ON dbo.citas_cambios INSTEAD OF UPDATE, DELETE AS
    THROW 50520, 'APPOINTMENT_CHANGE_IMMUTABLE', 1;
GO

CREATE TRIGGER dbo.TR_citas_no_delete ON dbo.citas INSTEAD OF DELETE AS
    THROW 50521, 'APPOINTMENT_NO_DELETE', 1;
GO

/* De una cita solo cambian el estado y la hora (reprogramar); el residente, el vínculo, quien reservó, el equipo y la modalidad no. Una cita
   cancelada o finalizada no vuelve a cambiar. */
CREATE TRIGGER dbo.TR_citas_update_guard ON dbo.citas AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(centro_id) OR UPDATE(residente_id) OR UPDATE(vinculo_id) OR UPDATE(cuenta_id) OR UPDATE(equipo_codigo) OR UPDATE(modalidad_codigo)
       OR UPDATE(operacion_id) OR UPDATE(creada_en) OR UPDATE(id)
        THROW 50522, 'APPOINTMENT_IDENTITY_IMMUTABLE', 1;
    IF EXISTS (SELECT 1 FROM deleted WHERE estado_codigo <> 'CONFIRMADA')
        THROW 50523, 'APPOINTMENT_CLOSED', 1;
END;
GO

/* Las franjas y los bloqueos solo cambian al retirarlos. */
CREATE TRIGGER dbo.TR_citas_franjas_update_guard ON dbo.citas_franjas AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(id) OR UPDATE(centro_id) OR UPDATE(equipo_codigo) OR UPDATE(dia_semana) OR UPDATE(hora_inicio) OR UPDATE(hora_fin)
       OR UPDATE(duracion_minutos) OR UPDATE(creada_por_cuenta_id) OR UPDATE(creada_en)
        THROW 50524, 'APPOINTMENT_WINDOW_IMMUTABLE', 1;
    IF EXISTS (SELECT 1 FROM deleted WHERE retirada_en IS NOT NULL)
        THROW 50524, 'APPOINTMENT_WINDOW_IMMUTABLE', 1;
END;
GO

CREATE TRIGGER dbo.TR_citas_bloqueos_update_guard ON dbo.citas_bloqueos AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(id) OR UPDATE(centro_id) OR UPDATE(equipo_codigo) OR UPDATE(modalidad_codigo) OR UPDATE(inicio_en) OR UPDATE(fin_en)
       OR UPDATE(motivo) OR UPDATE(creado_por_cuenta_id) OR UPDATE(creado_en)
        THROW 50525, 'APPOINTMENT_BLOCK_IMMUTABLE', 1;
    IF EXISTS (SELECT 1 FROM deleted WHERE retirado_en IS NOT NULL)
        THROW 50525, 'APPOINTMENT_BLOCK_IMMUTABLE', 1;
END;
GO

CREATE TRIGGER dbo.TR_citas_franjas_no_delete ON dbo.citas_franjas INSTEAD OF DELETE AS
    THROW 50526, 'APPOINTMENT_WINDOW_NO_DELETE', 1;
GO

CREATE TRIGGER dbo.TR_citas_bloqueos_no_delete ON dbo.citas_bloqueos INSTEAD OF DELETE AS
    THROW 50527, 'APPOINTMENT_BLOCK_NO_DELETE', 1;
GO

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_configuracion_centro,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_configuracion_centro AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_configuracion_centro AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_modalidades_habilitadas,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_modalidades_habilitadas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_modalidades_habilitadas AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_franjas,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_franjas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_franjas AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_bloqueos,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_bloqueos AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_bloqueos AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_cambios,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_cambios AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_cambios AFTER UPDATE;
GO
