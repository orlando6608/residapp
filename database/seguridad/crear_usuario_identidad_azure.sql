/*
 * Usuario de la identidad gestionada de la Web App en Azure SQL Database, sin contraseña (hallazgo M4 de docs/seguridad/hallazgos.md).
 * Es el sustituto de residapp_app (crear_usuario_aplicacion_azure.sql): mismos permisos, pero la Web App se conecta con la identidad que
 * le da Azure (Authentication=Active Directory Managed Identity) y no hay contraseña que guardar ni que se pueda filtrar.
 * No es un script de esquema: no lo ejecuta aplicar-scripts.sh. Se ejecuta UNA vez, conectado a la base de datos de la aplicación (no a
 * master) con la cuenta de administrador de Entra del servidor (en el Editor de consultas del portal: «Autenticación de Microsoft Entra»;
 * no uses EXECUTE AS). Requisitos: la Web App tiene la identidad asignada por el sistema activada y el script 0091 está aplicado.
 *   sqlcmd -S <servidor>.database.windows.net -d <base> -G -b -v IDENTIDAD="<nombre de la Web App>" -i database/seguridad/crear_usuario_identidad_azure.sql
 * o pegándolo en el Editor de consultas, sustituyendo $(IDENTIDAD). El nombre del usuario es el de la Web App (app-residapp-dev en dev).
 * Comprobación: SELECT name, type_desc FROM sys.database_principals WHERE name = N'<nombre>' devuelve EXTERNAL_USER, y con esa identidad
 * IS_MEMBER('db_owner') da 0 y SELECT COUNT(*) FROM dbo.residentes da 0 (sin ámbito activo no ve filas).
 */

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(IDENTIDAD)')
    CREATE USER [$(IDENTIDAD)] FROM EXTERNAL PROVIDER;
GO

IF DATABASE_PRINCIPAL_ID(N'residapp_aplicacion') IS NULL
    THROW 50911, 'ROLE_RESIDAPP_APLICACION_MISSING: aplica antes el script 0091', 1;
GO

-- Lectura general (la seguridad por filas filtra por centro) y el rol de permisos de escritura por tabla del script 0091 (M2): ni db_datawriter ni db_owner.
ALTER ROLE db_datareader ADD MEMBER [$(IDENTIDAD)];
ALTER ROLE residapp_aplicacion ADD MEMBER [$(IDENTIDAD)];
GO
