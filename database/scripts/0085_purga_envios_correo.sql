/*
 * Purga del registro de envíos de correo (ADR 0012; Orlando, 2026-11-02, decisión 1.2 A: 12 meses desde el envío como tope, a confirmar por la persona experta;
 * docs/privacidad/aviso-cita-por-correo.md y docs/privacidad/antes-de-datos-reales.md, apartado 7).
 *
 * El registro (dbo.envios_correo) no guarda la dirección ni el texto, pero sí qué familiar recibió qué aviso y cuándo: no debe conservarse más de lo necesario (RGPD, art. 5.1.e).
 * Su finalidad es evitar duplicados (días) y explicar un aviso que no llegó (meses).
 *
 * El plazo vive en la base de datos y no se puede alargar desde fuera: un procedimiento propio borra las filas cuya fecha de resolución (o, si nunca se resolvió, de creación) tiene
 * más de 12 meses. El rol de la tarea programada (residapp_tareas, 0083) solo recibe EXECUTE sobre el procedimiento, NO DELETE sobre la tabla: no puede borrar nada más reciente ni
 * nada distinto. El procedimiento se ejecuta como el propietario (EXECUTE AS OWNER) para saltarse el filtro de centro de la seguridad por filas: la purga es de todos los centros.
 * Borra en lotes para no tomar un bloqueo grande, y devuelve cuántas filas borró (sin ningún dato personal). No toca eventos_auditoria, que tiene su propio plazo.
 */

SET XACT_ABORT ON;
GO

CREATE PROCEDURE dbo.purgar_envios_correo
    @lote INT = 2000
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON;
    IF @lote < 1 OR @lote > 10000
        THROW 50850, 'EMAIL_LOG_PURGE_BATCH_INVALID', 1;

    DECLARE @limite DATETIME2(3) = DATEADD(MONTH, -12, SYSUTCDATETIME()), @borradas INT = 0, @lote_borrado INT = 1;
    WHILE @lote_borrado > 0
    BEGIN
        DELETE TOP (@lote) FROM dbo.envios_correo
         WHERE creado_en < @limite AND COALESCE(resuelto_en, creado_en) < @limite;
        SET @lote_borrado = @@ROWCOUNT;
        SET @borradas += @lote_borrado;
    END;

    SELECT @borradas AS borradas;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'residapp_tareas') IS NOT NULL
    GRANT EXECUTE ON OBJECT::dbo.purgar_envios_correo TO residapp_tareas;
GO
