/*
 * Citas externas de los residentes (pruebas, consultas y tratamientos fuera del centro), PR 1: esquema y reglas, sin pantallas. Diseño aprobado por Orlando el 2026-11-17 en
 * docs/producto/citas-externas-diseno.md (17 valores por defecto y nueve decisiones); condiciones de privacidad en docs/privacidad/citas-externas-lectura-dpo.md (borrador).
 *
 * Toda la ficha de la cita es dato de salud, por eso lleva listas cerradas y límites cortos que la propia base impone: ningún campo admite texto libre largo ni saltos de línea.
 *
 *   citas_externas           La cita, modificable (UPDATE). Día del centro (fecha DATE, hora de Madrid, no UTC) y hora opcional: hora vacía es «por confirmar». Tipo y especialidad de
 *                            lista cerrada. Lugar: una línea de 80 caracteres, solo el nombre del centro sanitario. Qué preparar: casillas más una línea de 100. Transporte y acompañante
 *                            con modo y estado («no necesita» queda confirmado; «sin determinar», pendiente). Cinco estados; «reprogramada» NO es un estado: es un cambio de fecha en el
 *                            historial. Tres niveles de visibilidad para la familia, elegidos a mano (VISIBLE por defecto). version: bloqueo optimista; cada UPDATE la sube en uno.
 *   citas_externas_cambios   Historial de solo añadir (INSTEAD OF UPDATE, DELETE): acción, cuenta, perfil, fecha y hora anterior y nueva, estado anterior y nuevo, motivo de lista cerrada más
 *                            una línea de 100 y causa. La causa BAJA_RESIDENTE solo la admite un trigger con el texto fijo de la aplicación y una baja del residente en la MISMA transacción
 *                            (patrón de 0078).
 *
 * La unidad de la cita no se guarda: es la ubicación vigente del residente, y la separación por unidad la hace la aplicación (la base solo separa por centro). Así un traslado de unidad
 * mueve la visibilidad de sus citas.
 *
 * Una cita realizada, no acudida o cancelada no vuelve a cambiar (como citas, 0054). No se puede borrar. Identidad (residente, centro, autoría) inmutable.
 *
 * Seguridad por filas por centro y permisos mínimos del usuario de la aplicación (0091): citas_externas, INSERT y UPDATE (sin DELETE); citas_externas_cambios, solo INSERT.
 *
 * Permiso nuevo EXTERNAL_APPOINTMENT_MANAGE (CJ, 3.1 C; RGPD art. 25.2): Administración registra y cambia siempre; Enfermería solo si Administración se lo concede a su perfil (apagado por
 * defecto). Medicina solo ve. Solo puede tenerlo Enfermería. La lista de permisos y el trigger repiten los de 0094 más EXTERNAL_APPOINTMENT_MANAGE.
 *
 * La auditoría de cada cambio la escribe la aplicación en dbo.eventos_auditoria con solo identificadores, nunca el contenido de un campo.
 */

SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO

