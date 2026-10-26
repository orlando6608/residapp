/*
 * Importación con las respuestas de CJ del 2026-10-25 (docs/pendientes-cj/importacion-exportacion-confirmaciones-cj, 1.1):
 *
 *  - residentes.fecha_ingreso: la fecha de ingreso que traiga el fichero de la importación. Es opcional (nulo = no consta): el alta a mano no la
 *    pide y los residentes que ya existían no la tienen ni se inventa. creado_en sigue siendo el momento en que se dio de alta en ResidApp.
 *    Que no sea posterior a hoy lo comprueba la aplicación (una restricción no puede mirar la fecha de hoy); que no sea anterior al nacimiento,
 *    la base de datos.
 */

ALTER TABLE dbo.residentes ADD fecha_ingreso DATE NULL;
GO

ALTER TABLE dbo.residentes ADD CONSTRAINT CK_res_fecha_ingreso CHECK (fecha_ingreso IS NULL OR fecha_ingreso >= fecha_nacimiento);
GO
