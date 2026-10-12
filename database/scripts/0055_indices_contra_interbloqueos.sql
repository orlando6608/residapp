/*
 * Interbloqueos entre una lectura que recorre la tabla entera y una escritura que inserta en ella.
 *
 * Las consultas que buscan por columnas sin índice recorren toda la tabla con bloqueos compartidos de página (READ COMMITTED), y un INSERT
 * concurrente, que toma bloqueos de intención de otra página, termina en interbloqueo con ellas: SQL Server aborta una de las dos (error 1205) y la
 * aplicación lo devuelve como «No disponible». En el CI ha tumbado tests de integración de forma intermitente; la sesión system_health de la BD local
 * registró 19 interbloqueos de este tipo entre el 01/10 y el 12/10. Con un índice por esas columnas la lectura busca por clave y toca solo las filas que necesita.
 *
 *  - eventos_auditoria (recurso_id, accion_codigo): la comparten el perfil que cerró un evento (SqlChangeInboxDirectory.ClosedByProfileSql, una
 *    subconsulta por cada evento de la bandeja y del detalle), los recuentos de auditoría por recurso y acción y la trazabilidad. Solo existía un
 *    índice por (centro, unidad, residente, fecha). perfil_activo va incluida para que esas lecturas no tengan que ir a la fila.
 *  - basales_version_areas (residente_id): la columna es la del residente del área y solo estaba dentro de la clave foránea compuesta. Sin ella,
 *    cualquier UPDATE o lectura por residente recorría la tabla (con respuestas_json NVARCHAR(MAX)) con bloqueos de página.
 *
 * No cambia datos ni comportamiento: solo añade índices no agrupados.
 */

CREATE INDEX IX_audit_recurso_accion ON dbo.eventos_auditoria (recurso_id, accion_codigo) INCLUDE (perfil_activo);
GO

CREATE INDEX IX_bva_residente ON dbo.basales_version_areas (residente_id);
GO