CREATE TABLE dbo.citas_externas (
    id                           UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_citas_externas PRIMARY KEY,
    centro_id                    UNIQUEIDENTIFIER NOT NULL,
    residente_id                 UNIQUEIDENTIFIER NOT NULL,
    fecha                        DATE NOT NULL,
    hora                         TIME(0) NULL,
    tipo_codigo                  NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    especialidad_codigo          NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NULL,
    lugar                        NVARCHAR(80) NULL,
    transporte_modo_codigo       NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_cext_transporte_modo DEFAULT ('SIN_DETERMINAR'),
    transporte_estado_codigo     NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_cext_transporte_estado DEFAULT ('PENDIENTE'),
    acompanante_modo_codigo      NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_cext_acompanante_modo DEFAULT ('SIN_DETERMINAR'),
    acompanante_estado_codigo    NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_cext_acompanante_estado DEFAULT ('PENDIENTE'),
    preparar_ayuno               BIT NOT NULL CONSTRAINT DF_cext_prep_ayuno DEFAULT (0),
    preparar_informe             BIT NOT NULL CONSTRAINT DF_cext_prep_informe DEFAULT (0),
    preparar_tarjeta             BIT NOT NULL CONSTRAINT DF_cext_prep_tarjeta DEFAULT (0),
    preparar_medicacion          BIT NOT NULL CONSTRAINT DF_cext_prep_medicacion DEFAULT (0),
    preparar_silla_ruedas        BIT NOT NULL CONSTRAINT DF_cext_prep_silla DEFAULT (0),
    preparar_otra                BIT NOT NULL CONSTRAINT DF_cext_prep_otra DEFAULT (0),
    preparar_texto               NVARCHAR(100) NULL,
    estado_codigo                NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_cext_estado DEFAULT ('PROGRAMADA'),
    visibilidad_familia_codigo   NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT DF_cext_visibilidad DEFAULT ('VISIBLE'),
    version                      INT NOT NULL CONSTRAINT DF_cext_version DEFAULT (1),
    creada_por_cuenta_id         UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_cext_cuenta REFERENCES dbo.cuentas(id),
    creada_por_perfil            NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    creada_en                    DATETIME2(3) NOT NULL,
    modificada_en                DATETIME2(3) NOT NULL,
    CONSTRAINT UQ_cext_centro UNIQUE (centro_id, id),
    CONSTRAINT FK_cext_residente FOREIGN KEY (centro_id, residente_id) REFERENCES dbo.residentes(centro_id, id),
    CONSTRAINT CK_cext_tipo CHECK (tipo_codigo IN ('PRUEBA', 'CONSULTA', 'TRATAMIENTO', 'OTRA')),
    CONSTRAINT CK_cext_especialidad CHECK (especialidad_codigo IS NULL OR especialidad_codigo IN (
        'GERIATRIA', 'MEDICINA_INTERNA', 'CARDIOLOGIA', 'NEUMOLOGIA', 'DIGESTIVO', 'ENDOCRINOLOGIA', 'NEFROLOGIA', 'NEUROLOGIA', 'TRAUMATOLOGIA_REHAB',
        'OFTALMOLOGIA', 'OTORRINOLARINGOLOGIA', 'DERMATOLOGIA', 'ODONTOLOGIA', 'PODOLOGIA', 'ANALISIS', 'RADIOLOGIA_ECOGRAFIA', 'SALUD_MENTAL', 'ONCOLOGIA',
        'RADIOTERAPIA', 'INFECCIOSAS', 'CONDUCTAS_ADICTIVAS', 'PALIATIVOS', 'GENETICA', 'GINECOLOGIA', 'UROLOGIA', 'PROCTOLOGIA', 'OTRA')),
    -- Una línea, sin saltos ni tabuladores y con algo escrito (DPO, apartado 3: un texto libre acaba conteniendo lo que nadie previó).
    CONSTRAINT CK_cext_lugar CHECK (lugar IS NULL OR (LEN(LTRIM(RTRIM(lugar))) > 0
        AND CHARINDEX(NCHAR(10), lugar) = 0 AND CHARINDEX(NCHAR(13), lugar) = 0 AND CHARINDEX(NCHAR(9), lugar) = 0)),
    CONSTRAINT CK_cext_preparar_texto CHECK (preparar_texto IS NULL OR (LEN(LTRIM(RTRIM(preparar_texto))) > 0
        AND CHARINDEX(NCHAR(10), preparar_texto) = 0 AND CHARINDEX(NCHAR(13), preparar_texto) = 0 AND CHARINDEX(NCHAR(9), preparar_texto) = 0)),
    CONSTRAINT CK_cext_transporte_modo CHECK (transporte_modo_codigo IN ('FAMILIA', 'CENTRO', 'AMBULANCIA', 'SIN_DETERMINAR')),
    CONSTRAINT CK_cext_acompanante_modo CHECK (acompanante_modo_codigo IN ('FAMILIAR', 'PERSONAL_CENTRO', 'LO_LLEVA_TRANSPORTE', 'NO_NECESITA', 'SIN_DETERMINAR')),
    CONSTRAINT CK_cext_transporte_estado CHECK (transporte_estado_codigo IN ('PENDIENTE', 'CONFIRMADO')),
    CONSTRAINT CK_cext_acompanante_estado CHECK (acompanante_estado_codigo IN ('PENDIENTE', 'CONFIRMADO')),
    -- «Sin determinar» siempre pendiente; «no necesita» queda confirmado solo (diseño, valor 8).
    CONSTRAINT CK_cext_transporte_coherente CHECK (transporte_modo_codigo <> 'SIN_DETERMINAR' OR transporte_estado_codigo = 'PENDIENTE'),
    CONSTRAINT CK_cext_acompanante_coherente CHECK (
        (acompanante_modo_codigo <> 'SIN_DETERMINAR' OR acompanante_estado_codigo = 'PENDIENTE')
        AND (acompanante_modo_codigo <> 'NO_NECESITA' OR acompanante_estado_codigo = 'CONFIRMADO')),
    CONSTRAINT CK_cext_estado CHECK (estado_codigo IN ('PROGRAMADA', 'CONFIRMADA_HOSPITAL', 'REALIZADA', 'NO_ACUDIDA', 'CANCELADA')),
    CONSTRAINT CK_cext_visibilidad CHECK (visibilidad_familia_codigo IN ('VISIBLE', 'SIN_ESPECIALIDAD', 'OCULTA')),
    CONSTRAINT CK_cext_version CHECK (version >= 1),
    CONSTRAINT CK_cext_perfil CHECK (creada_por_perfil IN ('ADMINISTRACION', 'ENFERMERIA'))
);
CREATE INDEX IX_cext_agenda ON dbo.citas_externas (centro_id, fecha);
CREATE INDEX IX_cext_residente ON dbo.citas_externas (centro_id, residente_id, fecha);
GO

