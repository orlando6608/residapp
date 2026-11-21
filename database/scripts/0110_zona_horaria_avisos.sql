/*
 * 0110 — Zona horaria por centro, F2: los avisos por correo usan la zona de cada centro (estudio docs/producto/zonas-horarias-por-centro-estudio.md, apartado 3.3)
 *
 * Hasta aquí, los procedimientos de los recordatorios de cita (0092) y de los avisos de citas externas (0105) calculaban «hoy», «mañana» y la franja de envío (de las 10:00 a las 18:59) con la
 * zona de Madrid escrita a mano para todos los centros a la vez. Ahora cada centro usa la suya (0109): la vigente es la fila de mayor número de zonas_centro, y un centro sin fila (los que ya
 * existían, y Plataforma) sigue en Madrid. No se edita ningún script aplicado: los procedimientos se sustituyen con ALTER.
 *
 *   zonas_vigentes_centros       Vista: la zona vigente de cada centro (código estándar y nombre de Windows, el único que entiende AT TIME ZONE). Sin permisos para la aplicación (DENY a residapp_aplicacion) ni para la tarea: solo la leen los
 *                                procedimientos de abajo, que se ejecutan con los permisos de su dueño (la tarea de correo no ve zonas_centro por la seguridad por filas).
 *   zona_horaria_del_centro      Devuelve el código estándar de la zona de un centro. Lo ejecuta la tarea de correo (residapp_tareas) para decidir, al enviar, si un aviso reservado sigue dentro
 *                                de la franja de SU centro. Devuelve solo un nombre de zona: no es un dato personal.
 *   reservar_recordatorios_cita_en       Para cada zona en uso, calcula «mañana» y solo reserva si es franja de envío allí; los centros de una zona fuera de franja no reservan nada.
 *   reservar_avisos_citas_externas_en    Igual: una vuelta por cada zona dentro de franja, solo con los centros de esa zona.
 *   comprobar_aviso_cita_externa_en      Calcula «hoy» con la zona del centro del aviso, no con la de Madrid.
 *
 * Con todos los centros en Madrid (hoy son todos), el resultado es el de antes.
 *
 * Además se corrige una comparación de fechas de 0071: CK_fotos_residente_consentimiento_fecha exigía que la fecha de consentimiento (el día del centro) no fuera posterior al día UTC de
 * actualizada_en. Entre las 00:00 y la 01:00 de Madrid en invierno (las 00:00 y las 02:00 en verano) el día local ya es el siguiente al día UTC, y guardar un consentimiento del mismo día
 * fallaba. Se permite un día de diferencia, que cubre cualquier zona del catálogo por delante de UTC; las zonas por detrás de UTC nunca superan el día UTC.
 */

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'zonas_centro')
    THROW 51100, 'ZONAS_CENTRO_NO_EXISTE: aplica antes 0109', 1;
GO

CREATE VIEW dbo.zonas_vigentes_centros AS
SELECT centro.id AS centro_id,
       ISNULL(actual.zona_codigo, N'Europe/Madrid') AS zona_codigo,
       CAST(ISNULL(zona.nombre_windows, N'Romance Standard Time') AS NVARCHAR(64)) COLLATE Latin1_General_100_BIN2 AS nombre_windows
  FROM dbo.centros centro
  OUTER APPLY (SELECT TOP (1) cambio.zona_codigo FROM dbo.zonas_centro cambio WHERE cambio.centro_id = centro.id ORDER BY cambio.numero DESC) actual
  LEFT JOIN dbo.zonas_horarias zona ON zona.codigo = actual.zona_codigo;
GO

CREATE PROCEDURE dbo.zona_horaria_del_centro
    @centro_id UNIQUEIDENTIFIER
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT zona_codigo FROM dbo.zonas_vigentes_centros WHERE centro_id = @centro_id;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'residapp_tareas') IS NOT NULL
    GRANT EXECUTE ON OBJECT::dbo.zona_horaria_del_centro TO residapp_tareas;
GO

-- La vista no la lee nadie con una cuenta de la aplicación (residapp_app es db_datareader, 0091): un centro solo vería la zona de los centros que su ámbito deja ver y «Madrid» por defecto en los demás, que sería falso.
IF DATABASE_PRINCIPAL_ID(N'residapp_aplicacion') IS NOT NULL
    DENY SELECT ON dbo.zonas_vigentes_centros TO residapp_aplicacion;
GO

