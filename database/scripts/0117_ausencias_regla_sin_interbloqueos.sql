/*
 * 0117 — La regla de las ausencias programadas del Centro de Día (TR_aup_reglas, script 0115) deja de provocar interbloqueos (error 1205) entre registros de personas distintas.
 *
 * Con READ_COMMITTED_SNAPSHOT apagado (la base local de pruebas), el disparador leía filas de otra inserción todavía sin confirmar: sus lecturas por id o por ausencia_id acababan recorriendo la tabla,
 * y dos inserciones a la vez se esperaban la una a la otra. Ahora todas sus lecturas de ausencias_programadas filtran por centro y residente y buscan (FORCESEEK) en IX_aup_residente, de modo que solo
 * tocan las filas de esa persona. Mismo remedio que en las valoraciones (ver docs/prompts-ia/lecciones-tecnicas.md).
 *
 * Las reglas y los códigos de error no cambian. Filtrar por centro y residente es equivalente porque la primera comprobación (51155) impide referir una ausencia de otra persona o de otro centro y las
 * filas son inmutables; por eso esa comprobación sigue yendo antes que las dos siguientes. Sin tablas, columnas, datos ni permisos nuevos (consulta previa a dpo-residapp, 2026-11-25).
 */

SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO

ALTER TRIGGER dbo.TR_aup_reglas ON dbo.ausencias_programadas AFTER INSERT AS
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
          LEFT JOIN dbo.ausencias_programadas base WITH (FORCESEEK (IX_aup_residente (centro_id, residente_id, ausencia_id)))
                 ON base.centro_id = i.centro_id AND base.residente_id = i.residente_id AND base.ausencia_id = i.ausencia_id
                AND base.id = i.ausencia_id AND base.tipo_codigo = N'REGISTRADA'
         WHERE i.tipo_codigo IN (N'ANULADA', N'ACORTADA')
           AND (base.id IS NULL
                OR EXISTS (SELECT 1 FROM dbo.ausencias_programadas anulada WITH (FORCESEEK (IX_aup_residente (centro_id, residente_id, ausencia_id)))
                            WHERE anulada.centro_id = i.centro_id AND anulada.residente_id = i.residente_id AND anulada.ausencia_id = base.id
                              AND anulada.tipo_codigo = N'ANULADA' AND anulada.id <> i.id)))
        THROW 51155, 'ASSISTANCE_ABSENCE_INVALID', 1;
    -- Un acortamiento termina antes del último día vigente y no antes del primero.
    IF EXISTS (
        SELECT 1 FROM inserted i
          JOIN dbo.ausencias_programadas base WITH (FORCESEEK (IX_aup_residente (centro_id, residente_id, ausencia_id)))
            ON base.centro_id = i.centro_id AND base.residente_id = i.residente_id AND base.ausencia_id = i.ausencia_id AND base.id = i.ausencia_id
         WHERE i.tipo_codigo = N'ACORTADA'
           AND (i.hasta < base.desde
                OR i.hasta >= (SELECT MIN(x.hasta) FROM dbo.ausencias_programadas x WITH (FORCESEEK (IX_aup_residente (centro_id, residente_id, ausencia_id)))
                                WHERE x.centro_id = i.centro_id AND x.residente_id = i.residente_id AND x.ausencia_id = base.id
                                  AND x.tipo_codigo IN (N'REGISTRADA', N'ACORTADA') AND x.id <> i.id)))
        THROW 51155, 'ASSISTANCE_ABSENCE_INVALID', 1;
    -- Sin solapes entre ausencias vigentes del mismo residente.
    IF EXISTS (
        SELECT 1 FROM inserted i
          JOIN dbo.ausencias_programadas other WITH (FORCESEEK (IX_aup_residente (centro_id, residente_id)))
            ON other.centro_id = i.centro_id AND other.residente_id = i.residente_id AND other.tipo_codigo = N'REGISTRADA' AND other.id <> i.id
         WHERE i.tipo_codigo = N'REGISTRADA'
           AND NOT EXISTS (SELECT 1 FROM dbo.ausencias_programadas anulada WITH (FORCESEEK (IX_aup_residente (centro_id, residente_id, ausencia_id)))
                            WHERE anulada.centro_id = i.centro_id AND anulada.residente_id = i.residente_id AND anulada.ausencia_id = other.id
                              AND anulada.tipo_codigo = N'ANULADA')
           AND other.desde <= i.hasta
           AND (SELECT MIN(x.hasta) FROM dbo.ausencias_programadas x WITH (FORCESEEK (IX_aup_residente (centro_id, residente_id, ausencia_id)))
                 WHERE x.centro_id = i.centro_id AND x.residente_id = i.residente_id AND x.ausencia_id = other.id
                   AND x.tipo_codigo IN (N'REGISTRADA', N'ACORTADA')) >= i.desde)
        THROW 51156, 'ASSISTANCE_ABSENCE_OVERLAP', 1;
END;
GO

COMMIT TRANSACTION;
GO