CREATE TABLE dbo.citas_externas_cambios (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_cext_cambios PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL,
    cita_id                  UNIQUEIDENTIFIER NOT NULL,
    numero                   INT NOT NULL,
    accion_codigo            NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    actor_cuenta_id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_cext_cambios_cuenta REFERENCES dbo.cuentas(id),
    actor_perfil_codigo      NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    fecha_anterior           DATE NULL,
    fecha_nueva              DATE NULL,
    hora_anterior            TIME(0) NULL,
    hora_nueva               TIME(0) NULL,
    estado_anterior_codigo   NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NULL,
    estado_nuevo_codigo      NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NULL,
    motivo_codigo            NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NULL,
    motivo                   NVARCHAR(100) NULL,
    causa_codigo             NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NULL,
    ocurrido_en              DATETIME2(3) NOT NULL,
    CONSTRAINT FK_cext_cambios_cita FOREIGN KEY (centro_id, cita_id) REFERENCES dbo.citas_externas (centro_id, id),
    CONSTRAINT CK_cext_cambios_accion CHECK (accion_codigo IN ('CREADA', 'MODIFICADA', 'REPROGRAMADA', 'ESTADO_CAMBIADO', 'CANCELADA')),
    CONSTRAINT CK_cext_cambios_perfil CHECK (actor_perfil_codigo IN ('ADMINISTRACION', 'ENFERMERIA')),
    CONSTRAINT CK_cext_cambios_numero CHECK (numero > 0),
    CONSTRAINT CK_cext_cambios_estado_anterior CHECK (estado_anterior_codigo IS NULL OR estado_anterior_codigo IN ('PROGRAMADA', 'CONFIRMADA_HOSPITAL', 'REALIZADA', 'NO_ACUDIDA', 'CANCELADA')),
    CONSTRAINT CK_cext_cambios_estado_nuevo CHECK (estado_nuevo_codigo IS NULL OR estado_nuevo_codigo IN ('PROGRAMADA', 'CONFIRMADA_HOSPITAL', 'REALIZADA', 'NO_ACUDIDA', 'CANCELADA')),
    -- Motivo de lista cerrada, sin datos clínicos (DPO, apartado 3), más una línea corta opcional.
    CONSTRAINT CK_cext_cambios_motivo_codigo CHECK (motivo_codigo IS NULL OR motivo_codigo IN (
        'CAMBIO_DEL_HOSPITAL', 'FALTA_TRANSPORTE_O_ACOMPANANTE', 'CAMBIO_EN_EL_CENTRO', 'PETICION_DE_LA_FAMILIA', 'ERROR_DE_REGISTRO', 'OTRO')),
    CONSTRAINT CK_cext_cambios_motivo CHECK (motivo IS NULL OR (LEN(LTRIM(RTRIM(motivo))) > 0
        AND CHARINDEX(NCHAR(10), motivo) = 0 AND CHARINDEX(NCHAR(13), motivo) = 0 AND CHARINDEX(NCHAR(9), motivo) = 0)),
    -- Reprogramar o cancelar lleva motivo de la lista; salvo la cancelación por baja, que lleva la causa y el texto fijo. El resto de acciones no llevan motivo.
    CONSTRAINT CK_cext_cambios_motivo_exigido CHECK (
        (accion_codigo IN ('REPROGRAMADA', 'CANCELADA') AND (motivo_codigo IS NOT NULL OR causa_codigo IS NOT NULL))
        OR (accion_codigo NOT IN ('REPROGRAMADA', 'CANCELADA') AND motivo_codigo IS NULL AND motivo IS NULL)),
    CONSTRAINT CK_cext_cambios_causa CHECK (causa_codigo IS NULL OR (causa_codigo = 'BAJA_RESIDENTE' AND accion_codigo = 'CANCELADA' AND motivo_codigo IS NULL))
);
CREATE UNIQUE INDEX UX_cext_cambios_numero ON dbo.citas_externas_cambios (cita_id, numero);
GO

