/*
 * Recordatorio de una cita confirmada, el día antes (ADR 0012; CJ, tercera tanda 6.2 B; lectura de dpo-residapp en docs/privacidad/recordatorio-cita-por-correo.md, apartados 2, 4 y 7).
 * Comparte la tabla de los otros dos avisos (0081, 0082): una plantilla nueva, RECORDATORIO_CITA, y dos motivos de omisión nuevos. La clave única (plantilla, referencia, clave)
 * sigue siendo lo que impide mandarlo dos veces:
 *
 *   referencia_id     la cita.
 *   referencia_clave  la FECHA LOCAL de la cita (Europe/Madrid, aaaa-mm-dd), no el día del envío: un recordatorio por cita y por fecha. Si la cita se mueve de día, la fecha nueva
 *                     tiene el suyo; si se mueve de hora el mismo día, no se repite.
 *
 *   CITA_RECIENTE   la cita se reservó, se aceptó o se movió el mismo día natural (hora de Madrid) del envío o después: un «tienes una cita mañana» a los pocos minutos sería ruido
 *                   (y el centro que la movió ya mandó el aviso de cambio). Se anota ya cerrada.
 *   FUERA_DE_PLAZO  ya no se puede decir «mañana» con verdad: fuera de la franja de envío, o la cita ya no es de mañana.
 *
 * A diferencia de los otros dos avisos, nadie lo desencadena: lo dispara el reloj. Por eso la reserva la hace un procedimiento y NO se da INSERT en envios_correo a
 * residapp_tareas (0083: solo resuelve filas que ya existen; con INSERT podría fabricar filas de cualquier plantilla, centro y familiar):
 *
 *   dbo.reservar_recordatorios_cita(@idioma, @version)   WITH EXECUTE AS OWNER. Calcula «mañana» él mismo con la hora de Madrid (el llamante no elige el día), no hace nada fuera
 *                   de la franja de las 10:00 a las 18:59, y reserva una fila por cada cita CONFIRMADA de mañana que aún no la tiene: PENDIENTE, o ya cerrada como OMITIDO /
 *                   CITA_RECIENTE. Lee citas.creada_en y citas_cambios.ocurrido_en como propietario (el rol de la tarea no puede) y NUNCA citas_cambios.motivo. Devuelve solo cuántas filas
 *                   creó: ni ids, ni familiares, ni direcciones. EXECUTE para residapp_tareas.
 *   dbo.reservar_recordatorios_cita_en(@ahora_utc, ...)  lo mismo con la hora como parámetro, para poder probar la franja y los cambios de hora. SIN permiso para residapp_tareas:
 *                   solo el propietario la puede llamar.
 *
 * dbo.cerrar_envios_correo_sin_resolver (0089) se reescribe para incluir el recordatorio; si no, uno atascado quedaría PENDIENTE para siempre:
 *   reclamado (intentos >= 1) y de más de 30 minutos            -> FALLIDO / RESULTADO_DESCONOCIDO («sin confirmar»: puede haber salido), como el aviso de cambio.
 *   sin reclamar (intentos = 0) y de más de 24 horas            -> OMITIDO / FUERA_DE_PLAZO (nunca se intentó y ya no puede enviarse: pasó la franja).
 * Lo demás del procedimiento (plazos de la propuesta y del cambio, EXECUTE AS OWNER, el permiso del rol) no cambia.
 *
 * Los CHECK se rehacen aquí con la lista completa de 0089 más los dos motivos nuevos (no se edita 0082 ni 0089, ya aplicados). El disparador TR_envios_correo_guard y la seguridad
 * por filas no cambian. La zona de Madrid se escribe con el nombre de Windows ('Romance Standard Time'), el que acepta AT TIME ZONE en SQL Server y en Azure SQL.
 */

SET XACT_ABORT ON;
BEGIN TRANSACTION;

ALTER TABLE dbo.envios_correo DROP CONSTRAINT CK_envios_correo_plantilla;
GO
ALTER TABLE dbo.envios_correo ADD CONSTRAINT CK_envios_correo_plantilla CHECK (plantilla_codigo IN ('AVISO_PROPUESTA_CITA', 'AVISO_CAMBIO_CITA', 'RECORDATORIO_CITA'));
GO
ALTER TABLE dbo.envios_correo DROP CONSTRAINT CK_envios_correo_motivo;
GO
ALTER TABLE dbo.envios_correo ADD CONSTRAINT CK_envios_correo_motivo CHECK (motivo_codigo IS NULL OR motivo_codigo IN
    ('SIN_CORREO', 'SIN_ACCESO', 'DIRECCION_NO_CONFIRMADA', 'PROPUESTA_NO_VIGENTE', 'CITA_NO_VIGENTE', 'CITA_PASADA', 'CITA_RECIENTE', 'FUERA_DE_PLAZO', 'DESTINO_NO_PERMITIDO',
     'ACS_RECHAZADO', 'ACS_NO_DISPONIBLE', 'TIEMPO_AGOTADO', 'RESULTADO_DESCONOCIDO'));
