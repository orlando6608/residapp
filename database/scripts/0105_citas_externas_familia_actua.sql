/*
 * Citas externas de los residentes, PR 6: la familia actúa, y correos (docs/tareas/alta-prioridad/pendientes-citas-externas.md, apartado 11; docs/privacidad/citas-externas-lectura-dpo.md,
 * apartados 4.4, 4.5 y 5.2; ocho decisiones de Orlando, 2026-11-19). Cuatro tablas de solo añadir (patrón de 0098 y 0104: el estado vigente es la fila de mayor numero, que hace de versión; sin
 * texto libre; ninguna guarda un nombre), el índice de los envíos de correo y los procedimientos que reservan y comprueban los avisos.
 *
 *   citas_externas_encargos       «Me encargo yo» / «Ya no puedo» de un familiar, por cita y por ámbito (TRANSPORTE o ACOMPANANTE). Guarda el VÍNCULO del familiar con el residente, no su nombre ni su
 *                                 cuenta (el nombre se resuelve al leer, y al anonimizar al familiar, 0087, sale «Persona anonimizada»). Quien lo tiene ahora es la última fila si es ME_ENCARGO y sigue
 *                                 valiendo (dbo.fn_citas_externas_encargos_vigentes). La asignación es ATÓMICA: el índice único (cita, ámbito, numero) hace que, de dos pulsaciones a la vez, gane una y la otra
 *                                 reciba la violación única; el disparador exige la secuencia (primero ME_ENCARGO; un ME_ENCARGO solo sobre una cita libre; SOLTAR y LIBERADO_AUTOMATICO solo del que la tiene).
 *   citas_externas_no_puedo       «No puedo» de un familiar en una cita (decisión 3): revocable; excluye a esa persona del correo «sigue sin cubrir». El personal ve solo un recuento, nunca el nombre ni un
 *                                 total por familiar (el DPO no admite perfilar a las familias).
 *   citas_externas_correo_centro  Interruptor de los avisos por correo del centro, apagado por defecto (RGPD art. 25.2). Encenderlo exige una confirmación y que el de las familias (0104) esté encendido.
 *   citas_externas_correo_familiar Baja de UN familiar de los avisos por correo de citas externas (art. 21 RGPD): la registra Administración. Sin fila, recibe los avisos.
 *
 * Los avisos (decisiones 1, 2, 5 y 6): plantillas AVISO_CITA_EXTERNA (a 7 días y la víspera) y AVISO_CITA_EXTERNA_SIN_CUBRIR (a 2 días, solo si sigue sin cubrir), un correo por persona. La clave de
 * envío es «tramo|fecha de la cita» (7D|aaaa-mm-dd), así que mover la cita crea avisos nuevos sin duplicar. A 7 días y la víspera escribe a quien tiene un encargo vigente y, si nadie lo tiene, a todos los
 * autorizados con correo confirmado; a 2 días, solo a quien no tiene encargo ni dijo «No puedo», y solo si hay algo que la familia puede cubrir. Un encargo cuenta como cubierto (decisión 6).
 *
 *   dbo.fn_familiares_con_acceso(@vinculo, @dia)   El vínculo, si PUEDE recibir y asumir hoy: vínculo vigente, residente activo y sin suspensión, autorización Activa en @dia, cuenta ACTIVA, perfil FAMILIAR
 *                                                 vigente en el centro y activación completada (una invitación de alta sin usar no cuenta). Es lo mismo que SqlFamilyPortalReader.AccessibleLinks más
 *                                                 lo que esa consulta no mira (decisión 5); una prueba las compara.
 *   dbo.fn_citas_externas_encargos_vigentes(@hoy)  Quién tiene cada ámbito hoy: la última fila ME_ENCARGO, de la fecha actual de la cita, con el modo de la cita aún de familia y con acceso. Una reprogramación,
 *                                                 un cambio de modo o la pérdida de acceso la «sueltan sola», sin escribir nada.
 *   dbo.calcular_avisos_citas_externas(@hoy, @cita)  Para cada cita de hoy+1, hoy+2 y hoy+7 (o solo @cita) y cada familiar vinculado, si el aviso procede (motivo_codigo nulo) o por qué no. Una sola regla para
 *                                                 reservar y para comprobar. Es un procedimiento y no una función en línea porque el cálculo por filas era inviable con muchas citas.
 *   dbo.reservar_avisos_citas_externas[_en]        WITH EXECUTE AS OWNER, en la franja de las 10:00 a las 18:59 (Madrid), como el recordatorio (0092); el rol de la tarea no recibe INSERT ni lectura de
 *                                                 los datos clínicos de la cita (lugar, especialidad, qué preparar). Devuelve solo cuántas filas creó.
 *   dbo.comprobar_aviso_cita_externa[_en]          Devuelve el motivo por el que un aviso reservado ya no debe salir (o nulo si sale) y, para la comprobación de acceso del portal, la cuenta, el vínculo y la fecha: la tarea lo llama al reclamarlo.
 * dbo.cerrar_envios_correo_sin_resolver (0089, 0092) se reescribe para incluir las plantillas nuevas (si no, un aviso atascado quedaría PENDIENTE para siempre).
 *
 * UX_envios_correo_referencia (0081) era (plantilla, referencia, clave): con «un mensaje por persona» solo se habría reservado el primer familiar de cada cita y los demás se habrían perdido sin aviso.
 * Se rehace con familiar_id; los tres avisos anteriores tienen un solo destinatario por referencia y clave, así que no cambia nada para ellos.
 *
 * Códigos de error nuevos: 50942 EXTERNAL_APPOINTMENT_FAMILY_ACTION_IMMUTABLE, 50943 EXTERNAL_APPOINTMENT_ARRANGEMENT_SEQUENCE_INVALID, 50944 EXTERNAL_APPOINTMENT_LINK_INVALID,
 * 50945 EXTERNAL_APPOINTMENT_MAIL_CENTER_INVALID, 50946 EXTERNAL_APPOINTMENT_MAIL_SEQUENCE_INVALID, 50947 EXTERNAL_APPOINTMENT_MAIL_NEEDS_FAMILY, 50948 EXTERNAL_APPOINTMENT_REFUSAL_SEQUENCE_INVALID.
 * Retención: sin decidir (propuesta del DPO: 12 meses desde la fecha de la cita, como envios_correo); con el expurgo del residente se borran antes (docs/privacidad/expurgo-y-supresion.md).
 */

SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO

/* ---- Quién se encarga ---- */

CREATE TABLE dbo.citas_externas_encargos (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_cexta PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL,
    cita_id                  UNIQUEIDENTIFIER NOT NULL,
    ambito_codigo            NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    numero                   INT NOT NULL,
    accion_codigo            NVARCHAR(24) COLLATE Latin1_General_100_BIN2 NOT NULL,
    vinculo_id               UNIQUEIDENTIFIER NOT NULL,
    actor_perfil_codigo      NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    fecha_cita               DATE NOT NULL,
    cambiado_en              DATETIME2(3) NOT NULL,
    CONSTRAINT FK_cexta_cita FOREIGN KEY (centro_id, cita_id) REFERENCES dbo.citas_externas (centro_id, id),
    CONSTRAINT FK_cexta_vinculo FOREIGN KEY (centro_id, vinculo_id) REFERENCES dbo.residentes_familiares (centro_id, id),
    CONSTRAINT CK_cexta_numero CHECK (numero >= 1),
    CONSTRAINT CK_cexta_ambito CHECK (ambito_codigo IN ('TRANSPORTE', 'ACOMPANANTE')),
    CONSTRAINT CK_cexta_accion CHECK (accion_codigo IN ('ME_ENCARGO', 'SOLTAR', 'LIBERADO_AUTOMATICO')),
    -- Lo hace la familia (tomar o soltar) o el sistema (soltar a quien ya no tiene acceso antes de que otra persona la tome); nunca el personal.
    CONSTRAINT CK_cexta_actor CHECK ((accion_codigo = 'LIBERADO_AUTOMATICO' AND actor_perfil_codigo = 'SISTEMA') OR (accion_codigo IN ('ME_ENCARGO', 'SOLTAR') AND actor_perfil_codigo = 'FAMILIAR'))
);
-- La asignación atómica: dos filas con el mismo número en la misma cita y ámbito no pueden existir.
CREATE UNIQUE INDEX UX_cexta_numero ON dbo.citas_externas_encargos (cita_id, ambito_codigo, numero);
CREATE INDEX IX_cexta_vinculo ON dbo.citas_externas_encargos (centro_id, vinculo_id);
GO

CREATE TABLE dbo.citas_externas_no_puedo (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_cextr PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL,
    cita_id                  UNIQUEIDENTIFIER NOT NULL,
    vinculo_id               UNIQUEIDENTIFIER NOT NULL,
    numero                   INT NOT NULL,
    no_puede                 BIT NOT NULL,
    cambiado_en              DATETIME2(3) NOT NULL,
    CONSTRAINT FK_cextr_cita FOREIGN KEY (centro_id, cita_id) REFERENCES dbo.citas_externas (centro_id, id),
    CONSTRAINT FK_cextr_vinculo FOREIGN KEY (centro_id, vinculo_id) REFERENCES dbo.residentes_familiares (centro_id, id),
    CONSTRAINT CK_cextr_numero CHECK (numero >= 1)
);
CREATE UNIQUE INDEX UX_cextr_numero ON dbo.citas_externas_no_puedo (cita_id, vinculo_id, numero);
GO

/* ---- Los avisos por correo: interruptor del centro y baja de un familiar ---- */

CREATE TABLE dbo.citas_externas_correo_centro (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_cextcc PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_cextcc_centro REFERENCES dbo.centros(id),
    numero                   INT NOT NULL,
    correo_activo            BIT NOT NULL,
    confirma_informada       BIT NOT NULL,
    cambiado_por_cuenta_id   UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_cextcc_cuenta REFERENCES dbo.cuentas(id),
    cambiado_por_perfil      NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    cambiado_en              DATETIME2(3) NOT NULL,
    CONSTRAINT CK_cextcc_numero CHECK (numero >= 1),
    CONSTRAINT CK_cextcc_perfil CHECK (cambiado_por_perfil = 'ADMINISTRACION'),
    -- Encender pide confirmar que las familias saben que recibirán estos avisos; apagar no pide nada.
    CONSTRAINT CK_cextcc_confirmacion CHECK (correo_activo = 0 OR confirma_informada = 1)
);
CREATE UNIQUE INDEX UX_cextcc_numero ON dbo.citas_externas_correo_centro (centro_id, numero);
GO

CREATE TABLE dbo.citas_externas_correo_familiar (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_cextcf PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL,
    familiar_id              UNIQUEIDENTIFIER NOT NULL,
    numero                   INT NOT NULL,
    sin_avisos               BIT NOT NULL,
    cambiado_por_cuenta_id   UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_cextcf_cuenta REFERENCES dbo.cuentas(id),
    cambiado_por_perfil      NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    cambiado_en              DATETIME2(3) NOT NULL,
    CONSTRAINT FK_cextcf_familiar FOREIGN KEY (centro_id, familiar_id) REFERENCES dbo.familiares (centro_id, id),
    CONSTRAINT CK_cextcf_numero CHECK (numero >= 1),
    CONSTRAINT CK_cextcf_perfil CHECK (cambiado_por_perfil = 'ADMINISTRACION')
);
CREATE UNIQUE INDEX UX_cextcf_numero ON dbo.citas_externas_correo_familiar (centro_id, familiar_id, numero);
GO

/* ---- Solo se añade, y la secuencia ---- */

CREATE TRIGGER dbo.TR_cexta_immutable ON dbo.citas_externas_encargos INSTEAD OF UPDATE, DELETE AS
    THROW 50942, 'EXTERNAL_APPOINTMENT_FAMILY_ACTION_IMMUTABLE', 1;