CREATE TRIGGER dbo.TR_cext_cambios_immutable ON dbo.citas_externas_cambios INSTEAD OF UPDATE, DELETE AS
    THROW 50930, 'EXTERNAL_APPOINTMENT_CHANGE_IMMUTABLE', 1;
GO

CREATE TRIGGER dbo.TR_cext_no_delete ON dbo.citas_externas INSTEAD OF DELETE AS
    THROW 50931, 'EXTERNAL_APPOINTMENT_NO_DELETE', 1;
GO

/* De una cita no cambian el residente, el centro ni la autoría. Una cita realizada, no acudida o cancelada no vuelve a cambiar. Cada cambio sube la versión en uno
   (bloqueo optimista: la aplicación hace UPDATE ... WHERE version = la vista, y si nadie lo hizo antes, la siguiente persona no pisa). */
CREATE TRIGGER dbo.TR_cext_update_guard ON dbo.citas_externas AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(id) OR UPDATE(centro_id) OR UPDATE(residente_id) OR UPDATE(creada_por_cuenta_id) OR UPDATE(creada_por_perfil) OR UPDATE(creada_en)
        THROW 50932, 'EXTERNAL_APPOINTMENT_IDENTITY_IMMUTABLE', 1;
    IF EXISTS (SELECT 1 FROM deleted WHERE estado_codigo IN ('REALIZADA', 'NO_ACUDIDA', 'CANCELADA'))
        THROW 50933, 'EXTERNAL_APPOINTMENT_CLOSED', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.id = i.id WHERE i.version <> d.version + 1)
        THROW 50934, 'EXTERNAL_APPOINTMENT_VERSION_INVALID', 1;
END;
GO

