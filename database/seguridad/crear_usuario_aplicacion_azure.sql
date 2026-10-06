/*
 * Usuario limitado de la aplicación para Azure SQL Database (ADR 0008): un usuario contenido en la propia base de datos, con
 * contraseña, sin db_owner, de modo que la seguridad por filas (database/scripts/0030_rls_centro.sql) se le aplique. A diferencia de
 * crear_usuario_aplicacion.sql, no crea un LOGIN (en Azure SQL Database solo se crea en master). No es un script de esquema: no lo
 * ejecuta aplicar-scripts.sh. Se ejecuta UNA vez, conectado a la base de datos de la aplicación (no a master) con el administrador:
 *   sqlcmd -S <servidor>.database.windows.net -d <base> -U <admin> -b -v CONTRASENA="<contraseña larga, sin comillas simples>" ^
 *          -i database/seguridad/crear_usuario_aplicacion_azure.sql
 * o pegándolo en el Editor de consultas del portal, sustituyendo $(CONTRASENA). La contraseña no se escribe nunca en el repositorio.
 * Comprobación: con ese usuario, IS_MEMBER('db_owner') da 0 y SELECT COUNT(*) FROM dbo.residentes da 0 (sin ámbito activo no ve filas).
 */

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'residapp_app')
    CREATE USER residapp_app WITH PASSWORD = N'$(CONTRASENA)';
GO

ALTER ROLE db_datareader ADD MEMBER residapp_app;
ALTER ROLE db_datawriter ADD MEMBER residapp_app;
GO