GO
CREATE TRIGGER dbo.TR_cextr_immutable ON dbo.citas_externas_no_puedo INSTEAD OF UPDATE, DELETE AS
    THROW 50942, 'EXTERNAL_APPOINTMENT_FAMILY_ACTION_IMMUTABLE', 1;
GO
CREATE TRIGGER dbo.TR_cextcc_immutable ON dbo.citas_externas_correo_centro INSTEAD OF UPDATE, DELETE AS
    THROW 50942, 'EXTERNAL_APPOINTMENT_FAMILY_ACTION_IMMUTABLE', 1;
GO
CREATE TRIGGER dbo.TR_cextcf_immutable ON dbo.citas_externas_correo_familiar INSTEAD OF UPDATE, DELETE AS
    THROW 50942, 'EXTERNAL_APPOINTMENT_FAMILY_ACTION_IMMUTABLE', 1;
GO

/* El vínculo es del residente de la cita. La primera fila de cada cita y ámbito es un ME_ENCARGO; un ME_ENCARGO solo cabe sobre una cita libre (la fila anterior es una suelta); soltar solo lo hace
   quien la tiene (la fila anterior es su ME_ENCARGO). */
CREATE TRIGGER dbo.TR_cexta_reglas ON dbo.citas_externas_encargos AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
          JOIN dbo.citas_externas c ON c.centro_id = i.centro_id AND c.id = i.cita_id
          JOIN dbo.residentes_familiares link ON link.centro_id = i.centro_id AND link.id = i.vinculo_id
         WHERE link.residente_id <> c.residente_id)
        THROW 50944, 'EXTERNAL_APPOINTMENT_LINK_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i
          LEFT JOIN dbo.citas_externas_encargos previous ON previous.cita_id = i.cita_id AND previous.ambito_codigo = i.ambito_codigo AND previous.numero = i.numero - 1
         WHERE (i.numero = 1 AND i.accion_codigo <> 'ME_ENCARGO')
            OR (i.numero > 1 AND (previous.id IS NULL
                OR (i.accion_codigo = 'ME_ENCARGO' AND previous.accion_codigo = 'ME_ENCARGO')
                OR (i.accion_codigo IN ('SOLTAR', 'LIBERADO_AUTOMATICO') AND (previous.accion_codigo <> 'ME_ENCARGO' OR previous.vinculo_id <> i.vinculo_id)))))
        THROW 50943, 'EXTERNAL_APPOINTMENT_ARRANGEMENT_SEQUENCE_INVALID', 1;
END;
GO

CREATE TRIGGER dbo.TR_cextr_reglas ON dbo.citas_externas_no_puedo AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
          JOIN dbo.citas_externas c ON c.centro_id = i.centro_id AND c.id = i.cita_id
          JOIN dbo.residentes_familiares link ON link.centro_id = i.centro_id AND link.id = i.vinculo_id
         WHERE link.residente_id <> c.residente_id)
        THROW 50944, 'EXTERNAL_APPOINTMENT_LINK_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i
          LEFT JOIN dbo.citas_externas_no_puedo previous ON previous.cita_id = i.cita_id AND previous.vinculo_id = i.vinculo_id AND previous.numero = i.numero - 1
         WHERE (i.numero = 1 AND i.no_puede = 0)
            OR (i.numero > 1 AND (previous.id IS NULL OR previous.no_puede = i.no_puede)))
        THROW 50948, 'EXTERNAL_APPOINTMENT_REFUSAL_SEQUENCE_INVALID', 1;
END;
GO

/* Ni en el centro reservado de Plataforma; cada fila cambia el valor de la anterior; no se encienden los correos con las familias apagadas. */
CREATE TRIGGER dbo.TR_cextcc_reglas ON dbo.citas_externas_correo_centro AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.centros c ON c.id = i.centro_id WHERE c.codigo = 'PLATAFORMA')
        THROW 50945, 'EXTERNAL_APPOINTMENT_MAIL_CENTER_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i
          LEFT JOIN dbo.citas_externas_correo_centro previous ON previous.centro_id = i.centro_id AND previous.numero = i.numero - 1
         WHERE (i.numero = 1 AND i.correo_activo = 0)
            OR (i.numero > 1 AND (previous.id IS NULL OR previous.correo_activo = i.correo_activo)))
        THROW 50946, 'EXTERNAL_APPOINTMENT_MAIL_SEQUENCE_INVALID', 1;
    IF EXISTS (
        SELECT 1 FROM inserted i
         WHERE i.correo_activo = 1
           AND NOT EXISTS (SELECT 1 FROM dbo.citas_externas_familia_centro family
                            WHERE family.centro_id = i.centro_id AND family.familia_ve = 1
                              AND family.numero = (SELECT MAX(latest.numero) FROM dbo.citas_externas_familia_centro latest WHERE latest.centro_id = family.centro_id)))
        THROW 50947, 'EXTERNAL_APPOINTMENT_MAIL_NEEDS_FAMILY', 1;
END;
GO

CREATE TRIGGER dbo.TR_cextcf_reglas ON dbo.citas_externas_correo_familiar AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
          LEFT JOIN dbo.citas_externas_correo_familiar previous ON previous.centro_id = i.centro_id AND previous.familiar_id = i.familiar_id AND previous.numero = i.numero - 1
         WHERE (i.numero = 1 AND i.sin_avisos = 0)
            OR (i.numero > 1 AND (previous.id IS NULL OR previous.sin_avisos = i.sin_avisos)))
        THROW 50946, 'EXTERNAL_APPOINTMENT_MAIL_SEQUENCE_INVALID', 1;
END;
GO

-- Seguridad por filas por centro, con el mismo predicado que el resto (0030, 0032, 0090, 0101, 0103, 0104).
ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas_encargos,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas_encargos AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas_encargos AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas_no_puedo,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas_no_puedo AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas_no_puedo AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas_correo_centro,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas_correo_centro AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas_correo_centro AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas_correo_familiar,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas_correo_familiar AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.citas_externas_correo_familiar AFTER UPDATE;
GO

