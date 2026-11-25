/*
 * 0118 — Las reglas de la asistencia diaria (TR_asd_reglas, script 0115) y de la pauta (TR_asp_reglas, versión del script 0116) del Centro de Día dejan de provocar interbloqueos (error 1205) entre
 * anotaciones o pautas de personas distintas.
 *
 * Con READ_COMMITTED_SNAPSHOT apagado (la base local de pruebas y la del CI), los disparadores leían la versión anterior recorriendo la tabla, tropezaban con filas de otra inserción todavía sin
 * confirmar y dos inserciones a la vez se esperaban la una a la otra. Ahora las lecturas de la propia tabla buscan (FORCESEEK) en el índice único por centro y residente, de modo que solo tocan las
 * filas de esa persona. Mismo remedio que en las ausencias (0117). La otra mitad del arreglo va en la aplicación (SqlAttendanceRepository), que ya no reserva un tramo de la tabla al leer la versión
 * anterior.
 *
 * Las reglas y los códigos de error no cambian. Sin tablas, columnas, índices, datos ni permisos nuevos (consulta previa a dpo-residapp, 2026-11-25).
 */

SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO

ALTER TRIGGER dbo.TR_asd_reglas ON dbo.asistencia_dia AFTER INSERT AS
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
          LEFT JOIN dbo.asistencia_dia previous WITH (FORCESEEK (UX_asd_numero (centro_id, residente_id, fecha, numero)))
                 ON previous.centro_id = i.centro_id AND previous.residente_id = i.residente_id AND previous.fecha = i.fecha AND previous.numero = i.numero - 1
         WHERE i.numero > 1 AND (previous.id IS NULL
            OR (previous.estado_codigo = i.estado_codigo AND ISNULL(previous.causa_codigo, N'') = ISNULL(i.causa_codigo, N'') AND ISNULL(previous.nota, N'') = ISNULL(i.nota, N''))))
        THROW 51151, 'ASSISTANCE_SEQUENCE_INVALID', 1;
END;
GO

ALTER TRIGGER dbo.TR_asp_reglas ON dbo.asistencia_pauta AFTER INSERT AS
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
          LEFT JOIN dbo.asistencia_pauta previous WITH (FORCESEEK (UX_asp_numero (centro_id, residente_id, numero)))
                 ON previous.centro_id = i.centro_id AND previous.residente_id = i.residente_id AND previous.numero = i.numero - 1
         WHERE i.numero > 1 AND (previous.id IS NULL OR previous.dias_esperados = i.dias_esperados))
        THROW 51151, 'ASSISTANCE_SEQUENCE_INVALID', 1;
    -- La primera versión vale desde siempre.
    IF EXISTS (SELECT 1 FROM inserted i WHERE i.numero = 1 AND i.vigente_desde <> '00010101')
        THROW 51157, 'ASSISTANCE_PATTERN_DATE_INVALID', 1;
    -- Una versión nueva: no anterior a la que está en vigor hoy, ni posterior a hoy + 90 días (hoy, en la zona del centro).
    IF EXISTS (
        SELECT 1 FROM inserted i
          JOIN dbo.zonas_vigentes_centros z ON z.centro_id = i.centro_id
          CROSS APPLY (SELECT CAST(CAST(SYSUTCDATETIME() AS DATETIMEOFFSET) AT TIME ZONE z.nombre_windows AS DATE) AS hoy) t
          OUTER APPLY (SELECT TOP (1) p.vigente_desde FROM dbo.asistencia_pauta p WITH (FORCESEEK (UX_asp_numero (centro_id, residente_id)))
                        WHERE p.centro_id = i.centro_id AND p.residente_id = i.residente_id AND p.numero < i.numero AND p.vigente_desde <= t.hoy
                        ORDER BY p.numero DESC) inforce
         WHERE i.numero > 1 AND (i.vigente_desde > DATEADD(DAY, 90, t.hoy) OR i.vigente_desde < ISNULL(inforce.vigente_desde, '00010101')))
        THROW 51157, 'ASSISTANCE_PATTERN_DATE_INVALID', 1;
END;
GO

COMMIT TRANSACTION;
GO