/* La causa BAJA_RESIDENTE solo se admite con el texto fijo de la aplicación y una baja de ese residente creada en la MISMA transacción (bajas_residente.transaccion_id, 0042):
   no hay otra forma de anotar una cancelación «por la baja». */
CREATE TRIGGER dbo.TR_cext_cambios_causa ON dbo.citas_externas_cambios AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
         WHERE i.causa_codigo IS NOT NULL
           AND (ISNULL(i.motivo, N'') <> N'Cancelada por el sistema al dar de baja al residente.'
                OR NOT EXISTS (SELECT 1 FROM dbo.citas_externas c WITH (FORCESEEK)
                                 JOIN dbo.bajas_residente b ON b.residente_id = c.residente_id AND b.centro_id = c.centro_id
                                WHERE c.id = i.cita_id AND c.centro_id = i.centro_id AND b.transaccion_id = CURRENT_TRANSACTION_ID())))
        THROW 50935, 'EXTERNAL_APPOINTMENT_CAUSE_INVALID', 1;
END;
GO

-- Seguridad por filas por centro, con el mismo predicado que el resto (0030, 0032, 0090).
ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas_cambios,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas_cambios AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas_cambios AFTER UPDATE;
GO

-- Permisos mínimos del usuario de la aplicación (0091): la cita se inserta y se actualiza, sin borrar; el historial solo se inserta.
GRANT INSERT, UPDATE ON dbo.citas_externas TO residapp_aplicacion;
DENY DELETE ON dbo.citas_externas TO residapp_aplicacion;
GRANT INSERT ON dbo.citas_externas_cambios TO residapp_aplicacion;
DENY UPDATE, DELETE ON dbo.citas_externas_cambios TO residapp_aplicacion;
GO

-- Catálogo de permisos: EXTERNAL_APPOINTMENT_MANAGE, solo para Enfermería.
ALTER TABLE dbo.permisos_perfil DROP CONSTRAINT CK_pp_code;
ALTER TABLE dbo.permisos_perfil ADD CONSTRAINT CK_pp_code CHECK (permiso_codigo IN
    ('RESIDENT_IDENTITY_CREATE', 'BASELINE_INITIAL_COMPLETE', 'BASELINE_REEVALUATE',
     'BASELINE_DRAFT_CONTRIBUTE', 'CLINICAL_DETAIL_READ', 'REFERENCE_RANGES_MANAGE', 'PROCESS_DEADLINES_MANAGE', 'RESIDENT_PHOTO_MANAGE',
     'QUALITY_REVIEW', 'HEALTH_BACKGROUND_EDIT', 'EXTERNAL_APPOINTMENT_MANAGE'));
GO

ALTER TRIGGER dbo.TR_pp_profile_catalog ON dbo.permisos_perfil AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
          JOIN dbo.ambitos_perfil p ON p.id = i.ambito_perfil_id
         WHERE (i.permiso_codigo IN ('RESIDENT_IDENTITY_CREATE', 'RESIDENT_PHOTO_MANAGE', 'EXTERNAL_APPOINTMENT_MANAGE') AND p.perfil_codigo <> 'ENFERMERIA')
            OR (i.permiso_codigo IN ('BASELINE_INITIAL_COMPLETE', 'BASELINE_REEVALUATE', 'BASELINE_DRAFT_CONTRIBUTE', 'HEALTH_BACKGROUND_EDIT')
                AND p.perfil_codigo NOT IN ('ENFERMERIA', 'MEDICINA'))
            OR (i.permiso_codigo IN ('CLINICAL_DETAIL_READ', 'PROCESS_DEADLINES_MANAGE', 'QUALITY_REVIEW') AND p.perfil_codigo <> 'DIRECCION_CLINICA'))
        THROW 50420, 'PROFILE_PERMISSION_NOT_ALLOWED', 1;
END;
GO

COMMIT TRANSACTION;
GO