ALTER PROCEDURE dbo.reservar_recordatorios_cita_en
    @ahora_utc DATETIME2(3),
    @idioma NVARCHAR(2),
    @version NVARCHAR(8)
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    IF @idioma NOT IN (N'es', N'ca') OR @version IS NULL OR LEN(@version) = 0
        THROW 50890, 'REMINDER_PARAMETERS_INVALID', 1;

    -- Una fila por zona en uso que está ahora dentro de la franja de envío (de las 10:00 a las 18:59 allí). Fuera de ella no se reserva nada (un «mañana» a otra hora podría no ser verdad).
    CREATE TABLE #ventana (
        nombre_windows NVARCHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL PRIMARY KEY, inicio_hoy DATETIME2(3) NOT NULL, desde DATETIME2(3) NOT NULL, hasta DATETIME2(3) NOT NULL, clave NVARCHAR(40) COLLATE Latin1_General_100_BIN2 NOT NULL);
    INSERT INTO #ventana (nombre_windows, inicio_hoy, desde, hasta, clave)
    SELECT zona.nombre_windows,
           CAST(CAST(day.hoy AS DATETIME2(3)) AT TIME ZONE zona.nombre_windows AT TIME ZONE 'UTC' AS DATETIME2(3)),
           CAST(CAST(DATEADD(DAY, 1, day.hoy) AS DATETIME2(3)) AT TIME ZONE zona.nombre_windows AT TIME ZONE 'UTC' AS DATETIME2(3)),
           CAST(CAST(DATEADD(DAY, 2, day.hoy) AS DATETIME2(3)) AT TIME ZONE zona.nombre_windows AT TIME ZONE 'UTC' AS DATETIME2(3)),
           CONVERT(NVARCHAR(10), DATEADD(DAY, 1, day.hoy), 23)
      FROM (SELECT DISTINCT nombre_windows FROM dbo.zonas_vigentes_centros) zona
     CROSS APPLY (SELECT CAST(@ahora_utc AT TIME ZONE 'UTC' AT TIME ZONE zona.nombre_windows AS DATETIME2(3)) AS local) now_local
     CROSS APPLY (SELECT CAST(now_local.local AS DATE) AS hoy) day
     WHERE DATEPART(HOUR, now_local.local) >= 10 AND DATEPART(HOUR, now_local.local) < 19;

    IF NOT EXISTS (SELECT 1 FROM #ventana)
    BEGIN
        SELECT CAST(0 AS INT) AS reservadas;
        RETURN;
    END;

    DECLARE @creadas INT = 0;
    BEGIN TRY
        INSERT INTO dbo.envios_correo
            (id, centro_id, plantilla_codigo, plantilla_version, idioma, familiar_id, referencia_id, referencia_clave, estado_codigo, motivo_codigo, intentos, creado_en, resuelto_en)
        SELECT NEWID(), c.centro_id, 'RECORDATORIO_CITA', @version, @idioma, link.familiar_id, c.id, w.clave,
               CASE WHEN r.reciente = 1 THEN 'OMITIDO' ELSE 'PENDIENTE' END, CASE WHEN r.reciente = 1 THEN 'CITA_RECIENTE' END, 0, @ahora_utc, CASE WHEN r.reciente = 1 THEN @ahora_utc END
          FROM dbo.citas c
          JOIN dbo.zonas_vigentes_centros zc ON zc.centro_id = c.centro_id
          JOIN #ventana w ON w.nombre_windows = zc.nombre_windows
          JOIN dbo.residentes_familiares link ON link.id = c.vinculo_id AND link.centro_id = c.centro_id
          CROSS APPLY (SELECT CASE WHEN c.creada_en >= w.inicio_hoy
                                     OR EXISTS (SELECT 1 FROM dbo.citas_cambios x
                                                 WHERE x.cita_id = c.id AND x.centro_id = c.centro_id AND x.accion_codigo IN ('CREADA', 'REPROGRAMADA') AND x.ocurrido_en >= w.inicio_hoy)
                                   THEN 1 ELSE 0 END AS reciente) r
         WHERE c.estado_codigo = 'CONFIRMADA' AND c.inicio_en >= w.desde AND c.inicio_en < w.hasta
           AND NOT EXISTS (SELECT 1 FROM dbo.envios_correo e WHERE e.plantilla_codigo = 'RECORDATORIO_CITA' AND e.referencia_id = c.id AND e.referencia_clave = w.clave);
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

ALTER PROCEDURE dbo.reservar_avisos_citas_externas_en
    @ahora_utc DATETIME2(3),
    @idioma NVARCHAR(2),
    @version NVARCHAR(8)
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    IF @idioma NOT IN (N'es', N'ca') OR @version IS NULL OR LEN(@version) = 0
        THROW 50890, 'REMINDER_PARAMETERS_INVALID', 1;

    -- Las zonas en uso que están ahora dentro de la franja de envío (de las 10:00 a las 18:59 allí), con su «hoy». Fuera de ella no se reserva nada.
    CREATE TABLE #ventana (nombre_windows NVARCHAR(64) COLLATE Latin1_General_100_BIN2 NOT NULL PRIMARY KEY, hoy DATE NOT NULL);
    INSERT INTO #ventana (nombre_windows, hoy)
    SELECT zona.nombre_windows, CAST(now_local.local AS DATE)
      FROM (SELECT DISTINCT nombre_windows FROM dbo.zonas_vigentes_centros) zona
     CROSS APPLY (SELECT CAST(@ahora_utc AT TIME ZONE 'UTC' AT TIME ZONE zona.nombre_windows AS DATETIME2(3)) AS local) now_local
     WHERE DATEPART(HOUR, now_local.local) >= 10 AND DATEPART(HOUR, now_local.local) < 19;

    IF NOT EXISTS (SELECT 1 FROM #ventana)
    BEGIN
        SELECT CAST(0 AS INT) AS reservadas;
        RETURN;
    END;

    DECLARE @creadas INT = 0, @zona NVARCHAR(64), @hoy DATE;
    CREATE TABLE #calculo (
        centro_id UNIQUEIDENTIFIER NOT NULL, cita_id UNIQUEIDENTIFIER NOT NULL, fecha DATE NOT NULL, tramo NVARCHAR(2) COLLATE Latin1_General_100_BIN2 NOT NULL, vinculo_id UNIQUEIDENTIFIER NOT NULL,
        familiar_id UNIQUEIDENTIFIER NOT NULL, correo NVARCHAR(254) NULL, correo_confirmado_en DATETIME2(3) NULL, motivo_codigo NVARCHAR(24) NULL);
    CREATE TABLE #calculo_zona (
        centro_id UNIQUEIDENTIFIER NOT NULL, cita_id UNIQUEIDENTIFIER NOT NULL, fecha DATE NOT NULL, tramo NVARCHAR(2) COLLATE Latin1_General_100_BIN2 NOT NULL, vinculo_id UNIQUEIDENTIFIER NOT NULL,
        familiar_id UNIQUEIDENTIFIER NOT NULL, correo NVARCHAR(254) NULL, correo_confirmado_en DATETIME2(3) NULL, motivo_codigo NVARCHAR(24) NULL);
    -- Una vuelta por zona (como mucho unas pocas): se calcula con el «hoy» de esa zona y solo se guardan los centros que la usan.
    WHILE EXISTS (SELECT 1 FROM #ventana)
    BEGIN
        SELECT TOP (1) @zona = nombre_windows, @hoy = hoy FROM #ventana ORDER BY nombre_windows;
        DELETE FROM #ventana WHERE nombre_windows = @zona;
        TRUNCATE TABLE #calculo_zona;
        INSERT INTO #calculo_zona EXEC dbo.calcular_avisos_citas_externas @hoy = @hoy;
        INSERT INTO #calculo
        SELECT notice.* FROM #calculo_zona notice JOIN dbo.zonas_vigentes_centros zc ON zc.centro_id = notice.centro_id WHERE zc.nombre_windows = @zona;
    END;

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

ALTER PROCEDURE dbo.comprobar_aviso_cita_externa_en
    @envio_id UNIQUEIDENTIFIER,
    @ahora_utc DATETIME2(3)
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @cita UNIQUEIDENTIFIER, @familiar UNIQUEIDENTIFIER, @centro UNIQUEIDENTIFIER, @clave NVARCHAR(40), @plantilla NVARCHAR(32), @estado NVARCHAR(10);
    SELECT @cita = referencia_id, @familiar = familiar_id, @centro = centro_id, @clave = referencia_clave, @plantilla = plantilla_codigo, @estado = estado_codigo
      FROM dbo.envios_correo WHERE id = @envio_id;

    -- «Hoy» en la zona del centro del aviso (sin centro, la de Madrid, y el aviso saldrá como «cita no vigente»).
    DECLARE @zona NVARCHAR(64) = ISNULL((SELECT nombre_windows FROM dbo.zonas_vigentes_centros WHERE centro_id = @centro), N'Romance Standard Time');
    DECLARE @local DATETIME2(3) = CAST(@ahora_utc AT TIME ZONE 'UTC' AT TIME ZONE @zona AS DATETIME2(3));
    DECLARE @hoy DATE = CAST(@local AS DATE);

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

-- La fecha de consentimiento es un día del centro; actualizada_en está en UTC. Por delante de UTC, el día del centro puede ser el siguiente al día UTC durante unas horas.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
ALTER TABLE dbo.fotos_residente DROP CONSTRAINT CK_fotos_residente_consentimiento_fecha;
ALTER TABLE dbo.fotos_residente ADD CONSTRAINT CK_fotos_residente_consentimiento_fecha CHECK (consentimiento_fecha <= DATEADD(DAY, 1, CAST(actualizada_en AS DATE)));
COMMIT TRANSACTION;
GO