-- Permisos mínimos del usuario de la aplicación (0091): las filas solo se insertan.
GRANT INSERT ON dbo.citas_externas_encargos TO residapp_aplicacion;
DENY UPDATE, DELETE ON dbo.citas_externas_encargos TO residapp_aplicacion;
GRANT INSERT ON dbo.citas_externas_no_puedo TO residapp_aplicacion;
DENY UPDATE, DELETE ON dbo.citas_externas_no_puedo TO residapp_aplicacion;
GRANT INSERT ON dbo.citas_externas_correo_centro TO residapp_aplicacion;
DENY UPDATE, DELETE ON dbo.citas_externas_correo_centro TO residapp_aplicacion;
GRANT INSERT ON dbo.citas_externas_correo_familiar TO residapp_aplicacion;
DENY UPDATE, DELETE ON dbo.citas_externas_correo_familiar TO residapp_aplicacion;
GO

/* ---- Los envíos de correo: plantillas, motivos y el índice con el familiar ---- */

ALTER TABLE dbo.envios_correo DROP CONSTRAINT CK_envios_correo_plantilla;
GO
ALTER TABLE dbo.envios_correo ADD CONSTRAINT CK_envios_correo_plantilla CHECK (plantilla_codigo IN
    ('AVISO_PROPUESTA_CITA', 'AVISO_CAMBIO_CITA', 'RECORDATORIO_CITA', 'AVISO_CITA_EXTERNA', 'AVISO_CITA_EXTERNA_SIN_CUBRIR'));
GO
ALTER TABLE dbo.envios_correo DROP CONSTRAINT CK_envios_correo_motivo;
GO
ALTER TABLE dbo.envios_correo ADD CONSTRAINT CK_envios_correo_motivo CHECK (motivo_codigo IS NULL OR motivo_codigo IN
    ('SIN_CORREO', 'SIN_ACCESO', 'DIRECCION_NO_CONFIRMADA', 'PROPUESTA_NO_VIGENTE', 'CITA_NO_VIGENTE', 'CITA_PASADA', 'CITA_RECIENTE', 'FUERA_DE_PLAZO', 'DESTINO_NO_PERMITIDO',
     'ACS_RECHAZADO', 'ACS_NO_DISPONIBLE', 'TIEMPO_AGOTADO', 'RESULTADO_DESCONOCIDO', 'FAMILIA_NO_VE', 'AVISOS_DESACTIVADOS', 'BAJA_DE_AVISOS', 'YA_CUBIERTA', 'NO_PUEDE'));
GO
DROP INDEX UX_envios_correo_referencia ON dbo.envios_correo;
GO
CREATE UNIQUE INDEX UX_envios_correo_referencia ON dbo.envios_correo (plantilla_codigo, referencia_id, referencia_clave, familiar_id);
GO

/* ---- Una sola regla: quién puede recibir y asumir, quién tiene qué, a quién se avisa ---- */

-- Índices para la regla de acceso (dbo.fn_familiares_con_acceso), que se mira por vínculo y por cuenta: sin ellos recorre toda la tabla de familiares y de invitaciones por cada cita.
CREATE INDEX IX_familiares_cuenta ON dbo.familiares (cuenta_id) INCLUDE (centro_id, correo, correo_confirmado_en);
CREATE INDEX IX_invitaciones_acceso_cuenta ON dbo.invitaciones_acceso (cuenta_id, tipo) INCLUDE (usada_en);
GO

CREATE FUNCTION dbo.fn_familiares_con_acceso(@vinculo UNIQUEIDENTIFIER, @dia DATE)
RETURNS TABLE
AS
RETURN
    SELECT link.centro_id AS centro_id, link.id AS vinculo_id, link.residente_id AS residente_id, link.familiar_id AS familiar_id, fam.cuenta_id AS cuenta_id,
           fam.correo AS correo, fam.correo_confirmado_en AS correo_confirmado_en
      FROM dbo.residentes_familiares link
      JOIN dbo.familiares fam ON fam.id = link.familiar_id AND fam.centro_id = link.centro_id
      JOIN dbo.residentes resident ON resident.id = link.residente_id AND resident.centro_id = link.centro_id
      JOIN dbo.cuentas account ON account.id = fam.cuenta_id
     WHERE link.id = @vinculo AND link.desvinculado_en IS NULL AND resident.estado = 'ACTIVE' AND account.estado = 'ACTIVE'
       AND NOT EXISTS (SELECT 1 FROM dbo.suspensiones_residente suspension
                        WHERE suspension.residente_id = resident.id AND suspension.centro_id = resident.centro_id AND suspension.finalizada_en IS NULL)
       AND EXISTS (SELECT 1 FROM dbo.familiares_autorizaciones_cambios auth
                    WHERE auth.vinculo_id = link.id AND auth.centro_id = link.centro_id AND auth.estado_codigo = 'ACTIVA'
                      AND (auth.valida_hasta IS NULL OR auth.valida_hasta >= @dia)
                      AND auth.numero = (SELECT MAX(latest.numero) FROM dbo.familiares_autorizaciones_cambios latest WHERE latest.vinculo_id = link.id))
       AND EXISTS (SELECT 1 FROM dbo.ambitos_perfil scope
                    WHERE scope.cuenta_id = fam.cuenta_id AND scope.centro_id = link.centro_id AND scope.perfil_codigo = 'FAMILIAR' AND scope.estado = 'ACTIVE' AND scope.revocado_en IS NULL)
       -- Activación completada: sin invitación de alta, o con una ya usada (una invitación de alta aún viva o anulada sin usar quiere decir que la persona no llegó a entrar).
       AND (NOT EXISTS (SELECT 1 FROM dbo.invitaciones_acceso invitation WHERE invitation.cuenta_id = fam.cuenta_id AND invitation.tipo = 'ALTA')
            OR EXISTS (SELECT 1 FROM dbo.invitaciones_acceso invitation WHERE invitation.cuenta_id = fam.cuenta_id AND invitation.tipo = 'ALTA' AND invitation.usada_en IS NOT NULL));
