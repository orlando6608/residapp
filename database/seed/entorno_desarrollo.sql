/*
 * Marca la base de datos como de DESARROLLO (script 0100, ADR 0015 punto 7). Solo para Azure dev, la base local y las bases de pruebas. NO ejecutar contra una base con datos
 * reales: con la marca, la aplicación respeta Modulos__ActivarTodos (todos los módulos activos sin filas). Idempotente.
 */
IF NOT EXISTS (SELECT 1 FROM dbo.entorno_bd)
    INSERT INTO dbo.entorno_bd (id, tipo) VALUES (1, N'Desarrollo');
GO
