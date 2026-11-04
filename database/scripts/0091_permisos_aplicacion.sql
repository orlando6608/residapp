/*
 * Permisos mínimos del usuario de la aplicación (hallazgo M2 de la revisión de seguridad; ADR 0008).
 *
 * Hasta ahora residapp_app pertenecía a db_datareader y db_datawriter: podía insertar, modificar y borrar en TODAS las tablas, y que no tocara la auditoría ni los
 * registros clínicos inmutables dependía solo de los disparadores INSTEAD OF. Este script añade la segunda barrera, en la propia base de datos:
 *
 *   1. Crea el rol residapp_aplicacion (mismo patrón que residapp_tareas, 0083): el usuario residapp_app se crea DESPUÉS de los scripts de esquema (CI) o a mano (Azure), así
 *      que los permisos van al rol y el usuario se une a él (database/seguridad/crear_usuario_aplicacion*.sql). Si residapp_app ya existe (Azure dev), este script lo une al
 *      rol y lo saca de db_datawriter. La lectura sigue siendo db_datareader (la seguridad por filas filtra por centro).
 *   2. Cada tabla recibe SOLO lo que la aplicación hace en ella: INSERT en las 100 tablas, UPDATE en las que modifica y DELETE en las 13 que borra (la lista de abajo es la
 *      matriz, verificada contra el código de src/ y contra los disparadores reales; una prueba de integración la compara con los permisos efectivos y con el código).
 *   3. Lo que NO concede lo NIEGA (DENY), que gana a cualquier GRANT heredado: aunque alguien vuelva a meter a residapp_app en db_datawriter, no podrá modificar ni borrar
 *      lo que no debe. Las tablas inmutables (disparadores INSTEAD OF UPDATE/DELETE) quedan así protegidas dos veces.
 *   4. Excepción: declaraciones_acceso_clinico y declaraciones_historial_traslado se actualizan solo para cerrar la declaración (terminada_en): UPDATE concedido por
 *      COLUMNA, y las demás columnas, con DENY de columna (un GRANT de columna prevalece sobre un DENY de tabla, pero un DENY de columna gana al UPDATE de tabla de
 *      db_datawriter). El disparador sigue rechazando cualquier otro cambio.
 *   5. Una tabla nueva no aparece en la lista: no recibe ningún permiso hasta que un script nuevo la añada (la prueba de cobertura obliga a decidirlo).
 *
 * Lo que este script NO hace: no acota el INSERT (lo acotan las políticas de seguridad por filas) ni limita qué columnas se actualizan en las tablas con UPDATE concedido, ni
 * impide que la aplicación inserte filas falsas en tablas donde sí inserta. Tampoco toca a residapp_tareas ni a los procedimientos EXECUTE AS OWNER (0085, 0089): una purga
 * futura de datos debe hacerse por un procedimiento así, no quitando un DENY. Para deshacer en una emergencia: ALTER ROLE db_datawriter ADD MEMBER residapp_app; (y dejar
 * constancia: no es un parche silencioso).
 */

SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF DATABASE_PRINCIPAL_ID(N'residapp_aplicacion') IS NULL
    CREATE ROLE residapp_aplicacion;