GO

CREATE FUNCTION dbo.fn_citas_externas_encargos_vigentes(@hoy DATE)
RETURNS TABLE
AS
RETURN
    SELECT arrangement.centro_id AS centro_id, arrangement.cita_id AS cita_id, arrangement.ambito_codigo AS ambito_codigo, arrangement.vinculo_id AS vinculo_id, arrangement.numero AS numero
      FROM dbo.citas_externas_encargos arrangement
      JOIN dbo.citas_externas appointment ON appointment.centro_id = arrangement.centro_id AND appointment.id = arrangement.cita_id
      -- APPLY por vínculo: se parte de los pocos encargos y se mira el acceso de cada uno por su clave; calcularlo para todas las familias del sistema por cada cita era inviable.
      CROSS APPLY dbo.fn_familiares_con_acceso(arrangement.vinculo_id, @hoy) access
     WHERE arrangement.accion_codigo = 'ME_ENCARGO'
       AND arrangement.numero = (SELECT MAX(latest.numero) FROM dbo.citas_externas_encargos latest WHERE latest.cita_id = arrangement.cita_id AND latest.ambito_codigo = arrangement.ambito_codigo)
       AND arrangement.fecha_cita = appointment.fecha
       AND appointment.estado_codigo IN ('PROGRAMADA', 'CONFIRMADA_HOSPITAL')
       -- Solo mientras el modo es de la familia Y el personal no lo ha confirmado: confirmado, ya no hay nada «ofrecido».
       AND ((arrangement.ambito_codigo = 'TRANSPORTE' AND appointment.transporte_modo_codigo = 'FAMILIA' AND appointment.transporte_estado_codigo = 'PENDIENTE')
         OR (arrangement.ambito_codigo = 'ACOMPANANTE' AND appointment.acompanante_modo_codigo = 'FAMILIAR' AND appointment.acompanante_estado_codigo = 'PENDIENTE'));
GO

/* A quién se avisa y por qué no: una sola regla para reservar y para comprobar justo antes de enviar. Es un procedimiento y no una función en línea a propósito: con una función, cada fila
   recalculaba los encargos y el acceso de todo el sistema (minutos con unos miles de citas); aquí se calculan una vez, por pasos, y con @cita solo se mira esa cita. Devuelve, para cada cita
   de hoy+1, hoy+2 y hoy+7 y cada familiar vinculado, si el aviso procede (motivo_codigo nulo) o por qué no. */
