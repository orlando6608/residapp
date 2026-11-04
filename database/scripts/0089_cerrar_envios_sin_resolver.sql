/*
 * Cierre de los avisos de correo que se quedaron PENDIENTE para siempre (ADR 0012; Orlando, 2026-11-04, punto 2a del plan de seguridad; lectura de dpo-residapp el mismo día).
 *
 * Una fila de dbo.envios_correo queda PENDIENTE si la aplicación se cae o se reinicia entre «reservar o reclamar» y «anotar el resultado», o si el correo salió y falló después
 * la escritura del resultado. Un estado «pendiente» falso engaña (RGPD, art. 5.1.d, exactitud). Un procedimiento propio las cierra pasado un plazo; lo llama una tarea programada.
 *
 *   - Estado FALLIDO con un motivo NUEVO, RESULTADO_DESCONOCIDO (cabe en NVARCHAR(24)): no se sabe si el correo salió. NO se reutiliza TIEMPO_AGOTADO (ACS no respondió en 10 s)
 *     y NUNCA se escribe ENVIADO. Cualquier pantalla o informe futuro debe leer FALLIDO / RESULTADO_DESCONOCIDO como «sin confirmar», no como «no enviado».
 *   - Plazo y motivo viven en la base de datos y no son parámetros. Aviso de propuesta (se envía en línea, en segundos): más de 15 minutos desde la reserva. Aviso de cambio:
 *     más de 30 minutos y SOLO si la tarea ya lo reclamó (intentos >= 1; la espera normal son 5 minutos más un ciclo de un minuto y la cola). Un aviso de cambio sin reclamar
 *     (intentos = 0) no se toca: sigue esperando de verdad y, si la tarea vuelve, se revalida (si la cita ya pasó, queda OMITIDO / CITA_PASADA). Se borra a los 12 meses (0085).
 *   - Solo cambia estado, motivo y resuelto_en (el momento del cierre, no del envío); no toca intentos, acs_operacion_id ni la identidad de la fila. Lo cerrado no se reabre y
 *     no se reenvía (política «como mucho una vez»): la resolución tardía de la tarea sobre una fila ya cerrada no hace nada (su UPDATE exige PENDIENTE).
 *   - El rol de la tarea (residapp_tareas, 0083) solo recibe EXECUTE sobre el procedimiento (permiso nuevo). El procedimiento se ejecuta como el propietario para saltarse el
 *     filtro de centro de la seguridad por filas (el cierre es de todos los centros). Cierra en lotes y devuelve solo cuántas cerró: ni ids, ni familiares, ni direcciones.
 *     Sin auditoría en eventos_auditoria (no actúa ninguna persona y obligaría a dar INSERT al rol): la huella es la propia fila.
 *
 * Los CHECK se rehacen aquí (no se edita 0084, que ya está aplicado). El disparador TR_envios_correo_guard (PENDIENTE -> final, identidad inmutable) no cambia.
 */

SET XACT_ABORT ON;
BEGIN TRANSACTION;

ALTER TABLE dbo.envios_correo DROP CONSTRAINT CK_envios_correo_motivo;
GO
ALTER TABLE dbo.envios_correo ADD CONSTRAINT CK_envios_correo_motivo CHECK (motivo_codigo IS NULL OR motivo_codigo IN
    ('SIN_CORREO', 'SIN_ACCESO', 'DIRECCION_NO_CONFIRMADA', 'PROPUESTA_NO_VIGENTE', 'CITA_NO_VIGENTE', 'CITA_PASADA', 'DESTINO_NO_PERMITIDO', 'ACS_RECHAZADO', 'ACS_NO_DISPONIBLE', 'TIEMPO_AGOTADO',
     'RESULTADO_DESCONOCIDO'));
GO

CREATE PROCEDURE dbo.cerrar_envios_correo_sin_resolver
    @lote INT = 2000
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    IF @lote < 1 OR @lote > 10000
        THROW 50880, 'EMAIL_LOG_CLOSE_BATCH_INVALID', 1;

    DECLARE @ahora DATETIME2(3) = SYSUTCDATETIME(), @cerradas INT = 0, @lote_cerrado INT = 1;
    DECLARE @limite_propuesta DATETIME2(3) = DATEADD(MINUTE, -15, @ahora), @limite_cambio DATETIME2(3) = DATEADD(MINUTE, -30, @ahora);
    WHILE @lote_cerrado > 0
    BEGIN
        UPDATE TOP (@lote) dbo.envios_correo
           SET estado_codigo = 'FALLIDO', motivo_codigo = 'RESULTADO_DESCONOCIDO', resuelto_en = @ahora
         WHERE estado_codigo = 'PENDIENTE' AND resuelto_en IS NULL
           AND ((plantilla_codigo = 'AVISO_PROPUESTA_CITA' AND creado_en < @limite_propuesta)
             OR (plantilla_codigo = 'AVISO_CAMBIO_CITA' AND intentos >= 1 AND creado_en < @limite_cambio));
        SET @lote_cerrado = @@ROWCOUNT;
        SET @cerradas += @lote_cerrado;
    END;

    SELECT @cerradas AS cerradas;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'residapp_tareas') IS NOT NULL
    GRANT EXECUTE ON OBJECT::dbo.cerrar_envios_correo_sin_resolver TO residapp_tareas;
GO

COMMIT TRANSACTION;
GO
