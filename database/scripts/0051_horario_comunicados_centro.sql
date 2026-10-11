/*
 * Hora de publicación y margen de los comunicados a la familia, por centro (CJ, 2026-10-10; documento confirmaciones-segunda-tanda-cj, 5.1:
 * «cada centro debe poder elegir su hora»). Hasta ahora eran 19:00 y 1 hora para todos (FamilyCommunicationSchedule).
 *
 * Mismo patrón que los plazos de los hitos (0046): comunicados_horario_centro guarda solo lo que el centro ha cambiado respecto a los
 * valores de CJ (sin fila valen 19:00 y 60 minutos) y todo cambio queda en un historial inmutable. Lo edita Administración del centro.
 *
 * comunicaciones_familiares.publicacion_programada_en: la hora a la que se publica el comunicado, fijada al guardarlo con el horario del
 * centro de ese momento. Así cambiar el horario no mueve los comunicados ya guardados ni «despublica» uno que ya salió. Es NULL en los
 * guardados antes de este script: para ellos vale el cálculo de siempre (19:00 y 1 hora).
 */

CREATE TABLE dbo.comunicados_horario_centro (
    centro_id        UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_chc PRIMARY KEY CONSTRAINT FK_chc_centro REFERENCES dbo.centros(id),
    hora_publicacion TIME(0) NOT NULL,
    margen_minutos   INT NOT NULL,
    CONSTRAINT CK_chc_margen CHECK (margen_minutos BETWEEN 0 AND 1440)
);
GO

CREATE TABLE dbo.comunicados_horario_centro_historial (
    id                       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_chch PRIMARY KEY,
    centro_id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_chch_centro REFERENCES dbo.centros(id),
    hora_anterior            TIME(0) NOT NULL,
    margen_anterior_minutos  INT NOT NULL,
    hora_nueva               TIME(0) NOT NULL,
    margen_nuevo_minutos     INT NOT NULL,
    cambiado_por_cuenta_id   UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_chch_cuenta REFERENCES dbo.cuentas(id),
    cambiado_por_perfil      NVARCHAR(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    cambiado_en              DATETIME2(3) NOT NULL,
    CONSTRAINT CK_chch_margen CHECK (margen_anterior_minutos BETWEEN 0 AND 1440 AND margen_nuevo_minutos BETWEEN 0 AND 1440),
    CONSTRAINT CK_chch_perfil CHECK (cambiado_por_perfil = 'ADMINISTRACION')
);
CREATE INDEX IX_chch_centro_fecha ON dbo.comunicados_horario_centro_historial (centro_id, cambiado_en);
GO

CREATE TRIGGER dbo.TR_chch_immutable ON dbo.comunicados_horario_centro_historial INSTEAD OF UPDATE, DELETE AS
    THROW 50500, 'FAMILY_COMMUNICATION_SCHEDULE_HISTORY_IMMUTABLE', 1;
GO

ALTER SECURITY POLICY seg.pol_centro
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.comunicados_horario_centro,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.comunicados_horario_centro AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.comunicados_horario_centro AFTER UPDATE,
    ADD FILTER PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.comunicados_horario_centro_historial,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.comunicados_horario_centro_historial AFTER INSERT,
    ADD BLOCK PREDICATE seg.fn_centro_del_ambito(centro_id) ON dbo.comunicados_horario_centro_historial AFTER UPDATE;
GO

ALTER TABLE dbo.comunicaciones_familiares ADD publicacion_programada_en DATETIME2(3) NULL;
GO
