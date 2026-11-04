/*
 * Usuario limitado de la aplicación para SQL Server con autenticación SQL (CI y pruebas en contenedor), sin db_owner, de modo que la
 * seguridad por filas (database/scripts/0030_rls_centro.sql, ADR 0008) se le aplique. No es un script de esquema: no lo ejecuta
 * aplicar-scripts.sh. Se ejecuta después de aplicar los scripts, con la contraseña en una variable de sqlcmd, nunca escrita aquí:
 *   sqlcmd -S <servidor> -d <base> -U <admin> -C -b -v CONTRASENA="<contraseña>" -i database/seguridad/crear_usuario_aplicacion.sql
 * Para Azure SQL Database, crear_usuario_aplicacion_azure.sql (usuario contenido, sin LOGIN).
 * Los permisos de escritura vienen del rol residapp_aplicacion (script de esquema 0091, hallazgo M2): aplícalo antes; este usuario ya no es db_datawriter.
 */

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'residapp_app')
    CREATE LOGIN residapp_app WITH PASSWORD = N'$(CONTRASENA)', CHECK_POLICY = OFF;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'residapp_app')
    CREATE USER residapp_app FOR LOGIN residapp_app;
GO

IF DATABASE_PRINCIPAL_ID(N'residapp_aplicacion') IS NULL
    THROW 50911, 'ROLE_RESIDAPP_APLICACION_MISSING: aplica antes el script 0091', 1;
GO

-- Lectura general (la seguridad por filas filtra por centro) y el rol de permisos de escritura por tabla del script 0091 (M2): ya NO db_datawriter.
ALTER ROLE db_datareader ADD MEMBER residapp_app;
ALTER ROLE residapp_aplicacion ADD MEMBER residapp_app;
GO
