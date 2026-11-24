/*
 * 0115 — Asistencia diaria del Centro de Día, fase 5a (ADR 0019; lectura de dpo-residapp en docs/privacidad/centro-de-dia-asistencia-lectura-dpo.md; decisiones de Orlando, 2026-11-24).
 * Solo esquema: ninguna pantalla lo usa todavía. Cuatro tablas de SOLO AÑADIR (INSTEAD OF UPDATE, DELETE y sin UPDATE ni DELETE para la aplicación), todas con seguridad por filas del centro:
 *
 *   asistencia_centro   El interruptor del centro, apagado por defecto (sin fila = apagado). Patrón de citas_externas_familia_centro (0104): una fila por CAMBIO, el estado vigente es el de
 *                       mayor numero, y Administración confirma al ENCENDER (dos casillas: informó a las personas atendidas y a sus representantes; actualizó su registro de actividades y su
 *                       evaluación de impacto). Solo existe en un centro CENTRO_DE_DIA (ADR 0017): una regla de la base rechaza cualquier otro.
 *   asistencia_pauta    Qué días de la semana se espera a cada persona (siete casillas, sin horas ni jornada), con versiones. Sin ella «3 días seguidos» no significa nada. dias_esperados es una
 *                       máscara de bits: lunes = 1, martes = 2, miércoles = 4, jueves = 8, viernes = 16, sábado = 32, domingo = 64. La familia no la ve nunca.
 *   asistencia_dia      Una fila por residente, día y versión: vino (ASISTIO) o no vino (NO_ASISTIO) y nada más (ni «tarde» ni hora de llegada). Con NO_ASISTIO, una causa cerrada
 *                       (AVISO_FAMILIA, SIN_AVISO u OTRO); solo con OTRO, una nota corta de 80 caracteres como máximo (valor propuesto, sin validar por CJ) y sin saltos de línea. Una corrección es
 *                       una fila nueva (numero siguiente) y vale la última. «Sin anotar» es la ausencia de fila. anotado_en es cuándo se anotó, NO la hora de llegada: ninguna pantalla lo enseña así.
 *                       La fecha es la LOCAL del centro (zonas_vigentes_centros, 0110), nunca futura ni anterior al alta del residente, y solo se anota a un residente activo y no suspendido.
 *   ausencias_programadas   Una ausencia anunciada por adelantado (ingreso hospitalario, vacaciones u otra), de solo añadir: REGISTRADA (desde y hasta, hasta obligatorio, 90 días como máximo,
 *                       valor propuesto sin validar por CJ; sin solapes), ANULADA (era un error) y ACORTADA (vuelve antes; un hasta menor). Motivo cerrado, SIN texto libre. Una ausencia no crea
 *                       filas NO_ASISTIO: es una regla de lectura. El motivo solo lo leen Enfermería y Medicina (lo aplica la lectura de la aplicación, no la base). Al dar de baja al residente
 *                       las ausencias dejan de contar por la lectura (un residente de baja no sale); no se cierran con una fila nueva, a diferencia de lo que sugirió el DPO (anotado en el ADR 0019).
 *
 * Las cuentas son datos personales seudonimizados (igual que en 0104, 0109 y 0113). Plazos de conservación: sin decidir, uno por tabla (docs/privacidad/expurgo-y-supresion.md); por ser de solo
 * añadir, un expurgo futuro exigirá un procedimiento escrito. Auditoría (en el código, no aquí): solo identificadores; nunca el estado, la causa, la nota, el motivo, las fechas ni la pauta.
 *
 * Códigos de error nuevos: 51150 ASSISTANCE_CENTER_INVALID, 51151 ASSISTANCE_SEQUENCE_INVALID, 51152 ASSISTANCE_IMMUTABLE, 51153 ASSISTANCE_RESIDENT_INVALID, 51154 ASSISTANCE_DATE_INVALID,
 * 51155 ASSISTANCE_ABSENCE_INVALID, 51156 ASSISTANCE_ABSENCE_OVERLAP.
 */

SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO

/* ---- El interruptor del centro ---- */