-- tabla | u: 0 sin UPDATE (DENY), 1 UPDATE de la tabla, 2 UPDATE solo de terminada_en | d: 1 DELETE concedido, 0 DENY
DECLARE @matriz TABLE (tabla SYSNAME NOT NULL PRIMARY KEY, u TINYINT NOT NULL, d BIT NOT NULL);
INSERT INTO @matriz (tabla, u, d) VALUES
    (N'administraciones_principales_cambios', 0, 0),
    (N'ambitos_perfil', 1, 0),
    (N'ambitos_perfil_residente', 1, 0),
    (N'ambitos_perfil_unidad', 1, 0),
    (N'bajas_residente', 1, 0),
    (N'basales_borrador', 1, 0),
    (N'basales_borrador_areas', 0, 1),
    (N'basales_borrador_barthel', 0, 1),
    (N'basales_borrador_barthel_items', 0, 0),
    (N'basales_sustituciones', 0, 0),
    (N'basales_version', 0, 0),
    (N'basales_version_areas', 0, 0),
    (N'basales_version_barthel', 0, 0),
    (N'basales_version_barthel_items', 0, 0),
    (N'basales_vigentes_residente', 1, 0),
    (N'cargos', 1, 0),
    (N'cargos_ocupantes', 0, 1),
    (N'cargos_unidades', 0, 1),
    (N'centros', 1, 0),
    (N'cierres_cotidianos_cambio_area_opciones', 0, 0),
    (N'cierres_cotidianos_cambio_areas', 0, 0),
    (N'cierres_cotidianos_residente', 0, 0),
    (N'cierres_excepcionales_direccion', 0, 0),
    (N'citas', 1, 0),
    (N'citas_bloqueos', 1, 0),
    (N'citas_cambios', 0, 0),
    (N'citas_configuracion_centro', 1, 0),
    (N'citas_franjas', 1, 0),
    (N'citas_modalidades_habilitadas', 0, 1),
    (N'citas_solicitudes', 1, 0),
    (N'citas_solicitudes_cambios', 0, 0),
    (N'comunicaciones_familiares', 0, 0),
    (N'comunicaciones_familiares_correcciones', 0, 0),
    (N'comunicaciones_familiares_correctoras', 0, 0),
    (N'comunicaciones_familiares_publicacion_anticipada', 0, 0),
    (N'comunicaciones_familiares_retiradas', 0, 0),
    (N'comunicados_horario_centro', 0, 1),
    (N'comunicados_horario_centro_historial', 0, 0),
    (N'credenciales', 1, 1),
    (N'cuentas', 1, 0),
    (N'declaraciones_acceso_clinico', 2, 0),
    (N'declaraciones_historial_traslado', 2, 0),
    (N'edificios', 1, 0),
    (N'envios_correo', 1, 0),
    (N'episodios_residente_centro', 1, 0),
    (N'equipos', 1, 0),
    (N'equipos_miembros', 1, 0),
    (N'escalados_medicina', 0, 0),
    (N'eventos_acceso', 0, 0),
    (N'eventos_asistenciales', 1, 0),
    (N'eventos_auditoria', 0, 0),
    (N'eventos_clinicos', 0, 0),
    (N'eventos_contexto', 0, 0),
    (N'familiares', 1, 0),
    (N'familiares_autorizaciones_cambios', 0, 0),
    (N'fotos_residente', 1, 1),
    (N'fotos_residente_negativas', 0, 1),
    (N'habitaciones', 1, 0),
    (N'indicaciones_medicas', 1, 0),
    (N'informes_derivacion', 0, 0),
    (N'intentos_llamada_familia', 0, 0),
    (N'intervalos_ubicacion_residente', 1, 0),
    (N'invitaciones_acceso', 1, 0),
    (N'logos_centro', 1, 1),
    (N'operaciones_idempotencia', 1, 0),
    (N'passkeys', 1, 1),
    (N'permisos_perfil', 1, 0),
    (N'planificacion_turnos', 1, 0),
    (N'plantas', 1, 0),
    (N'plantillas_intercambio', 1, 0),
    (N'plazas', 1, 0),
    (N'plazos_hitos_centro', 0, 1),
    (N'plazos_hitos_centro_historial', 0, 0),
    (N'portal_familiar_visitas', 1, 0),
    (N'protocolo_urgente_registros', 0, 0),
    (N'protocolos_urgentes', 0, 0),
    (N'rangos_referencia_constantes', 1, 1),
    (N'rangos_referencia_constantes_historial', 0, 0),
    (N'residentes', 1, 0),
    (N'residentes_contacto_urgente', 0, 0),
    (N'residentes_familiares', 1, 0),
    (N'residentes_identidad_correcciones', 0, 0),
    (N'seguimiento_acciones', 0, 0),
    (N'seguimiento_medico_acciones', 0, 0),
    (N'seguimientos', 0, 0),
    (N'seguimientos_medicos', 0, 0),
    (N'sesiones', 1, 0),
    (N'suspensiones_residente', 1, 0),
    (N'traslados_residente', 0, 0),
    (N'turnos_catalogo', 1, 0),
    (N'unidades', 1, 0),
    (N'valoraciones_enfermeria', 1, 0),
    (N'valoraciones_enfermeria_correcciones', 0, 0),
    (N'valoraciones_enfermeria_versiones', 0, 0),
    (N'valoraciones_medicas', 1, 0),
    (N'valoraciones_medicas_correcciones', 0, 0),
    (N'valoraciones_medicas_versiones', 0, 0),
    (N'valoraciones_rectificaciones', 0, 0),
    (N'vinculaciones_cuenta', 1, 0),
    (N'vinculaciones_cuenta_unidades', 0, 0);

DECLARE @tabla SYSNAME, @u TINYINT, @d BIT, @sql NVARCHAR(MAX);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT tabla, u, d FROM @matriz ORDER BY tabla;
OPEN c;
FETCH NEXT FROM c INTO @tabla, @u, @d;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF OBJECT_ID(N'dbo.' + QUOTENAME(@tabla), N'U') IS NULL
        THROW 50910, 'PERMISOS_APLICACION_TABLA_INEXISTENTE', 1;
    SET @sql = N'GRANT INSERT ON dbo.' + QUOTENAME(@tabla) + N' TO residapp_aplicacion;'
        + CASE @u WHEN 1 THEN N' GRANT UPDATE ON dbo.' + QUOTENAME(@tabla) + N' TO residapp_aplicacion;'
                  WHEN 2 THEN N' GRANT UPDATE (terminada_en) ON dbo.' + QUOTENAME(@tabla) + N' TO residapp_aplicacion;'
                            + ISNULL(N' DENY UPDATE (' + (SELECT STRING_AGG(CAST(QUOTENAME(col.name) AS NVARCHAR(MAX)), N', ') FROM sys.columns col
                                       WHERE col.object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@tabla)) AND col.name <> N'terminada_en') + N') ON dbo.' + QUOTENAME(@tabla) + N' TO residapp_aplicacion;', N'')
                  ELSE N' DENY UPDATE ON dbo.' + QUOTENAME(@tabla) + N' TO residapp_aplicacion;' END
        + CASE @d WHEN 1 THEN N' GRANT DELETE ON dbo.' + QUOTENAME(@tabla) + N' TO residapp_aplicacion;'
                  ELSE N' DENY DELETE ON dbo.' + QUOTENAME(@tabla) + N' TO residapp_aplicacion;' END;
    EXEC sys.sp_executesql @sql;
    FETCH NEXT FROM c INTO @tabla, @u, @d;
END;
CLOSE c;
DEALLOCATE c;

-- Azure dev (y cualquier base donde el usuario ya exista): se une al rol y deja de ser escritor general.
IF DATABASE_PRINCIPAL_ID(N'residapp_app') IS NOT NULL
BEGIN
    ALTER ROLE residapp_aplicacion ADD MEMBER residapp_app;
    IF IS_ROLEMEMBER(N'db_datawriter', N'residapp_app') = 1
        ALTER ROLE db_datawriter DROP MEMBER residapp_app;
END;

COMMIT TRANSACTION;
