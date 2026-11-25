/*
 * 0119 — Plazos de los hitos del proceso: todo centro tiene siempre sus siete filas (decidido por Orlando el 2026-11-25; revisión de privacidad de dpo-residapp pendiente de validar por una persona).
 *
 * Hasta ahora dbo.plazos_hitos_centro (0046) guardaba solo lo que el centro había cambiado respecto a los plazos de CJ y, sin fila, valía la constante del código. Calidad de proceso mide cada
 * caso con el plazo que valía cuando empezó y, para un hito que el centro nunca había cambiado, usaba la constante de hoy: cambiarla habría recalculado casos ya pasados. Ahora cada centro tiene sus
 * filas desde que existe, y la aplicación deja de borrarlas cuando un plazo vuelve al de CJ. La lectura conserva la constante como respaldo si faltara una fila.
 *
 * Valores iniciales, en minutos (plazo / plazo si el evento es prioritario): los de CJ, iguales para Residencia y Centro de Día; no cambia nada para ningún centro actual.
 *   VALORACION_ENFERMERIA 480/30, VALORACION_MEDICA 1440/30, RECEPCION_TRANSFERENCIA 480/30, INFORME_DERIVACION 30/30, LLAMADA_FAMILIA 60/30, LECTURA_INDICACION 480/30,
 *   REALIZACION_INDICACION 480/30.
 * Las filas que ya existen no se tocan. El centro reservado de Plataforma no es un servicio y no tiene filas. No se escribe historial: un valor inicial no es un cambio (la versión de la pantalla de
 * plazos no se mueve) ni auditoría, porque no cambia ningún plazo vigente.
 *
 * PARA EL FUTURO. Ningún script cambia las filas de un centro que ya existe sin dejar historial: Calidad de proceso volvería a recalcular casos pasados. El historial (0046) hoy no admite un cambio
 * hecho por un script (exige una cuenta y el perfil DIRECCION_CLINICA): si CJ corrige un valor y hay que llevarlo a los centros existentes, se cambia desde la pantalla de Dirección o con un diseño
 * nuevo, revisado antes. Un hito nuevo exige sus filas para todos los centros y actualizar el procedimiento de abajo.
 *
 * El procedimiento fijar_plazos_iniciales_centro lo llama el alta de centro dentro de su transacción, como fijar_horario_inicial_centro (0114) y por lo mismo: la seguridad por filas de la tabla
 * (0046) solo deja insertar con ámbito en el propio centro y Plataforma no lo tiene. Corre con los permisos del dueño (se salta esa seguridad), así que comprueba POR SU CUENTA que el ámbito activo
 * de la sesión es un ámbito vigente de perfil PLATAFORMA. Es idempotente (no pisa una fila que ya exista) y no devuelve nada.
 */

SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO

-- 1. Relleno de los centros que ya existen: solo las filas que faltan. Sin historial.
INSERT INTO dbo.plazos_hitos_centro (centro_id, hito_codigo, plazo_minutos, plazo_prioritario_minutos)
SELECT c.id, p.hito_codigo, p.plazo_minutos, p.plazo_prioritario_minutos
  FROM dbo.centros c
 CROSS JOIN (VALUES
        (N'VALORACION_ENFERMERIA',    480, 30),
        (N'VALORACION_MEDICA',       1440, 30),
        (N'RECEPCION_TRANSFERENCIA',  480, 30),
        (N'INFORME_DERIVACION',        30, 30),
        (N'LLAMADA_FAMILIA',           60, 30),
        (N'LECTURA_INDICACION',       480, 30),
        (N'REALIZACION_INDICACION',   480, 30)) p (hito_codigo, plazo_minutos, plazo_prioritario_minutos)
 WHERE c.codigo <> N'PLATAFORMA'
   AND NOT EXISTS (SELECT 1 FROM dbo.plazos_hitos_centro f WHERE f.centro_id = c.id AND f.hito_codigo = p.hito_codigo);
GO

-- 2. Los plazos iniciales de un centro nuevo.
CREATE PROCEDURE dbo.fijar_plazos_iniciales_centro
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
        THROW 51160, 'PROCESS_DEADLINES_INITIAL_FORBIDDEN', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.centros WHERE id = @centro_id AND codigo <> N'PLATAFORMA')
        THROW 51161, 'PROCESS_DEADLINES_INITIAL_CENTER_INVALID', 1;

    INSERT INTO dbo.plazos_hitos_centro (centro_id, hito_codigo, plazo_minutos, plazo_prioritario_minutos)
    SELECT @centro_id, p.hito_codigo, p.plazo_minutos, p.plazo_prioritario_minutos
      FROM (VALUES
            (N'VALORACION_ENFERMERIA',    480, 30),
            (N'VALORACION_MEDICA',       1440, 30),
            (N'RECEPCION_TRANSFERENCIA',  480, 30),
            (N'INFORME_DERIVACION',        30, 30),
            (N'LLAMADA_FAMILIA',           60, 30),
            (N'LECTURA_INDICACION',       480, 30),
            (N'REALIZACION_INDICACION',   480, 30)) p (hito_codigo, plazo_minutos, plazo_prioritario_minutos)
     WHERE NOT EXISTS (SELECT 1 FROM dbo.plazos_hitos_centro f WHERE f.centro_id = @centro_id AND f.hito_codigo = p.hito_codigo);
END;
GO

-- Solo el usuario de la aplicación lo ejecuta: ni PUBLIC ni residapp_tareas.
GRANT EXECUTE ON OBJECT::dbo.fijar_plazos_iniciales_centro TO residapp_aplicacion;
GO

COMMIT TRANSACTION;
GO