GO

CREATE PROCEDURE dbo.reservar_recordatorios_cita_en
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
    -- La franja de envío: de las 10:00 a las 18:59, hora de Madrid. Fuera de ella no se reserva nada (un «mañana» a otra hora podría no ser verdad).
    IF DATEPART(HOUR, @local) < 10 OR DATEPART(HOUR, @local) >= 19
    BEGIN
        SELECT CAST(0 AS INT) AS reservadas;
        RETURN;
    END;

    DECLARE @hoy DATE = CAST(@local AS DATE), @manana DATE = DATEADD(DAY, 1, CAST(@local AS DATE)), @pasado DATE = DATEADD(DAY, 2, CAST(@local AS DATE));
    DECLARE @inicio_hoy DATETIME2(3) = CAST(CAST(@hoy AS DATETIME2(3)) AT TIME ZONE 'Romance Standard Time' AT TIME ZONE 'UTC' AS DATETIME2(3));
    DECLARE @desde DATETIME2(3) = CAST(CAST(@manana AS DATETIME2(3)) AT TIME ZONE 'Romance Standard Time' AT TIME ZONE 'UTC' AS DATETIME2(3));
    DECLARE @hasta DATETIME2(3) = CAST(CAST(@pasado AS DATETIME2(3)) AT TIME ZONE 'Romance Standard Time' AT TIME ZONE 'UTC' AS DATETIME2(3));
    DECLARE @clave NVARCHAR(40) = CONVERT(NVARCHAR(10), @manana, 23), @creadas INT = 0;

    BEGIN TRY
        INSERT INTO dbo.envios_correo
            (id, centro_id, plantilla_codigo, plantilla_version, idioma, familiar_id, referencia_id, referencia_clave, estado_codigo, motivo_codigo, intentos, creado_en, resuelto_en)
        SELECT NEWID(), c.centro_id, 'RECORDATORIO_CITA', @version, @idioma, link.familiar_id, c.id, @clave,
               CASE WHEN r.reciente = 1 THEN 'OMITIDO' ELSE 'PENDIENTE' END, CASE WHEN r.reciente = 1 THEN 'CITA_RECIENTE' END, 0, @ahora_utc, CASE WHEN r.reciente = 1 THEN @ahora_utc END
          FROM dbo.citas c
          JOIN dbo.residentes_familiares link ON link.id = c.vinculo_id AND link.centro_id = c.centro_id
          CROSS APPLY (SELECT CASE WHEN c.creada_en >= @inicio_hoy
                                     OR EXISTS (SELECT 1 FROM dbo.citas_cambios x
                                                 WHERE x.cita_id = c.id AND x.centro_id = c.centro_id AND x.accion_codigo IN ('CREADA', 'REPROGRAMADA') AND x.ocurrido_en >= @inicio_hoy)
                                   THEN 1 ELSE 0 END AS reciente) r
         WHERE c.estado_codigo = 'CONFIRMADA' AND c.inicio_en >= @desde AND c.inicio_en < @hasta
           AND NOT EXISTS (SELECT 1 FROM dbo.envios_correo e WHERE e.plantilla_codigo = 'RECORDATORIO_CITA' AND e.referencia_id = c.id AND e.referencia_clave = @clave);
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

CREATE PROCEDURE dbo.reservar_recordatorios_cita
    @idioma NVARCHAR(2),
    @version NVARCHAR(8)
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ahora DATETIME2(3) = SYSUTCDATETIME();
    EXEC dbo.reservar_recordatorios_cita_en @ahora_utc = @ahora, @idioma = @idioma, @version = @version;
END;
GO

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
             OR (plantilla_codigo IN ('AVISO_CAMBIO_CITA', 'RECORDATORIO_CITA') AND intentos >= 1 AND creado_en < @limite_cambio));
        SET @lote_cerrado = @@ROWCOUNT;
        SET @cerradas += @lote_cerrado;
    END;

    -- Un recordatorio que nunca se reclamó y tiene más de 24 horas ya no puede enviarse (pasó la franja): «no se envió» es verdad, no «sin confirmar».
    SET @lote_cerrado = 1;
    WHILE @lote_cerrado > 0
    BEGIN
        UPDATE TOP (@lote) dbo.envios_correo
           SET estado_codigo = 'OMITIDO', motivo_codigo = 'FUERA_DE_PLAZO', resuelto_en = @ahora
         WHERE estado_codigo = 'PENDIENTE' AND resuelto_en IS NULL
           AND plantilla_codigo = 'RECORDATORIO_CITA' AND intentos = 0 AND creado_en < @limite_sin_reclamar;
        SET @lote_cerrado = @@ROWCOUNT;
        SET @cerradas += @lote_cerrado;
    END;

    SELECT @cerradas AS cerradas;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'residapp_tareas') IS NOT NULL
    GRANT EXECUTE ON OBJECT::dbo.reservar_recordatorios_cita TO residapp_tareas;
GO

COMMIT TRANSACTION;
GO
