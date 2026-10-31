/*
 * Identidad propia de la tarea programada del aviso de cambio de cita (ADR 0012, ADR 0008; lectura en docs/privacidad/tarea-correo-identidad-rls.md).
 *
 * La tarea (ChangeNoticeWorker) se ejecuta sin ninguna persona conectada, así que no tiene sujeto ni ámbito y la seguridad por filas (0030) no le dejaría ver nada con el
 * usuario limitado de la aplicación (residapp_app). En vez de prestarle el ámbito de una cuenta de Administración, tiene un ROL propio, residapp_tareas, que nace SIN
 * miembros: lo ocupará un usuario aparte (database/seguridad/crear_usuario_tarea_azure.sql, lo ejecuta quien administra la base) que solo usa la tarea, con su propia cadena
 * de conexión. La aplicación sigue conectándose con residapp_app.
 *
 *   1. El rol puede saltarse el filtro de centro (la tarea es transversal a todos los centros), pero SOLO en las filas de las tablas con permiso.
 *   2. Permisos mínimos, por columnas: lo que necesita el aviso y nada más. En concreto NO puede leer citas_cambios.motivo (el motivo interno de Administración, que la
 *      familia no ve; con un DENY explícito): que el motivo nunca se lee lo garantiza la base y no solo el código. Tampoco inserta ni borra nada, ni lee tablas clínicas,
 *      nombres ni teléfonos de familiares, ni cuentas ni ámbitos.
 *   3. Solo puede actualizar las columnas de resolución de envios_correo (estado, motivo del código, operación de ACS, intentos, resuelto_en); la identidad de la fila ya la
 *      protege TR_envios_correo_guard.
 *
 * La función de la política no se puede modificar mientras la política la referencia (error 3729). Por eso, en UNA transacción: se anotan los predicados actuales, se quita
 * la política, se recrea la función con la excepción del rol y se vuelve a crear la política con exactamente los mismos predicados (todos son seg.fn_centro_del_ambito(centro_id)
 * y se comprueba). Todo el script va en la misma transacción, con XACT_ABORT: si algo no cuadra se deshace entero (rol y permisos incluidos) y la política queda como estaba.
 * Toma bloqueos de esquema sobre todas las tablas de la política: los demás procesos esperan unos segundos y no ven una tabla sin política; en un entorno con usuarios
 * reales, aplicarlo en una ventana de mantenimiento. Se puede relanzar si falla.
 */

SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF DATABASE_PRINCIPAL_ID(N'residapp_tareas') IS NULL
    CREATE ROLE residapp_tareas;

GRANT SELECT ON dbo.envios_correo TO residapp_tareas;
GRANT UPDATE (estado_codigo, motivo_codigo, acs_operacion_id, intentos, resuelto_en) ON dbo.envios_correo TO residapp_tareas;
GRANT SELECT (id, centro_id, estado_codigo, inicio_en, cuenta_id, vinculo_id) ON dbo.citas TO residapp_tareas;
GRANT SELECT (cita_id, centro_id, numero, accion_codigo, actor_perfil_codigo, causa_codigo, inicio_anterior_en, inicio_nuevo_en) ON dbo.citas_cambios TO residapp_tareas;
GRANT SELECT (id, centro_id, familiar_id, residente_id, desvinculado_en) ON dbo.residentes_familiares TO residapp_tareas;
GRANT SELECT (id, centro_id, cuenta_id, correo) ON dbo.familiares TO residapp_tareas;
GRANT SELECT (id, centro_id, estado) ON dbo.residentes TO residapp_tareas;
GRANT SELECT (residente_id, centro_id, finalizada_en) ON dbo.suspensiones_residente TO residapp_tareas;
GRANT SELECT (vinculo_id, centro_id, numero, estado_codigo, valida_hasta) ON dbo.familiares_autorizaciones_cambios TO residapp_tareas;
-- Un DENY gana a cualquier GRANT posterior más amplio (incluso a un GRANT SELECT sobre toda la tabla): el motivo queda fuera del alcance de la tarea aunque alguien lo olvide.
DENY SELECT (motivo) ON dbo.citas_cambios TO residapp_tareas;

IF (SELECT COUNT(DISTINCT sp.predicate_definition) FROM sys.security_predicates sp WHERE sp.object_id = OBJECT_ID(N'seg.pol_centro')) <> 1
   OR EXISTS (SELECT 1 FROM sys.security_predicates sp WHERE sp.object_id = OBJECT_ID(N'seg.pol_centro')
                 AND sp.predicate_definition <> N'([seg].[fn_centro_del_ambito]([centro_id]))')
    THROW 50830, 'RLS_POLICY_UNEXPECTED_PREDICATE', 1;

DECLARE @antes INT = (SELECT COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_centro'));
DECLARE @predicados NVARCHAR(MAX) = (
    SELECT STRING_AGG(CAST(
        CASE sp.predicate_type WHEN 0 THEN N'ADD FILTER PREDICATE' ELSE N'ADD BLOCK PREDICATE' END
        + N' seg.fn_centro_del_ambito(centro_id) ON ' + QUOTENAME(OBJECT_SCHEMA_NAME(sp.target_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(sp.target_object_id))
        + CASE sp.predicate_type WHEN 1 THEN N' ' + sp.operation_desc ELSE N'' END AS NVARCHAR(MAX)), N', ')
    FROM sys.security_predicates sp WHERE sp.object_id = OBJECT_ID(N'seg.pol_centro'));
IF @antes = 0 OR @predicados IS NULL
    THROW 50831, 'RLS_POLICY_EMPTY', 1;

DROP SECURITY POLICY seg.pol_centro;

EXEC (N'
ALTER FUNCTION seg.fn_centro_del_ambito(@centro_id UNIQUEIDENTIFIER)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS permitido
     WHERE IS_MEMBER(N''db_owner'') = 1
        OR IS_MEMBER(N''residapp_tareas'') = 1
        OR EXISTS (
            SELECT 1
              FROM dbo.ambitos_perfil ambito
              JOIN dbo.cuentas cuenta ON cuenta.id = ambito.cuenta_id
             WHERE ambito.id = TRY_CONVERT(UNIQUEIDENTIFIER, SESSION_CONTEXT(N''ambito_perfil_id''))
               AND cuenta.sujeto_externo = CONVERT(NVARCHAR(200), SESSION_CONTEXT(N''sujeto_externo''))
               AND ambito.centro_id = @centro_id
               AND ambito.estado = ''ACTIVE'' AND ambito.revocado_en IS NULL
               AND cuenta.estado = ''ACTIVE'')');

EXEC (N'CREATE SECURITY POLICY seg.pol_centro ' + @predicados + N' WITH (STATE = ON)');

IF (SELECT COUNT(*) FROM sys.security_predicates WHERE object_id = OBJECT_ID(N'seg.pol_centro')) <> @antes
   OR NOT EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'pol_centro' AND is_enabled = 1 AND is_schema_bound = 1)
    THROW 50832, 'RLS_POLICY_NOT_RESTORED', 1;

COMMIT TRANSACTION;
GO
