/*
 * Usuario limitado de la aplicación para SQL Server con autenticación SQL (CI y pruebas en contenedor), sin db_owner, de modo que la
 * seguridad por filas (database/scripts/0030_rls_centro.sql, ADR 0008) se le aplique. No es un script de esquema: no lo ejecuta
 * aplicar-scripts.sh. Se ejecuta después de aplicar los scripts, con la contraseña en una variable de sqlcmd, nunca escrita aquí:
 *   sqlcmd -S <servidor> -d <base> -U <admin> -C -b -v CONTRASENA="<contraseña>" -i database/seguridad/crear_usuario_aplicacion.sql
 * Para Azure SQL Database, crear_usuario_aplicacion_azure.sql (usuario contenido, sin LOGIN).
 */

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'residapp_app')
    CREATE LOGIN residapp_app WITH PASSWORD = N'$(CONTRASENA)', CHECK_POLICY = OFF;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'residapp_app')
    CREATE USER residapp_app FOR LOGIN residapp_app;
GO

ALTER ROLE db_datareader ADD MEMBER residapp_app;
ALTER ROLE db_datawriter ADD MEMBER residapp_app;
GO