CREATE TABLE dbo.asistencia_centro (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_asc PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_asc_centro REFERENCES dbo.centros(id),
    numero                   INT NOT NULL,
    activa                   BIT NOT NULL,
    confirma_informada       BIT NOT NULL,
    confirma_eipd            BIT NOT NULL,
    cambiado_por_cuenta_id   UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_asc_cuenta REFERENCES dbo.cuentas(id),
    cambiado_por_perfil      NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    cambiado_en              DATETIME2(3) NOT NULL,
    CONSTRAINT CK_asc_numero CHECK (numero >= 1),
    CONSTRAINT CK_asc_perfil CHECK (cambiado_por_perfil = 'ADMINISTRACION'),
    -- Encender exige las dos confirmaciones; apagar no pide ninguna.
    CONSTRAINT CK_asc_confirmaciones CHECK (activa = 0 OR (confirma_informada = 1 AND confirma_eipd = 1))
);
CREATE UNIQUE INDEX UX_asc_numero ON dbo.asistencia_centro (centro_id, numero);
GO

/* ---- La pauta de cada persona ---- */

CREATE TABLE dbo.asistencia_pauta (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_asp PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL,
    residente_id             UNIQUEIDENTIFIER NOT NULL,
    numero                   INT NOT NULL,
    dias_esperados           TINYINT NOT NULL,
    cambiado_por_cuenta_id   UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_asp_cuenta REFERENCES dbo.cuentas(id),
    cambiado_por_perfil      NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    cambiado_en              DATETIME2(3) NOT NULL,
    CONSTRAINT FK_asp_residente FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes (centro_id, id),
    CONSTRAINT CK_asp_numero CHECK (numero >= 1),
    CONSTRAINT CK_asp_dias CHECK (dias_esperados BETWEEN 1 AND 127),
    CONSTRAINT CK_asp_perfil CHECK (cambiado_por_perfil IN ('ADMINISTRACION', 'ENFERMERIA'))
);
CREATE UNIQUE INDEX UX_asp_numero ON dbo.asistencia_pauta (centro_id, residente_id, numero);
GO

/* ---- La asistencia de cada día ---- */

CREATE TABLE dbo.asistencia_dia (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_asd PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL,
    residente_id             UNIQUEIDENTIFIER NOT NULL,
    fecha                    DATE NOT NULL,
    numero                   INT NOT NULL,
    estado_codigo            NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    causa_codigo             NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NULL,
    nota                     NVARCHAR(80) NULL,
    anotado_por_cuenta_id    UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_asd_cuenta REFERENCES dbo.cuentas(id),
    anotado_por_perfil       NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    anotado_en               DATETIME2(3) NOT NULL,
    CONSTRAINT FK_asd_residente FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes (centro_id, id),
    CONSTRAINT CK_asd_numero CHECK (numero >= 1),
    CONSTRAINT CK_asd_estado CHECK (estado_codigo IN ('ASISTIO', 'NO_ASISTIO')),
    CONSTRAINT CK_asd_perfil CHECK (anotado_por_perfil IN ('AUXILIAR', 'ENFERMERIA', 'MEDICINA')),
    -- La causa es obligatoria con NO_ASISTIO y está prohibida con ASISTIO.
    CONSTRAINT CK_asd_causa CHECK (
        (estado_codigo = 'ASISTIO' AND causa_codigo IS NULL)
        OR (estado_codigo = 'NO_ASISTIO' AND causa_codigo IS NOT NULL AND causa_codigo IN ('AVISO_FAMILIA', 'SIN_AVISO', 'OTRO'))),
    -- La nota solo acompaña a OTRO; con texto, de una línea y sin espacios de más.
    CONSTRAINT CK_asd_nota CHECK (
        nota IS NULL
        OR (causa_codigo = 'OTRO' AND LEN(LTRIM(RTRIM(nota))) > 0
            AND CHARINDEX(CHAR(10), nota) = 0 AND CHARINDEX(CHAR(13), nota) = 0))
);
CREATE UNIQUE INDEX UX_asd_numero ON dbo.asistencia_dia (centro_id, residente_id, fecha, numero);
CREATE INDEX IX_asd_fecha ON dbo.asistencia_dia (centro_id, fecha) INCLUDE (residente_id, numero, estado_codigo);
GO

/* ---- Las ausencias programadas ---- */

