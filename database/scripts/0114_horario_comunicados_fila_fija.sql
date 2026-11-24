/*
 * 0114 — Horario de los comunicados: todo centro tiene siempre su fila (decidido por Orlando el 2026-11-24; revisión de privacidad de dpo-residapp pendiente de validar por una persona).
 *
 * Hasta ahora dbo.comunicados_horario_centro (0051) guardaba solo lo que el centro había cambiado respecto al horario de CJ (19:00, 1 hora, resumen semanal) y, sin fila, valía ese horario. Eso
 * ataba a todos los centros a una constante del código: cambiarla habría movido el horario de todos sin que nadie lo decidiera, y un Centro de Día no podía arrancar con otro. Ahora cada centro
 * tiene su fila desde que existe, y la aplicación deja de borrarla cuando el horario vuelve al de siempre. La lectura conserva el horario de CJ como respaldo si no hubiera fila.
 *
 * Valores iniciales (los pone este script y, para los centros nuevos, el procedimiento de abajo):
 *   RESIDENCIA      19:00, margen de 60 minutos, resumen SEMANAL  (los de CJ; no cambia nada para ningún centro actual).
 *   CENTRO_DE_DIA   18:00, margen de 30 minutos, resumen SEMANAL  (propuesta de Orlando, 2026-11-24, SIN validar por CJ; la Administración del centro puede cambiarla en su pantalla Horario).
 * El centro reservado de Plataforma no es un servicio y no tiene fila. No se escribe historial: un horario inicial no es un cambio (la versión de la pantalla Horario sigue en 0).
 *
 * El procedimiento fijar_horario_inicial_centro lo llama el alta de centro dentro de su transacción. Hace falta porque la seguridad por filas de la tabla (0051) solo deja insertar con ámbito
 * en el propio centro, y quien da de alta un centro es Plataforma, que no lo tiene: el procedimiento corre con los permisos del dueño (se salta esa seguridad, ver la trampa de 0113), así que
 * comprueba POR SU CUENTA que el ámbito activo de la sesión es un ámbito vigente de perfil PLATAFORMA. Es idempotente (no pisa una fila que ya exista) y no devuelve nada.
 */

SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO

-- 1. Relleno de los centros que ya existen. Sin historial.
INSERT INTO dbo.comunicados_horario_centro (centro_id, hora_publicacion, margen_minutos, resumen_frecuencia_codigo)
SELECT c.id,
       CASE WHEN c.tipo_de_servicio = N'CENTRO_DE_DIA' THEN CAST('18:00' AS TIME(0)) ELSE CAST('19:00' AS TIME(0)) END,
       CASE WHEN c.tipo_de_servicio = N'CENTRO_DE_DIA' THEN 30 ELSE 60 END,
       N'SEMANAL'
  FROM dbo.centros c
 WHERE c.codigo <> N'PLATAFORMA'
   AND NOT EXISTS (SELECT 1 FROM dbo.comunicados_horario_centro h WHERE h.centro_id = c.id);
GO

-- 2. El horario inicial de un centro nuevo.
CREATE PROCEDURE dbo.fijar_horario_inicial_centro
    @centro_id UNIQUEIDENTIFIER
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (
        SELECT 1
          FROM dbo.ambitos_perfil ambito
          JOIN dbo.cuentas cuenta ON cuenta.id = ambito.cuenta_id
         WHERE ambito.id = TRY_CONVERT(UNIQUEIDENTIFIER, SESSION_CONTEXT(N'ambito_perfil_id'))
           AND cuenta.sujeto_externo = CONVERT(NVARCHAR(200), SESSION_CONTEXT(N'sujeto_externo'))
           AND ambito.estado = 'ACTIVE' AND ambito.revocado_en IS NULL AND cuenta.estado = 'ACTIVE'
           AND ambito.perfil_codigo = N'PLATAFORMA')
        THROW 51140, 'FAMILY_SCHEDULE_INITIAL_FORBIDDEN', 1;

    DECLARE @tipo NVARCHAR(16);
    SELECT @tipo = tipo_de_servicio FROM dbo.centros WHERE id = @centro_id AND codigo <> N'PLATAFORMA';
    IF @tipo IS NULL
        THROW 51141, 'FAMILY_SCHEDULE_INITIAL_CENTER_INVALID', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.comunicados_horario_centro WHERE centro_id = @centro_id)
        INSERT INTO dbo.comunicados_horario_centro (centro_id, hora_publicacion, margen_minutos, resumen_frecuencia_codigo)
        VALUES (@centro_id,
                CASE WHEN @tipo = N'CENTRO_DE_DIA' THEN CAST('18:00' AS TIME(0)) ELSE CAST('19:00' AS TIME(0)) END,
                CASE WHEN @tipo = N'CENTRO_DE_DIA' THEN 30 ELSE 60 END,
                N'SEMANAL');
END;
GO

-- Solo el usuario de la aplicación lo ejecuta: ni PUBLIC ni residapp_tareas.
GRANT EXECUTE ON OBJECT::dbo.fijar_horario_inicial_centro TO residapp_aplicacion;
GO

COMMIT TRANSACTION;
GO
