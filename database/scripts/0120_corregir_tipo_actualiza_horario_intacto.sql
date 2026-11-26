/*
 * 0120 — Al corregir el tipo de un centro, un horario de comunicados nunca cambiado pasa al inicial del tipo nuevo (decidido por Orlando el 2026-11-26; lectura de dpo-residapp: admisible, sin datos
 * personales nuevos).
 *
 * Desde 0114 cada centro nace con el horario de su tipo (Residencia 19:00 y 60 minutos; Centro de Día 18:00 y 30 minutos, estos SIN validar por CJ), pero corregir_tipo_servicio_centro (0113) no lo
 * tocaba: el centro corregido se quedaba con el del tipo anterior y la pantalla Horario decía que seguía el sugerido sin ser cierto.
 *
 * LA REGLA. Si el centro no tiene ninguna fila en comunicados_horario_centro_historial (su Administración nunca ha cambiado el horario), la corrección sustituye su fila por la inicial del tipo nuevo,
 * con fijar_horario_inicial_centro (0114), para que los valores estén en un solo sitio. Si tiene historial, no se toca, aunque el horario vigente coincida con el inicial.
 * No se escribe historial del horario: como en el alta, un horario inicial no es un cambio; la corrección ya queda en la auditoría (CENTER_SERVICE_TYPE_CHANGE), en la misma transacción. No choca con
 * la regla de 0119 (ningún script cambia filas de un centro sin historial): este script no cambia ningún dato al aplicarse, y la corrección solo vale para un centro sin residentes ni familiares, que
 * no tiene comunicados que recalcular. Los centros corregidos antes de este script no se tocan.
 *
 * El resto del procedimiento es el de 0113, sin cambios (permisos del dueño, comprobaciones de Plataforma, bloqueos y auditoría); ver su cabecera. El historial se lee con UPDLOCK y HOLDLOCK, como al
 * guardar el horario (SqlFamilyCommunicationScheduleRepository): un cambio a medias de la Administración se espera y no se pisa. Si los dos se cruzan puede darse un interbloqueo (1205), que se acepta
 * con el mismo criterio que en 0113. La versión de la pantalla Horario sigue en 0: un formulario abierto antes de la corrección puede guardarse después sin aviso, y queda en el historial.
 */

SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO

IF OBJECT_ID(N'dbo.corregir_tipo_servicio_centro', N'P') IS NULL OR OBJECT_ID(N'dbo.fijar_horario_inicial_centro', N'P') IS NULL
    THROW 51170, 'PROCEDIMIENTOS_NO_EXISTEN: aplica antes 0113 y 0114', 1;
GO

ALTER PROCEDURE dbo.corregir_tipo_servicio_centro
    @centro_id   UNIQUEIDENTIFIER,
    @tipo_codigo NVARCHAR(16),
    @cuenta_id   UNIQUEIDENTIFIER
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
           AND cuenta.id = @cuenta_id AND ambito.perfil_codigo = N'PLATAFORMA')
        THROW 51136, 'CENTER_SERVICE_TYPE_FORBIDDEN', 1;
    IF @tipo_codigo IS NULL OR @tipo_codigo COLLATE Latin1_General_100_BIN2 NOT IN (N'RESIDENCIA', N'CENTRO_DE_DIA')
        THROW 51137, 'CENTER_SERVICE_TYPE_INVALID', 1;
    -- SQL Server ignora los espacios finales al comparar, también en binario: «CENTRO_DE_DIA » pasaría la lista. Se guarda siempre el valor canónico.
    SET @tipo_codigo = CASE WHEN @tipo_codigo COLLATE Latin1_General_100_BIN2 = N'RESIDENCIA' THEN N'RESIDENCIA' ELSE N'CENTRO_DE_DIA' END;

    BEGIN TRANSACTION;

    DECLARE @actual NVARCHAR(16);
    SELECT @actual = tipo_de_servicio FROM dbo.centros WITH (XLOCK, ROWLOCK) WHERE id = @centro_id AND codigo <> N'PLATAFORMA';
    IF @actual IS NULL
        THROW 51131, 'CENTER_SERVICE_TYPE_CENTER_INVALID', 1;
    IF @actual = @tipo_codigo
        THROW 51133, 'CENTER_SERVICE_TYPE_SEQUENCE_INVALID', 1;
    IF EXISTS (SELECT 1 FROM dbo.residentes WITH (READCOMMITTEDLOCK) WHERE centro_id = @centro_id)
       OR EXISTS (SELECT 1 FROM dbo.familiares WITH (READCOMMITTEDLOCK) WHERE centro_id = @centro_id)
        THROW 51135, 'CENTER_SERVICE_TYPE_NOT_EMPTY', 1;

    DECLARE @numero INT = ISNULL((SELECT MAX(numero) FROM dbo.tipos_servicio_centro WITH (READCOMMITTEDLOCK) WHERE centro_id = @centro_id), 0) + 1;
    DECLARE @fila UNIQUEIDENTIFIER = NEWID(), @ahora DATETIME2(3) = SYSUTCDATETIME();

    INSERT INTO dbo.tipos_servicio_centro (id, centro_id, numero, tipo_codigo, motivo_codigo, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
    VALUES (@fila, @centro_id, @numero, @tipo_codigo, N'ERROR_DE_ALTA', @cuenta_id, N'PLATAFORMA', @ahora);
    UPDATE dbo.centros SET tipo_de_servicio = @tipo_codigo WHERE id = @centro_id;
    INSERT INTO dbo.eventos_auditoria (id, cuenta_id, perfil_activo, centro_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
    VALUES (NEWID(), @cuenta_id, N'PLATAFORMA', @centro_id, N'CENTER_SERVICE_TYPE', @fila, N'CENTER_SERVICE_TYPE_CHANGE', @ahora);

    -- 0120: el horario de comunicados nunca cambiado pasa al inicial del tipo nuevo. Va después de cambiar el tipo, que es el que lee el procedimiento del horario inicial.
    IF NOT EXISTS (SELECT 1 FROM dbo.comunicados_horario_centro_historial WITH (UPDLOCK, HOLDLOCK) WHERE centro_id = @centro_id)
    BEGIN
        DELETE FROM dbo.comunicados_horario_centro WHERE centro_id = @centro_id;
        EXEC dbo.fijar_horario_inicial_centro @centro_id;
    END;

    COMMIT TRANSACTION;
END;
GO

COMMIT TRANSACTION;
GO