CREATE TABLE dbo.ausencias_programadas (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_aup PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL,
    residente_id             UNIQUEIDENTIFIER NOT NULL,
    tipo_codigo              NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    ausencia_id              UNIQUEIDENTIFIER NOT NULL,
    desde                    DATE NULL,
    hasta                    DATE NULL,
    motivo_codigo            NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NULL,
    causa_codigo             NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NULL,
    registrado_por_cuenta_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_aup_cuenta REFERENCES dbo.cuentas(id),
    registrado_por_perfil    NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    registrado_en            DATETIME2(3) NOT NULL,
    CONSTRAINT FK_aup_residente FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes (centro_id, id),
    CONSTRAINT FK_aup_ausencia FOREIGN KEY (ausencia_id) REFERENCES dbo.ausencias_programadas (id),
    CONSTRAINT CK_aup_tipo CHECK (tipo_codigo IN ('REGISTRADA', 'ANULADA', 'ACORTADA')),
    CONSTRAINT CK_aup_perfil CHECK (registrado_por_perfil IN ('ADMINISTRACION', 'ENFERMERIA')),
    CONSTRAINT CK_aup_motivo CHECK (motivo_codigo IS NULL OR motivo_codigo IN ('INGRESO_HOSPITALARIO', 'VACACIONES', 'OTRO')),
    CONSTRAINT CK_aup_causa CHECK (causa_codigo IS NULL OR causa_codigo IN ('ERROR', 'REGRESO_ANTES')),
    -- Una REGISTRADA es su propia referencia, lleva desde, hasta (obligatorio, de 90 días como máximo) y motivo, y ninguna causa. Una ANULADA lleva la causa ERROR; una ACORTADA, REGRESO_ANTES y el
    -- hasta nuevo. Ni la anulación ni el acortamiento llevan desde ni motivo.
    CONSTRAINT CK_aup_forma CHECK (
        (tipo_codigo = 'REGISTRADA' AND ausencia_id = id AND desde IS NOT NULL AND hasta IS NOT NULL AND hasta >= desde AND DATEDIFF(DAY, desde, hasta) + 1 <= 90
            AND motivo_codigo IS NOT NULL AND causa_codigo IS NULL)
        OR (tipo_codigo = 'ANULADA' AND ausencia_id <> id AND desde IS NULL AND hasta IS NULL AND motivo_codigo IS NULL AND causa_codigo = 'ERROR')
        OR (tipo_codigo = 'ACORTADA' AND ausencia_id <> id AND desde IS NULL AND hasta IS NOT NULL AND motivo_codigo IS NULL AND causa_codigo = 'REGRESO_ANTES'))
);
CREATE INDEX IX_aup_residente ON dbo.ausencias_programadas (centro_id, residente_id, ausencia_id);
GO

/* ---- Solo se añade ---- */

CREATE TRIGGER dbo.TR_asc_immutable ON dbo.asistencia_centro INSTEAD OF UPDATE, DELETE AS
    THROW 51152, 'ASSISTANCE_IMMUTABLE', 1;
GO
CREATE TRIGGER dbo.TR_asp_immutable ON dbo.asistencia_pauta INSTEAD OF UPDATE, DELETE AS
    THROW 51152, 'ASSISTANCE_IMMUTABLE', 1;
GO
CREATE TRIGGER dbo.TR_asd_immutable ON dbo.asistencia_dia INSTEAD OF UPDATE, DELETE AS
    THROW 51152, 'ASSISTANCE_IMMUTABLE', 1;
GO
CREATE TRIGGER dbo.TR_aup_immutable ON dbo.ausencias_programadas INSTEAD OF UPDATE, DELETE AS
    THROW 51152, 'ASSISTANCE_IMMUTABLE', 1;
GO

/* ---- Las reglas de cada tabla ---- */

-- El interruptor: solo un Centro de Día; la primera fila es un encendido y cada una cambia el valor de la anterior (como 0104).
CREATE TRIGGER dbo.TR_asc_reglas ON dbo.asistencia_centro AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.centros c ON c.id = i.centro_id WHERE c.tipo_de_servicio <> N'CENTRO_DE_DIA' OR c.codigo = N'PLATAFORMA')
        THROW 51150, 'ASSISTANCE_CENTER_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i
          LEFT JOIN dbo.asistencia_centro previous ON previous.centro_id = i.centro_id AND previous.numero = i.numero - 1
         WHERE (i.numero = 1 AND i.activa = 0)
            OR (i.numero > 1 AND (previous.id IS NULL OR previous.activa = i.activa)))
        THROW 51151, 'ASSISTANCE_SEQUENCE_INVALID', 1;
