/*
 * Servicio de citas de prueba para el centro del escenario integrado (script 0054): reserva directa, las tres modalidades para los dos equipos y
 * franjas de lunes a viernes, para que la familia ficticia ('dev-integrado-familiar') tenga huecos que reservar y 'dev-integrado-administracion'
 * una agenda que ver. Exclusivamente ficticio. NO ejecutar contra un entorno con datos reales.
 * Idempotente: si el centro ya tiene configuración de citas (por este seed o porque Administración la cambió), no toca nada.
 *
 * Equipo médico: de 10:00 a 13:00 y de 16:00 a 18:00, citas de 30 minutos. Equipo de Enfermería: de 09:00 a 12:00 y de 16:00 a 18:00, citas de 30 minutos.
 * Antelación mínima de 24 horas y horizonte de 30 días. Va después del escenario integrado (los seeds se aplican por orden alfabético); si aún no
 * existe, no hace nada.
 */

SET QUOTED_IDENTIFIER ON;
GO

IF EXISTS (SELECT 1 FROM dbo.cuentas WHERE sujeto_externo = 'dev-integrado-administracion')
   AND EXISTS (SELECT 1 FROM dbo.unidades WHERE codigo = 'UNIDAD-DEV-INTEGRADO')
   AND NOT EXISTS (
       SELECT 1 FROM dbo.citas_configuracion_centro
        WHERE centro_id = (SELECT centro_id FROM dbo.unidades WHERE codigo = 'UNIDAD-DEV-INTEGRADO'))
BEGIN
    DECLARE @CenterId UNIQUEIDENTIFIER = (SELECT centro_id FROM dbo.unidades WHERE codigo = 'UNIDAD-DEV-INTEGRADO');
    DECLARE @AdminId UNIQUEIDENTIFIER = (SELECT id FROM dbo.cuentas WHERE sujeto_externo = 'dev-integrado-administracion');
    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();

    INSERT INTO dbo.citas_configuracion_centro (centro_id, modo_codigo, antelacion_minima_horas, horizonte_maximo_dias, version)
    VALUES (@CenterId, 'RESERVA_DIRECTA', 24, 30, 1);

    INSERT INTO dbo.citas_modalidades_habilitadas (centro_id, equipo_codigo, modalidad_codigo)
    SELECT @CenterId, team.code, modality.code
      FROM (VALUES ('MEDICO'), ('ENFERMERIA')) AS team(code)
     CROSS JOIN (VALUES ('PRESENCIAL'), ('TELEFONICA'), ('VIDEOCONFERENCIA')) AS modality(code);

    INSERT INTO dbo.citas_franjas (id, centro_id, equipo_codigo, dia_semana, hora_inicio, hora_fin, duracion_minutos, creada_por_cuenta_id, creada_en)
    SELECT NEWID(), @CenterId, window.team, day.value, window.starts, window.ends, 30, @AdminId, @Now
      FROM (VALUES
               ('MEDICO',     CAST('10:00' AS TIME(0)), CAST('13:00' AS TIME(0))),
               ('MEDICO',     CAST('16:00' AS TIME(0)), CAST('18:00' AS TIME(0))),
               ('ENFERMERIA', CAST('09:00' AS TIME(0)), CAST('12:00' AS TIME(0))),
               ('ENFERMERIA', CAST('16:00' AS TIME(0)), CAST('18:00' AS TIME(0)))) AS window(team, starts, ends)
     CROSS JOIN (VALUES (1), (2), (3), (4), (5)) AS day(value);
END
GO