CREATE PROCEDURE dbo.calcular_avisos_citas_externas
    @hoy DATE,
    @cita UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    CREATE TABLE #candidatos (
        centro_id UNIQUEIDENTIFIER NOT NULL, cita_id UNIQUEIDENTIFIER NOT NULL, residente_id UNIQUEIDENTIFIER NOT NULL, fecha DATE NOT NULL, tramo NVARCHAR(2) COLLATE Latin1_General_100_BIN2 NOT NULL,
        transporte_pendiente_familia BIT NOT NULL, acompanante_pendiente_familia BIT NOT NULL,
        vinculo_id UNIQUEIDENTIFIER NOT NULL, familiar_id UNIQUEIDENTIFIER NOT NULL, correo NVARCHAR(254) NULL, correo_confirmado_en DATETIME2(3) NULL, tiene_acceso BIT NOT NULL);

    INSERT INTO #candidatos
    SELECT appointment.centro_id, appointment.id, appointment.residente_id, appointment.fecha, step.tramo,
           CASE WHEN appointment.transporte_modo_codigo = 'FAMILIA' AND appointment.transporte_estado_codigo = 'PENDIENTE' THEN 1 ELSE 0 END,
           CASE WHEN appointment.acompanante_modo_codigo = 'FAMILIAR' AND appointment.acompanante_estado_codigo = 'PENDIENTE' THEN 1 ELSE 0 END,
           link.id, link.familiar_id, fam.correo, fam.correo_confirmado_en, CASE WHEN access.vinculo_id IS NULL THEN 0 ELSE 1 END
      FROM dbo.citas_externas appointment
      JOIN (SELECT CAST('7D' AS NVARCHAR(2)) COLLATE Latin1_General_100_BIN2 AS tramo, DATEADD(DAY, 7, @hoy) AS fecha
            UNION ALL SELECT '1D', DATEADD(DAY, 1, @hoy) UNION ALL SELECT '2D', DATEADD(DAY, 2, @hoy)) step ON step.fecha = appointment.fecha
      JOIN dbo.residentes_familiares link ON link.centro_id = appointment.centro_id AND link.residente_id = appointment.residente_id AND link.desvinculado_en IS NULL
      JOIN dbo.familiares fam ON fam.id = link.familiar_id AND fam.centro_id = link.centro_id
      OUTER APPLY (SELECT a.vinculo_id FROM dbo.fn_familiares_con_acceso(link.id, appointment.fecha) a) access
     WHERE appointment.estado_codigo IN ('PROGRAMADA', 'CONFIRMADA_HOSPITAL') AND appointment.visibilidad_familia_codigo <> 'OCULTA'
       AND (@cita IS NULL OR appointment.id = @cita);

    -- Quién tiene hoy cada parte (una sola vez) y, de ellos, quién puede recibir el correo: acceso a la fecha de la cita, dirección confirmada y sin baja.
    CREATE TABLE #encargos (cita_id UNIQUEIDENTIFIER NOT NULL, ambito_codigo NVARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL, vinculo_id UNIQUEIDENTIFIER NOT NULL, alcanzable BIT NOT NULL);
    -- Primero los encargos vigentes (pocos), y solo después se mira el acceso de cada uno: unidos en una sola consulta, el plan calculaba el acceso de todo el sistema.
    SELECT held.cita_id, held.centro_id, held.ambito_codigo, held.vinculo_id INTO #vigentes FROM dbo.fn_citas_externas_encargos_vigentes(@hoy) held;
    INSERT INTO #encargos
    SELECT held.cita_id, held.ambito_codigo, held.vinculo_id,
           CASE WHEN reachable.vinculo_id IS NOT NULL AND reachable.correo IS NOT NULL AND reachable.correo_confirmado_en IS NOT NULL
                 AND NOT EXISTS (SELECT 1 FROM dbo.citas_externas_correo_familiar opt_out
                                  WHERE opt_out.centro_id = reachable.centro_id AND opt_out.familiar_id = reachable.familiar_id AND opt_out.sin_avisos = 1
                                    AND opt_out.numero = (SELECT MAX(latest.numero) FROM dbo.citas_externas_correo_familiar latest WHERE latest.centro_id = opt_out.centro_id AND latest.familiar_id = opt_out.familiar_id))
                THEN 1 ELSE 0 END
      FROM #vigentes held
      JOIN dbo.citas_externas held_appointment ON held_appointment.id = held.cita_id AND held_appointment.centro_id = held.centro_id
      OUTER APPLY dbo.fn_familiares_con_acceso(held.vinculo_id, held_appointment.fecha) reachable
     WHERE @cita IS NULL OR held.cita_id = @cita
    OPTION (RECOMPILE);

    SELECT c.centro_id AS centro_id, c.cita_id AS cita_id, c.fecha AS fecha, c.tramo AS tramo, c.vinculo_id AS vinculo_id, c.familiar_id AS familiar_id,
           c.correo AS correo, c.correo_confirmado_en AS correo_confirmado_en,
           CAST(CASE
               WHEN NOT EXISTS (SELECT 1 FROM dbo.citas_externas_familia_centro center_switch
                                 WHERE center_switch.centro_id = c.centro_id AND center_switch.familia_ve = 1
                                   AND center_switch.numero = (SELECT MAX(latest.numero) FROM dbo.citas_externas_familia_centro latest WHERE latest.centro_id = center_switch.centro_id))
                 OR EXISTS (SELECT 1 FROM dbo.citas_externas_familia_residente resident_switch
                             WHERE resident_switch.centro_id = c.centro_id AND resident_switch.residente_id = c.residente_id AND resident_switch.familia_no_ve = 1
                               AND resident_switch.numero = (SELECT MAX(latest.numero) FROM dbo.citas_externas_familia_residente latest
                                                              WHERE latest.centro_id = resident_switch.centro_id AND latest.residente_id = resident_switch.residente_id))
                   THEN 'FAMILIA_NO_VE'
               WHEN NOT EXISTS (SELECT 1 FROM dbo.citas_externas_correo_centro mail_switch
                                 WHERE mail_switch.centro_id = c.centro_id AND mail_switch.correo_activo = 1
                                   AND mail_switch.numero = (SELECT MAX(latest.numero) FROM dbo.citas_externas_correo_centro latest WHERE latest.centro_id = mail_switch.centro_id))
                   THEN 'AVISOS_DESACTIVADOS'
               WHEN c.tiene_acceso = 0 THEN 'SIN_ACCESO'
               WHEN EXISTS (SELECT 1 FROM dbo.citas_externas_correo_familiar opt_out
                             WHERE opt_out.centro_id = c.centro_id AND opt_out.familiar_id = c.familiar_id AND opt_out.sin_avisos = 1
                               AND opt_out.numero = (SELECT MAX(latest.numero) FROM dbo.citas_externas_correo_familiar latest WHERE latest.centro_id = opt_out.centro_id AND latest.familiar_id = opt_out.familiar_id))
                   THEN 'BAJA_DE_AVISOS'
               -- A 7 días y la víspera: a quien tiene un encargo y PUEDE recibir el correo; si nadie lo tiene (o quien lo tiene no puede), a todos.
               WHEN c.tramo IN ('7D', '1D') AND EXISTS (SELECT 1 FROM #encargos e WHERE e.cita_id = c.cita_id AND e.alcanzable = 1)
                    AND NOT EXISTS (SELECT 1 FROM #encargos e WHERE e.cita_id = c.cita_id AND e.vinculo_id = c.vinculo_id)
                   THEN 'YA_CUBIERTA'
               -- A 2 días: solo si la familia aún tiene algo que cubrir, y no a quien ya se encargó.
               WHEN c.tramo = '2D'
                    AND (EXISTS (SELECT 1 FROM #encargos e WHERE e.cita_id = c.cita_id AND e.vinculo_id = c.vinculo_id)
                         OR NOT ((c.transporte_pendiente_familia = 1 AND NOT EXISTS (SELECT 1 FROM #encargos e WHERE e.cita_id = c.cita_id AND e.ambito_codigo = 'TRANSPORTE'))
                              OR (c.acompanante_pendiente_familia = 1 AND NOT EXISTS (SELECT 1 FROM #encargos e WHERE e.cita_id = c.cita_id AND e.ambito_codigo = 'ACOMPANANTE'))))
                   THEN 'YA_CUBIERTA'
               WHEN c.tramo = '2D'
                    AND EXISTS (SELECT 1 FROM dbo.citas_externas_no_puedo refusal
                                 WHERE refusal.cita_id = c.cita_id AND refusal.vinculo_id = c.vinculo_id AND refusal.no_puede = 1
                                   AND refusal.numero = (SELECT MAX(latest.numero) FROM dbo.citas_externas_no_puedo latest WHERE latest.cita_id = refusal.cita_id AND latest.vinculo_id = refusal.vinculo_id))
                   THEN 'NO_PUEDE'
               ELSE NULL
           END AS NVARCHAR(24)) AS motivo_codigo
      FROM #candidatos c;
END;
GO

/* ---- Reservar y comprobar los avisos (el rol de la tarea no lee los datos de la cita ni inserta) ---- */

CREATE PROCEDURE dbo.reservar_avisos_citas_externas_en
    @ahora_utc DATETIME2(3),
    @idioma NVARCHAR(2),
    @version NVARCHAR(8)
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    IF @idioma NOT IN (N'es', N'ca') OR @version IS NULL OR LEN(@version) = 0
        THROW 50890, 'REMINDER_PARAMETERS_INVALID', 1;

    DECLARE @local DATETIME2(3) = CAST(@ahora_utc AT TIME ZONE 'UTC' AT TIME ZONE 'Romance Standard Time' AS DATETIME2(3));
    -- La franja de envío, como el recordatorio: de las 10:00 a las 18:59, hora de Madrid.
    IF DATEPART(HOUR, @local) < 10 OR DATEPART(HOUR, @local) >= 19
    BEGIN
        SELECT CAST(0 AS INT) AS reservadas;
        RETURN;
    END;

    DECLARE @hoy DATE = CAST(@local AS DATE), @creadas INT = 0;
    CREATE TABLE #calculo (
        centro_id UNIQUEIDENTIFIER NOT NULL, cita_id UNIQUEIDENTIFIER NOT NULL, fecha DATE NOT NULL, tramo NVARCHAR(2) COLLATE Latin1_General_100_BIN2 NOT NULL, vinculo_id UNIQUEIDENTIFIER NOT NULL,
        familiar_id UNIQUEIDENTIFIER NOT NULL, correo NVARCHAR(254) NULL, correo_confirmado_en DATETIME2(3) NULL, motivo_codigo NVARCHAR(24) NULL);
    INSERT INTO #calculo EXEC dbo.calcular_avisos_citas_externas @hoy = @hoy;
    BEGIN TRY
        INSERT INTO dbo.envios_correo
            (id, centro_id, plantilla_codigo, plantilla_version, idioma, familiar_id, referencia_id, referencia_clave, estado_codigo, motivo_codigo, intentos, creado_en, resuelto_en)
        SELECT NEWID(), notice.centro_id, CASE WHEN notice.tramo = '2D' THEN 'AVISO_CITA_EXTERNA_SIN_CUBRIR' ELSE 'AVISO_CITA_EXTERNA' END, @version, @idioma, notice.familiar_id, notice.cita_id,
               notice.tramo + '|' + CONVERT(NVARCHAR(10), notice.fecha, 23), 'PENDIENTE', NULL, 0, @ahora_utc, NULL
          FROM #calculo notice
         WHERE notice.motivo_codigo IS NULL AND notice.correo IS NOT NULL AND notice.correo_confirmado_en IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM dbo.envios_correo existing
                            WHERE existing.plantilla_codigo = CASE WHEN notice.tramo = '2D' THEN 'AVISO_CITA_EXTERNA_SIN_CUBRIR' ELSE 'AVISO_CITA_EXTERNA' END
                              AND existing.referencia_id = notice.cita_id AND existing.referencia_clave = notice.tramo + '|' + CONVERT(NVARCHAR(10), notice.fecha, 23)
                              AND existing.familiar_id = notice.familiar_id);
        SET @creadas = @@ROWCOUNT;
    END TRY
    BEGIN CATCH
        -- Otra copia de la aplicación reservó lo mismo a la vez: la clave única lo impide y esa copia ya lo tiene. La vuelta siguiente reserva lo que falte.
        IF ERROR_NUMBER() NOT IN (2601, 2627)
            THROW;
        SET @creadas = 0;
    END CATCH;

    SELECT @creadas AS reservadas;
END;
GO

CREATE PROCEDURE dbo.reservar_avisos_citas_externas
    @idioma NVARCHAR(2),
    @version NVARCHAR(8)
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ahora DATETIME2(3) = SYSUTCDATETIME();
    EXEC dbo.reservar_avisos_citas_externas_en @ahora_utc = @ahora, @idioma = @idioma, @version = @version;
END;
GO

/* Devuelve una fila con el motivo por el que el aviso @envio_id ya no debe salir (nulo si sale) y, cuando sale o se pudo identificar a la persona, su cuenta, su vínculo y la fecha de la cita:
   la tarea los usa para volver a comprobar el acceso del portal con la misma consulta que usa el portal (la tarea no lee la cita ni sus datos). Mira la fila, no confía en lo que estaba cuando se reservó. */
CREATE PROCEDURE dbo.comprobar_aviso_cita_externa_en
    @envio_id UNIQUEIDENTIFIER,
    @ahora_utc DATETIME2(3)
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @local DATETIME2(3) = CAST(@ahora_utc AT TIME ZONE 'UTC' AT TIME ZONE 'Romance Standard Time' AS DATETIME2(3));
    DECLARE @hoy DATE = CAST(@local AS DATE);
    DECLARE @cita UNIQUEIDENTIFIER, @familiar UNIQUEIDENTIFIER, @centro UNIQUEIDENTIFIER, @clave NVARCHAR(40), @plantilla NVARCHAR(32), @estado NVARCHAR(10);
    SELECT @cita = referencia_id, @familiar = familiar_id, @centro = centro_id, @clave = referencia_clave, @plantilla = plantilla_codigo, @estado = estado_codigo
      FROM dbo.envios_correo WHERE id = @envio_id;

    DECLARE @tramo NVARCHAR(2) = LEFT(@clave, 2), @fecha DATE = TRY_CONVERT(DATE, SUBSTRING(@clave, 4, 10), 23);
    DECLARE @motivo NVARCHAR(24), @cuenta UNIQUEIDENTIFIER, @vinculo UNIQUEIDENTIFIER;

    IF @cita IS NULL OR @estado <> 'PENDIENTE' OR @fecha IS NULL OR @tramo NOT IN ('7D', '1D', '2D')
       OR @plantilla <> CASE WHEN @tramo = '2D' THEN 'AVISO_CITA_EXTERNA_SIN_CUBRIR' ELSE 'AVISO_CITA_EXTERNA' END
        SET @motivo = 'CITA_NO_VIGENTE';
    ELSE IF @fecha <> DATEADD(DAY, CASE @tramo WHEN '7D' THEN 7 WHEN '1D' THEN 1 ELSE 2 END, @hoy)
        SET @motivo = 'FUERA_DE_PLAZO';
    ELSE IF NOT EXISTS (SELECT 1 FROM dbo.citas_externas WHERE id = @cita AND centro_id = @centro AND fecha = @fecha
                           AND estado_codigo IN ('PROGRAMADA', 'CONFIRMADA_HOSPITAL') AND visibilidad_familia_codigo <> 'OCULTA')
        SET @motivo = CASE WHEN EXISTS (SELECT 1 FROM dbo.citas_externas WHERE id = @cita AND centro_id = @centro AND fecha = @fecha
                                           AND estado_codigo IN ('PROGRAMADA', 'CONFIRMADA_HOSPITAL') AND visibilidad_familia_codigo = 'OCULTA')
                           THEN 'FAMILIA_NO_VE' ELSE 'CITA_NO_VIGENTE' END;
    ELSE
    BEGIN
        -- Una fila sin motivo quiere decir que sale; si la persona ya no figura entre los vinculados de la cita, no hay a quién escribir.
        DECLARE @encontrado BIT = 0;
        CREATE TABLE #calculo (
            centro_id UNIQUEIDENTIFIER NOT NULL, cita_id UNIQUEIDENTIFIER NOT NULL, fecha DATE NOT NULL, tramo NVARCHAR(2) COLLATE Latin1_General_100_BIN2 NOT NULL, vinculo_id UNIQUEIDENTIFIER NOT NULL,
            familiar_id UNIQUEIDENTIFIER NOT NULL, correo NVARCHAR(254) NULL, correo_confirmado_en DATETIME2(3) NULL, motivo_codigo NVARCHAR(24) NULL);
        INSERT INTO #calculo EXEC dbo.calcular_avisos_citas_externas @hoy = @hoy, @cita = @cita;
        SELECT TOP (1) @encontrado = 1, @motivo = notice.motivo_codigo, @vinculo = notice.vinculo_id
          FROM #calculo notice
         WHERE notice.cita_id = @cita AND notice.familiar_id = @familiar AND notice.tramo = @tramo AND notice.fecha = @fecha;
        IF @encontrado = 0
            SET @motivo = 'SIN_ACCESO';
        ELSE
            SELECT @cuenta = cuenta_id FROM dbo.familiares WHERE id = @familiar AND centro_id = @centro;
    END;

    SELECT @motivo AS motivo, @cuenta AS cuenta_id, @vinculo AS vinculo_id, @fecha AS fecha;
END;
GO

CREATE PROCEDURE dbo.comprobar_aviso_cita_externa
    @envio_id UNIQUEIDENTIFIER
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ahora DATETIME2(3) = SYSUTCDATETIME();
    EXEC dbo.comprobar_aviso_cita_externa_en @envio_id = @envio_id, @ahora_utc = @ahora;
END;
GO

/* ---- Cerrar los avisos atascados: las plantillas nuevas se suman a las del cambio y el recordatorio ---- */

ALTER PROCEDURE dbo.cerrar_envios_correo_sin_resolver
    @lote INT = 2000
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    IF @lote < 1 OR @lote > 10000
        THROW 50880, 'EMAIL_LOG_CLOSE_BATCH_INVALID', 1;

    DECLARE @ahora DATETIME2(3) = SYSUTCDATETIME(), @cerradas INT = 0, @lote_cerrado INT = 1;
    DECLARE @limite_propuesta DATETIME2(3) = DATEADD(MINUTE, -15, @ahora), @limite_cambio DATETIME2(3) = DATEADD(MINUTE, -30, @ahora), @limite_sin_reclamar DATETIME2(3) = DATEADD(HOUR, -24, @ahora);
    WHILE @lote_cerrado > 0
    BEGIN
        UPDATE TOP (@lote) dbo.envios_correo
           SET estado_codigo = 'FALLIDO', motivo_codigo = 'RESULTADO_DESCONOCIDO', resuelto_en = @ahora
         WHERE estado_codigo = 'PENDIENTE' AND resuelto_en IS NULL
           AND ((plantilla_codigo = 'AVISO_PROPUESTA_CITA' AND creado_en < @limite_propuesta)
             OR (plantilla_codigo IN ('AVISO_CAMBIO_CITA', 'RECORDATORIO_CITA', 'AVISO_CITA_EXTERNA', 'AVISO_CITA_EXTERNA_SIN_CUBRIR') AND intentos >= 1 AND creado_en < @limite_cambio));
        SET @lote_cerrado = @@ROWCOUNT;
        SET @cerradas += @lote_cerrado;
    END;

    -- Un recordatorio o un aviso de cita externa que nunca se reclamó y tiene más de 24 horas ya no puede enviarse (pasó la franja o el día): «no se envió» es verdad, no «sin confirmar».
    SET @lote_cerrado = 1;
    WHILE @lote_cerrado > 0
    BEGIN
        UPDATE TOP (@lote) dbo.envios_correo
           SET estado_codigo = 'OMITIDO', motivo_codigo = 'FUERA_DE_PLAZO', resuelto_en = @ahora
         WHERE estado_codigo = 'PENDIENTE' AND resuelto_en IS NULL
           AND plantilla_codigo IN ('RECORDATORIO_CITA', 'AVISO_CITA_EXTERNA', 'AVISO_CITA_EXTERNA_SIN_CUBRIR') AND intentos = 0 AND creado_en < @limite_sin_reclamar;
        SET @lote_cerrado = @@ROWCOUNT;
        SET @cerradas += @lote_cerrado;
    END;

    SELECT @cerradas AS cerradas;
END;
GO

/* ---- Permisos de la tarea programada y de la aplicación sobre las funciones y los procedimientos ---- */

IF DATABASE_PRINCIPAL_ID(N'residapp_tareas') IS NOT NULL
BEGIN
    GRANT EXECUTE ON OBJECT::dbo.reservar_avisos_citas_externas TO residapp_tareas;
    GRANT EXECUTE ON OBJECT::dbo.comprobar_aviso_cita_externa TO residapp_tareas;
END;
GO
GRANT SELECT ON dbo.fn_familiares_con_acceso TO residapp_aplicacion;
GRANT SELECT ON dbo.fn_citas_externas_encargos_vigentes TO residapp_aplicacion;
GO

COMMIT TRANSACTION;
GO