END;
GO

-- La pauta: un Centro de Día, un residente activo y no suspendido, y cada versión distinta de la anterior.
CREATE TRIGGER dbo.TR_asp_reglas ON dbo.asistencia_pauta AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.centros c ON c.id = i.centro_id WHERE c.tipo_de_servicio <> N'CENTRO_DE_DIA' OR c.codigo = N'PLATAFORMA')
        THROW 51150, 'ASSISTANCE_CENTER_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN dbo.residentes r ON r.centro_id = i.centro_id AND r.id = i.residente_id
         WHERE r.estado <> N'ACTIVE'
            OR EXISTS (SELECT 1 FROM dbo.suspensiones_residente s WHERE s.residente_id = i.residente_id AND s.finalizada_en IS NULL))
        THROW 51153, 'ASSISTANCE_RESIDENT_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i
          LEFT JOIN dbo.asistencia_pauta previous ON previous.centro_id = i.centro_id AND previous.residente_id = i.residente_id AND previous.numero = i.numero - 1
         WHERE i.numero > 1 AND (previous.id IS NULL OR previous.dias_esperados = i.dias_esperados))
        THROW 51151, 'ASSISTANCE_SEQUENCE_INVALID', 1;
END;
GO

-- La asistencia: un Centro de Día, un residente activo y no suspendido, una fecha local del centro que no es futura ni anterior al alta, y cada versión distinta de la anterior.
CREATE TRIGGER dbo.TR_asd_reglas ON dbo.asistencia_dia AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.centros c ON c.id = i.centro_id WHERE c.tipo_de_servicio <> N'CENTRO_DE_DIA' OR c.codigo = N'PLATAFORMA')
        THROW 51150, 'ASSISTANCE_CENTER_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN dbo.residentes r ON r.centro_id = i.centro_id AND r.id = i.residente_id
         WHERE r.estado <> N'ACTIVE'
            OR EXISTS (SELECT 1 FROM dbo.suspensiones_residente s WHERE s.residente_id = i.residente_id AND s.finalizada_en IS NULL))
        THROW 51153, 'ASSISTANCE_RESIDENT_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i
          JOIN dbo.zonas_vigentes_centros z ON z.centro_id = i.centro_id
          JOIN dbo.residentes r ON r.centro_id = i.centro_id AND r.id = i.residente_id
         WHERE i.fecha > CAST(CAST(SYSUTCDATETIME() AS DATETIMEOFFSET) AT TIME ZONE z.nombre_windows AS DATE)
            OR i.fecha < CAST(CAST(r.creado_en AS DATETIMEOFFSET) AT TIME ZONE z.nombre_windows AS DATE))
        THROW 51154, 'ASSISTANCE_DATE_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i
          LEFT JOIN dbo.asistencia_dia previous
                 ON previous.centro_id = i.centro_id AND previous.residente_id = i.residente_id AND previous.fecha = i.fecha AND previous.numero = i.numero - 1
         WHERE i.numero > 1 AND (previous.id IS NULL
            OR (previous.estado_codigo = i.estado_codigo AND ISNULL(previous.causa_codigo, N'') = ISNULL(i.causa_codigo, N'') AND ISNULL(previous.nota, N'') = ISNULL(i.nota, N''))))
        THROW 51151, 'ASSISTANCE_SEQUENCE_INVALID', 1;
END;
GO

-- Las ausencias: un Centro de Día y un residente activo y no suspendido. Una REGISTRADA no se solapa con otra vigente del mismo residente (vigente = no anulada; su último día es el menor de su hasta
-- y de los hasta de sus acortamientos). Una ANULADA o ACORTADA remite a una REGISTRADA del mismo residente que no esté anulada; un acortamiento termina antes del último día vigente y no antes del
-- primero.
CREATE TRIGGER dbo.TR_aup_reglas ON dbo.ausencias_programadas AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.centros c ON c.id = i.centro_id WHERE c.tipo_de_servicio <> N'CENTRO_DE_DIA' OR c.codigo = N'PLATAFORMA')
        THROW 51150, 'ASSISTANCE_CENTER_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i JOIN dbo.residentes r ON r.centro_id = i.centro_id AND r.id = i.residente_id
         WHERE r.estado <> N'ACTIVE'
            OR EXISTS (SELECT 1 FROM dbo.suspensiones_residente s WHERE s.residente_id = i.residente_id AND s.finalizada_en IS NULL))
        THROW 51153, 'ASSISTANCE_RESIDENT_INVALID', 1;
    -- Una anulación o un acortamiento remite a una REGISTRADA del mismo centro y residente, todavía no anulada.
    IF EXISTS (
        SELECT 1 FROM inserted i
          LEFT JOIN dbo.ausencias_programadas base ON base.id = i.ausencia_id AND base.tipo_codigo = N'REGISTRADA'
                                                  AND base.centro_id = i.centro_id AND base.residente_id = i.residente_id
         WHERE i.tipo_codigo IN (N'ANULADA', N'ACORTADA')
           AND (base.id IS NULL
                OR EXISTS (SELECT 1 FROM dbo.ausencias_programadas anulada WHERE anulada.ausencia_id = base.id AND anulada.tipo_codigo = N'ANULADA' AND anulada.id <> i.id)))
        THROW 51155, 'ASSISTANCE_ABSENCE_INVALID', 1;
    -- Un acortamiento termina antes del último día vigente y no antes del primero.
    IF EXISTS (
        SELECT 1 FROM inserted i
          JOIN dbo.ausencias_programadas base ON base.id = i.ausencia_id
         WHERE i.tipo_codigo = N'ACORTADA'
           AND (i.hasta < base.desde
                OR i.hasta >= (SELECT MIN(x.hasta) FROM dbo.ausencias_programadas x
                                WHERE x.ausencia_id = base.id AND x.tipo_codigo IN (N'REGISTRADA', N'ACORTADA') AND x.id <> i.id)))
        THROW 51155, 'ASSISTANCE_ABSENCE_INVALID', 1;
    -- Sin solapes entre ausencias vigentes del mismo residente.
    IF EXISTS (
        SELECT 1 FROM inserted i
          JOIN dbo.ausencias_programadas other ON other.centro_id = i.centro_id AND other.residente_id = i.residente_id
                                              AND other.tipo_codigo = N'REGISTRADA' AND other.id <> i.id
         WHERE i.tipo_codigo = N'REGISTRADA'
           AND NOT EXISTS (SELECT 1 FROM dbo.ausencias_programadas anulada WHERE anulada.ausencia_id = other.id AND anulada.tipo_codigo = N'ANULADA')
           AND other.desde <= i.hasta
           AND (SELECT MIN(x.hasta) FROM dbo.ausencias_programadas x WHERE x.ausencia_id = other.id AND x.tipo_codigo IN (N'REGISTRADA', N'ACORTADA')) >= i.desde)
        THROW 51156, 'ASSISTANCE_ABSENCE_OVERLAP', 1;
END;
GO

/* ---- Seguridad por filas por centro, con el mismo predicado que el resto (0030, 0090, 0104) ---- */

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.asistencia_centro,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.asistencia_centro AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.asistencia_centro AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.asistencia_pauta,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.asistencia_pauta AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.asistencia_pauta AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.asistencia_dia,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.asistencia_dia AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.asistencia_dia AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.ausencias_programadas,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.ausencias_programadas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.ausencias_programadas AFTER UPDATE;
GO

/* ---- Permisos mínimos del usuario de la aplicación (0091): las filas solo se insertan; nada para residapp_tareas ---- */

GRANT INSERT ON dbo.asistencia_centro TO residapp_aplicacion;
DENY UPDATE, DELETE ON dbo.asistencia_centro TO residapp_aplicacion;
GRANT INSERT ON dbo.asistencia_pauta TO residapp_aplicacion;
DENY UPDATE, DELETE ON dbo.asistencia_pauta TO residapp_aplicacion;
GRANT INSERT ON dbo.asistencia_dia TO residapp_aplicacion;
DENY UPDATE, DELETE ON dbo.asistencia_dia TO residapp_aplicacion;
GRANT INSERT ON dbo.ausencias_programadas TO residapp_aplicacion;
DENY UPDATE, DELETE ON dbo.ausencias_programadas TO residapp_aplicacion;
GO

COMMIT TRANSACTION;
GO
